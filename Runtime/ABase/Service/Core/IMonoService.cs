namespace AlicizaX
{
    /// <summary>
    /// 标记一个 IService 实现来自 MonoBehaviour。
    /// ServiceScope 通过此接口识别 Mono 服务，避免 Core 层依赖 UnityEngine。
    /// </summary>
    public interface IMonoService : IService { }
}
