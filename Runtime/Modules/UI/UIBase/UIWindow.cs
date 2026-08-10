namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 业务具体窗体基类。UI 源生成器只接受继承本类型的封闭具体窗体。
    /// Tab 容器等开放泛型中间层应继承 UIWindowBase，避免 UI002。
    /// </summary>
    public abstract class UIWindow<T> : UIWindowBase<T> where T : UIHolderObjectBase
    {
    }
}
