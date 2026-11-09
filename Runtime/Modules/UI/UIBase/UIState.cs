namespace AlicizaX.UI.Runtime
{
    public enum UIState : byte
    {
        /// <summary>资源加载中，尚未绑定 Holder。</summary>
        Loading,
        /// <summary>已初始化，不可见。</summary>
        Closed,
        /// <summary>OnOpen 已执行，开场转场播放中。</summary>
        Opening,
        Opened,
        /// <summary>OnClose 已执行，关闭转场播放中。</summary>
        Closing,
        Destroyed,
    }
}
