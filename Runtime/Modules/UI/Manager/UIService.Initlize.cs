using System;
using System.Collections.Generic;
using Cysharp.Text;
using AlicizaX.Timer.Runtime;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        public Camera UICamera { get; private set; }
        public Canvas UICanvas;
        public Transform UICanvasRoot { get; private set; }
        public Transform UIRoot;

        private const int UI_ROOT_OFFSET = 1000;

        internal const int WINDOW_DEEP = 20;
        internal const int LAYER_DEEP = 200 * WINDOW_DEEP;

        private readonly RectTransform[] m_AllWindowLayer = new RectTransform[(int)UILayer.All];

        private RectTransform UICacheLayer;

        private bool _initialized;

        public void Initialize(Transform root, bool isOrthographic)
        {
            if (_initialized || _shuttingDown)
            {
                Log.Error("[UI] UIService is already initialized or stopped.");
                return;
            }
            if (root == null)
            {
                Log.Error("[UI] UI root is missing.");
                return;
            }
            Canvas canvas = root.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
            {
                Log.Error("[UI] UI root must contain a Canvas.");
                return;
            }
            Camera camera = canvas.worldCamera;
            if (camera == null)
            {
                Log.Error("[UI] UI Canvas must have a worldCamera.");
                return;
            }
            if (!AppServices.TryGet(out _timerService))
            {
                Log.Error("[UI] Timer service is unavailable.");
                return;
            }
            UICanvas = canvas;
            UICamera = camera;
            UICanvasRoot = UICanvas.transform;

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
            root.position = new Vector3(UI_ROOT_OFFSET, UI_ROOT_OFFSET, 0);
            UIRoot = root;
            _initialized = true;
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
            _openUI[layer] = new List<UIWindowRecord>(16);
        }


        private RectTransform GetLayerRect(int layer) => m_AllWindowLayer[layer];
    }
}
