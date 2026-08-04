# DelvUI Input Guard

A separate Dalamud companion plugin that prevents DelvUI HUD elements from reacting through overlapping interactive plugin windows such as Penumbra, QoLBar, Glamourer, or Dalamud settings.

## Behavior

When an interactive ImGui window owns the mouse at the cursor position, DelvUI HUD mouse queries are suppressed for that frame. DelvUI remains visible, but underlying HUD elements do not:

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

The plugin uses Harmony to replace DelvUI HUD calls to relevant Dear ImGui mouse-query methods with guarded wrappers. Only methods declared by classes derived from DelvUI's `HudElement` are considered; DelvUI's own configuration windows are not patched.
