using AlicizaX.Timer.Runtime;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/UI")]
    [UnityEngine.Scripting.Preserve]
    [DefaultExecutionOrder(-500)]
    public sealed partial class UIComponent : MonoBehaviour
    {
        [SerializeField] private GameObject uiRoot = null;
        [SerializeField] private bool _isOrthographic = true;
        [SerializeField] private float _nearClipPlane = 10f;
        [SerializeField] private float _farClipPlane = 1000f;

        /// <summary>窗口根节点所在的层，UI 相机按它裁剪。</summary>
        public const int UIShowLayer = 5;

        private void Awake()
        {
            Transform root = Instantiate(uiRoot, Vector3.zero, Quaternion.identity).transform;
            root.name = "------UI Root------";
            var service = new UIService(root, AppServices.Require<ITimerService>());
            ApplyCameraSettings(service.UICamera);
            AppServices.Register<IUIService>(service);
            DontDestroyOnLoad(root);
        }

        /// <summary>透视相机用面板上配置的裁剪面；正交相机沿用预制体上的设置。</summary>
        private void ApplyCameraSettings(Camera camera)
        {
            camera.orthographic = _isOrthographic;
            if (_isOrthographic) return;
            camera.nearClipPlane = _nearClipPlane;
            camera.farClipPlane = _farClipPlane;
        }
    }
}
