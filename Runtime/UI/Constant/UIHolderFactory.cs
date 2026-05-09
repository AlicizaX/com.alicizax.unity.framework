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
            GameObject obj = await LoadUIResourcesAsync(meta.ResInfo, parent);
            if (meta.CancellationToken.IsCancellationRequested || meta.View == null || meta.State != UIState.CreatedUI)
            {
                if (obj != null)
                {
                    Object.Destroy(obj);
                }

                return;
            }

            ValidateAndBind(meta, obj, owner);
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

        private static void ValidateAndBind(UIMetadata meta, GameObject holderObject, UIBase owner)
        {
            if (!holderObject) throw new NullReferenceException(ZString.Format("UI resource load failed: {0}", meta.ResInfo.Location));

            var holder = (UIHolderObjectBase)holderObject.GetComponent(meta.View.UIHolderType);

            if (holder == null)
            {
                throw new InvalidCastException(ZString.Format("UI resource {0} missing holder component {1}", holderObject.name, meta.View.UIHolderType.FullName));
            }

            meta.View?.BindUIHolder(holder, owner);
        }
    }
}
