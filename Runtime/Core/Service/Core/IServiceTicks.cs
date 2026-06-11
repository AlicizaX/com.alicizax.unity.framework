namespace AlicizaX
{
    public interface IServiceTickable
    {
        void Tick(float deltaTime);
    }

    public interface IServiceLateTickable
    {
        void LateTick(float deltaTime);
    }

    public interface IServiceFixedTickable
    {
        void FixedTick(float fixedDeltaTime);
    }

    public interface IServiceGizmoDrawable
    {
        void DrawGizmos();
    }
}
