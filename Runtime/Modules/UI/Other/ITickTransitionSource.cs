namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 由 UI 服务逐帧推进的转场源。实现该接口后，转场不再每帧调度 PlayerLoop，
    /// 而是由 <see cref="UIService"/> 的 Tick 驱动；未实现时保持原有异步 <see cref="IUITransitionSource.Play"/> 路径。
    /// 生命周期：<see cref="Begin"/> 建立一次播放，<see cref="Advance"/> 每帧推进，直到返回 true 或 Holder 取消播放。
    /// </summary>
    public interface ITickTransitionSource : IUITransitionSource
    {
        /// <summary>建立转场起止状态并应用起始帧；返回 false 表示时长为 0、已直接应用终态。</summary>
        bool Begin(bool open);

        /// <summary>推进一帧；返回 true 表示已到达终态并已应用终态。</summary>
        bool Advance(float deltaTime);
    }
}
