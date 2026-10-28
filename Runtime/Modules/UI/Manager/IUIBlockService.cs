namespace AlicizaX.UI.Runtime
{
    /// <summary>全屏输入遮挡层。</summary>
    public interface IUIBlockService
    {
        /// <summary>显示输入遮挡层，并在指定时长后自动解除。</summary>
        void SetUIBlock(float timeDuration);

        /// <summary>强制解除输入遮挡状态。</summary>
        void ForceExitBlock();
    }
}
