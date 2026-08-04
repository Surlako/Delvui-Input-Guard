# DelvUI Input Guard

A separate Dalamud companion plugin that prevents DelvUI HUD elements from reacting through overlapping interactive plugin windows such as Penumbra, QoLBar, Glamourer, or Dalamud settings.

## Behavior

When the cursor is over another interactive ImGui window, DelvUI HUD mouse queries are suppressed. The tracker keeps the previous frame as a fallback so it works regardless of plugin draw order. DelvUI remains visible, but underlying HUD elements do not:

- mouseover-target actors;
- change targets on left click;
- open context menus on right click;
- show hover reactions or tooltips;
- begin mouse-driven drag interaction.

When the cursor leaves the overlapping window, DelvUI immediately works normally again.

The plugin does not edit, replace, or redistribute DelvUI files. All compatibility changes are applied in memory and are removed when this plugin unloads.

## Commands

- `/duiguard` — open settings
- `/dig` — short alias
- `/duiguard on`
- `/duiguard off`
- `/duiguard toggle`

## Compatibility

The initial compatibility target is DelvUI `2.7.0.1` and Dalamud API level 15. The plugin locates DelvUI dynamically and patches HUD-element mouse-query calls by reflection, so minor DelvUI changes may continue working. If DelvUI changes its internal HUD structure, the settings window will show that no compatible methods were found instead of patching blindly.

## Build

Run the included GitHub Actions workflow:

1. Open **Actions**.
2. Select **Build and publish plugin**.
3. Choose **Run workflow** on `main`.
4. Wait for the green checkmark.

The workflow creates a release containing `latest.zip` and writes `pluginmaster.json` to the repository root.

## Custom repository URL

After the first successful build:

```text
https://raw.githubusercontent.com/YOUR_GITHUB_NAME/YOUR_REPOSITORY/main/pluginmaster.json
```

Add that URL under `/xlsettings` → **Experimental** → **Custom Plugin Repositories**.

## Technical note

The plugin reads DelvUI's installed version through Dalamud, identifies the live DelvUI assembly through Dalamud's assembly ownership API, and uses Harmony to replace runtime mouse-query calls with guarded wrappers. DelvUI configuration classes are excluded from patching. A native `igBegin`/`igEnd` tracker identifies overlapping interactive ImGui windows without relying on the global `WantCaptureMouse` flag.
