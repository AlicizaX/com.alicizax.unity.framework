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

        [Tooltip("池空闲多少帧后允许目标空闲缓存降为0。@60fps: 7200帧≈2分钟")]
        [SerializeField]
        private int m_ZeroFreeReserveStartFrames = 7200;

        [Tooltip("池空闲多少帧后，若已完全空闲则自动释放native metadata。@60fps: 18000帧≈5分钟")]
        [SerializeField]
        private int m_AutoTrimNativeMetadataFrames = 18000;

        [Header("Capacity Settings")]
        [Tooltip("默认空闲缓存软上限。新池会使用该值，运行时修改会同步到已创建池。")]
        [SerializeField]
        private int m_SoftFreeReserveLimit = 128;

        [Tooltip("默认空闲缓存硬上限。释放对象时超过该值会直接驱逐。")]
        [SerializeField]
        private int m_HardFreeReserveLimit = 512;

        [SerializeField]
        private MemoryPoolPhase m_Phase = MemoryPoolPhase.Gameplay;

        private void OnValidate()
        {
            NormalizeSettings();
        }

        private void Awake()
        {
            NormalizeSettings();
            MemoryPoolRegistry.InitializeMainThread();
            MemoryPool.ShortDecayStartFrames = m_ShortDecayStartFrames;
            MemoryPool.LongDecayStartFrames = m_LongDecayStartFrames;
            MemoryPool.UnscheduleIdleFrames = m_UnscheduleIdleFrames;
            MemoryPool.ZeroFreeReserveStartFrames = m_ZeroFreeReserveStartFrames;
            MemoryPool.AutoTrimNativeMetadataFrames = m_AutoTrimNativeMetadataFrames;
            MemoryPool.SetDefaultCapacity(m_SoftFreeReserveLimit, m_HardFreeReserveLimit);
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

        private void NormalizeSettings()
        {
            m_ShortDecayStartFrames = Mathf.Max(0, m_ShortDecayStartFrames);
            m_LongDecayStartFrames = Mathf.Max(m_ShortDecayStartFrames, m_LongDecayStartFrames);
            m_ZeroFreeReserveStartFrames = Mathf.Max(m_LongDecayStartFrames, m_ZeroFreeReserveStartFrames);
            m_UnscheduleIdleFrames = Mathf.Max(m_ZeroFreeReserveStartFrames, m_UnscheduleIdleFrames);
            m_AutoTrimNativeMetadataFrames = m_AutoTrimNativeMetadataFrames < 0
                ? -1
                : Mathf.Max(m_ZeroFreeReserveStartFrames, m_AutoTrimNativeMetadataFrames);
            m_SoftFreeReserveLimit = Mathf.Max(MemoryPool.MinimumFreeReserveLimit, m_SoftFreeReserveLimit);
            m_HardFreeReserveLimit = Mathf.Max(m_SoftFreeReserveLimit, m_HardFreeReserveLimit);
        }

    }
}
