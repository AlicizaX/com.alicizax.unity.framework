using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
    public interface IGameObjectPoolService : IService
    {
        GameObject GetGameObject(string assetName, Transform parent = null);

        UniTask<GameObject> GetGameObjectAsync(string assetName, Transform parent = null, CancellationToken cancellationToken = default);

        UniTask PreloadAsync(string assetName, int count = 1, CancellationToken cancellationToken = default);

        void Release(GameObject gameObject);

        void ForceCleanup();
    }

    internal interface IGameObjectPoolDebugService : IService
    {
        GameObjectPoolSummarySnapshot GetDebugSummary();

        int GetDebugSnapshots(GameObjectPoolSnapshot[] snapshots);
    }
}
