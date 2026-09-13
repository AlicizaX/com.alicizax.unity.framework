using System;
using System.Threading;
using AlicizaX.Resource.Runtime;
using AlicizaX;
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
            try
            {
                if (UIResRegistry.TryGet(typeof(T).TypeHandle, out UIResRegistry.UIResInfo resInfo))
                {
                    GameObject obj = await LoadUIResourcesAsync(resInfo, parent, CancellationToken.None);
                    return GetHolderOrDestroy<T>(obj, resInfo.Location);
                }
            }
            catch (Exception error) { Log.Exception(error); }
            return null;
        }

        public static T CreateUIHolderSync<T>(Transform parent) where T : UIHolderObjectBase
        {
            try
            {
                if (UIResRegistry.TryGet(typeof(T).TypeHandle, out UIResRegistry.UIResInfo resInfo))
                {
                    GameObject obj = LoadUIResourcesSync(resInfo, parent);
                    return GetHolderOrDestroy<T>(obj, resInfo.Location);
                }
            }
            catch (Exception error) { Log.Exception(error); }
            return null;
        }


        internal static async UniTask<GameObject> LoadUIResourcesAsync(UIResRegistry.UIResInfo resInfo,
            Transform parent, CancellationToken cancellationToken)
        {
            return resInfo.LoadType == EUIResLoadType.AssetBundle
                ? await ResourceService.LoadGameObjectAsync(resInfo.Location, parent, cancellationToken)
                : await InstantiateResourceAsync(resInfo.Location, parent, cancellationToken);
        }

        internal static GameObject LoadUIResourcesSync(UIResRegistry.UIResInfo resInfo, Transform parent)
        {
            return resInfo.LoadType == EUIResLoadType.AssetBundle
                ? ResourceService.LoadGameObject(resInfo.Location, parent)
                : InstantiateResourceSync(resInfo.Location, parent);
        }


        internal static async UniTask<bool> CreateUIResourceAsync(UIBase view, Transform parent,
            CancellationToken cancellationToken)
        {
            GameObject obj = null;
            bool bound = false;
            try
            {
                obj = await view.Service.ResourceLoader.LoadAsync(view.Metadata.ResInfo, parent, cancellationToken);
                if (cancellationToken.IsCancellationRequested || view.DestroyRequested) return false;
                return bound = ValidateAndBind(view, obj);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
            catch (Exception error) { Log.Exception(error); return false; }
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
            catch (Exception error) { Log.Exception(error); return false; }
            finally { if (!bound) DestroyLoadedObject(obj); }
        }

        private static async UniTask<GameObject> InstantiateResourceAsync(string location, Transform parent,
            CancellationToken cancellationToken)
        {
            GameObject prefab;

            prefab = (GameObject)await Resources.LoadAsync<GameObject>(location)
                .ToUniTask(cancellationToken: cancellationToken);

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

        private static bool ValidateAndBind(UIBase view, GameObject holderObject)
        {
            if (!holderObject)
            {
                Log.Exception(new InvalidOperationException($"UI resource could not be loaded: {view.Metadata.ResInfo.Location}."));
                return false;
            }
            var holder = (UIHolderObjectBase)holderObject.GetComponent(view.UIHolderType);
            if (holder == null)
            {
                string message = $"UI resource {holderObject.name} is missing holder {view.UIHolderType.FullName}.";
                Log.Exception(new InvalidOperationException(message));
                return false;
            }

            view.BindUIHolder(holder);
            view.SetDestroyHolderOnDispose(true);
            return true;
        }

        private static T GetHolderOrDestroy<T>(GameObject holderObject, string location) where T : UIHolderObjectBase
        {
            if (!holderObject)
            {
                Log.Exception(new InvalidOperationException($"UI resource could not be loaded: {location}."));
                return null;
            }

            T holder = holderObject.GetComponent<T>();
            if (holder != null)
            {
                return holder;
            }

            DestroyLoadedObject(holderObject);
            Log.Exception(new InvalidOperationException($"UI resource {location} is missing holder {typeof(T).FullName}."));
            return null;
        }

        private static void DestroyLoadedObject(GameObject obj)
        {
            if (obj == null)
            {
                return;
            }

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
