using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWidget : UIBase
    {
        internal UIBase Parent;
        internal bool OpenIntent;

        public bool AllowOpenTransitionOnParent;
        public bool AllowCloseTransitionOnParent;

        public void Open(params object[] userDatas)
        {
            ValidateOpen();
            if (DestroyRequested) return;
            if (State == UIState.Opening) return;
            OpenIntent = true;
            RefreshParams(userDatas);
            if (Parent.ChildrenCanOpen) InternalOpen();
        }

        internal void OpenFromParent()
        {
            if (OpenIntent && !DestroyRequested) InternalOpen(skipTransition: !AllowOpenTransitionOnParent);
        }

        public void Close()
        {
            if (DestroyRequested) return;
            OpenIntent = false;
            InternalClose().Forget();
        }

        public UICloseHandle Destroy()
        {
            OpenIntent = false;
            return new UICloseHandle(InternalDestroy());
        }

        internal UniTask<bool> CloseFromParent(bool destroy, bool skipTransition)
        {
            if (destroy) return InternalDestroy(skipTransition || !AllowCloseTransitionOnParent);
            if (State == UIState.CreatedUI || State == UIState.Loaded) return UniTask.FromResult(true);
            return InternalClose(skipTransition || !AllowCloseTransitionOnParent);
        }

        internal override void OnFrameworkDestroyed() => Parent.DetachWidget(this);
    }

    public abstract class UIWidget<T> : UIWidget where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
        internal sealed override Type UIHolderType => typeof(T);
        internal sealed override void BindUIHolder(UIHolderObjectBase holder)
        {
            BindHolderCommon(holder, false, false);
        }
    }
}
