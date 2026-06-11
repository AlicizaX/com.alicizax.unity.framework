namespace AlicizaX.UI.Runtime
{
    public struct UIRouteOptions
    {
        public bool AddToHistory;
        public bool CloseCurrent;
        public bool SuppressDuplicate;
    }

    public static class UIRouteOptionsPreset
    {
        public static UIRouteOptions Page => new UIRouteOptions
        {
            AddToHistory = true,
            CloseCurrent = true,
            SuppressDuplicate = true,
        };
    }
}
