using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;

namespace DelvUIInputGuard;

/// <summary>
/// Tracks whether the mouse is over an interactive ImGui window that is not
/// owned by DelvUI. The previous frame is retained as a fallback so the guard
/// works regardless of plugin draw order.
/// </summary>
internal sealed class WindowHoverTracker : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte BeginDelegate(IntPtr name, IntPtr open, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EndDelegate();

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    private readonly Stack<WindowEntry> windowStack = new();
    private Hook<BeginDelegate>? beginHook;
    private Hook<EndDelegate>? endHook;

    private int frame = -1;
    private bool hoveredThisFrame;
    private bool hoveredPreviousFrame;
    private bool disposed;

    public bool IsInitialized { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public string LastHoveredWindow { get; private set; } = "None";

    public bool IsOtherInteractiveWindowHovered
    {
        get
        {
            EnsureFrame();
            return hoveredThisFrame || hoveredPreviousFrame;
        }
    }

    public void Initialize()
    {
        if (disposed || IsInitialized)
            return;

        try
        {
            var moduleHandle = GetModuleHandle("cimgui.dll");
            if (moduleHandle == IntPtr.Zero)
            {
                var module = Process.GetCurrentProcess().Modules
                    .Cast<ProcessModule>()
                    .FirstOrDefault(candidate => candidate.ModuleName.Contains("cimgui", StringComparison.OrdinalIgnoreCase));
                moduleHandle = module?.BaseAddress ?? IntPtr.Zero;
            }

            if (moduleHandle == IntPtr.Zero)
                throw new InvalidOperationException("Could not find cimgui.dll.");

            var beginAddress = GetProcAddress(moduleHandle, "igBegin");
            var endAddress = GetProcAddress(moduleHandle, "igEnd");
            if (beginAddress == IntPtr.Zero || endAddress == IntPtr.Zero)
                throw new InvalidOperationException("Could not resolve igBegin/igEnd from cimgui.dll.");

            beginHook = Plugin.GameInteropProvider.HookFromAddress<BeginDelegate>(beginAddress, BeginDetour);
            endHook = Plugin.GameInteropProvider.HookFromAddress<EndDelegate>(endAddress, EndDetour);

            beginHook.Enable();
            endHook.Enable();

            IsInitialized = true;
            LastError = string.Empty;
            Plugin.Log.Information("DelvUI Input Guard window-hover tracker initialized.");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Plugin.Log.Error(ex, "Failed to initialize the DelvUI Input Guard window-hover tracker.");
        }
    }

    private byte BeginDetour(IntPtr namePointer, IntPtr openPointer, int flags)
    {
        var result = beginHook is not null
            ? beginHook.Original(namePointer, openPointer, flags)
            : (byte)0;

        try
        {
            EnsureFrame();
            var name = Marshal.PtrToStringUTF8(namePointer) ?? string.Empty;
            windowStack.Push(new WindowEntry(name, (ImGuiWindowFlags)flags, result != 0));
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "Failed to track an ImGui window begin call.");
            windowStack.Push(default);
        }

        return result;
    }

    private void EndDetour()
    {
        try
        {
            EnsureFrame();

            if (windowStack.TryPop(out var entry) && ShouldInspect(entry))
            {
                const ImGuiHoveredFlags hoverFlags =
                    ImGuiHoveredFlags.RootAndChildWindows |
                    ImGuiHoveredFlags.AllowWhenBlockedByPopup |
                    ImGuiHoveredFlags.AllowWhenBlockedByActiveItem;

                if (ImGui.IsWindowHovered(hoverFlags))
                {
                    hoveredThisFrame = true;
                    LastHoveredWindow = SanitizeWindowName(entry.Name);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "Failed while checking an ImGui window for mouse overlap.");
        }
        finally
        {
            endHook?.Original();
        }
    }

    private static bool ShouldInspect(WindowEntry entry)
    {
        if (!entry.Visible || string.IsNullOrWhiteSpace(entry.Name))
            return false;

        if ((entry.Flags & ImGuiWindowFlags.NoMouseInputs) != 0 ||
            (entry.Flags & ImGuiWindowFlags.NoInputs) == ImGuiWindowFlags.NoInputs)
            return false;

        return true;
    }

    private void EnsureFrame()
    {
        int currentFrame;
        try
        {
            currentFrame = ImGui.GetFrameCount();
        }
        catch
        {
            return;
        }

        if (currentFrame == frame)
            return;

        hoveredPreviousFrame = hoveredThisFrame;
        hoveredThisFrame = false;
        frame = currentFrame;

        // A stale stack can only occur if a plugin issued mismatched Begin/End
        // calls or was unloaded during a frame. Clearing here prevents leakage.
        windowStack.Clear();
    }

    private static string SanitizeWindowName(string name)
    {
        var marker = name.IndexOf("###", StringComparison.Ordinal);
        if (marker >= 0)
            name = name[..marker];

        marker = name.IndexOf("##", StringComparison.Ordinal);
        if (marker >= 0 && marker > 0)
            name = name[..marker];

        return string.IsNullOrWhiteSpace(name) ? "Unnamed ImGui window" : name.Trim();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        beginHook?.Dispose();
        beginHook = null;
        endHook?.Dispose();
        endHook = null;
        windowStack.Clear();
        IsInitialized = false;
    }

    private readonly record struct WindowEntry(string Name, ImGuiWindowFlags Flags, bool Visible);
}
