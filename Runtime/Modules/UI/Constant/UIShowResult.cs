namespace AlicizaX.UI.Runtime
{
    public enum UIShowResultState : byte
    {
        Failed,
        Opened,
        OcclusionAccepted,
        Cancelled,
    }

    public readonly struct UIShowResult
    {
        public readonly UIBase View;
        public readonly UIShowResultState State;

        public UIShowResult(UIBase view, UIShowResultState state)
        {
            View = view;
            State = state;
        }

        public bool HasView => View != null;
        public bool IsOpened => State == UIShowResultState.Opened;
        public bool IsAccepted => State == UIShowResultState.Opened || State == UIShowResultState.OcclusionAccepted;

        public static UIShowResult Failed => new(null, UIShowResultState.Failed);
        public static UIShowResult Cancelled => new(null, UIShowResultState.Cancelled);
    }

    public readonly struct UIShowResult<T> where T : UIBase
    {
        public readonly T View;
        public readonly UIShowResultState State;

        public UIShowResult(T view, UIShowResultState state)
        {
            View = view;
            State = state;
        }

        public bool HasView => View != null;
        public bool IsOpened => State == UIShowResultState.Opened;
        public bool IsAccepted => State == UIShowResultState.Opened || State == UIShowResultState.OcclusionAccepted;
    }
}
