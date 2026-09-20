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
                return GetHolderOrDestroy<T>(await LoadUIResourcesAsync(resource, parent, CancellationToken.None), resource.Location);
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
            try { return GetHolderOrDestroy<T>(LoadUIResourcesSync(resource, parent), resource.Location); }
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
                return bound = ValidateAndBind(view, obj);
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
                return bound = ValidateAndBind(view, obj);
            }
            catch (Exception error)
            {
                Log.Error("[UI] Failed to load {0}: {1}", view.GetType().Name, error);
                return false;
            }
            finally { if (!bound) DestroyLoadedObject(obj); }
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

        private static bool ValidateAndBind(UIBase view, GameObject obj)
        {
            if (obj == null)
            {
                Log.Error("[UI] UI resource could not be loaded: {0}.", view.Metadata.ResInfo.Location);
                return false;
            }
            var holder = (UIHolderObjectBase)obj.GetComponent(view.UIHolderType);
            if (holder == null)
            {
                Log.Error("[UI] UI resource {0} is missing holder {1}.", obj.name, view.UIHolderType.FullName);
                return false;
            }
            view.BindUIHolder(holder);
            view.SetDestroyHolderOnDispose(true);
            return true;
        }

        private static T GetHolderOrDestroy<T>(GameObject obj, string location) where T : UIHolderObjectBase
        {
            if (obj == null)
            {
                Log.Error("[UI] UI resource could not be loaded: {0}.", location);
                return null;
            }
            T holder = obj.GetComponent<T>();
            if (holder != null) return holder;
            DestroyLoadedObject(obj);
            Log.Error("[UI] UI resource {0} is missing holder {1}.", location, typeof(T).FullName);
            return null;
        }

        private static void DestroyLoadedObject(GameObject obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
