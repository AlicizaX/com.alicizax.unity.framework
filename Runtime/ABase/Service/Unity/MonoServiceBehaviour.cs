using Cysharp.Text;
using UnityEngine;

namespace AlicizaX
{
    public abstract class MonoServiceBehaviour : MonoBehaviour, IMonoService, IServiceLifecycle
    {
        protected ServiceContext Context { get; private set; }

        protected bool IsInitialized { get; private set; }

        void IServiceLifecycle.Initialize(ServiceContext context)
        {
            if (IsInitialized)
                throw new System.InvalidOperationException(ZString.Format("{0} is already initialized.", GetType().FullName));

            Context = context;
            IsInitialized = true;
            OnInitialize();
        }

        void IServiceLifecycle.Destroy()
        {
            if (!IsInitialized) return;

            OnDestroyService();
            IsInitialized = false;
            Context = default;
        }

        protected virtual void OnInitialize() { }

        protected virtual void OnDestroyService() { }
    }

    public abstract class MonoServiceBehaviour<TScope> : MonoServiceBehaviour
        where TScope : IScope
    {
        [SerializeField] private bool _dontDestroyOnLoad = false;

        private void Awake()
        {
            OnAwake();
        }

        private void Start()
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
            if (kind == ServiceScopeKind.App) return AppServices.RequireWorld().App;
            if (kind == ServiceScopeKind.Scene) return AppServices.EnsureScene();
            return AppServices.EnsureGameplay();
        }

        private static bool TryResolveScope(out ServiceScope scope)
        {
            var kind = ScopeKindCache<TScope>.Kind;
            if (kind == ServiceScopeKind.App)
            {
                scope = AppServices.RequireWorld().App;
                return true;
            }

            if (kind == ServiceScopeKind.Scene)
                return AppServices.TryGetScene(out scope);

            return AppServices.TryGetGameplay(out scope);
        }

        protected virtual void OnAwake() { }
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
