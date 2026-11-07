namespace AlicizaX
{
    /// <summary>
    /// MonoBehaviour 形式的服务标记。它的 tick 由 Unity 生命周期（Update 等）驱动，
    /// 因此注册时不允许再实现 <see cref="IServiceTickable"/> 等 tick 接口，避免被驱动两次。
    /// </summary>
    public interface IMonoService : IService
    {
    }
}
