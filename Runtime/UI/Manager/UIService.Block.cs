using AlicizaX.Timer.Runtime;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private GameObject m_LayerBlock;
        private ulong m_LastCountDownHandle;

        private void InitUIBlock()
        {
            m_LayerBlock = new GameObject("LayerBlock");
            RectTransform rect = m_LayerBlock.AddComponent<RectTransform>();
            m_LayerBlock.AddComponent<CanvasRenderer>();
            m_LayerBlock.AddComponent<UIBlock>();
            rect.SetParent(UICanvasRoot);
            rect.SetAsLastSibling();
            rect.ResetToFullScreen();
            SetLayerBlockOption(false);
        }

        public void SetUIBlock(float timeDuration)
        {
            ITimerService timerService = GetTimerService();
            if (m_LastCountDownHandle != 0UL)
            {
                timerService.RemoveTimer(m_LastCountDownHandle);
            }

            SetLayerBlockOption(true);
            m_LastCountDownHandle = timerService.AddTimer(OnBlockCountDown, timeDuration);
        }

        public void ForceExitBlock()
        {
            ITimerService timerService = GetTimerService();
            if (m_LastCountDownHandle != 0UL)
            {
                timerService.RemoveTimer(m_LastCountDownHandle);
            }

            RecoverLayerOptionAll();
        }

        private void OnBlockCountDown()
        {
            RecoverLayerOptionAll();
        }

        private void SetLayerBlockOption(bool value)
        {
            m_LayerBlock.SetActive(value);
        }

        public void RecoverLayerOptionAll()
        {
            SetLayerBlockOption(false);
            m_LastCountDownHandle = 0UL;
        }
    }
}
