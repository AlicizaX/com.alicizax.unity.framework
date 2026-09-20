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
[assembly: InternalsVisibleTo("AlicizaX.Framework.GameObjectPool.Tests")]
[assembly: InternalsVisibleTo("AlicizaX.Framework.GameObjectPool.Editor.Tests")]

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
        internal sealed class Request
        {
            internal readonly object Provider;
            internal readonly Object Asset;
            internal readonly string Package;
            internal readonly string Location;
            internal readonly Type AssetType;
            internal readonly bool SubAssets;
            internal Request(object provider, Object asset, string package, string location, Type assetType, bool subAssets)
            { Provider = provider; Asset = asset; Package = package; Location = location; AssetType = assetType; SubAssets = subAssets; }
        }

        private readonly Dictionary<(string package, string location, Type type, bool subAssets), Request> providers = new Dictionary<(string, string, Type, bool), Request>();
        internal readonly Dictionary<(string package, string location, Type type, bool subAssets), Object> KeyedAssets = new Dictionary<(string, string, Type, bool), Object>();
        internal readonly List<Request> Requests = new List<Request>();
        internal readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
        internal readonly List<HandleBase> Handles = new List<HandleBase>();
        internal bool CompleteImmediately = true;
        internal bool ThrowOnLoad;
        internal int Loads;
        internal string DefaultPackageName = "DefaultPackage";
        public bool IsAvailable { get; set; } = true;

        public AssetHandle LoadAsset(string packageName, string location, Type assetType, bool synchronous, uint priority)
        {
            return Load<AssetHandle>(packageName, location, assetType, synchronous, false);
        }

        public SubAssetsHandle LoadSubAssets(string packageName, string location, bool synchronous)
        {
            return Load<SubAssetsHandle>(packageName, location, typeof(Sprite), synchronous, true);
        }

        private T Load<T>(string packageName, string location, Type assetType, bool synchronous, bool subAssets) where T : HandleBase
        {
            using var marker = new Unity.Profiling.ProfilerMarker("ResourceAudit.Backend").Auto();
            Loads++;
            if (ThrowOnLoad) throw new InvalidOperationException("controlled load failure");
            var key = (packageName, location, assetType, subAssets);
            if (!providers.TryGetValue(key, out Request request) ||
                (int)ProviderType.GetProperty("RefCount").GetValue(request.Provider) == 0)
            {
                object provider = Activator.CreateInstance(AssetProviderType, new[] { manager, string.Empty, null });
                if (!KeyedAssets.TryGetValue(key, out Object asset) && packageName == DefaultPackageName)
                    Assets.TryGetValue(location, out asset);
                request = new Request(provider, asset, packageName, location, assetType, subAssets);
                providers[key] = request;
                Requests.Add(request);
            }
            var handle = (T)CreateHandle.MakeGenericMethod(typeof(T)).Invoke(request.Provider, null);
            Handles.Add(handle);
            if (synchronous || CompleteImmediately) Complete(request);
            return handle;
        }

        internal void Complete(string location, bool success = true)
        {
            Complete(RequestFor(location), success);
        }

        internal Request RequestFor(string location)
        {
            Request result = null;
            foreach (Request request in providers.Values)
            {
                if (request.Location != location || request.Package != DefaultPackageName) continue;
                Assert.That(result, Is.Null, "Use the complete resource key to select an ambiguous request.");
                result = request;
            }
            Assert.That(result, Is.Not.Null, "No request for " + location);
            return result;
        }

        internal Request RequestFor(string package, string location, Type assetType, bool subAssets = false)
            => providers[(package, location, assetType, subAssets)];

        internal bool HasRequest(string location)
        {
            foreach (Request request in providers.Values)
                if (request.Location == location && request.Package == DefaultPackageName) return true;
            return false;
        }

        internal void ClearRequests()
        {
            Assert.That(LiveHandles, Is.Zero);
            providers.Clear();
            Requests.Clear();
        }

        internal void Complete(Request request, bool success = true)
        {
            object provider = request.Provider;
            if (((AsyncOperationBase)provider).IsDone) return;
            if (success)
            {
                Assert.That(request.Asset, Is.Not.Null, "No asset registered for " + request.Package + "/" + request.Location);
                ProviderType.GetProperty("AssetObject").SetValue(provider, request.Asset);
                ProviderType.GetProperty("SubAssetObjects").SetValue(provider, new[] { request.Asset });
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
            Loader.DefaultPackageName = Service.DefaultPackageName;
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
            Assert.That(TryFindInfo(location, out ResourceAssetInfo info), Is.True, "Missing resource record: " + location);
            return info;
        }

        internal bool TryFindInfo(string location, out ResourceAssetInfo result, string package = null, Type assetType = null, ResourceAssetKind? kind = null)
        {
            bool found = false;
            result = default;
            foreach (var info in AssetInfos())
            {
                if (info.Location != location || info.Package != (package ?? Service.DefaultPackageName) ||
                    (assetType != null && info.TypeName != assetType.Name) || (kind.HasValue && info.Kind != kind.Value)) continue;
                Assert.That(found, Is.False, "Specify package, type and kind for ambiguous records: " + location);
                found = true;
                result = info;
            }
            return found;
        }

        internal ResourceAssetInfo[] AssetInfos()
        {
            var infos = new ResourceAssetInfo[Service.GetAssetInfos(null, 0, 0)];
            Assert.That(Service.GetAssetInfos(infos, 0, infos.Length), Is.EqualTo(infos.Length));
            foreach (var info in infos)
            {
                Assert.That(info.RefCountTotal, Is.EqualTo(info.DirectRefCount + info.LegacyDirectRefCount + info.BindingRefCount + info.PendingRefCount));
                Assert.That(info.DirectRefCount, Is.GreaterThanOrEqualTo(0));
                Assert.That(info.LegacyDirectRefCount, Is.GreaterThanOrEqualTo(0));
                Assert.That(info.BindingRefCount, Is.GreaterThanOrEqualTo(0));
                Assert.That(info.PendingRefCount, Is.GreaterThanOrEqualTo(0));
                if (info.State != ResourceAssetState.Released) continue;
                Assert.That(info.RefCountTotal, Is.Zero, "Released slot retained references.");
                Assert.That(info.HandleValid, Is.False, "Released slot retained a backend handle.");
            }
            return infos;
        }

        internal void AssertNoReferences(string location)
        {
            foreach (var info in AssetInfos())
                if (info.Location == location) Assert.That(info.RefCountTotal, Is.Zero, location);
        }

        internal void AssertUnloaded(string location)
        {
            foreach (var info in AssetInfos())
            {
                if (info.Location != location) continue;
                Assert.That(info.RefCountTotal, Is.Zero, location);
                Assert.That(info.HandleValid, Is.False, location);
            }
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
                foreach (var info in AssetInfos()) Assert.That(info.RefCountTotal, Is.Zero, "Service shutdown left resource references.");
            }
            finally
            {
                foreach (Object value in Objects) if (value != null) Object.DestroyImmediate(value);
                foreach (HandleBase handle in Loader.Handles) if (handle.IsValid) handle.Dispose();
            }
        }
    }
}
