namespace AlicizaX
{
    public interface IServiceRegistry
    {
        T RegisterSelf<T>(T service) where T : class, IService;
        TContract Register<TContract>(IService service) where TContract : class, IService;
        bool TryGet<T>(out T service) where T : class, IService;
        T Require<T>() where T : class, IService;
    }
}
