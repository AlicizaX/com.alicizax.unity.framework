using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// 徽标/计数角标：背景、圆角、色调都在容器上，文字放在内部无背景的 Label 里。
    /// 带背景的 Label 无法被 <see cref="AlicizaEditorText"/> 接管（隐藏原生字形的 opacity 会连背景一起隐藏），
    /// 拆开后文字同样走 hinted 位图绘制。颜色、字号、字重、对齐都由容器继承给内部 Label。
    /// </summary>
    public sealed class AlicizaBadge : VisualElement
    {
        public const string UssClassName = "alicia-badge";
        public const string TextUssClassName = "alicia-badge__text";

        private readonly Label _label;

        /// <param name="text">徽标文字。</param>
        /// <param name="ussClassName">容器样式类（决定背景/尺寸），默认 alicia-badge。</param>
        public AlicizaBadge(string text = null, string ussClassName = UssClassName)
        {
            AddToClassList(ussClassName);
            _label = new Label(text ?? string.Empty) { name = "alicia-badge-text", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(TextUssClassName);
            Add(_label);
        }

        /// <summary>文字内容；与 Label.text 同名，原先按 Label 使用的代码不用改。</summary>
        public string text
        {
            get => _label.text;
            set => _label.text = value ?? string.Empty;
        }

        /// <summary>内部承载文字的 Label。</summary>
        public Label TextLabel => _label;
    }
}
