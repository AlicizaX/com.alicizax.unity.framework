using Cysharp.Text;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        public Camera UICamera { get; }
        public Canvas UICanvas { get; }
        public Transform UICanvasRoot { get; }
        public Transform UIRoot { get; }

        private const int UI_ROOT_OFFSET = 1000;

        internal const int LayerCount = (int)UILayer.Top + 1;
        internal const int WindowsPerLayerLimit = 200;
        internal const int WINDOW_DEEP = 20;
        internal const int LAYER_DEEP = WindowsPerLayerLimit * WINDOW_DEEP;

        private readonly RectTransform[] _layers = new RectTransform[LayerCount];

        public RectTransform GetLayer(UILayer layer) => _layers[(int)layer];

        public void SetUIBlock(float timeDuration) => _blocker.Show(timeDuration);

        public void ForceExitBlock() => _blocker.Hide();

        private RectTransform CreateLayer(int layer)
        {
            var rect = new GameObject(ZString.Format("Layer{0}-{1}", layer, (UILayer)layer)).AddComponent<RectTransform>();
            rect.SetParent(UICanvasRoot);
            rect.ResetToFullScreen();
            return rect;
        }
    }
}
