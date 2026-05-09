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
            if (!AppServices.App.TryGet<ISceneService>(out _))
            {
                AppServices.App.Register<ISceneService>(new SceneService());
            }
        }
    }
}
