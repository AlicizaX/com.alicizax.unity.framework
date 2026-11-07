using UnityEngine;

namespace AlicizaX
{
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public abstract class AppServiceRoot : MonoBehaviour
    {
        private static AppServiceRoot s_activeRoot;

        [SerializeField] private bool _dontDestroyOnLoad = true;

        private ServiceRegistry _registry;

        protected virtual void Awake()
        {
            if (s_activeRoot != null && s_activeRoot != this)
            {
                Debug.LogWarning("[AppServiceRoot] 场景中存在多个服务根节点，已禁用。", this);
                enabled = false;
                return;
            }

            s_activeRoot = this;

            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            _registry = AppServices.GetOrCreateRegistry();
        }

        protected virtual void Update()
        {
            if (_registry != null) _registry.Tick(Time.deltaTime);
        }

        protected virtual void LateUpdate()
        {
            if (_registry != null) _registry.LateTick(Time.deltaTime);
        }

        protected virtual void FixedUpdate()
        {
            if (_registry != null) _registry.FixedTick(Time.fixedDeltaTime);
        }

        protected virtual void OnDrawGizmos()
        {
            if (_registry != null) _registry.DrawGizmos();
        }

        protected virtual void OnDestroy()
        {
            if (s_activeRoot == this)
                s_activeRoot = null;

            if (_registry == null) return;

            var registry = _registry;
            _registry = null;

            // 只销毁自己创建的那个容器：外部已经 Shutdown 并重建过容器时不能误伤新实例。
            if (ReferenceEquals(AppServices.Registry, registry))
                AppServices.Shutdown();
        }
    }
}
