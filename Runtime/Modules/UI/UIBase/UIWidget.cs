using System;
using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWidget : UIBase
    {
        internal UIBase _parent;
        internal UIBase Parent => _parent;

        public void Open(params System.Object[] userDatas)
        {
            OpenAsync(userDatas).Forget(LogWidgetException);
        }

        public async UniTask<bool> OpenAsync(params System.Object[] userDatas)
        {
            RefreshParams(userDatas);
            if (State == UIState.Opened)
            {
                InternalRefreshOpened();
                return true;
            }

            return await InternalOpen();
        }

        public void Close()
        {
            CloseAsync().Forget(LogWidgetException);
        }

        public UniTask<bool> CloseAsync()
        {
            return State == UIState.Closed || State == UIState.Closing
                ? UniTask.FromResult(State == UIState.Closed)
                : InternalClose();
        }

        public void Destroy()
        {
            if (Parent != null)
            {
                Parent.RemoveWidget(this).Forget(LogWidgetException);
            }
        }

        private void LogWidgetException(Exception exception)
        {
            Log.Error("[UI] Widget async operation failed for {0}.", CachedTypeName);
            Log.Exception(exception);
        }
    }

    public abstract class UIWidget<T> : UIWidget where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
        internal sealed  override Type UIHolderType => typeof(T);

        internal sealed  override void BindUIHolder(UIHolderObjectBase holder, UIBase owner)
        {
            if (_state != UIState.CreatedUI)
            {
                Log.Error("Cannot bind UI holder because widget has already been created.");
                return;
            }

            _parent = owner;
            BindHolderCommon(holder, false, false);
            Depth = owner.Depth + 5;
        }
    }
}
