using UnityEngine;

namespace AlicizaX
{
    public enum MemoryPoolPhase
    {
        Boot,
        Loading,
        Gameplay,
        Background,
        LowMemory
    }

    /// <summary>
    /// 内存池模块。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class MemoryPoolSetting : MonoBehaviour
    {
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

        [SerializeField]
        private MemoryPoolPhase m_Phase = MemoryPoolPhase.Gameplay;

        private void Awake()
        {
            MemoryPool.ShortDecayStartFrames = m_ShortDecayStartFrames;
            MemoryPool.LongDecayStartFrames = m_LongDecayStartFrames;
            MemoryPool.UnscheduleIdleFrames = m_UnscheduleIdleFrames;
            MemoryPoolRegistry.Phase = m_Phase;
        }

        private void Update()
        {
            MemoryPoolRegistry.TickAll(Time.frameCount);
        }

        private void OnDestroy()
        {
            MemoryPoolRegistry.ClearAllNativeMetadata();
        }

    }
}
