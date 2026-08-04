namespace DelvUIInputGuard;

internal static class GuardState
{
    internal static Configuration? Configuration { get; set; }
    internal static WindowHoverTracker? WindowTracker { get; set; }

    internal static bool ShouldBlock
    {
        get
        {
            try
            {
                return Configuration?.Enabled == true &&
                       WindowTracker?.IsOtherInteractiveWindowHovered == true;
            }
            catch
            {
                return false;
            }
        }
    }
}
