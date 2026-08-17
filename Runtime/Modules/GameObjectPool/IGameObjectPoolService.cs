using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
    public interface IGameObjectPoolService : IService
    {
        GameObject Spawn(string location, Transform parent = null);

        T Spawn<T>(string location, Transform parent = null) where T : Component;

        bool TrySpawn(string location, Transform parent, out GameObject instance);

        UniTask<GameObject> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default);

        UniTask<T> SpawnAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component;

        GameObject LoadPrefab(string location);

        UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default);

        UniTask WarmupAsync(string location, int count, CancellationToken cancellationToken = default);

        void Despawn(GameObject instance);

        void Despawn(GameObjectPoolHandle handle);

        void Flush(string location);

        void FlushGroup(string group);

        void FlushAll();

        void LoadCatalog(PoolConfigScriptableObject config);

        void LoadCatalog(string poolConfigPath);
    }

    internal interface IGameObjectPoolDebugService : IService
    {
        GameObjectPoolSummarySnapshot GetDebugSummary();

        int GetDebugSnapshots(GameObjectPoolSnapshot[] snapshots);

        void FillDebugInstances(GameObjectPoolSnapshot snapshot);

        void Flush(string location);

        void FlushGroup(string group);
    }
}
