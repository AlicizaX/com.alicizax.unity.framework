using AlicizaX.Timer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private GameObject m_LayerBlock;
        private ulong m_LastCountDownHandle;
        private TimerHandlerNoArgs _onBlockCountDown;
        private const int UI_BLOCK_SORTING_ORDER = short.MaxValue;

        private void InitUIBlock()
        {
            m_LayerBlock = new GameObject("LayerBlock");
            RectTransform rect = m_LayerBlock.AddComponent<RectTransform>();
            Canvas canvas = m_LayerBlock.AddComponent<Canvas>();
            canvas.renderMode = UICanvas.renderMode;
            canvas.worldCamera = UICamera;
            canvas.planeDistance = UICanvas.planeDistance;
            canvas.overrideSorting = true;
            canvas.sortingOrder = UI_BLOCK_SORTING_ORDER;
            m_LayerBlock.AddComponent<GraphicRaycaster>();
            m_LayerBlock.AddComponent<UIBlock>();
            rect.SetParent(UICanvasRoot);
            rect.SetAsLastSibling();
            rect.ResetToFullScreen();
            SetLayerBlockOption(false);
        }

        /// <summary>
        /// 设置 UI 阻挡层，并在指定时长后自动解除。
        /// </summary>
        /// <param name="timeDuration">阻挡持续时间，单位为秒</param>
        public void SetUIBlock(float timeDuration)
        {
            ITimerService timerService = GetTimerService();
            if (m_LastCountDownHandle != 0UL)
            {
                timerService.RemoveTimer(m_LastCountDownHandle);
            }

            SetLayerBlockOption(true);
            _onBlockCountDown ??= OnBlockCountDown;
            m_LastCountDownHandle = timerService.AddTimer(_onBlockCountDown, timeDuration);
            if (m_LastCountDownHandle == 0UL)
            {
                SetLayerBlockOption(false);
            }
        }

        /// <summary>
        /// 强制退出 UI 阻挡状态。
        /// </summary>
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
