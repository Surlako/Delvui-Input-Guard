using Dalamud.Bindings.ImGui;

namespace DelvUIInputGuard;

internal static class GuardState
{
    internal static Configuration? Configuration { get; set; }

    internal static bool ShouldBlock
    {
        get
        {
            try
            {
                return Configuration?.Enabled == true && ImGui.GetIO().WantCaptureMouse;
            }
            catch
            {
                return false;
            }
        }
    }
}
