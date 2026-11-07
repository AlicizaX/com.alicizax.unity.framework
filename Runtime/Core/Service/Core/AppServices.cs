using System;

namespace AlicizaX
{
    public static class AppServices
    {
        private static ServiceRegistry _registry;

        /// <summary>服务容器是否已创建（<see cref="AppServiceRoot"/> 启动后为 true）。</summary>
        public static bool IsAvailable => _registry != null;

        internal static ServiceRegistry Registry => _registry;

        public static IService Register(IService service)
            => RequireRegistry().Register(service);

        public static IService Register(IService service, Type[] contracts)
            => RequireRegistry().Register(service, contracts);

        public static TContract Register<TContract>(IService service) where TContract : class, IService
            => RequireRegistry().Register<TContract>(service);

        public static TContract Register<TContract, TExtraContract>(IService service)
            where TContract : class, IService
            where TExtraContract : class, IService
            => RequireRegistry().Register<TContract, TExtraContract>(service);

        /// <summary>
        /// 注销服务。容器已销毁时返回 false 而不是抛异常，供模块 OnDestroy 直接调用。
        /// </summary>
        public static bool Unregister(IService service)
            => _registry != null && _registry.Unregister(service);

        public static bool TryGet<T>(out T service) where T : class, IService
        {
            if (_registry != null) return _registry.TryGet(out service);

            service = null;
            return false;
        }

        public static T Require<T>() where T : class, IService
            => RequireRegistry().Require<T>();

        internal static ServiceRegistry GetOrCreateRegistry()
        {
            if (_registry == null)
                _registry = new ServiceRegistry();
            return _registry;
        }

        private static ServiceRegistry RequireRegistry()
        {
            if (_registry == null)
                throw new InvalidOperationException("ServiceRegistry has not been created yet.");
            return _registry;
        }

        public static void Shutdown()
        {
            if (_registry == null) return;
            _registry.Dispose();
            _registry = null;
        }
    }
}
