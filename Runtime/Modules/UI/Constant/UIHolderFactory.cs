using System;
using System.Threading;
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
                GameObject obj = await LoadUIResourcesAsync(resInfo, parent, CancellationToken.None);
                return GetHolderOrDestroy<T>(obj, resInfo.Location);
            }

            return null;
        }

        public static T CreateUIHolderSync<T>(Transform parent) where T : UIHolderObjectBase
        {
            if (UIResRegistry.TryGet(typeof(T).TypeHandle, out UIResRegistry.UIResInfo resInfo))
            {
                GameObject obj = LoadUIResourcesSync(resInfo, parent);
                return GetHolderOrDestroy<T>(obj, resInfo.Location);
            }

            return null;
        }


        internal static async UniTask<GameObject> LoadUIResourcesAsync(UIResRegistry.UIResInfo resInfo, Transform parent, CancellationToken cancellationToken)
        {
            return resInfo.LoadType == EUIResLoadType.AssetBundle
                ? await ResourceService.LoadGameObjectAsync(resInfo.Location, parent, cancellationToken)
                : await LoadResourceWithFallbackAsync(resInfo.Location, parent, cancellationToken);
        }

        internal static GameObject LoadUIResourcesSync(UIResRegistry.UIResInfo resInfo, Transform parent)
        {
            return resInfo.LoadType == EUIResLoadType.AssetBundle
                ? ResourceService.LoadGameObject(resInfo.Location, parent)
                : LoadResourceWithFallbackSync(resInfo.Location, parent);
        }


        internal static async UniTask CreateUIResourceAsync(UIMetadata meta, Transform parent, CancellationToken cancellationToken, UIBase owner = null)
        {
            if (meta.State != UIState.CreatedUI) return;
            int operationVersion = meta.OperationVersion;
            GameObject obj = await LoadUIResourcesAsync(meta.ResInfo, parent, cancellationToken);
            if (operationVersion != meta.OperationVersion || cancellationToken.IsCancellationRequested || meta.View == null || meta.State != UIState.CreatedUI)
            {
                if (obj != null)
                {
                    DestroyLoadedObject(obj);
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

        private static async UniTask<GameObject> InstantiateResourceAsync(string location, Transform parent, CancellationToken cancellationToken)
        {
            GameObject prefab;
            try
            {
                prefab = (GameObject)await Resources.LoadAsync<GameObject>(location).ToUniTask(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (!prefab || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            if (cancellationToken.IsCancellationRequested)
            {
                DestroyLoadedObject(instance);
                return null;
            }

            return instance;
        }

        private static GameObject InstantiateResourceSync(string location, Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(location);
            if (!prefab)
            {
                return null;
            }

            return Object.Instantiate(prefab, parent);
        }

        private static async UniTask<GameObject> LoadResourceWithFallbackAsync(string location, Transform parent, CancellationToken cancellationToken)
        {
            return await InstantiateResourceAsync(location, parent, cancellationToken);
        }

        private static GameObject LoadResourceWithFallbackSync(string location, Transform parent)
        {
            return InstantiateResourceSync(location, parent);
        }

        private static bool ValidateAndBind(UIMetadata meta, GameObject holderObject, UIBase owner)
        {
            if (!holderObject)
            {
                Log.Error("UI resource load failed: {0}", meta.ResInfo.Location);
                return false;
            }

            if (meta.View == null)
            {
                Log.Error("UI logic missing while binding holder: {0}", holderObject.name);
                DestroyLoadedObject(holderObject);
                return false;
            }

            var holder = (UIHolderObjectBase)holderObject.GetComponent(meta.View.UIHolderType);
            if (holder == null)
            {
                Log.Error("UI resource {0} missing holder component {1}", holderObject.name, meta.View.UIHolderType.FullName);
                DestroyLoadedObject(holderObject);
                return false;
            }

            meta.View.BindUIHolder(holder, owner);
            meta.View.SetDestroyHolderOnDispose(true);
            return true;
        }

        private static T GetHolderOrDestroy<T>(GameObject holderObject, string location) where T : UIHolderObjectBase
        {
            if (!holderObject)
            {
                Log.Error("UI holder resource load failed: {0}", location);
                return null;
            }

            T holder = holderObject.GetComponent<T>();
            if (holder != null)
            {
                return holder;
            }

            Log.Error("UI resource {0} missing holder component {1}", holderObject.name, typeof(T).FullName);
            DestroyLoadedObject(holderObject);
            return null;
        }

        private static void DestroyLoadedObject(GameObject obj)
        {
            if (obj == null)
            {
                return;
            }

            ResourceOwner.ReleaseBindingsInHierarchy(obj);
            DestroyObject(obj);
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
