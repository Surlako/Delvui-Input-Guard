# Changelog

## 0.2.0.0

- Replaced the ineffective Harmony/input-query patching approach.
- Uses DelvUI's native `DelvUI.ClipRects` third-party window-clipping data share.
- Publishes complete interactive ImGui window rectangles instead of only checking the mouse position.
- Captures normal plugin windows and popup windows such as QoLBar categories.
- Keeps the previous frame's rectangles to remain correct regardless of plugin draw order.
- Shows DelvUI Window Clipping and third-party clipping status when runtime reflection can read it.
- No longer depends on Lib.Harmony and does not patch DelvUI methods.

## 0.1.1.0

- Improved DelvUI detection and attempted window-overlap tracking.

## 0.1.0.0

- Initial prototype.
