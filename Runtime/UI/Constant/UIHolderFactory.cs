using System;
using AlicizaX.Resource.Runtime;
using AlicizaX;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Runtime
{
    public static class UIHolderFactory
    {
        private static IResourceService ResourceService => AppServices.App.Require<IResourceService>();

        public static async UniTask<T> CreateUIHolderAsync<T>(Transform parent) where T : UIHolderObjectBase
        {
            if (UIResRegistry.TryGet(typeof(T).TypeHandle, out UIResRegistry.UIResInfo resInfo))
            {
                GameObject obj = await LoadUIResourcesAsync(resInfo, parent);

                return obj.GetComponent<T>();
            }

            return null;
        }

        public static T CreateUIHolderSync<T>(Transform parent) where T : UIHolderObjectBase
        {
            if (UIResRegistry.TryGet(typeof(T).TypeHandle, out UIResRegistry.UIResInfo resInfo))
            {
                GameObject obj = LoadUIResourcesSync(resInfo, parent);

                return obj.GetComponent<T>();
            }

            return null;
        }


        internal static async UniTask<GameObject> LoadUIResourcesAsync(UIResRegistry.UIResInfo resInfo, Transform parent)
        {
            return resInfo.LoadType == EUIResLoadType.AssetBundle
                ? await ResourceService.LoadGameObjectAsync(resInfo.Location, parent)
                : await LoadResourceWithFallbackAsync(resInfo.Location, parent);
        }

        internal static GameObject LoadUIResourcesSync(UIResRegistry.UIResInfo resInfo, Transform parent)
        {
            return resInfo.LoadType == EUIResLoadType.AssetBundle
                ? ResourceService.LoadGameObject(resInfo.Location, parent)
                : LoadResourceWithFallbackSync(resInfo.Location, parent);
        }


        internal static async UniTask CreateUIResourceAsync(UIMetadata meta, Transform parent, UIBase owner = null)
        {
            if (meta.State != UIState.CreatedUI) return;
            int operationVersion = meta.OperationVersion;
            GameObject obj = await LoadUIResourcesAsync(meta.ResInfo, parent);
            if (operationVersion != meta.OperationVersion || meta.ExistingCancellationToken.IsCancellationRequested || meta.View == null || meta.State != UIState.CreatedUI)
            {
                if (obj != null)
                {
                    ResourceOwner.ReleaseBindingsInHierarchy(obj);
                    DestroyObject(obj);
                }

                return;
            }

            if (!ValidateAndBind(meta, obj, owner))
            {
                return;
            }
        }

        internal static void CreateUIResourceSync(UIMetadata meta, Transform parent, UIBase owner = null)
        {
            if (meta.State != UIState.CreatedUI) return;
            GameObject obj = LoadUIResourcesSync(meta.ResInfo, parent);
            ValidateAndBind(meta, obj, owner);
        }

        private static async UniTask<GameObject> InstantiateResourceAsync(string location, Transform parent)
        {
            GameObject prefab = (GameObject)await Resources.LoadAsync<GameObject>(location);
            return Object.Instantiate(prefab, parent);
        }

        private static GameObject InstantiateResourceSync(string location, Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(location);
            return Object.Instantiate(prefab, parent);
        }

        private static async UniTask<GameObject> LoadResourceWithFallbackAsync(string location, Transform parent)
        {
            return await InstantiateResourceAsync(location, parent);
        }

        private static GameObject LoadResourceWithFallbackSync(string location, Transform parent)
        {
            return InstantiateResourceSync(location, parent);
        }

        private static bool ValidateAndBind(UIMetadata meta, GameObject holderObject, UIBase owner)
        {
            if (!holderObject)
            {
                Log.Error(ZString.Format("UI resource load failed: {0}", meta.ResInfo.Location));
                return false;
            }

            if (meta.View == null)
            {
                Log.Error(ZString.Format("UI logic missing while binding holder: {0}", holderObject.name));
                ResourceOwner.ReleaseBindingsInHierarchy(holderObject);
                DestroyObject(holderObject);
                return false;
            }

            var holder = (UIHolderObjectBase)holderObject.GetComponent(meta.View.UIHolderType);
            if (holder == null)
            {
                Log.Error(ZString.Format("UI resource {0} missing holder component {1}", holderObject.name, meta.View.UIHolderType.FullName));
                ResourceOwner.ReleaseBindingsInHierarchy(holderObject);
                DestroyObject(holderObject);
                return false;
            }

            meta.View.BindUIHolder(holder, owner);
            return true;
        }

        private static void DestroyObject(GameObject obj)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(obj);
            }
            else
            {
                Object.DestroyImmediate(obj);
            }
        }
    }
}
