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


        #region 设置安全区域

        /// <summary>
        /// 应用屏幕安全区域，适配刘海屏等显示区域。
        /// </summary>
        /// <param name="safeRect">安全区域。</param>
        public void ApplyScreenSafeRect(Rect safeRect)
        {
            CanvasScaler scaler = _uiService.UICanvasRoot.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                Log.Error(CanvasScalerMissingMessage);
                return;
            }

            // Convert safe area rectangle from absolute pixels to UGUI coordinates
            float rateX = scaler.referenceResolution.x / Screen.width;
            float rateY = scaler.referenceResolution.y / Screen.height;
            float posX = (int)(safeRect.position.x * rateX);
            float posY = (int)(safeRect.position.y * rateY);
            float width = (int)(safeRect.size.x * rateX);
            float height = (int)(safeRect.size.y * rateY);

            float offsetMaxX = scaler.referenceResolution.x - width - posX;
            float offsetMaxY = scaler.referenceResolution.y - height - posY;

            // 注意：安全区域坐标系原点为左下角。
            var rectTrans = _uiService.UICanvasRoot.transform as RectTransform;
            if (rectTrans != null)
            {
                rectTrans.offsetMin = new Vector2(posX, posY); // 锚点状态下的屏幕左下角偏移量。
                rectTrans.offsetMax = new Vector2(-offsetMaxX, -offsetMaxY); // 锚点状态下的屏幕右上角偏移量。
            }
        }

        public void SimulateIPhoneXNotchScreen()
        {
            Rect rect;
            if (Screen.height > Screen.width)
            {
                // 竖屏
                float deviceWidth = 1125;
                float deviceHeight = 2436;
                rect = new Rect(0f / deviceWidth, 102f / deviceHeight, 1125f / deviceWidth, 2202f / deviceHeight);
            }
            else
            {
                // 横屏
                float deviceWidth = 2436;
                float deviceHeight = 1125;
                rect = new Rect(132f / deviceWidth, 63f / deviceHeight, 2172f / deviceWidth, 1062f / deviceHeight);
            }

            Rect safeArea = new Rect(Screen.width * rect.x, Screen.height * rect.y, Screen.width * rect.width, Screen.height * rect.height);
            ApplyScreenSafeRect(safeArea);
        }

        #endregion
    }
}
