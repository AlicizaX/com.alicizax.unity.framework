namespace AlicizaX.UI.Runtime
{
    public enum UIState:byte
    {
        Uninitialized,
        CreatedUI,
        Loaded,
        Initialized,
        Opening,
        Opened,
        Closing,
        Closed,
        Cached,
        Destroying,
        Destroyed,
    }
}
