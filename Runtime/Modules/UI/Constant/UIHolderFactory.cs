using System;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 节点的实例化：按 <see cref="UIResRegistry"/> 的资源配置从 Resources 或 AssetBundle 实例化并解析 Holder。
    /// 对外提供直接创建 Holder 的入口（不经过 UIService，生命周期由调用方自己管理）。
    /// </summary>
    public static class UIHolderFactory
    {
        private static IResourceService ResourceService => AppServices.Require<IResourceService>();

        /// <summary>异步实例化 T 的资源并返回 Holder；失败时记录错误并返回 null。</summary>
        public static async UniTask<T> CreateUIHolderAsync<T>(Transform parent) where T : UIHolderObjectBase =>
            (T)await LoadAsync(typeof(T), parent, CancellationToken.None);

        /// <summary>同步实例化 T 的资源并返回 Holder；失败时记录错误并返回 null。</summary>
        public static T CreateUIHolderSync<T>(Transform parent) where T : UIHolderObjectBase =>
            (T)Load(typeof(T), parent);

        internal static UIHolderObjectBase Load(Type holderType, Transform parent)
        {
            if (!TryGetResource(holderType, out UIResRegistry.UIResInfo resource)) return null;
            GameObject node;
            try
            {
                node = resource.LoadType == EUIResLoadType.AssetBundle
                    ? ResourceService.Instantiate(resource.Location, parent)
                    : InstantiateResourceSync(resource.Location, parent);
            }
            catch (Exception error)
            {
                Log.Error("[UI] Failed to load {0}: {1}", resource.Location, error);
                return null;
            }
            return ResolveHolder(node, holderType, resource.Location);
        }

        internal static async UniTask<UIHolderObjectBase> LoadAsync(Type holderType, Transform parent,
            CancellationToken cancellationToken)
        {
            if (!TryGetResource(holderType, out UIResRegistry.UIResInfo resource)) return null;
            GameObject node;
            try
            {
                node = resource.LoadType == EUIResLoadType.AssetBundle
                    ? await ResourceService.InstantiateAsync(resource.Location, parent, cancellationToken)
                    : await InstantiateResourceAsync(resource.Location, parent, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log.Error("[UI] Failed to load {0}: {1}", resource.Location, error);
                return null;
            }
            return ResolveHolder(node, holderType, resource.Location);
        }

        internal static void DestroyNode(GameObject node)
        {
            if (node == null) return;
            if (Application.isPlaying) Object.Destroy(node);
            else Object.DestroyImmediate(node);
        }

        private static bool TryGetResource(Type holderType, out UIResRegistry.UIResInfo resource)
        {
            if (UIResRegistry.TryGet(holderType.TypeHandle, out resource)) return true;
            Log.Error("[UI] UI resource is not registered: {0}.", holderType);
            return false;
        }

        private static UIHolderObjectBase ResolveHolder(GameObject node, Type holderType, string location)
        {
            if (node == null)
            {
                Log.Error("[UI] UI resource could not be loaded: {0}.", location);
                return null;
            }
            var holder = (UIHolderObjectBase)node.GetComponent(holderType);
            if (holder != null) return holder;
            Log.Error("[UI] UI resource {0} is missing holder {1}.", location, holderType.FullName);
            DestroyNode(node);
            return null;
        }

        private static async UniTask<GameObject> InstantiateResourceAsync(string location, Transform parent, CancellationToken token)
        {
            var prefab = (GameObject)await Resources.LoadAsync<GameObject>(location).ToUniTask(cancellationToken: token);
            return prefab != null ? Object.Instantiate(prefab, parent) : null;
        }

        private static GameObject InstantiateResourceSync(string location, Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(location);
            return prefab != null ? Object.Instantiate(prefab, parent) : null;
        }
    }
}
