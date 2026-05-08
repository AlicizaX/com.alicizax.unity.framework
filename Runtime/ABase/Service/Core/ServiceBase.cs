using Cysharp.Text;

namespace AlicizaX
{
    internal interface IServiceLifecycle
    {
        void Initialize(ServiceContext context);
        void Destroy();
    }

    public abstract class ServiceBase : IService, IServiceLifecycle
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

        protected abstract void OnInitialize();

        protected abstract void OnDestroyService();
    }
}
