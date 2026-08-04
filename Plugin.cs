using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DelvUIInputGuard;

public sealed class Plugin : IDalamudPlugin
{
    private const string MainCommand = "/duiguard";
    private const string ShortCommand = "/dig";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly Configuration configuration;
    private readonly ClipRectPublisher publisher;
    private readonly DelvUiCompatibilityStatus compatibility = new();

    private bool settingsOpen;
    private int lastStatusFrame = -1000;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        publisher = new ClipRectPublisher(configuration);
        compatibility.Refresh();

        CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open DelvUI Input Guard settings."
        });
        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open DelvUI Input Guard settings."
        });

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(ShortCommand);
        publisher.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "on":
                SetEnabled(true);
                break;
            case "off":
                SetEnabled(false);
                break;
            case "toggle":
                SetEnabled(!configuration.Enabled);
                break;
            default:
                settingsOpen = true;
                break;
        }
    }

    private void SetEnabled(bool enabled)
    {
        configuration.Enabled = enabled;
        configuration.Save();
        publisher.Tick();
        Log.Information("DelvUI Input Guard is now {State}.", enabled ? "enabled" : "disabled");
    }

    private void OpenConfig() => settingsOpen = true;

    private void Draw()
    {
        publisher.Tick();

        var frame = ImGui.GetFrameCount();
        if (frame - lastStatusFrame >= 60)
        {
            compatibility.Refresh();
            lastStatusFrame = frame;
        }

        if (settingsOpen)
            DrawSettings();
    }

    private void DrawSettings()
    {
        ImGui.SetNextWindowSize(new Vector2(620f, 540f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("DelvUI Input Guard", ref settingsOpen))
        {
            ImGui.End();
            return;
        }

        var changed = false;

        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Enable input guard", ref enabled))
        {
            configuration.Enabled = enabled;
            changed = true;
        }

        ImGui.TextWrapped(
            "Publishes Penumbra, QoLBar, and other interactive ImGui window rectangles to DelvUI's built-in third-party clipping system. DelvUI then blocks its own unit-frame hover and click handling underneath those windows. No DelvUI code is patched."
        );

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Status");

        DrawStatusLine("DelvUI installed", compatibility.IsInstalled);
        DrawStatusLine("DelvUI loaded", compatibility.IsLoaded);
        ImGui.TextUnformatted($"DelvUI version: {compatibility.Version}");
        DrawStatusLine("Native DelvUI clip data connected", publisher.SharedDataConnected);
        DrawStatusLine("Normal window capture ready", publisher.RootWindowHookReady);
        DrawStatusLine("Popup capture ready", publisher.PopupHookReady);
        ImGui.TextUnformatted($"Published rectangles this frame: {publisher.PublishedThisFrame}");
        ImGui.TextUnformatted($"Active published rectangles: {publisher.PublishedEntryCount}");
        ImGui.TextUnformatted($"Last captured window: {publisher.LastPublishedWindow}");

        DrawOptionalStatusLine("DelvUI Window Clipping enabled", compatibility.WindowClippingEnabled);
        DrawOptionalStatusLine("DelvUI third-party clipping enabled", compatibility.ThirdPartyClippingEnabled);

        if (compatibility.WindowClippingEnabled == false || compatibility.ThirdPartyClippingEnabled == false)
        {
            ImGui.Spacing();
            ImGui.TextColored(
                new Vector4(1f, 0.55f, 0.25f, 1f),
                "DelvUI's Window Clipping and third-party plugin-window clipping must both be enabled."
            );
        }

        if (!string.IsNullOrWhiteSpace(publisher.LastError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.35f, 1f), publisher.LastError);
        }

        if (!string.IsNullOrWhiteSpace(compatibility.LastError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.25f, 1f), compatibility.LastError);
        }

        ImGui.Spacing();
        if (ImGui.Button("Open DelvUI settings"))
            compatibility.OpenDelvUiConfig();

        ImGui.SameLine();
        if (ImGui.Button("Refresh status"))
            compatibility.Refresh();

        var capturePopups = configuration.CapturePopups;
        if (ImGui.Checkbox("Capture popup windows such as QoLBar categories", ref capturePopups))
        {
            configuration.CapturePopups = capturePopups;
            changed = true;
        }

        var logWindows = configuration.LogPublishedWindows;
        if (ImGui.Checkbox("Write captured window rectangles to the Dalamud log", ref logWindows))
        {
            configuration.LogPublishedWindows = logWindows;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Commands: /duiguard or /dig; add on, off, or toggle for direct control.");
        ImGui.TextDisabled("Compatibility target: DelvUI 2.7.0.1 / Dalamud API 15.");

        if (changed)
            configuration.Save();

        ImGui.End();
    }

    private static void DrawStatusLine(string label, bool state)
    {
        ImGui.TextUnformatted(label + ":");
        ImGui.SameLine();
        ImGui.TextColored(
            state ? new Vector4(0.35f, 0.9f, 0.45f, 1f) : new Vector4(0.95f, 0.4f, 0.35f, 1f),
            state ? "Yes" : "No"
        );
    }

    private static void DrawOptionalStatusLine(string label, bool? state)
    {
        ImGui.TextUnformatted(label + ":");
        ImGui.SameLine();

        if (state is null)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), "Unknown");
            return;
        }

        ImGui.TextColored(
            state.Value ? new Vector4(0.35f, 0.9f, 0.45f, 1f) : new Vector4(0.95f, 0.4f, 0.35f, 1f),
            state.Value ? "Yes" : "No"
        );
    }
}
