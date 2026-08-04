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
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly Configuration configuration;
    private readonly DelvUiPatchManager patchManager;

    private bool settingsOpen;
    private bool? previousGuardState;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        GuardState.Configuration = configuration;

        patchManager = new DelvUiPatchManager();
        patchManager.TryAttach(force: true);

        var commandInfo = new CommandInfo(OnCommand)
        {
            HelpMessage = "Open DelvUI Input Guard settings."
        };

        CommandManager.AddHandler(MainCommand, commandInfo);
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

        patchManager.Dispose();
        GuardState.Configuration = null;
    }

    private void OnCommand(string command, string arguments)
    {
        var argument = arguments.Trim().ToLowerInvariant();
        switch (argument)
        {
            case "on":
                SetEnabled(true);
                return;
            case "off":
                SetEnabled(false);
                return;
            case "toggle":
                SetEnabled(!configuration.Enabled);
                return;
            default:
                settingsOpen = true;
                return;
        }
    }

    private void SetEnabled(bool enabled)
    {
        configuration.Enabled = enabled;
        configuration.Save();
        Log.Information("DelvUI Input Guard is now {State}.", enabled ? "enabled" : "disabled");
    }

    private void OpenConfig() => settingsOpen = true;

    private void Draw()
    {
        patchManager.TryAttach();

        var currentGuardState = GuardState.ShouldBlock;
        if (configuration.LogStateChanges && previousGuardState != currentGuardState)
        {
            Log.Debug("DelvUI Input Guard active under cursor: {State}", currentGuardState);
            previousGuardState = currentGuardState;
        }

        if (settingsOpen)
            DrawSettings();
    }

    private void DrawSettings()
    {
        ImGui.SetNextWindowSize(new Vector2(560f, 420f), ImGuiCond.FirstUseEver);
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
            "When the cursor is over an interactive Dalamud/ImGui window, DelvUI HUD elements underneath it will ignore mouse hover, targeting, clicks, context menus, and drag input. DelvUI stays visible."
        );

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Status");

        DrawStatusLine("DelvUI detected", patchManager.IsAttached);
        ImGui.TextUnformatted($"DelvUI version: {patchManager.DetectedVersion}");
        ImGui.TextUnformatted($"Patched HUD methods: {patchManager.PatchedMethodCount}");
        DrawStatusLine("Another ImGui window currently captures the mouse", ImGui.GetIO().WantCaptureMouse);
        DrawStatusLine("DelvUI input currently blocked", GuardState.ShouldBlock);

        if (!string.IsNullOrWhiteSpace(patchManager.LastError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.25f, 1f), patchManager.LastError);
        }

        ImGui.Spacing();
        if (ImGui.Button("Retry DelvUI detection"))
            patchManager.TryAttach(force: true);

        ImGui.SameLine();
        if (ImGui.Button("Save settings"))
            configuration.Save();

        var logChanges = configuration.LogStateChanges;
        if (ImGui.Checkbox("Write guard state changes to the Dalamud log", ref logChanges))
        {
            configuration.LogStateChanges = logChanges;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Commands: /duiguard or /dig; add on, off, or toggle for direct control.");
        ImGui.TextDisabled("Compatibility target: DelvUI 2.7.0.1. No DelvUI files are modified.");

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
}
