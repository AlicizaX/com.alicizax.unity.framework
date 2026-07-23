namespace AlicizaX.UI.Runtime
{

    internal static class UIStateMachine
    {
        private static readonly ushort[] ValidTransitionMasks =
        {
            Mask(UIState.CreatedUI),
            Mask(UIState.Loaded, UIState.Destroying),
            Mask(UIState.Initialized, UIState.Destroying),
            Mask(UIState.Opening, UIState.Destroying),
            Mask(UIState.Opened, UIState.Closing, UIState.Destroying),
            Mask(UIState.Closing, UIState.Destroying),
            Mask(UIState.Opening, UIState.Closed, UIState.Destroying),
            Mask(UIState.Opening, UIState.Destroying),
            Mask(UIState.Destroyed),
            0,
        };

        public static bool IsValidTransition(UIState from, UIState to)
        {
            int fromIndex = (int)from;
            return (uint)fromIndex < (uint)ValidTransitionMasks.Length && (ValidTransitionMasks[fromIndex] & Mask(to)) != 0;
        }

        public static bool ValidateTransition(string uiName, UIState from, UIState to)
        {
            if (IsValidTransition(from, to))
                return true;

            Log.Error("[UI] Invalid state transition for {0}: {1} -> {2}", uiName, from, to);
            return false;
        }

        public static bool IsDisplayActive(UIState state)
        {
            return state == UIState.Opening || state == UIState.Opened || state == UIState.Closing;
        }

        private static ushort Mask(UIState state)
        {
            return (ushort)(1 << (int)state);
        }

        private static ushort Mask(UIState stateA, UIState stateB)
        {
            return (ushort)(Mask(stateA) | Mask(stateB));
        }

        private static ushort Mask(UIState stateA, UIState stateB, UIState stateC)
        {
            return (ushort)(Mask(stateA) | Mask(stateB) | Mask(stateC));
        }
    }
}
