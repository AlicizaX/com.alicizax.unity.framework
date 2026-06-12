using Cysharp.Text;
using AlicizaX.Timer.Runtime;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        public Camera UICamera { get; set; }
        public Canvas UICanvas;
        public Transform UICanvasRoot { get; set; }
        public Transform UIRoot;

        private const int UI_ROOT_OFFSET = 1000;

        private const int LAYER_DEEP = 2000;
        private const int WINDOW_DEEP = 100;

        private readonly RectTransform[] m_AllWindowLayer = new RectTransform[(int)UILayer.All];

        private RectTransform UICacheLayer;
        private bool _isOrthographic;

        public IUIRouter Router { get; private set; }
        private IUIRouterInternal _routerInternal;

        public void Initialize(Transform root, bool isOrthographic)
        {
            if (root == null)
            {
                Log.Error("[UI] Initialize failed: root is null.");
                return;
            }

            UIRoot = root;
            Object.DontDestroyOnLoad(root.gameObject);

            UIRoot.transform.position = new Vector3(UI_ROOT_OFFSET, UI_ROOT_OFFSET, 0);

            UICanvas = UIRoot.GetComponentInChildren<Canvas>();
            if (UICanvas == null)
            {
                Log.Error("[UI] Initialize failed: Canvas is missing under UI root.");
                UIRoot = null;
                return;
            }

            UICamera = UICanvas.worldCamera;
            if (UICamera == null)
            {
                Log.Error("[UI] Initialize failed: Canvas worldCamera is missing.");
                UICanvas = null;
                UIRoot = null;
                return;
            }

            UICanvasRoot = UICanvas.transform;

            _isOrthographic = isOrthographic;
            UICamera.orthographic = isOrthographic;
            if (!isOrthographic)
            {
                UICamera.nearClipPlane = 10;
                UICamera.farClipPlane = 1000;
            }

            const int len = (int)UILayer.All;
            for (var i = len - 1; i >= 0; i--)
            {
                AddLayer(i);
            }

            AddLayer((int)UILayer.All);
            InitUIBlock();
            _timerService = AppServices.App.Require<ITimerService>();
            UIRouter router = new UIRouter(this);
            Router = router;
            _routerInternal = router;
        }

        public RectTransform GetLayer(UILayer layer)
        {
            if ((uint)layer >= (uint)UILayer.All)
            {
                Log.Error("[UI] Invalid layer: {0}", layer);
                return null;
            }

            return m_AllWindowLayer[(int)layer];
        }

        private void AddLayer(int layer)
        {
            var layerObject = new GameObject(ZString.Format("Layer{0}-{1}", layer, (UILayer)layer));
            var rect = layerObject.AddComponent<RectTransform>();
            rect.SetParent(UICanvasRoot);
            rect.localScale = Vector3.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMax = Vector2.one;
            rect.anchorMin = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localPosition = Vector3.zero;
            if (layer == (int)UILayer.All)
            {
                UICacheLayer = rect;
                return;
            }

            m_AllWindowLayer[layer] = rect;
            _openUI[layer] = new LayerData(16);
        }


        public RectTransform GetLayerRect(int layer)
        {
            if ((uint)layer >= (uint)m_AllWindowLayer.Length)
            {
                Log.Error("[UI] Invalid layer index: {0}", layer);
                return null;
            }

            return m_AllWindowLayer[layer];
        }
    }
}
