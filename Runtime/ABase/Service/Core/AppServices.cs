using System;

namespace AlicizaX
{
    public static class AppServices
    {
        private static ServiceWorld _world;

        public static bool HasWorld => _world != null;

        public static ServiceScope App => RequireWorld().App;

        public static ServiceScope Scene => RequireWorld().EnsureScene();

        public static ServiceScope Gameplay => RequireWorld().EnsureGameplay();

        public static bool TryGet<T>(out T service) where T : class, IService
        {
            if (_world != null) return _world.TryGet(out service);

            service = null;
            return false;
        }

        public static T Require<T>() where T : class, IService
            => RequireWorld().Require<T>();

        internal static ServiceWorld EnsureWorld(int appScopeOrder = ServiceDomainOrder.App)
        {
            if (_world == null)
                _world = new ServiceWorld(appScopeOrder);
            return _world;
        }

        internal static ServiceWorld RequireWorld()
        {
            if (_world == null)
                throw new InvalidOperationException("ServiceWorld has not been created yet.");
            return _world;
        }

        public static void Shutdown()
        {
            if (_world == null) return;
            _world.Dispose();
            _world = null;
        }
    }
}
