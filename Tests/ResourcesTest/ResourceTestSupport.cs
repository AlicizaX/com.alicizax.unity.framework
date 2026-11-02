using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Unity.Profiling;
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
        internal static readonly ProfilerMarker BackendMarker = new ProfilerMarker("ResourceAudit.Backend");
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
            {
                Provider = provider;
                Asset = asset;
                Package = package;
                Location = location;
                AssetType = assetType;
                SubAssets = subAssets;
            }
        }

        private readonly Dictionary<(string package, string location, Type type, bool subAssets), Request> providers = new Dictionary<(string, string, Type, bool), Request>();
        private readonly Dictionary<HandleBase, Request> requestByHandle = new Dictionary<HandleBase, Request>();
        internal readonly Dictionary<(string package, string location, Type type, bool subAssets), Object> KeyedAssets = new Dictionary<(string, string, Type, bool), Object>();
        internal readonly List<Request> Requests = new List<Request>();
        internal readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
        internal readonly List<HandleBase> Handles = new List<HandleBase>();
        internal bool CompleteImmediately = true;
        internal bool ThrowOnLoad;
        internal int Loads;
        internal int SynchronousLoads;
        internal int WaitCalls;
        internal string DefaultPackageName = "DefaultPackage";
        public bool IsAvailable { get; set; } = true;

        public AssetHandle LoadAsset(string packageName, string location, Type assetType, bool synchronous)
        {
            return Load<AssetHandle>(packageName, location, assetType, synchronous, false);
        }

        public SubAssetsHandle LoadSubAssets(string packageName, string location, bool synchronous)
        {
            return Load<SubAssetsHandle>(packageName, location, typeof(Sprite), synchronous, true);
        }

        public void WaitForCompletion(HandleBase handle)
        {
            WaitCalls++;
            Complete(requestByHandle[handle]);
        }

        private T Load<T>(string packageName, string location, Type assetType, bool synchronous, bool subAssets) where T : HandleBase
        {
            using var marker = BackendMarker.Auto();
            Loads++;
            if (synchronous) SynchronousLoads++;
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
            requestByHandle[handle] = request;
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
            requestByHandle.Clear();
            Requests.Clear();
        }

        internal void CompleteLast()
        {
            Complete(Requests[Requests.Count - 1]);
        }

        internal void Complete(Request request, bool success = true)
        {
            using var marker = BackendMarker.Auto();
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

    internal sealed class ResourceFrameDriver : IPlayerLoopItem, IDisposable
    {
        private readonly ResourceService service;
        private bool stopped;

        private ResourceFrameDriver(ResourceService service)
        {
            this.service = service;
        }

        internal static ResourceFrameDriver Start(ResourceService service)
        {
            var driver = new ResourceFrameDriver(service);
            PlayerLoopHelper.AddAction(PlayerLoopTiming.Update, driver);
            return driver;
        }

        public bool MoveNext()
        {
            if (stopped) return false;
            service.ProcessFrame(Time.unscaledTime);
            return true;
        }

        public void Dispose() => stopped = true;
    }

    internal sealed class ResourceFixture : IDisposable
    {
        internal readonly ResourceService Service;
        internal readonly ControlledLoader Loader = new ControlledLoader();
        internal readonly List<Object> Objects = new List<Object>();
        private readonly ResourceFrameDriver driver;
        internal float Now;

        internal ResourceFixture(bool drivePlayerLoop = false)
        {
            Service = new ResourceService();
            Loader.DefaultPackageName = Service.DefaultPackageName;
            Service.Loader = Loader;
            if (drivePlayerLoop) driver = ResourceFrameDriver.Start(Service);
        }

        internal void Tick(float seconds = 0f)
        {
            Now += seconds;
            Service.ProcessFrame(Now);
        }

        internal T Keep<T>(T value) where T : Object
        {
            Objects.Add(value);
            return value;
        }

        internal TextAsset Text(string key)
        {
            var asset = Keep(new TextAsset(key));
            asset.name = key;
            Loader.Assets.Add(key, asset);
            return asset;
        }

        internal Material Material(string key)
        {
            var asset = Keep(new Material(Shader.Find("UI/Default")));
            asset.name = key;
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

        internal GameObject Prefab(string key, bool active = true)
        {
            var prefab = Keep(new GameObject(key));
            prefab.SetActive(active);
            Loader.Assets.Add(key, prefab);
            return prefab;
        }

        internal GameObject GameObject(string name = "resource-test", bool active = true)
        {
            var go = Keep(new GameObject(name));
            go.SetActive(active);
            return go;
        }

        internal ResourceAssetInfo Info(string location, Type assetType = null, bool subAssets = false)
        {
            Assert.That(TryFindInfo(location, out ResourceAssetInfo info, assetType, subAssets), Is.True, "Missing resource record: " + location);
            return info;
        }

        internal bool TryFindInfo(string location, out ResourceAssetInfo result, Type assetType = null, bool subAssets = false)
        {
            bool found = false;
            result = default;
            foreach (ResourceAssetInfo info in AssetInfos())
            {
                if (info.Location != location || info.SubAssets != subAssets || (assetType != null && info.TypeName != assetType.Name)) continue;
                Assert.That(found, Is.False, "Specify type for ambiguous records: " + location);
                found = true;
                result = info;
            }
            return found;
        }

        internal ResourceAssetInfo[] AssetInfos()
        {
            var infos = new ResourceAssetInfo[Service.AssetCount];
            int start = 1;
            int offset = 0;
            var page = new ResourceAssetInfo[32];
            int count;
            while ((count = Service.GetAssetInfos(page, start)) > 0)
            {
                Array.Copy(page, 0, infos, offset, count);
                offset += count;
                start += count;
            }
            Assert.That(offset, Is.EqualTo(infos.Length));
            return infos;
        }

        internal void AssertInvariants()
        {
            ResourceStats stats = Service.GetStats();
            Assert.That(stats.ReferenceCount, Is.EqualTo(stats.ScopeEntryCount + stats.LeaseCount + stats.BindingReferenceCount),
                "Record references must equal scope entries + leases + bindings.");
            Assert.That(Service.CountIdleList(out bool ordered), Is.EqualTo(stats.IdleCount), "Idle list length mismatch.");
            Assert.That(ordered, Is.True, "Idle list must hold unreferenced idle records in idle order.");
            foreach (ResourceAssetInfo info in AssetInfos())
            {
                switch (info.State)
                {
                    case ResourceAssetState.Active:
                        Assert.That(info.RefCount, Is.GreaterThan(0), info.Location);
                        break;
                    default:
                        Assert.That(info.RefCount, Is.Zero, info.Location);
                        break;
                }
            }
        }

        internal void AssertNoReferences(string location)
        {
            foreach (ResourceAssetInfo info in AssetInfos())
                if (info.Location == location) Assert.That(info.RefCount, Is.Zero, location);
        }

        internal void AssertReleased(string location)
        {
            foreach (ResourceAssetInfo info in AssetInfos())
                if (info.Location == location) Assert.That(info.State, Is.EqualTo(ResourceAssetState.Released), location);
        }

        internal T Result<T>(UniTask<T> task)
        {
            var awaiter = task.GetAwaiter();
            Assert.That(awaiter.IsCompleted, Is.True, "Task must be completed by the preceding tick.");
            return awaiter.GetResult();
        }

        internal static IEnumerator WaitFor<T>(UniTask<T> task, Action<T> result)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 600; frame++)
                yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "Operation did not terminate within 600 player loop updates.");
            result(awaiter.GetResult());
        }

        internal IEnumerator Wait<T>(UniTask<T> task, Action<T> result)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 120; frame++)
            {
                if (driver == null) Tick();
                yield return null;
            }
            Assert.That(awaiter.IsCompleted, Is.True, "Operation did not terminate within 120 player loop updates.");
            result(awaiter.GetResult());
        }

        public void Dispose()
        {
            try
            {
                driver?.Dispose();
                Service.Shutdown();
                Assert.That(Loader.LiveHandles, Is.Zero, "Service shutdown left live backend handles.");
                Assert.That(Service.GetStats().ReferenceCount, Is.Zero, "Service shutdown left resource references.");
            }
            finally
            {
                foreach (Object value in Objects) if (value != null) Object.DestroyImmediate(value);
                foreach (HandleBase handle in Loader.Handles) if (handle.IsValid) handle.Release();
            }
        }
    }
}
