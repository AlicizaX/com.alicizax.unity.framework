namespace AlicizaX
{
    /// <summary>
    /// 由 <see cref="ServiceRegistry"/> 调用的服务生命周期。服务不直接实现 IDisposable，
    /// 避免与业务自身的 Dispose 语义混淆。
    /// </summary>
    internal interface IServiceLifecycle
    {
        void Initialize(ServiceRegistry registry);

        void Dispose();
    }
}
