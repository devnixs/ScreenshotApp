namespace Snapper.Core.App;

public static class TrayMenuClickGate
{
    public static bool ShouldOpenMenu(bool menuVisible, long? millisecondsSinceMenuClosed, int doubleClickTimeMs) =>
        !menuVisible && (millisecondsSinceMenuClosed is null || millisecondsSinceMenuClosed >= doubleClickTimeMs);
}
