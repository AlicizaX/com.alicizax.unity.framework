using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
    public interface IGameObjectPoolService : IService
    {
        bool TryGetPoolAssetId(string assetPath, out PoolAssetId assetId);

        GameObject GetGameObject(PoolAssetId assetId, Transform parent = null);

        UniTask<GameObject> GetGameObjectAsync(PoolAssetId assetId, Transform parent = null, CancellationToken cancellationToken = default);

        UniTask PreloadAsync(PoolAssetId assetId, int count = 1, CancellationToken cancellationToken = default);

        void Release(GameObject gameObject);

        void ForceCleanup();
    }

    internal interface IGameObjectPoolDebugService : IService
    {
        GameObjectPoolSummarySnapshot GetDebugSummary();

        int GetDebugSnapshots(GameObjectPoolSnapshot[] snapshots);
    }
}
