using System;
using AlicizaX;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWindow<T> : UIBase where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;

        internal sealed override Type UIHolderType => typeof(T);

        /// <summary>
        /// 关闭自身 如果存在缓存 则会强制从缓存中移除
        /// </summary>
        protected void ForceCloseSelf()
        {
            AppServices.App.Require<IUIService>().CloseUI(RuntimeTypeHandler, true);
        }

        protected void CloseSelf()
        {
            AppServices.App.Require<IUIService>().CloseUI(RuntimeTypeHandler, false);
        }

        internal sealed override void BindUIHolder(UIHolderObjectBase holder, UIBase owner)
        {
            if (_state != UIState.CreatedUI)
            {
                Log.Error("Cannot bind UI holder because UI has already been created.");
                return;
            }

            BindHolderCommon(holder, owner == null, true);
        }
    }
}
