namespace AlicizaX
{
    public static class ServiceDomainOrder
    {
        public const int App = -10000;
        public const int Scene = -5000;
        public const int Gameplay = 0;
    }

    public sealed class AppScope : IScope
    {
        public int Order => ServiceDomainOrder.App;
    }

    public sealed class SceneScope : IScope
    {
        public int Order => ServiceDomainOrder.Scene;
    }

    public sealed class GameplayScope : IScope
    {
        public int Order => ServiceDomainOrder.Gameplay;
    }

    public interface IScope
    {
        public int Order { get; }
    }

    internal enum ServiceScopeKind : byte
    {
        App = 0,
        Scene = 1,
        Gameplay = 2,
    }
}
