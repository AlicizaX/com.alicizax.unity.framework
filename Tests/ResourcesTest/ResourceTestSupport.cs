using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("AlicizaX.Framework.Resources.Editor.Tests")]
[assembly: InternalsVisibleTo("AlicizaX.Framework.MemoryPool.Tests")]
[assembly: InternalsVisibleTo("AlicizaX.Framework.MemoryPool.Editor.Tests")]
[assembly: InternalsVisibleTo("AlicizaX.Framework.Audio.Tests")]
[assembly: InternalsVisibleTo("AlicizaX.Framework.Audio.Editor.Tests")]

namespace AlicizaX.Resource.Tests
{
    internal sealed class ControlledLoader : IResourceLoader
    {
        private static readonly Assembly YooAssembly = typeof(AssetHandle).Assembly;
        private static readonly Type ProviderType = YooAssembly.GetType("YooAsset.ProviderBase", true);
        private static readonly Type AssetProviderType = YooAssembly.GetType("YooAsset.AssetProvider", true);
        private static readonly Type ManagerType = YooAssembly.GetType("YooAsset.ResourceManager", true);
        private static readonly MethodInfo CreateHandle = ProviderType.GetMethod("CreateHandle");
        private readonly object manager = Activator.CreateInstance(ManagerType, new object[] { "ResourceAudit" });
        internal readonly Dictionary<string, object> Providers = new Dictionary<string, object>();
        internal readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
        internal readonly List<HandleBase> Handles = new List<HandleBase>();
        internal bool CompleteImmediately = true;
        internal bool ThrowOnLoad;
        internal int Loads;
        public bool IsAvailable { get; set; } = true;

        public AssetHandle LoadAsset(string packageName, string location, Type assetType, bool synchronous, uint priority)
        {
            return Load<AssetHandle>(location, synchronous);
        }

        public SubAssetsHandle LoadSubAssets(string packageName, string location, bool synchronous)
        {
            return Load<SubAssetsHandle>(location, synchronous);
        }

        private T Load<T>(string location, bool synchronous) where T : HandleBase
        {
            using var marker = new Unity.Profiling.ProfilerMarker("ResourceAudit.Backend").Auto();
            Loads++;
            if (ThrowOnLoad) throw new InvalidOperationException("controlled load failure");
            if (!Providers.TryGetValue(location, out object provider) ||
                (int)ProviderType.GetProperty("RefCount").GetValue(provider) == 0)
            {
                provider = Activator.CreateInstance(AssetProviderType, new[] { manager, string.Empty, null });
                Providers[location] = provider;
            }
            var handle = (T)CreateHandle.MakeGenericMethod(typeof(T)).Invoke(provider, null);
            Handles.Add(handle);
            if (synchronous || CompleteImmediately) Complete(location);
            return handle;
        }

        internal void Complete(string location, bool success = true)
        {
            object provider = Providers[location];
            if (((AsyncOperationBase)provider).IsDone) return;
            if (success)
            {
                ProviderType.GetProperty("AssetObject").SetValue(provider, Assets[location]);
                ProviderType.GetProperty("SubAssetObjects").SetValue(provider, new[] { Assets[location] });
                ProviderType.GetMethod("SetSuccess", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(provider, null);
            }
            else
            {
                ProviderType.GetMethod("SetFail", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(provider, new object[] { "controlled failure" });
            }
        }

        internal int LiveHandles
        {
            get
            {
                int count = 0;
                foreach (HandleBase handle in Handles) if (handle.IsValid) count++;
                return count;
            }
        }
    }

    internal sealed class ResourceFixture : IDisposable
    {
        internal readonly ResourceService Service;
        internal readonly ControlledLoader Loader = new ControlledLoader();
        internal readonly List<Object> Objects = new List<Object>();
        internal ResourceBindingService Bindings => (ResourceBindingService)Service.BindingService;

        internal ResourceFixture(ResourceService service = null)
        {
            Service = service ?? new ResourceService();
            Service.Loader = Loader;
            typeof(ResourceService).GetMethod("InitializeAssetRecords", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Service, null);
            typeof(ResourceService).GetField("_bindingService", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Service, new ResourceBindingService(Service));
        }

        internal T Keep<T>(T value) where T : Object
        {
            Objects.Add(value);
            return value;
        }

        internal TextAsset Text(string key)
        {
            var asset = Keep(new TextAsset(key));
            Loader.Assets.Add(key, asset);
            return asset;
        }

        internal Material Material(string key)
        {
            var asset = Keep(new Material(Shader.Find("UI/Default")));
            Loader.Assets.Add(key, asset);
            return asset;
        }

        internal Sprite Sprite(string key)
        {
            var texture = Keep(new Texture2D(4, 4));
            var sprite = Keep(UnityEngine.Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero));
            sprite.name = key;
            Loader.Assets.Add(key, sprite);
            return sprite;
        }

        internal ResourceOwner Owner(bool active = true)
        {
            var go = Keep(new GameObject("resource-owner"));
            go.SetActive(active);
            return go.AddComponent<ResourceOwner>();
        }

        internal ResourceAssetInfo Info(string location)
        {
            var infos = new ResourceAssetInfo[Service.GetAssetInfos(null, 0, 0)];
            Service.GetAssetInfos(infos, 0, infos.Length);
            foreach (var info in infos) if (info.Location == location && info.HandleValid) return info;
            return default;
        }

        internal static IEnumerator Wait<T>(UniTask<T> task, Action<T> result)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 120; frame++) yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "Operation did not terminate within 120 player loop updates.");
            result(awaiter.GetResult());
        }

        public void Dispose()
        {
            try
            {
                typeof(ResourceService).GetMethod("OnDestroyService", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Service, null);
                Assert.That(Loader.LiveHandles, Is.Zero, "Service shutdown left live backend handles.");
            }
            finally
            {
                foreach (Object value in Objects) if (value != null) Object.DestroyImmediate(value);
                foreach (HandleBase handle in Loader.Handles) if (handle.IsValid) handle.Dispose();
            }
        }
    }
}
