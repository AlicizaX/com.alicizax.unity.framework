using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    /// <summary>不绘制、只拦截射线的全屏 Graphic。</summary>
    public class UIBlock : Graphic
    {
        public override bool raycastTarget
        {
            get => true;
            set { }
        }

        public override Texture mainTexture => null;

        public override Material materialForRendering => null;

        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        public override void SetAllDirty() { }

        public override void SetLayoutDirty() { }

        public override void SetVerticesDirty() { }

        public override void SetMaterialDirty() { }
    }
}
