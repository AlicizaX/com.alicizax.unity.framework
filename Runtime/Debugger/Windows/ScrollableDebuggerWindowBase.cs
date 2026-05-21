using System;
using System.Collections.Generic;
using AlicizaX;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private abstract class ScrollableDebuggerWindowBase : IDebuggerWindow
        {
            private readonly List<VisualElement> _dynamicContent = new List<VisualElement>();
            private ScrollView _scrollView;
            private VisualElement _contentRoot;

            public virtual void Initialize(params object[] args)
            {
            }

            public virtual void Shutdown()
            {
            }

            public virtual void OnEnter()
            {
            }

            public virtual void OnLeave()
            {
            }

            public virtual void OnUpdate(float elapseSeconds, float realElapseSeconds)
            {
            }

            public virtual VisualElement CreateView()
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                _scrollView = new ScrollView(ScrollViewMode.Vertical)
                {
                    name = "debugger-scroll-view"
                };
                _scrollView.mode = ScrollViewMode.Vertical;
                _scrollView.style.flexGrow = 1f;
                _scrollView.style.flexShrink = 1f;
                _scrollView.style.paddingLeft = 12f * scale;
                _scrollView.style.paddingRight = 12f * scale;
                _scrollView.style.paddingTop = 12f * scale;
                _scrollView.style.paddingBottom = 12f * scale;
                _scrollView.contentContainer.style.flexDirection = FlexDirection.Column;
                _scrollView.usageHints = UsageHints.DynamicTransform;
                StyleScrollView(_scrollView, scale);

                _contentRoot = new VisualElement
                {
                    name = "debugger-window-content"
                };
                _contentRoot.style.flexDirection = FlexDirection.Column;
                _contentRoot.usageHints = UsageHints.DynamicTransform;

                _scrollView.Add(_contentRoot);
                BuildWindow(_contentRoot);
                return _scrollView;
            }

            protected abstract void BuildWindow(VisualElement root);

            protected void Rebuild()
            {
                if (_contentRoot == null)
                {
                    return;
                }

                _contentRoot.Clear();
                _dynamicContent.Clear();
                BuildWindow(_contentRoot);
            }

            internal static VisualElement CreateSection(string title)
            {
                return CreateSection(title, out _);
            }

            internal static VisualElement CreateSection(string title, out VisualElement card)
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                VisualElement section = new VisualElement();
                section.style.flexDirection = FlexDirection.Column;
                section.style.marginBottom = 10f * scale;

                if (!string.IsNullOrEmpty(title))
                {
                    Label titleLabel = new Label(title);
                    titleLabel.style.color = DebuggerTheme.PrimaryText;
                    titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    titleLabel.style.fontSize = 24f * scale;
                    titleLabel.style.marginBottom = 3f * scale;
                    section.Add(titleLabel);
                }

                card = CreateCard();
                section.Add(card);
                return section;
            }

            internal static VisualElement CreateCard()
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                VisualElement card = new VisualElement();
                card.style.flexDirection = FlexDirection.Column;
                card.style.paddingLeft = 12f * scale;
                card.style.paddingRight = 12f * scale;
                card.style.paddingTop = 12f * scale;
                card.style.paddingBottom = 12f * scale;
                card.style.borderTopLeftRadius = 0f;
                card.style.borderTopRightRadius = 0f;
                card.style.borderBottomLeftRadius = 0f;
                card.style.borderBottomRightRadius = 0f;
                card.style.backgroundColor = DebuggerTheme.PanelSurface;
                card.style.borderTopWidth = 1f;
                card.style.borderRightWidth = 1f;
                card.style.borderBottomWidth = 1f;
                card.style.borderLeftWidth = 1f;
                card.style.borderTopColor = DebuggerTheme.Border;
                card.style.borderRightColor = DebuggerTheme.Border;
                card.style.borderBottomColor = DebuggerTheme.Border;
                card.style.borderLeftColor = DebuggerTheme.Border;
                card.style.unityBackgroundImageTintColor = DebuggerTheme.PanelSurface;
                card.style.marginBottom = 2f * scale;
                card.usageHints = UsageHints.DynamicTransform;
                return card;
            }

            internal static VisualElement CreateRow(string title, string content)
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.minHeight = 36f * scale;
                row.style.marginBottom = 4f * scale;

                Label titleLabel = new Label(title);
                titleLabel.style.minWidth = 280f * scale;
                titleLabel.style.maxWidth = 280f * scale;
                titleLabel.style.color = DebuggerTheme.SecondaryText;
                titleLabel.style.fontSize = 18f * scale;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.flexShrink = 0f;
                titleLabel.style.whiteSpace = WhiteSpace.Normal;

                Button contentButton = CreateGhostButton(content);
                contentButton.clicked += () => CopyToClipboard(content);
                contentButton.style.flexGrow = 1f;
                contentButton.style.justifyContent = Justify.FlexStart;
                contentButton.style.unityTextAlign = TextAnchor.MiddleLeft;

                row.Add(titleLabel);
                row.Add(contentButton);
                return row;
            }

            internal static Button CreateGhostButton(string text)
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                Button button = new Button
                {
                    text = text ?? string.Empty
                };
                button.style.height = 32f * scale;
                button.style.minHeight = 32f * scale;
                button.style.backgroundColor = Color.clear;
                button.style.color = DebuggerTheme.PrimaryText;
                button.style.borderTopWidth = 0f;
                button.style.borderRightWidth = 0f;
                button.style.borderBottomWidth = 0f;
                button.style.borderLeftWidth = 0f;
                button.style.paddingLeft = 0f;
                button.style.paddingRight = 0f;
                button.style.fontSize = 18f * scale;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.whiteSpace = WhiteSpace.Normal;
                ApplyButtonStateStyles(
                    button,
                    Color.clear,
                    DebuggerTheme.GhostHover,
                    DebuggerTheme.GhostPressed,
                    DebuggerTheme.PrimaryText,
                    DebuggerTheme.PrimaryText,
                    DebuggerTheme.PrimaryText);
                return button;
            }

            internal static Button CreateActionButton(string text, Action onClick, Color? background = null, Color? foreground = null)
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                Button button = new Button(onClick)
                {
                    text = text ?? string.Empty
                };
                button.style.height = 38f * scale;
                button.style.minHeight = 38f * scale;
                button.style.paddingLeft = 12f * scale;
                button.style.paddingRight = 12f * scale;
                button.style.borderTopLeftRadius = 6f * scale;
                button.style.borderTopRightRadius = 6f * scale;
                button.style.borderBottomLeftRadius = 6f * scale;
                button.style.borderBottomRightRadius = 6f * scale;
                button.style.borderTopWidth = 0f;
                button.style.borderRightWidth = 0f;
                button.style.borderBottomWidth = 0f;
                button.style.borderLeftWidth = 0f;
                button.style.backgroundColor = background ?? DebuggerTheme.ButtonSurface;
                button.style.color = foreground ?? DebuggerTheme.PrimaryText;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.fontSize = 18f * scale;
                Color defaultBackground = background ?? DebuggerTheme.ButtonSurface;
                Color hoverBackground = ColorEquals(defaultBackground, DebuggerTheme.ButtonSurface)
                    ? DebuggerTheme.ButtonSurfaceHover
                    : TintColor(defaultBackground, 0.12f);
                Color pressedBackground = ColorEquals(defaultBackground, DebuggerTheme.ButtonSurface)
                    ? DebuggerTheme.ButtonSurfacePressed
                    : TintColor(defaultBackground, -0.12f);
                Color defaultForeground = foreground ?? DebuggerTheme.PrimaryText;
                ApplyButtonStateStyles(
                    button,
                    defaultBackground,
                    hoverBackground,
                    pressedBackground,
                    defaultForeground,
                    defaultForeground,
                    defaultForeground);
                return button;
            }

            internal static Toggle CreateToggle(string label, bool value, Action<bool> onValueChanged)
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                Toggle toggle = new Toggle(label)
                {
                    value = value
                };
                toggle.style.color = value ? DebuggerTheme.PrimaryText : DebuggerTheme.SecondaryText;
                toggle.style.fontSize = 18f * scale;
                toggle.style.minHeight = 34f * scale;
                toggle.style.marginRight = 6f * scale;
                toggle.style.marginBottom = 4f * scale;
                toggle.style.paddingLeft = 10f * scale;
                toggle.style.paddingRight = 12f * scale;
                toggle.style.paddingTop = 5f * scale;
                toggle.style.paddingBottom = 5f * scale;
                toggle.style.backgroundColor = value ? DebuggerTheme.ToggleSurfaceActive : DebuggerTheme.ToggleSurface;
                toggle.style.borderTopLeftRadius = 0f;
                toggle.style.borderTopRightRadius = 0f;
                toggle.style.borderBottomLeftRadius = 0f;
                toggle.style.borderBottomRightRadius = 0f;
                toggle.style.borderTopWidth = 0f;
                toggle.style.borderRightWidth = 0f;
                toggle.style.borderBottomWidth = 0f;
                toggle.style.borderLeftWidth = value ? 2f * scale : 0f;
                toggle.style.borderLeftColor = value ? DebuggerTheme.Accent : Color.clear;
                StyleToggle(toggle, scale);
                ApplyToggleStateStyles(toggle, scale);
                toggle.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
                return toggle;
            }

            internal static Toggle CreateConsoleFilterToggle(string label, bool value, Color indicatorColor, Action<bool> onValueChanged)
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                Toggle toggle = new Toggle()
                {
                    value = value
                };
                toggle.style.flexDirection = FlexDirection.Row;
                toggle.style.alignItems = Align.Center;
                toggle.style.height = 30f * scale;
                toggle.style.minHeight = 30f * scale;
                toggle.style.marginRight = 4f * scale;
                toggle.style.marginBottom = 0f;
                toggle.style.paddingLeft = 8f * scale;
                toggle.style.paddingRight = 10f * scale;
                toggle.style.paddingTop = 0f;
                toggle.style.paddingBottom = 0f;
                toggle.style.backgroundColor = value ? DebuggerTheme.ToggleSurfaceActive : DebuggerTheme.ToggleSurface;
                toggle.style.borderTopLeftRadius = 0f;
                toggle.style.borderTopRightRadius = 0f;
                toggle.style.borderBottomLeftRadius = 0f;
                toggle.style.borderBottomRightRadius = 0f;
                toggle.style.borderTopWidth = 0f;
                toggle.style.borderRightWidth = 0f;
                toggle.style.borderBottomWidth = 0f;
                toggle.style.borderLeftWidth = 0f;

                VisualElement input = toggle.Q(className: "unity-toggle__input");
                if (input != null)
                {
                    input.style.display = DisplayStyle.None;
                }

                VisualElement dot = new VisualElement();
                dot.style.width = 8f * scale;
                dot.style.height = 8f * scale;
                dot.style.borderTopLeftRadius = 4f * scale;
                dot.style.borderTopRightRadius = 4f * scale;
                dot.style.borderBottomLeftRadius = 4f * scale;
                dot.style.borderBottomRightRadius = 4f * scale;
                dot.style.backgroundColor = value ? indicatorColor : DebuggerTheme.SecondaryText;
                dot.style.marginRight = 6f * scale;
                dot.style.opacity = value ? 1f : 0.4f;
                dot.pickingMode = PickingMode.Ignore;
                toggle.Insert(0, dot);

                Label textLabel = new Label(label);
                textLabel.style.fontSize = 16f * scale;
                textLabel.style.color = value ? DebuggerTheme.PrimaryText : DebuggerTheme.SecondaryText;
                textLabel.pickingMode = PickingMode.Ignore;
                toggle.Add(textLabel);

                toggle.RegisterValueChangedCallback(evt =>
                {
                    bool v = evt.newValue;
                    dot.style.backgroundColor = v ? indicatorColor : DebuggerTheme.SecondaryText;
                    dot.style.opacity = v ? 1f : 0.4f;
                    textLabel.style.color = v ? DebuggerTheme.PrimaryText : DebuggerTheme.SecondaryText;
                    toggle.style.backgroundColor = v ? DebuggerTheme.ToggleSurfaceActive : DebuggerTheme.ToggleSurface;
                    onValueChanged?.Invoke(v);
                });

                bool isHovered = false;
                bool isPressed = false;
                void ApplyState()
                {
                    bool v = toggle.value;
                    toggle.style.backgroundColor = isPressed
                        ? v ? DebuggerTheme.ToggleSurfaceActivePressed : DebuggerTheme.ToggleSurfacePressed
                        : isHovered
                            ? v ? DebuggerTheme.ToggleSurfaceActiveHover : DebuggerTheme.ToggleSurfaceHover
                            : v ? DebuggerTheme.ToggleSurfaceActive : DebuggerTheme.ToggleSurface;
                }
                toggle.RegisterCallback<PointerEnterEvent>(_ => { isHovered = true; ApplyState(); });
                toggle.RegisterCallback<PointerLeaveEvent>(_ => { isHovered = false; isPressed = false; ApplyState(); });
                toggle.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) { isPressed = true; ApplyState(); } });
                toggle.RegisterCallback<PointerUpEvent>(_ => { isPressed = false; ApplyState(); });

                return toggle;
            }

            internal static Slider CreateSlider(float min, float max, float value, Action<float> onValueChanged)
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                Slider slider = new Slider(min, max)
                {
                    value = value
                };
                slider.style.flexGrow = 1f;
                slider.style.minHeight = 28f * scale;
                StyleSlider(slider, scale);
                ApplyButtonStateStyles(
                    slider,
                    Color.clear,
                    Color.clear,
                    Color.clear,
                    DebuggerTheme.PrimaryText,
                    DebuggerTheme.PrimaryText,
                    DebuggerTheme.PrimaryText);
                slider.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
                return slider;
            }

            internal static TextField CreateReadOnlyMultilineText(string value)
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                TextField textField = new TextField
                {
                    multiline = true,
                    isReadOnly = true,
                    value = value ?? string.Empty
                };
                textField.style.minHeight = 140f * scale;
                textField.style.whiteSpace = WhiteSpace.Normal;
                textField.style.flexGrow = 1f;
                textField.style.color = DebuggerTheme.PrimaryText;
                textField.style.fontSize = 18f * scale;
                textField.style.paddingLeft = 12f * scale;
                textField.style.paddingRight = 12f * scale;
                textField.style.paddingTop = 10f * scale;
                textField.style.paddingBottom = 10f * scale;
                textField.style.backgroundColor = DebuggerTheme.PanelSurfaceStrong;
                textField.style.borderTopLeftRadius = 0f;
                textField.style.borderTopRightRadius = 0f;
                textField.style.borderBottomLeftRadius = 0f;
                textField.style.borderBottomRightRadius = 0f;
                textField.style.borderTopWidth = 1f;
                textField.style.borderRightWidth = 1f;
                textField.style.borderBottomWidth = 1f;
                textField.style.borderLeftWidth = 1f;
                textField.style.borderTopColor = DebuggerTheme.Border;
                textField.style.borderRightColor = DebuggerTheme.Border;
                textField.style.borderBottomColor = DebuggerTheme.Border;
                textField.style.borderLeftColor = DebuggerTheme.Border;
                textField.schedule.Execute(() => StyleReadOnlyTextFieldInput(textField, scale)).ExecuteLater(0);
                return textField;
            }

            internal static VisualElement CreateToolbarRow()
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.flexWrap = Wrap.Wrap;
                row.style.marginBottom = 4f * scale;
                return row;
            }

            internal static void StyleToggle(Toggle toggle, float scale)
            {
                if (toggle == null)
                {
                    return;
                }

                VisualElement input = toggle.Q(className: "unity-toggle__input");
                if (input != null)
                {
                    input.style.width = 14f * scale;
                    input.style.minWidth = 14f * scale;
                    input.style.height = 14f * scale;
                    input.style.minHeight = 14f * scale;
                    input.style.backgroundColor = DebuggerTheme.PanelSurfaceStrong;
                    input.style.borderTopLeftRadius = 2f * scale;
                    input.style.borderTopRightRadius = 2f * scale;
                    input.style.borderBottomLeftRadius = 2f * scale;
                    input.style.borderBottomRightRadius = 2f * scale;
                    input.style.borderTopWidth = 1f;
                    input.style.borderRightWidth = 1f;
                    input.style.borderBottomWidth = 1f;
                    input.style.borderLeftWidth = 1f;
                    input.style.borderTopColor = DebuggerTheme.Border;
                    input.style.borderRightColor = DebuggerTheme.Border;
                    input.style.borderBottomColor = DebuggerTheme.Border;
                    input.style.borderLeftColor = DebuggerTheme.Border;
                    input.style.marginRight = 8f * scale;
                }

                VisualElement checkmark = toggle.Q(className: "unity-toggle__checkmark");
                if (checkmark != null)
                {
                    checkmark.style.borderBottomColor = DebuggerTheme.Accent;
                    checkmark.style.borderRightColor = DebuggerTheme.Accent;
                }

                toggle.schedule.Execute(() => RefreshToggleVisualState(toggle, scale)).ExecuteLater(0);
            }

            internal static void StyleSlider(Slider slider, float scale)
            {
                if (slider == null)
                {
                    return;
                }

                VisualElement tracker = slider.Q(className: "unity-base-slider__tracker");
                if (tracker != null)
                {
                    tracker.style.height = 6f * scale;
                    tracker.style.backgroundColor = DebuggerTheme.PanelSurfaceStrong;
                    tracker.style.borderTopLeftRadius = 999f;
                    tracker.style.borderTopRightRadius = 999f;
                    tracker.style.borderBottomLeftRadius = 999f;
                    tracker.style.borderBottomRightRadius = 999f;
                }

                VisualElement dragger = slider.Q(className: "unity-base-slider__dragger");
                if (dragger != null)
                {
                    dragger.style.width = 12f * scale;
                    dragger.style.height = 12f * scale;
                    dragger.style.backgroundColor = DebuggerTheme.Accent;
                    dragger.style.borderTopLeftRadius = 999f;
                    dragger.style.borderTopRightRadius = 999f;
                    dragger.style.borderBottomLeftRadius = 999f;
                    dragger.style.borderBottomRightRadius = 999f;
                    dragger.style.borderTopWidth = 1f;
                    dragger.style.borderRightWidth = 1f;
                    dragger.style.borderBottomWidth = 1f;
                    dragger.style.borderLeftWidth = 1f;
                    dragger.style.borderTopColor = DebuggerTheme.SelectionBorder;
                    dragger.style.borderRightColor = DebuggerTheme.SelectionBorder;
                    dragger.style.borderBottomColor = DebuggerTheme.SelectionBorder;
                    dragger.style.borderLeftColor = DebuggerTheme.SelectionBorder;
                }

                slider.RegisterCallback<PointerEnterEvent>(_ => UpdateSliderDragVisual(slider, scale, false));
                slider.RegisterCallback<PointerLeaveEvent>(_ => UpdateSliderDragVisual(slider, scale, false));
                slider.RegisterCallback<PointerDownEvent>(_ => UpdateSliderDragVisual(slider, scale, true));
                slider.RegisterCallback<PointerUpEvent>(_ => UpdateSliderDragVisual(slider, scale, false));
                slider.RegisterValueChangedCallback(_ => UpdateSliderDragVisual(slider, scale, false));
                UpdateSliderDragVisual(slider, scale, false);
            }

            internal static void StyleScrollView(ScrollView scrollView, float scale)
            {
                if (scrollView == null)
                {
                    return;
                }

                scrollView.style.backgroundColor = Color.clear;
                scrollView.contentContainer.style.backgroundColor = Color.clear;
                scrollView.mouseWheelScrollSize = 240f * scale;
                StyleScroller(scrollView.verticalScroller, scale);
                StyleScroller(scrollView.horizontalScroller, scale);
            }

            internal static void StyleScrollers(VisualElement root, float scale)
            {
                if (root == null)
                {
                    return;
                }

                root.Query<Scroller>().ForEach(scroller => StyleScroller(scroller, scale));
            }

            internal static void StyleScroller(Scroller scroller, float scale)
            {
                if (scroller == null)
                {
                    return;
                }

                const float ThumbRadius = 3f;
                bool isHorizontal = scroller.direction == SliderDirection.Horizontal;
                scroller.style.display = DisplayStyle.Flex;
                scroller.style.visibility = Visibility.Visible;
                scroller.style.opacity = 1f;
                scroller.style.width = isHorizontal ? StyleKeyword.Auto : 6f * scale;
                scroller.style.minWidth = isHorizontal ? 0f : 6f * scale;
                scroller.style.maxWidth = isHorizontal ? StyleKeyword.None : 6f * scale;
                scroller.style.height = isHorizontal ? 6f * scale : StyleKeyword.Auto;
                scroller.style.minHeight = isHorizontal ? 6f * scale : 0f;
                scroller.style.maxHeight = isHorizontal ? 6f * scale : StyleKeyword.None;
                scroller.style.marginLeft = 1f * scale;
                scroller.style.marginRight = 1f * scale;
                scroller.style.marginTop = 1f * scale;
                scroller.style.marginBottom = 1f * scale;
                scroller.style.paddingLeft = 0f;
                scroller.style.paddingRight = 0f;
                scroller.style.paddingTop = 0f;
                scroller.style.paddingBottom = 0f;
                scroller.style.backgroundColor = Color.clear;
                scroller.style.borderTopWidth = 0f;
                scroller.style.borderRightWidth = 0f;
                scroller.style.borderBottomWidth = 0f;
                scroller.style.borderLeftWidth = 0f;
                scroller.style.borderTopLeftRadius = ThumbRadius * scale;
                scroller.style.borderTopRightRadius = ThumbRadius * scale;
                scroller.style.borderBottomLeftRadius = ThumbRadius * scale;
                scroller.style.borderBottomRightRadius = ThumbRadius * scale;

                VisualElement lowButton = scroller.Q(className: "unity-scroller__low-button");
                if (lowButton != null)
                {
                    lowButton.style.display = DisplayStyle.None;
                }

                VisualElement highButton = scroller.Q(className: "unity-scroller__high-button");
                if (highButton != null)
                {
                    highButton.style.display = DisplayStyle.None;
                }

                Slider slider = scroller.slider;
                if (slider != null)
                {
                    slider.style.display = DisplayStyle.Flex;
                    slider.style.visibility = Visibility.Visible;
                    slider.style.opacity = 1f;
                    slider.style.flexGrow = 1f;
                    slider.style.minHeight = 0f;
                    slider.style.minWidth = 0f;
                    slider.style.backgroundColor = Color.clear;
                    slider.style.borderTopLeftRadius = ThumbRadius * scale;
                    slider.style.borderTopRightRadius = ThumbRadius * scale;
                    slider.style.borderBottomLeftRadius = ThumbRadius * scale;
                    slider.style.borderBottomRightRadius = ThumbRadius * scale;
                    slider.style.borderTopWidth = 0f;
                    slider.style.borderRightWidth = 0f;
                    slider.style.borderBottomWidth = 0f;
                    slider.style.borderLeftWidth = 0f;
                    slider.style.paddingLeft = 0f;
                    slider.style.paddingRight = 0f;
                    slider.style.paddingTop = 0f;
                    slider.style.paddingBottom = 0f;

                    VisualElement dragger = slider.Q(className: "unity-dragger") ?? slider.Q(className: "unity-base-slider__dragger");
                    if (dragger != null)
                    {
                        dragger.style.display = DisplayStyle.Flex;
                        dragger.style.visibility = Visibility.Visible;
                        dragger.style.opacity = 1f;
                        dragger.style.backgroundColor = DebuggerTheme.ScrollbarThumb;
                        dragger.style.borderTopLeftRadius = ThumbRadius * scale;
                        dragger.style.borderTopRightRadius = ThumbRadius * scale;
                        dragger.style.borderBottomLeftRadius = ThumbRadius * scale;
                        dragger.style.borderBottomRightRadius = ThumbRadius * scale;
                        dragger.style.borderTopWidth = 0f;
                        dragger.style.borderRightWidth = 0f;
                        dragger.style.borderBottomWidth = 0f;
                        dragger.style.borderLeftWidth = 0f;
                        ApplyButtonStateStyles(
                            dragger,
                            DebuggerTheme.ScrollbarThumb,
                            DebuggerTheme.ScrollbarThumbHover,
                            DebuggerTheme.ScrollbarThumbPressed,
                            DebuggerTheme.PrimaryText,
                            DebuggerTheme.PrimaryText,
                            DebuggerTheme.PrimaryText);
                    }
                }
            }

            internal static void ApplyButtonStateStyles(VisualElement element, Color normalBackground, Color hoverBackground, Color pressedBackground, Color normalText, Color hoverText, Color pressedText)
            {
                if (element == null)
                {
                    return;
                }

                bool isHovered = false;
                bool isPressed = false;

                void Apply()
                {
                    element.style.backgroundColor = isPressed ? pressedBackground : isHovered ? hoverBackground : normalBackground;
                    element.style.color = isPressed ? pressedText : isHovered ? hoverText : normalText;
                }

                element.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    isHovered = true;
                    Apply();
                });
                element.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    isHovered = false;
                    isPressed = false;
                    Apply();
                });
                element.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    isPressed = true;
                    Apply();
                });
                element.RegisterCallback<PointerUpEvent>(_ =>
                {
                    isPressed = false;
                    Apply();
                });
                element.RegisterCallback<PointerCancelEvent>(_ =>
                {
                    isPressed = false;
                    isHovered = false;
                    Apply();
                });

                Apply();
            }

            internal static void ApplySelectableStateStyles(VisualElement element, Color normalText, Color inactiveText, Color hoverBackground, Color pressedBackground, Color selectedBackground, Color selectedText)
            {
                if (element == null)
                {
                    return;
                }

                bool isHovered = false;
                bool isPressed = false;

                void Apply()
                {
                    bool isSelected = element is BaseBoolField boolField && boolField.value;
                    element.style.backgroundColor = isPressed
                        ? pressedBackground
                        : isSelected
                            ? selectedBackground
                            : isHovered
                                ? hoverBackground
                                : Color.clear;
                    element.style.color = isSelected ? selectedText : isHovered ? normalText : inactiveText;
                }

                element.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    isHovered = true;
                    Apply();
                });
                element.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    isHovered = false;
                    isPressed = false;
                    Apply();
                });
                element.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    isPressed = true;
                    Apply();
                });
                element.RegisterCallback<PointerUpEvent>(_ =>
                {
                    isPressed = false;
                    Apply();
                });
                element.RegisterCallback<PointerCancelEvent>(_ =>
                {
                    isPressed = false;
                    isHovered = false;
                    Apply();
                });

                Apply();
            }

            internal static void ApplyToggleStateStyles(Toggle toggle, float scale)
            {
                if (toggle == null)
                {
                    return;
                }

                bool isHovered = false;
                bool isPressed = false;

                void Apply()
                {
                    RefreshToggleVisualState(toggle, scale, isHovered, isPressed);
                }

                toggle.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    isHovered = true;
                    Apply();
                });
                toggle.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    isHovered = false;
                    isPressed = false;
                    Apply();
                });
                toggle.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    isPressed = true;
                    Apply();
                });
                toggle.RegisterCallback<PointerUpEvent>(_ =>
                {
                    isPressed = false;
                    Apply();
                });
                toggle.RegisterCallback<PointerCancelEvent>(_ =>
                {
                    isPressed = false;
                    isHovered = false;
                    Apply();
                });
                toggle.RegisterValueChangedCallback(_ => Apply());

                Apply();
            }

            internal static void StyleReadOnlyTextFieldInput(TextField textField, float scale)
            {
                if (textField == null)
                {
                    return;
                }

                VisualElement input = textField.Q(className: "unity-base-text-field__input") ?? textField.Q(className: "unity-text-field__input");
                if (input != null)
                {
                    input.style.backgroundColor = Color.clear;
                    input.style.unityBackgroundImageTintColor = Color.clear;
                    input.style.color = DebuggerTheme.PrimaryText;
                    input.style.fontSize = 18f * scale;
                    input.style.whiteSpace = WhiteSpace.Normal;
                    input.style.borderTopWidth = 0f;
                    input.style.borderRightWidth = 0f;
                    input.style.borderBottomWidth = 0f;
                    input.style.borderLeftWidth = 0f;
                    input.style.paddingLeft = 0f;
                    input.style.paddingRight = 0f;
                    input.style.paddingTop = 0f;
                    input.style.paddingBottom = 0f;
                    input.style.marginLeft = 0f;
                    input.style.marginRight = 0f;
                    input.style.marginTop = 0f;
                    input.style.marginBottom = 0f;
                }

                ScrollView innerScrollView = textField.Q<ScrollView>();
                if (innerScrollView != null)
                {
                    innerScrollView.style.backgroundColor = Color.clear;
                    innerScrollView.contentContainer.style.backgroundColor = Color.clear;
                    StyleScrollView(innerScrollView, scale);
                }
            }

            internal static void RefreshToggleVisualState(Toggle toggle, float scale)
            {
                RefreshToggleVisualState(toggle, scale, false, false);
            }

            private static void RefreshToggleVisualState(Toggle toggle, float scale, bool isHovered, bool isPressed)
            {
                if (toggle == null)
                {
                    return;
                }

                bool value = toggle.value;
                toggle.style.backgroundColor = isPressed
                    ? value ? DebuggerTheme.ToggleSurfaceActivePressed : DebuggerTheme.ToggleSurfacePressed
                    : isHovered
                        ? value ? DebuggerTheme.ToggleSurfaceActiveHover : DebuggerTheme.ToggleSurfaceHover
                        : value ? DebuggerTheme.ToggleSurfaceActive : DebuggerTheme.ToggleSurface;
                toggle.style.borderTopWidth = 0f;
                toggle.style.borderRightWidth = 0f;
                toggle.style.borderBottomWidth = 0f;
                toggle.style.borderLeftWidth = value ? 2f * scale : 0f;
                toggle.style.borderLeftColor = value ? DebuggerTheme.Accent : Color.clear;
                toggle.style.color = value || isHovered ? DebuggerTheme.PrimaryText : DebuggerTheme.SecondaryText;

                VisualElement input = toggle.Q(className: "unity-toggle__input");
                if (input != null)
                {
                    input.style.backgroundColor = value ? DebuggerTheme.Accent : DebuggerTheme.PanelSurfaceStrong;
                    Color borderColor = value ? DebuggerTheme.Accent : DebuggerTheme.Border;
                    input.style.borderTopColor = borderColor;
                    input.style.borderRightColor = borderColor;
                    input.style.borderBottomColor = borderColor;
                    input.style.borderLeftColor = borderColor;
                }

                Label label = toggle.Q<Label>();
                if (label != null)
                {
                    label.style.fontSize = 18f * scale;
                    label.style.color = value || isHovered ? DebuggerTheme.PrimaryText : DebuggerTheme.SecondaryText;
                }
            }

            private static void UpdateSliderDragVisual(Slider slider, float scale, bool isPressed)
            {
                if (slider == null)
                {
                    return;
                }

                VisualElement tracker = slider.Q(className: "unity-base-slider__tracker");
                if (tracker != null)
                {
                    tracker.style.backgroundColor = isPressed ? DebuggerTheme.ButtonSurfacePressed : DebuggerTheme.PanelSurfaceStrong;
                }

                VisualElement dragger = slider.Q(className: "unity-base-slider__dragger");
                if (dragger != null)
                {
                    dragger.style.width = (isPressed ? 14f : 12f) * scale;
                    dragger.style.height = (isPressed ? 14f : 12f) * scale;
                    dragger.style.backgroundColor = isPressed ? DebuggerTheme.SelectionBorder : DebuggerTheme.Accent;
                }
            }

            private static Color TintColor(Color color, float amount)
            {
                return new Color(
                    Mathf.Clamp01(color.r + amount),
                    Mathf.Clamp01(color.g + amount),
                    Mathf.Clamp01(color.b + amount),
                    color.a);
            }

            private static bool ColorEquals(Color lhs, Color rhs)
            {
                return Mathf.Approximately(lhs.r, rhs.r)
                    && Mathf.Approximately(lhs.g, rhs.g)
                    && Mathf.Approximately(lhs.b, rhs.b)
                    && Mathf.Approximately(lhs.a, rhs.a);
            }

            internal static string GetByteLengthString(long byteLength)
            {
                if (byteLength < 1024L)
                {
                    return Utility.Text.Format("{0} Bytes", byteLength);
                }

                if (byteLength < 1048576L)
                {
                    return Utility.Text.Format("{0:F2} KB", byteLength / 1024f);
                }

                if (byteLength < 1073741824L)
                {
                    return Utility.Text.Format("{0:F2} MB", byteLength / 1048576f);
                }

                if (byteLength < 1099511627776L)
                {
                    return Utility.Text.Format("{0:F2} GB", byteLength / 1073741824f);
                }

                if (byteLength < 1125899906842624L)
                {
                    return Utility.Text.Format("{0:F2} TB", byteLength / 1099511627776f);
                }

                if (byteLength < 1152921504606846976L)
                {
                    return Utility.Text.Format("{0:F2} PB", byteLength / 1125899906842624f);
                }

                return Utility.Text.Format("{0:F2} EB", byteLength / 1152921504606846976f);
            }
        }

        private abstract class PollingDebuggerWindowBase : ScrollableDebuggerWindowBase
        {
            private readonly float _refreshInterval;
            private float _refreshCountdown;

            protected PollingDebuggerWindowBase(float refreshInterval = 0.25f)
            {
                _refreshInterval = Mathf.Max(0.05f, refreshInterval);
            }

            public override void OnEnter()
            {
                base.OnEnter();
                _refreshCountdown = 0f;
                Rebuild();
            }

            public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
            {
                _refreshCountdown -= realElapseSeconds;
                if (_refreshCountdown > 0f)
                {
                    return;
                }

                _refreshCountdown = _refreshInterval;
                Rebuild();
            }
        }
    }
}
