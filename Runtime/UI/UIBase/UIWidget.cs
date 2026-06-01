using System;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWidget : UIBase
    {
        internal UIBase _parent;
        internal UIBase Parent => _parent;

        public void Open(params System.Object[] userDatas)
        {
            RefreshParams(userDatas);
            InternalOpenSync();
        }

        public void Close()
        {
            InternalCloseSync();
        }

        public void Destroy()
        {
            Parent.RemoveWidget(this).Forget();
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

            Holder = holder;
            _parent = owner;
            _canvas = Holder.transform.GetComponent<Canvas>();
            if (_canvas != null)
            {
                _canvas.overrideSorting = false;
            }
            _raycaster = Holder.transform.GetComponent<GraphicRaycaster>();
            Depth = owner.Depth + 5;
            SetState(UIState.Loaded);
        }
    }
}
