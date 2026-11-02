using UnityEngine;

namespace AlicizaX
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class MemoryPoolSetting : MonoBehaviour
    {
        [Header("Free Object Budget")]
        [Tooltip("长期保留的空闲对象数。")]
        [SerializeField]
        private int m_RetainFreeCount;

        [Tooltip("空闲对象软上限。超过近期峰值需求的空闲对象会逐帧淘汰。")]
        [SerializeField]
        private int m_SoftFreeCount = 128;

        [Tooltip("空闲对象硬上限。归还时空闲数已达该值则直接丢弃。")]
        [SerializeField]
        private int m_HardFreeCount = 512;

        [Tooltip("每帧每个池最多淘汰的空闲对象数。")]
        [SerializeField]
        private int m_TrimPerTick = 16;

        [Tooltip("峰值需求统计窗口，单位秒，不受 timeScale 影响。")]
        [SerializeField]
        private float m_PeakWindowSeconds = 30f;

        private void OnValidate()
        {
            m_RetainFreeCount = Mathf.Max(0, m_RetainFreeCount);
            m_SoftFreeCount = Mathf.Max(m_RetainFreeCount, m_SoftFreeCount);
            m_HardFreeCount = Mathf.Max(m_SoftFreeCount, m_HardFreeCount);
            m_TrimPerTick = Mathf.Max(1, m_TrimPerTick);
            m_PeakWindowSeconds = Mathf.Max(0.1f, m_PeakWindowSeconds);
        }

        private void Awake()
        {
            MemoryPool.SetBudget(new MemoryPoolBudget(m_RetainFreeCount, m_SoftFreeCount, m_HardFreeCount, m_TrimPerTick, m_PeakWindowSeconds));
            Application.lowMemory += OnLowMemory;
        }

        private void Update()
        {
            MemoryPoolRegistry.TickAll(Time.unscaledTime);
        }

        private void OnDestroy()
        {
            Application.lowMemory -= OnLowMemory;
            MemoryPool.TrimAll();
        }

        private static void OnLowMemory()
        {
            MemoryPoolRegistry.TrimAll(MemoryPoolRegistry.Budget.RetainFreeCount);
        }
    }
}
