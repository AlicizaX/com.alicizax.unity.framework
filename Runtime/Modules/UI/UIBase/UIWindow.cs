using System;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 业务具体窗体基类。UI 源生成器只接受继承本类型的封闭具体窗体。
    /// Tab 容器等开放泛型中间层应继承 UIWindowBase，避免 UI002。
    /// </summary>
    public abstract class UIWindow<T> : UIBase where T : UIHolderObjectBase
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
