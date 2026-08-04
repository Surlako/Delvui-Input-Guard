# Changelog

## 0.1.1.0

- Fixed DelvUI detection by using Dalamud's installed-plugin list and runtime assembly ownership.
- Removed the outdated dependency on a specific DelvUI `HudElement` base type.
- Matches ImGui calls by signature instead of assembly metadata identity.
- Added a native ImGui window-overlap tracker so the guard distinguishes foreign plugin windows from DelvUI itself.
- Added detailed status diagnostics, including the last overlapping window.

## 0.1.0.0

- Initial release.
