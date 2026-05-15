using UnityEngine;

namespace AlicizaX
{
    /// <summary>
    /// 内存强制检查类型。
    /// </summary>
    public enum MemoryStrictCheckType : byte
    {
        /// <summary>
        /// 总是启用。
        /// </summary>
        AlwaysEnable = 0,

        /// <summary>
        /// 仅在开发模式时启用。
        /// </summary>
        OnlyEnableWhenDevelopment,

        /// <summary>
        /// 仅在编辑器中启用。
        /// </summary>
        OnlyEnableInEditor,

        /// <summary>
        /// 总是禁用。
        /// </summary>
        AlwaysDisable,
    }

    /// <summary>
    /// 内存池模块。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class MemoryPoolSetting : MonoBehaviour
    {
        [SerializeField]
        private MemoryStrictCheckType m_EnableStrictCheck = MemoryStrictCheckType.OnlyEnableWhenDevelopment;

        [Header("Idle Trim Settings")]
        [Tooltip("池空闲多少帧后开始缓慢缩容（每tick释放4个）。@60fps: 1800帧≈30秒")]
        [SerializeField]
        private int m_ShortDecayStartFrames = 1800;

        [Tooltip("池空闲多少帧后加速缩容（每tick释放16个）。@60fps: 7200帧≈2分钟")]
        [SerializeField]
        private int m_LongDecayStartFrames = 7200;

        [Tooltip("池空闲多少帧后停止调度Tick。@60fps: 18000帧≈5分钟")]
        [SerializeField]
        private int m_UnscheduleIdleFrames = 18000;

        /// <summary>
        /// 获取或设置是否开启强制检查。
        /// </summary>
        public bool EnableStrictCheck
        {
            get => MemoryPool.EnableStrictCheck;
            set
            {
                MemoryPool.EnableStrictCheck = value;
                if (value)
                {
                    Log.Info("Strict checking is enabled for the Memory Pool. It will drastically affect the performance.");
                }
            }
        }

        private void Awake()
        {
            MemoryPool.ShortDecayStartFrames = m_ShortDecayStartFrames;
            MemoryPool.LongDecayStartFrames = m_LongDecayStartFrames;
            MemoryPool.UnscheduleIdleFrames = m_UnscheduleIdleFrames;

            switch (m_EnableStrictCheck)
            {
                case MemoryStrictCheckType.AlwaysEnable:
                    EnableStrictCheck = true;
                    break;

                case MemoryStrictCheckType.OnlyEnableWhenDevelopment:
                    EnableStrictCheck = Debug.isDebugBuild;
                    break;

                case MemoryStrictCheckType.OnlyEnableInEditor:
                    EnableStrictCheck = Application.isEditor;
                    break;

                default:
                    EnableStrictCheck = false;
                    break;
            }
        }

        private void Update()
        {
            MemoryPoolRegistry.TickAll(Time.frameCount);
        }

    }
}
