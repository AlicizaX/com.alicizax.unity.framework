namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 模块门面：根节点、面板开关、页面导航、遮挡层四组能力的聚合接口。
    /// 只依赖其中一组能力时，请使用对应的细分接口。
    /// </summary>
    public interface IUIService : IService, IUIRootService, IUIPanelService, IUINavigationService, IUIBlockService
    {
    }
}
