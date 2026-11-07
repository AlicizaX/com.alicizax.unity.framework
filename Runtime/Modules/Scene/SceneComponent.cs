using UnityEngine;

namespace AlicizaX.Scene.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Scene")]
    [DefaultExecutionOrder(-600)]
    public sealed class SceneComponent : MonoBehaviour
    {
        private ISceneService _sceneService;

        private void Awake()
        {
            _sceneService = new SceneService();
            AppServices.Register<ISceneService>(_sceneService);
        }

        private void OnDestroy()
        {
            if (_sceneService == null)
            {
                return;
            }

            AppServices.Unregister(_sceneService);
            _sceneService = null;
        }
    }
}
