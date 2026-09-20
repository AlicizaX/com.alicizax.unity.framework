namespace AlicizaX.UI.Runtime
{
    public enum UIState : byte
    {
        Uninitialized,
        CreatedUI,
        Loaded,
        Initializing,
        Initialized,
        Opening,
        Opened,
        Closing,
        Closed,
        Destroying,
        Destroyed,
    }
}
