# DelvUI Input Guard

A Dalamud companion plugin that prevents DelvUI HUD elements from reacting to mouse input through overlapping plugin windows.

## What changed in 0.2

Versions 0.1.x attempted to patch DelvUI input checks. That approach was ineffective because DelvUI performs input handling through its own clipping/input pipeline.

Version 0.2 uses DelvUI's built-in third-party clipping integration. It publishes interactive ImGui window rectangles through the shared `DelvUI.ClipRects` dictionary. DelvUI then rejects its own unit-frame mouse handling inside those rectangles.

## Behaviour

- Penumbra or another normal plugin window over a DelvUI unit frame: DelvUI does not react underneath it.
- QoLBar category popup over a DelvUI unit frame: DelvUI does not react underneath it.
- The DelvUI HUD remains visible.
- No DelvUI files are edited and no DelvUI methods are patched.
- Settings persist across restarts.

## Required DelvUI settings

DelvUI's **Window Clipping** must be enabled, and its option allowing **third-party plugin windows** to be clipped must also be enabled. Both are normally enabled by default. Open `/dig` to see whether the plugin can read their runtime state.

## Commands

```text
/dig
/duiguard
/dig on
/dig off
/dig toggle
```

## Compatibility

- Targeted against DelvUI 2.7.0.1
- Dalamud API 15

The plugin uses the public Dalamud data-share API and DelvUI's native `DelvUI.ClipRects` contract.
