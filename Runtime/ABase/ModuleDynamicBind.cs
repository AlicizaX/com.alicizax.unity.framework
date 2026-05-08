using System;
using AlicizaX.Debugger.Runtime;
using AlicizaX.Localization.Runtime;
using AlicizaX.Resource.Runtime;
using UnityEngine;
using UnityEngine.Serialization;

namespace AlicizaX
{
    public class ModuleDynamicBind : MonoBehaviour
    {
        [SerializeField] private ResourceComponent resourceComponent;
        [SerializeField] private DebuggerComponent debuggerComponent;
        [SerializeField] private LocalizationComponent localizationComponent;
        private ModuleDynamicBindInfo _dynamicBindInfo;

        private void OnValidate()
        {
            resourceComponent = GetComponentInChildren<ResourceComponent>();
            debuggerComponent = GetComponentInChildren<DebuggerComponent>();
            localizationComponent = GetComponentInChildren<LocalizationComponent>();
        }

        private void Awake()
        {
            if (Application.isEditor) return;
            TextAsset text = null;
            if (AppServices.TryGet<IResourceService>(out var resourceService))
            {
                text = resourceService.LoadAsset<TextAsset>("ModuleDynamicBindInfo");
            }

            if (text == null)
            {
                text = Resources.Load<TextAsset>("ModuleDynamicBindInfo");
            }

            if (text == null)
            {
                Log.Warning("ModuleDynamicBindInfo not found.");
                return;
            }

            _dynamicBindInfo = Utility.Json.ToObject<ModuleDynamicBindInfo>(text.text);

            if (resourceComponent != null)
            {
                resourceComponent.SetPlayMode(_dynamicBindInfo.ResMode);
                resourceComponent.SetDecryptionServices(_dynamicBindInfo.DecryptionServices);
            }

            if (debuggerComponent != null)
            {
                debuggerComponent.SetActiveMode(_dynamicBindInfo.DebuggerActiveWindowType);
            }

            if (localizationComponent != null)
            {
                localizationComponent.SetLanguage(_dynamicBindInfo.Language);
            }
        }
    }

    public struct ModuleDynamicBindInfo
    {
        public DebuggerActiveWindowType DebuggerActiveWindowType;
        public int ResMode;
        public string Language;
        public string DecryptionServices;
    }
}
