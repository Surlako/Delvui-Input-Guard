using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;

namespace DelvUIInputGuard;

/// <summary>
/// Publishes interactive ImGui window rectangles through DelvUI's native
/// third-party clipping data share. DelvUI then performs its own normal input
/// rejection for HUD elements underneath those rectangles.
/// </summary>
internal sealed class ClipRectPublisher : IDisposable
{
    private const string DataShareTag = "DelvUI.ClipRects";
    private const string KeyPrefix = "DelvUIInputGuard:";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte BeginDelegate(nint name, nint open, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte BeginPopupDelegate(nint name, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte BeginPopupModalDelegate(nint name, nint open, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern nint GetProcAddress(nint module, string procedureName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern nint GetModuleHandle(string moduleName);

    private readonly Configuration configuration;
    private readonly Dictionary<string, int> occurrenceCounters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> lastSeenFrame = new(StringComparer.Ordinal);

    private Dictionary<string, Vector4>? sharedRects;
    private Hook<BeginDelegate>? beginHook;
    private Hook<BeginPopupDelegate>? beginPopupHook;
    private Hook<BeginPopupModalDelegate>? beginPopupModalHook;

    private int currentFrame = -1;
    private bool dataShareAcquired;
    private bool disposed;

    public bool SharedDataConnected => sharedRects is not null;
    public bool RootWindowHookReady => beginHook is not null;
    public bool PopupHookReady => beginPopupHook is not null;
    public bool PopupModalHookReady => beginPopupModalHook is not null;
    public int PublishedThisFrame { get; private set; }
    public int PublishedEntryCount => sharedRects?.Keys.Count(key => key.StartsWith(KeyPrefix, StringComparison.Ordinal)) ?? 0;
    public string LastPublishedWindow { get; private set; } = "None";
    public string LastError { get; private set; } = string.Empty;

    public ClipRectPublisher(Configuration configuration)
    {
        this.configuration = configuration;
        Initialize();
    }

    public void Tick()
    {
        EnsureFrame();
        if (!configuration.Enabled)
            ClearOwnedEntries();
    }

    private void Initialize()
    {
        try
        {
            sharedRects = Plugin.PluginInterface.GetOrCreateData<Dictionary<string, Vector4>>(
                DataShareTag,
                static () => new Dictionary<string, Vector4>(StringComparer.Ordinal)
            );
            dataShareAcquired = true;

            var moduleHandle = FindCimguiModule();
            if (moduleHandle == nint.Zero)
                throw new InvalidOperationException("Could not find cimgui.dll.");

            var beginAddress = GetProcAddress(moduleHandle, "igBegin");
            if (beginAddress == nint.Zero)
                throw new InvalidOperationException("Could not resolve igBegin from cimgui.dll.");

            beginHook = Plugin.GameInteropProvider.HookFromAddress<BeginDelegate>(beginAddress, BeginDetour);
            beginHook.Enable();

            // Popup hooks are optional so a cimgui export change cannot disable
            // ordinary Penumbra/Dalamud window protection.
            var popupAddress = GetProcAddress(moduleHandle, "igBeginPopup");
            if (popupAddress != nint.Zero)
            {
                beginPopupHook = Plugin.GameInteropProvider.HookFromAddress<BeginPopupDelegate>(popupAddress, BeginPopupDetour);
                beginPopupHook.Enable();
            }

            var popupModalAddress = GetProcAddress(moduleHandle, "igBeginPopupModal");
            if (popupModalAddress != nint.Zero)
            {
                beginPopupModalHook = Plugin.GameInteropProvider.HookFromAddress<BeginPopupModalDelegate>(popupModalAddress, BeginPopupModalDetour);
                beginPopupModalHook.Enable();
            }

            LastError = string.Empty;
            Plugin.Log.Information(
                "Connected to DelvUI native clip rectangles. Root={Root}, Popup={Popup}, Modal={Modal}",
                RootWindowHookReady,
                PopupHookReady,
                PopupModalHookReady
            );
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Plugin.Log.Error(ex, "Failed to initialize DelvUI native clip rectangle publishing.");
        }
    }

    private byte BeginDetour(nint namePointer, nint openPointer, int flags)
    {
        var result = beginHook?.Original(namePointer, openPointer, flags) ?? (byte)0;
        if (result != 0)
            TryPublish(namePointer, (ImGuiWindowFlags)flags, "window");
        return result;
    }

    private byte BeginPopupDetour(nint namePointer, int flags)
    {
        var result = beginPopupHook?.Original(namePointer, flags) ?? (byte)0;
        if (result != 0 && configuration.CapturePopups)
            TryPublish(namePointer, (ImGuiWindowFlags)flags, "popup");
        return result;
    }

    private byte BeginPopupModalDetour(nint namePointer, nint openPointer, int flags)
    {
        var result = beginPopupModalHook?.Original(namePointer, openPointer, flags) ?? (byte)0;
        if (result != 0 && configuration.CapturePopups)
            TryPublish(namePointer, (ImGuiWindowFlags)flags, "modal");
        return result;
    }

    private void TryPublish(nint namePointer, ImGuiWindowFlags flags, string kind)
    {
        try
        {
            EnsureFrame();

            if (!configuration.Enabled || sharedRects is null)
                return;

            if ((flags & ImGuiWindowFlags.NoMouseInputs) != 0 ||
                (flags & ImGuiWindowFlags.NoInputs) == ImGuiWindowFlags.NoInputs)
                return;

            var rawName = Marshal.PtrToStringUTF8(namePointer) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawName) || IsDelvUiHudWindow(rawName))
                return;

            var position = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            if (!IsFinite(position) || !IsFinite(size) || size.X <= 1f || size.Y <= 1f)
                return;

            var baseKey = kind + ":" + rawName;
            occurrenceCounters.TryGetValue(baseKey, out var occurrence);
            occurrenceCounters[baseKey] = occurrence + 1;

            var key = $"{KeyPrefix}{baseKey}:{occurrence}";
            sharedRects[key] = new Vector4(
                position.X,
                position.Y,
                position.X + size.X,
                position.Y + size.Y
            );
            lastSeenFrame[key] = currentFrame;
            PublishedThisFrame++;
            LastPublishedWindow = SanitizeWindowName(rawName);

            if (configuration.LogPublishedWindows)
            {
                Plugin.Log.Verbose(
                    "Published DelvUI clip rectangle for {Window}: {MinX},{MinY} -> {MaxX},{MaxY}",
                    LastPublishedWindow,
                    position.X,
                    position.Y,
                    position.X + size.X,
                    position.Y + size.Y
                );
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Plugin.Log.Verbose(ex, "Failed to publish an ImGui window rectangle to DelvUI.");
        }
    }

    private void EnsureFrame()
    {
        int frame;
        try
        {
            frame = ImGui.GetFrameCount();
        }
        catch
        {
            return;
        }

        if (frame == currentFrame)
            return;

        currentFrame = frame;
        PublishedThisFrame = 0;
        occurrenceCounters.Clear();

        if (sharedRects is null)
            return;

        // Keep the previous frame so protection works regardless of whether
        // DelvUI draws before or after the window-owning plugin this frame.
        var staleKeys = lastSeenFrame
            .Where(pair => frame - pair.Value > 1)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in staleKeys)
        {
            sharedRects.Remove(key);
            lastSeenFrame.Remove(key);
        }
    }

    private void ClearOwnedEntries()
    {
        if (sharedRects is null)
            return;

        foreach (var key in sharedRects.Keys
                     .Where(key => key.StartsWith(KeyPrefix, StringComparison.Ordinal))
                     .ToArray())
        {
            sharedRects.Remove(key);
        }

        lastSeenFrame.Clear();
        occurrenceCounters.Clear();
        PublishedThisFrame = 0;
    }

    private static nint FindCimguiModule()
    {
        var moduleHandle = GetModuleHandle("cimgui.dll");
        if (moduleHandle != nint.Zero)
            return moduleHandle;

        var module = Process.GetCurrentProcess().Modules
            .Cast<ProcessModule>()
            .FirstOrDefault(candidate => candidate.ModuleName.Contains("cimgui", StringComparison.OrdinalIgnoreCase));
        return module?.BaseAddress ?? nint.Zero;
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsDelvUiHudWindow(string name)
    {
        return name.StartsWith("DelvUI_HUD", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("DelvUI_grid", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("DelvUI_draggables", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("DelvUI_tooltip", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("DelvUI_Windows", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("JobHud", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("##DelvUI", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeWindowName(string name)
    {
        var marker = name.IndexOf("###", StringComparison.Ordinal);
        if (marker >= 0)
            name = name[..marker];

        marker = name.IndexOf("##", StringComparison.Ordinal);
        if (marker > 0)
            name = name[..marker];

        return string.IsNullOrWhiteSpace(name) ? "Unnamed ImGui window" : name.Trim();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        beginPopupModalHook?.Dispose();
        beginPopupModalHook = null;
        beginPopupHook?.Dispose();
        beginPopupHook = null;
        beginHook?.Dispose();
        beginHook = null;

        ClearOwnedEntries();
        sharedRects = null;

        if (dataShareAcquired)
        {
            try
            {
                Plugin.PluginInterface.RelinquishData(DataShareTag);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Could not relinquish DelvUI clip rectangle data share.");
            }
        }

        dataShareAcquired = false;
    }
}
