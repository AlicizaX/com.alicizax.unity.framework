using AlicizaX;
using UnityEngine;

namespace AlicizaX.Scene.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Scene")]
    public sealed class SceneComponent : MonoBehaviour
    {
        private void Awake()
        {
            if (!AppServices.TryGetApp<ISceneService>(out _))
            {
            AppServices.RegisterApp<ISceneService>(new SceneService());
            }

            AppServices.EnsureScene();
        }
    }
}
