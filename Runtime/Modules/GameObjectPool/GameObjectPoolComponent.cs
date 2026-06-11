using System;
using UnityEngine;

namespace AlicizaX
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/GameObjectPool")]
    [UnityEngine.Scripting.Preserve]
    public sealed class GameObjectPoolComponent:MonoBehaviour
    {
        private void Awake()
        {
            AppServices.App.Register<IGameObjectPoolService, IGameObjectPoolDebugService>(new GameObjectPoolService(transform));
        }
    }
}
