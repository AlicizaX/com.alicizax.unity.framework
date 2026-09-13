using System;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWindow : UIBase
    {
        protected UICloseHandle CloseSelf(bool force = false, bool skipTransition = false) => Service.CloseWindow(this, force, skipTransition);
        internal override void OnFrameworkClosed() => Service.OnWindowClosed(this);
        internal override void OnFrameworkDestroyed() => Service.OnWindowDestroyed(this);
    }

    public abstract class UIWindow<T> : UIWindow where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
        internal sealed override Type UIHolderType => typeof(T);
        internal sealed override void BindUIHolder(UIHolderObjectBase holder) =>
            BindHolderCommon(holder, true, true);
    }
}
