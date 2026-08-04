using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DelvUIInputGuard;

internal static class MouseQueryWrappers
{
    public static bool IsMouseHoveringRect2(Vector2 minimum, Vector2 maximum)
        => !GuardState.ShouldBlock && ImGui.IsMouseHoveringRect(minimum, maximum);

    public static bool IsMouseHoveringRect3(Vector2 minimum, Vector2 maximum, bool clip)
        => !GuardState.ShouldBlock && ImGui.IsMouseHoveringRect(minimum, maximum, clip);

    public static bool IsItemHovered0()
        => !GuardState.ShouldBlock && ImGui.IsItemHovered();

    public static bool IsItemHovered1(ImGuiHoveredFlags flags)
        => !GuardState.ShouldBlock && ImGui.IsItemHovered(flags);

    public static bool IsWindowHovered0()
        => !GuardState.ShouldBlock && ImGui.IsWindowHovered();

    public static bool IsWindowHovered1(ImGuiHoveredFlags flags)
        => !GuardState.ShouldBlock && ImGui.IsWindowHovered(flags);

    public static bool IsAnyItemHovered()
        => !GuardState.ShouldBlock && ImGui.IsAnyItemHovered();

    public static bool IsItemClicked0()
        => !GuardState.ShouldBlock && ImGui.IsItemClicked();

    public static bool IsItemClicked1(ImGuiMouseButton button)
        => !GuardState.ShouldBlock && ImGui.IsItemClicked(button);

    public static bool IsMouseDown(ImGuiMouseButton button)
        => !GuardState.ShouldBlock && ImGui.IsMouseDown(button);

    public static bool IsMouseClicked1(ImGuiMouseButton button)
        => !GuardState.ShouldBlock && ImGui.IsMouseClicked(button);

    public static bool IsMouseClicked2(ImGuiMouseButton button, bool repeat)
        => !GuardState.ShouldBlock && ImGui.IsMouseClicked(button, repeat);

    public static bool IsMouseReleased(ImGuiMouseButton button)
        => !GuardState.ShouldBlock && ImGui.IsMouseReleased(button);

    public static bool IsMouseDoubleClicked(ImGuiMouseButton button)
        => !GuardState.ShouldBlock && ImGui.IsMouseDoubleClicked(button);

    public static bool IsMouseDragging1(ImGuiMouseButton button)
        => !GuardState.ShouldBlock && ImGui.IsMouseDragging(button);

    public static bool IsMouseDragging2(ImGuiMouseButton button, float lockThreshold)
        => !GuardState.ShouldBlock && ImGui.IsMouseDragging(button, lockThreshold);
}
