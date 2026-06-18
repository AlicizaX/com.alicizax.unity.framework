using AlicizaX;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

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
        private const string CanvasScalerMissingMessage = "Not found CanvasScaler !";

        private IUIService _uiService;

        public const int UIHideLayer = 2; // Ignore Raycast
        public const int UIShowLayer = 5; // UI


        private void Awake()
        {
            if (uiRoot == null)
            {
                Log.Error("UIRoot Prefab is invalid.");
                return;
            }

            GameObject obj = Instantiate(uiRoot, Vector3.zero, Quaternion.identity);
            obj.name = "------UI Root------";
            Transform instanceRoot = obj.transform;
            Object.DontDestroyOnLoad(instanceRoot);
            _uiService = AppServices.App.Register<IUIService>(new UIService());
            _uiService.Initialize(instanceRoot, _isOrthographic);
        }
    }
}
