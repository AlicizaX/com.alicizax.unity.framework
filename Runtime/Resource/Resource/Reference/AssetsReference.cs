using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.Resource.Runtime
{
    [Serializable]
    public struct AssetsRefInfo
    {
        public int instanceId;

        public Object refAsset;

        public AssetsRefInfo(Object refAsset)
        {
            this.refAsset = refAsset;
            instanceId = this.refAsset.GetInstanceID();
        }
    }

    [DisallowMultipleComponent]
    public sealed class AssetsReference : MonoBehaviour
    {
        [SerializeField]
        private GameObject sourceGameObject;

        [SerializeField]
        private AssetsRefInfo[] refAssetInfoArray;

        [SerializeField]
        private int refAssetInfoCount;

        private static IResourceService _resourceService;

        private bool TryEnsureResourceModule()
        {
            if (_resourceService != null)
            {
                return true;
            }

            _resourceService = AppServices.Require<IResourceService>();
            return _resourceService != null;
        }

        private void ReleaseSourceReference()
        {
            if (sourceGameObject != null)
            {
                _resourceService.UnloadAsset(sourceGameObject);
                sourceGameObject = null;
            }
        }

        private bool ContainsRefAsset(int instanceId)
        {
            for (int i = 0; i < refAssetInfoCount; i++)
            {
                AssetsRefInfo refInfo = refAssetInfoArray[i];
                int refInstanceId = refInfo.instanceId != 0 ? refInfo.instanceId : refInfo.refAsset != null ? refInfo.refAsset.GetInstanceID() : 0;
                if (refInstanceId == instanceId)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDestroy()
        {
            if (!TryEnsureResourceModule())
            {
                sourceGameObject = null;
                ClearRefAssetInfoArray();
                return;
            }

            ReleaseSourceReference();
            ReleaseRefAssetInfoArray();
        }

        private void ReleaseRefAssetInfoArray()
        {
            for (int i = 0; i < refAssetInfoCount; i++)
            {
                _resourceService.UnloadAsset(refAssetInfoArray[i].refAsset);
            }

            ClearRefAssetInfoArray();
        }

        private void ClearRefAssetInfoArray()
        {
            for (int i = 0; i < refAssetInfoCount; i++)
            {
                refAssetInfoArray[i] = default;
            }

            refAssetInfoCount = 0;
        }

        private void EnsureRefAssetCapacity(int capacity)
        {
            if (refAssetInfoArray != null && refAssetInfoArray.Length >= capacity)
            {
                return;
            }

            int newCapacity = refAssetInfoArray == null || refAssetInfoArray.Length == 0 ? 4 : refAssetInfoArray.Length << 1;
            while (newCapacity < capacity)
            {
                newCapacity <<= 1;
            }

            Array.Resize(ref refAssetInfoArray, newCapacity);
        }

        public AssetsReference Ref(GameObject source, IResourceService resourceService = null)
        {
            if (source == null)
            {
                throw new GameFrameworkException("Source gameObject is null.");
            }

            if (source.scene.name != null)
            {
                throw new GameFrameworkException("Source gameObject is in scene.");
            }

            if (resourceService != null)
            {
                _resourceService = resourceService;
            }

            if (sourceGameObject != null && sourceGameObject != source && TryEnsureResourceModule())
            {
                ReleaseSourceReference();
            }

            sourceGameObject = source;
            return this;
        }

        public AssetsReference Ref<T>(T source, IResourceService resourceService = null) where T : Object
        {
            if (source == null)
            {
                throw new GameFrameworkException("Source gameObject is null.");
            }

            if (resourceService != null)
            {
                _resourceService = resourceService;
            }

            int instanceId = source.GetInstanceID();
            if (ContainsRefAsset(instanceId))
            {
                return this;
            }

            EnsureRefAssetCapacity(refAssetInfoCount + 1);
            refAssetInfoArray[refAssetInfoCount++] = new AssetsRefInfo(source);
            return this;
        }

        internal static AssetsReference Instantiate(GameObject source, Transform parent = null, IResourceService resourceService = null)
        {
            if (source == null)
            {
                throw new GameFrameworkException("Source gameObject is null.");
            }

            if (source.scene.name != null)
            {
                throw new GameFrameworkException("Source gameObject is in scene.");
            }

            GameObject instance = Object.Instantiate(source, parent);
            return instance.AddComponent<AssetsReference>().Ref(source, resourceService);
        }

        public static AssetsReference Ref(GameObject source, GameObject instance, IResourceService resourceService = null)
        {
            if (source == null)
            {
                throw new GameFrameworkException("Source gameObject is null.");
            }

            if (source.scene.name != null)
            {
                throw new GameFrameworkException("Source gameObject is in scene.");
            }

            var comp = instance.GetComponent<AssetsReference>();
            return comp ? comp.Ref(source, resourceService) : instance.AddComponent<AssetsReference>().Ref(source, resourceService);
        }

        public static AssetsReference Ref<T>(T source, GameObject instance, IResourceService resourceService = null) where T : Object
        {
            if (source == null)
            {
                throw new GameFrameworkException("Source gameObject is null.");
            }

            var comp = instance.GetComponent<AssetsReference>();
            return comp ? comp.Ref(source, resourceService) : instance.AddComponent<AssetsReference>().Ref(source, resourceService);
        }
    }
}