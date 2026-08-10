using System;
using AlicizaX;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 带 Holder 的顶层窗公共基类：绑定、关闭入口。
    /// UIWindow / UITabWindow 共用；业务具体窗体应继承 UIWindow，不要直接继承本类型。
    /// </summary>
    public abstract class UIWindowBase<T> : UIBase where T : UIHolderObjectBase
    {
        private IUIService _uiService;

        protected T baseui => (T)Holder;

        internal sealed override Type UIHolderType => typeof(T);

        private IUIService UIService => _uiService ??= AppServices.App.Require<IUIService>();

        protected void CloseSelf(bool force = false)
        {
            UIService.CloseUI(RuntimeTypeHandler, force);
        }

        internal sealed override void BindUIHolder(UIHolderObjectBase holder, UIBase owner)
        {
            if (_state != UIState.CreatedUI)
            {
                Log.Error("Cannot bind UI holder because window has already been created.");
                return;
            }

            BindHolderCommon(holder, owner == null, true);
        }
    }
}
