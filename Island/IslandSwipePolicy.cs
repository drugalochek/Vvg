namespace Island;

internal enum SwipeAction { None, Stow, Restore }

/// <summary>Pure gesture rules (tested on any OS).</summary>
internal static class IslandSwipePolicy
{
    internal const double StartDistance = 6, TriggerDistance = 22;
    internal static int HideDirection(string? verticalAlign) => verticalAlign == "Bottom" ? 1 : -1;
    /// <param name="dy">Vertical travel in DIPs, positive = down.</param>
    internal static SwipeAction Resolve(double dy, int hideDirection, bool stowed)
    {
        double toward = dy * hideDirection;                  // > 0: toward the screen edge
        if (!stowed && toward >= TriggerDistance) return SwipeAction.Stow;
        if (stowed && toward <= -TriggerDistance) return SwipeAction.Restore;
        return SwipeAction.None;
    }
    internal static string RestoreHint(bool ru, int hideDirection) => ru
        ? (hideDirection < 0 ? "Потяни вниз или нажми — островок вернётся" : "Потяни вверх или нажми — островок вернётся")
        : (hideDirection < 0 ? "Swipe down or click to bring the island back" : "Swipe up or click to bring the island back");
}
