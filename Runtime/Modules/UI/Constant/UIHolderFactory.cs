using System;
using System.Threading;
using AlicizaX.Resource.Runtime;
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
            if (!UIResRegistry.TryGet(typeof(T).TypeHandle, out var resource)) return null;
            try
            {
                GameObject obj = await LoadUIResourcesAsync(resource, parent, CancellationToken.None);
                if (TryBindHolder(obj, typeof(T), resource.Location, out UIHolderObjectBase holder)) return (T)holder;
                DestroyLoadedObject(obj);
                return null;
            }
            catch (Exception error)
            {
                Log.Error("[UI] Failed to load {0}: {1}", resource.Location, error);
                return null;
            }
        }

        public static T CreateUIHolderSync<T>(Transform parent) where T : UIHolderObjectBase
        {
            if (!UIResRegistry.TryGet(typeof(T).TypeHandle, out var resource)) return null;
            try
            {
                GameObject obj = LoadUIResourcesSync(resource, parent);
                if (TryBindHolder(obj, typeof(T), resource.Location, out UIHolderObjectBase holder)) return (T)holder;
                DestroyLoadedObject(obj);
                return null;
            }
            catch (Exception error)
            {
                Log.Error("[UI] Failed to load {0}: {1}", resource.Location, error);
                return null;
            }
        }

        internal static async UniTask<GameObject> LoadUIResourcesAsync(UIResRegistry.UIResInfo resource,
            Transform parent, CancellationToken cancellationToken) =>
            resource.LoadType == EUIResLoadType.AssetBundle
                ? await ResourceService.LoadGameObjectAsync(resource.Location, parent, cancellationToken)
                : await InstantiateResourceAsync(resource.Location, parent, cancellationToken);

        internal static GameObject LoadUIResourcesSync(UIResRegistry.UIResInfo resource, Transform parent) =>
            resource.LoadType == EUIResLoadType.AssetBundle
                ? ResourceService.LoadGameObject(resource.Location, parent)
                : InstantiateResourceSync(resource.Location, parent);

        internal static async UniTask<bool> CreateUIResourceAsync(UIBase view, Transform parent, CancellationToken token)
        {
            GameObject obj = null;
            bool bound = false;
            try
            {
                obj = await view.Service.ResourceLoader.LoadAsync(view.Metadata.ResInfo, parent, token);
                if (token.IsCancellationRequested || view.DestroyRequested) return false;
                if (!TryBindHolder(obj, view.UIHolderType, view.Metadata.ResInfo.Location, out UIHolderObjectBase holder))
                    return false;
                view.BindUIHolder(holder);
                view.SetDestroyHolderOnDispose(true);
                bound = true;
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
            catch (Exception error)
            {
                Log.Error("[UI] Failed to load {0}: {1}", view.GetType().Name, error);
                return false;
            }
            finally { if (!bound) DestroyLoadedObject(obj); }
        }

        internal static bool CreateUIResourceSync(UIBase view, Transform parent)
        {
            GameObject obj = null;
            bool bound = false;
            try
            {
                obj = view.Service.ResourceLoader.Load(view.Metadata.ResInfo, parent);
                if (view.DestroyRequested) return false;
                if (!TryBindHolder(obj, view.UIHolderType, view.Metadata.ResInfo.Location, out UIHolderObjectBase holder))
                    return false;
                view.BindUIHolder(holder);
                view.SetDestroyHolderOnDispose(true);
                bound = true;
                return true;
            }
            catch (Exception error)
            {
                Log.Error("[UI] Failed to load {0}: {1}", view.GetType().Name, error);
                return false;
            }
            finally { if (!bound) DestroyLoadedObject(obj); }
        }

        private static bool TryBindHolder(GameObject obj, Type holderType, string location, out UIHolderObjectBase holder)
        {
            holder = null;
            if (obj == null)
            {
                Log.Error("[UI] UI resource could not be loaded: {0}.", location);
                return false;
            }
            holder = (UIHolderObjectBase)obj.GetComponent(holderType);
            if (holder != null) return true;
            Log.Error("[UI] UI resource {0} is missing holder {1}.", location, holderType.FullName);
            return false;
        }

        private static async UniTask<GameObject> InstantiateResourceAsync(string location, Transform parent, CancellationToken token)
        {
            var prefab = (GameObject)await Resources.LoadAsync<GameObject>(location).ToUniTask(cancellationToken: token);
            if (prefab == null || token.IsCancellationRequested) return null;
            return Object.Instantiate(prefab, parent);
        }

        private static GameObject InstantiateResourceSync(string location, Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(location);
            return prefab != null ? Object.Instantiate(prefab, parent) : null;
        }

        private static void DestroyLoadedObject(GameObject obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
