using Cysharp.Text;
using UnityEngine;

namespace AlicizaX
{
    public abstract class MonoServiceBehaviour : MonoBehaviour, IService, IServiceLifecycle
    {
        private ServiceWorld _world;
        private ServiceScope _scope;

        protected bool IsInitialized { get; private set; }

        void IServiceLifecycle.Initialize(ServiceWorld world, ServiceScope scope)
        {
            if (IsInitialized)
                throw new System.InvalidOperationException(ZString.Format("{0} is already initialized.", GetType().FullName));

            _world = world;
            _scope = scope;
            IsInitialized = true;
            OnInitialize();
        }

        void IServiceLifecycle.Destroy()
        {
            if (!IsInitialized) return;

            OnDestroyService();
            IsInitialized = false;
            _world = null;
            _scope = null;
        }

        protected T Require<T>() where T : class, IService
            => _world.Require<T>(_scope);

        protected bool TryGet<T>(out T service) where T : class, IService
            => _world.TryGet(_scope, out service);

        protected T RequireApp<T>() where T : class, IService
            => _world.App.Require<T>();

        protected T RequireScene<T>() where T : class, IService
            => _world.Scene.Require<T>();

        protected T RequireGameplay<T>() where T : class, IService
            => _world.Gameplay.Require<T>();

        protected virtual void OnInitialize()
        {
        }

        protected virtual void OnDestroyService()
        {
        }
    }

    public abstract class MonoServiceBehaviour<TScope> : MonoServiceBehaviour
        where TScope : IScope
    {
        [SerializeField] private bool _dontDestroyOnLoad = false;

        protected virtual void Awake()
        {
            var scope = ResolveOrCreateScope();

            if (scope.HasContract(GetType()))
            {
                Destroy(gameObject);
                return;
            }

            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            scope.RegisterSelf(this);
        }

        private void OnDestroy()
        {
            if (!IsInitialized) return;
            if (!AppServices.HasWorld) return;
            if (!TryResolveScope(out var scope)) return;

            scope.Unregister(this);
        }

        private static ServiceScope ResolveOrCreateScope()
        {
            var kind = ScopeKindCache<TScope>.Kind;
            if (kind == ServiceScopeKind.App) return AppServices.App;
            if (kind == ServiceScopeKind.Scene) return AppServices.Scene;
            return AppServices.Gameplay;
        }

        private static bool TryResolveScope(out ServiceScope scope)
        {
            var kind = ScopeKindCache<TScope>.Kind;
            if (kind == ServiceScopeKind.App)
            {
                scope = AppServices.App;
                return true;
            }

            if (kind == ServiceScopeKind.Scene)
            {
                scope = AppServices.Scene;
                return true;
            }

            scope = AppServices.Gameplay;
            return true;
        }
    }

    internal static class ScopeKindCache<TScope>
        where TScope : IScope
    {
        public static readonly ServiceScopeKind Kind = Resolve();

        private static ServiceScopeKind Resolve()
        {
            if (typeof(TScope) == typeof(AppScope)) return ServiceScopeKind.App;
            if (typeof(TScope) == typeof(SceneScope)) return ServiceScopeKind.Scene;
            if (typeof(TScope) == typeof(GameplayScope)) return ServiceScopeKind.Gameplay;
            throw new System.InvalidOperationException(ZString.Format("Unsupported service scope: {0}.", typeof(TScope).FullName));
        }
    }
}
