using System;
using System.Reflection;
using AlicizaX;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    public abstract class UIWindow<T> : UIBase where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;

        internal sealed override Type UIHolderType => typeof(T);

        /// <summary>
        /// 关闭自身 如果存在缓存 则会强制从缓存中移除
        /// </summary>
        protected void ForceCloseSlef()
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
                throw new InvalidOperationException("UI already Created");
            Holder = holder;
            _canvas = Holder.transform.GetComponent<Canvas>();
            if (_canvas != null)
            {
                _canvas.overrideSorting = owner == null;
            }
            _raycaster = Holder.transform.GetComponent<GraphicRaycaster>();
            Holder.RectTransform.localPosition = Vector3.zero;
            Holder.RectTransform.pivot = new Vector2(0.5f, 0.5f);
            Holder.RectTransform.anchorMin = Vector2.zero;
            Holder.RectTransform.anchorMax = Vector2.one;
            Holder.RectTransform.offsetMin = Vector2.zero;
            Holder.RectTransform.offsetMax = Vector2.zero;
            Holder.RectTransform.localScale = Vector3.one;
            _state = UIState.Loaded;
        }
    }
}
