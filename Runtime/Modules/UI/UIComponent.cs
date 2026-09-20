using System;
using AlicizaX;
using UnityEngine;
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

        public const int UIHideLayer = 2;
        public const int UIShowLayer = 5;


        private void Awake()
        {
            if (uiRoot == null)
            {
                Log.Error("[UI] UIRoot Prefab is invalid.");
                return;
            }

            GameObject obj = Instantiate(uiRoot, Vector3.zero, Quaternion.identity);
            obj.name = "------UI Root------";
            Transform instanceRoot = obj.transform;
            var service = new UIService();
            service.Initialize(instanceRoot, _isOrthographic);
            if (service.UIRoot == null)
            {
                Destroy(obj);
                return;
            }
            AppServices.App.Register<IUIService>(service);
            Object.DontDestroyOnLoad(instanceRoot);
        }
    }
}
