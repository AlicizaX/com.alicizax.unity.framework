using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// 重绘面板内的静态 Label，让小字号也清晰。
    /// UI Toolkit 编辑器文字走 TextCore SDF（90pt 采样、无 hinting），IMGUI 在 10px 下同样发虚；
    /// 这里用 FreeType hinted 位图 + 对比度曲线自建字形图集（见 <see cref="AlicizaGlyphAtlas"/>），
    /// 每次面板重绘把整棵树的文字拼成一个网格一次画完，坐标对齐到设备像素。
    /// 隐藏原生字形由 AlicizaEditor.uss 负责（host 下 .unity-label:enabled 的 opacity 为 0），
    /// 新建/重建的 Label 第一帧就是隐藏的，覆盖层在同一帧按 --alicia-imgui-text 标记把它们画出来。
    /// 富文本、所有字体都缺的字符、没有对应字重的字体退回 IMGUI 绘制。
    /// </summary>
    public static class AlicizaEditorText
    {
        /// <summary>覆盖层正在接管文字的根节点（USS 据此隐藏原生字形）；原生 Bitmap 模式下会被撤掉。</summary>
        public const string HostUssClassName = "alicia-imgui-text-host";

        /// <summary>挂了覆盖层的根节点（常驻）。嵌套根节点里的 Label 由内层负责。</summary>
        public const string RootUssClassName = "alicia-imgui-text-root";

        /// <summary>加在 Label 或其祖先上，保留 UI Toolkit 原生文字渲染。</summary>
        public const string OptOutUssClassName = "alicia-uitk-text";

        /// <summary>默认对比度（覆盖率的 gamma），越大笔画越实。</summary>
        public const float DefaultContrast = 1.5f;

        private const string EnabledPrefKey = "AlicizaX.EditorUI.CrispText";
        private const string ContrastPrefKey = "AlicizaX.EditorUI.CrispTextContrast";
        private const int ElideCacheCapacity = 512;

        private static readonly CustomStyleProperty<float> s_ManagedProperty = new CustomStyleProperty<float>("--alicia-imgui-text");
        private static readonly GUIContent s_Content = new GUIContent();
        private static readonly Func<VisualElement, Rect> s_WorldClip = CreateWorldClipGetter();
        private static readonly bool[] s_WrapByWhiteSpace = BuildWrapTable();
        private static readonly List<PageBatch> s_Batches = new List<PageBatch>();
        private static readonly Dictionary<ElideKey, bool> s_ElideDecisions = new Dictionary<ElideKey, bool>();
        private static GUIStyle s_Regular;
        private static GUIStyle s_Bold;

        /// <summary>全局开关（EditorPrefs），修改后重新选中对象生效。</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, true);
            set => EditorPrefs.SetBool(EnabledPrefKey, value);
        }

        /// <summary>对比度（1 = 原始覆盖率，范围 1~2.2），修改后立即重建字形。</summary>
        public static float Contrast
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(ContrastPrefKey, DefaultContrast), 1f, 2.2f);
            set
            {
                EditorPrefs.SetFloat(ContrastPrefKey, Mathf.Clamp(value, 1f, 2.2f));
                AlicizaGlyphAtlas.Reset();
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary>给根节点挂上覆盖层；已挂过时把覆盖层移到最上层（构建完内容后再调一次）。</summary>
        public static void Attach(VisualElement root)
        {
            if (root == null || !Enabled)
            {
                return;
            }

            LabelOverlay existing = FindOverlay(root);
            if (existing != null)
            {
                existing.BringToFront();
                return;
            }

            root.AddToClassList(RootUssClassName);
            root.hierarchy.Add(new LabelOverlay(root));
        }

        /// <summary>
        /// 原生渲染是否已经是 Bitmap（Unity 6.1+ 的 FreeType + 像素对齐）。是则覆盖层让位，全部交给原生；
        /// SDF 或更早的版本继续由覆盖层接管。
        /// </summary>
        /// <param name="element">要判断的元素。</param>
        /// <param name="styleResolved">元素样式是否已解析；刚挂上面板时为 false。</param>
        private static bool UsesNativeBitmap(VisualElement element, bool styleResolved)
        {
#if UNITY_6000_1_OR_NEWER
            if (styleResolved)
            {
                return element.resolvedStyle.unityEditorTextRenderingMode == EditorTextRenderingMode.Bitmap;
            }

            // 刚挂上面板时自身样式还没解析：编辑器把偏好写在窗口根的内联样式上并向下继承，沿祖先找到它，
            // 这样首帧就选对渲染方，不会先画一帧再切换。
            for (VisualElement current = element; current != null; current = current.hierarchy.parent)
            {
                StyleEnum<EditorTextRenderingMode> inline = current.style.unityEditorTextRenderingMode;
                if (inline.keyword == StyleKeyword.Undefined)
                {
                    return inline.value == EditorTextRenderingMode.Bitmap;
                }
            }

            return element.resolvedStyle.unityEditorTextRenderingMode == EditorTextRenderingMode.Bitmap;
#else
            return false;
#endif
        }

        private static LabelOverlay FindOverlay(VisualElement root)
        {
            int count = root.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                if (root.hierarchy[i] is LabelOverlay overlay)
                {
                    return overlay;
                }
            }

            return null;
        }

        // worldClip 是 internal（2022.3 ~ 6.x 均存在）；取不到时退回到只按 ScrollView 视口裁剪。
        private static Func<VisualElement, Rect> CreateWorldClipGetter()
        {
            PropertyInfo property = typeof(VisualElement).GetProperty(
                "worldClip",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getter = property != null && property.PropertyType == typeof(Rect) ? property.GetGetMethod(true) : null;
            if (getter == null)
            {
                return null;
            }

            try
            {
                return (Func<VisualElement, Rect>)Delegate.CreateDelegate(typeof(Func<VisualElement, Rect>), getter);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // 按名字建表：新版本可能新增 WhiteSpace 成员（如 PreWrap），直接引用会在 2022.3 编不过。
        private static bool[] BuildWrapTable()
        {
            Array values = Enum.GetValues(typeof(WhiteSpace));
            int max = 0;
            foreach (object value in values)
            {
                max = Mathf.Max(max, (int)value);
            }

            var table = new bool[max + 1];
            foreach (object value in values)
            {
                int index = (int)value;
                string name = Enum.GetName(typeof(WhiteSpace), value);
                if (index >= 0)
                {
                    table[index] = name == "Normal" || name == "PreWrap";
                }
            }

            return table;
        }

        private static bool IsWrapping(WhiteSpace whiteSpace)
        {
            int index = (int)whiteSpace;
            return index >= 0 && index < s_WrapByWhiteSpace.Length && s_WrapByWhiteSpace[index];
        }

        private static bool IsManaged(Label label)
        {
            // 用户内联设置的 opacity 优先，不接管。
            return label.resolvedStyle.opacity <= 0f
                   && label.style.opacity.keyword == StyleKeyword.Null
                   && label.customStyle.TryGetValue(s_ManagedProperty, out float managed)
                   && managed > 0.5f;
        }

        private static AlicizaGlyphAtlas.Face ResolveFace(IResolvedStyle resolved, int pixelSize, out bool bold)
        {
            FontStyle fontStyle = resolved.unityFontStyleAndWeight;
            bold = fontStyle == FontStyle.Bold || fontStyle == FontStyle.BoldAndItalic;
            bool italic = fontStyle == FontStyle.Italic || fontStyle == FontStyle.BoldAndItalic;

            FontDefinition definition = resolved.unityFontDefinition;
            TextCoreFontAsset asset = definition.fontAsset;
            Font font;
            if (asset != null)
            {
                TextCoreFontAsset styled = asset;
                if (bold || italic)
                {
                    // 只用字体自带的字重/斜体，不做假粗体；没有就交给 IMGUI。
                    var table = asset.fontWeightTable;
                    int index = bold ? 7 : 4;
                    if (table == null || index >= table.Length)
                    {
                        return null;
                    }

                    styled = italic ? table[index].italicTypeface : table[index].regularTypeface;
                }

                font = styled != null ? styled.sourceFontFile : null;
            }
            else
            {
                font = definition.font;
                if (bold || italic)
                {
                    return null;
                }
            }

            return AlicizaGlyphAtlas.GetFace(font, pixelSize);
        }

        private static bool UitkWouldElide(Label label, string text, IResolvedStyle resolved, float widthPoints)
        {
            var key = new ElideKey(text, resolved.fontSize, resolved.unityFontStyleAndWeight, widthPoints);
            if (s_ElideDecisions.TryGetValue(key, out bool elide))
            {
                return elide;
            }

            // hinted 字形比 SDF 略宽；只有 UI Toolkit 自己也会截断时才截断，避免凭空多出省略号。
            Vector2 measured = label.MeasureTextSize(text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            elide = measured.x > widthPoints + 0.5f;
            if (s_ElideDecisions.Count >= ElideCacheCapacity)
            {
                s_ElideDecisions.Clear();
            }

            s_ElideDecisions[key] = elide;
            return elide;
        }

        private static void AddQuad(int page, Rect rect, Rect uv, Color32 color, Rect clip)
        {
            float x0 = Mathf.Max(rect.xMin, clip.xMin);
            float x1 = Mathf.Min(rect.xMax, clip.xMax);
            float y0 = Mathf.Max(rect.yMin, clip.yMin);
            float y1 = Mathf.Min(rect.yMax, clip.yMax);
            if (x0 >= x1 || y0 >= y1)
            {
                return;
            }

            // 轴对齐四边形在 CPU 上裁剪，UV 按比例收缩，不依赖着色器裁剪。
            float u0 = uv.xMin + (x0 - rect.xMin) / rect.width * uv.width;
            float u1 = uv.xMin + (x1 - rect.xMin) / rect.width * uv.width;
            float vTop = uv.yMax - (y0 - rect.yMin) / rect.height * uv.height;
            float vBottom = uv.yMax - (y1 - rect.yMin) / rect.height * uv.height;

            while (s_Batches.Count <= page)
            {
                s_Batches.Add(new PageBatch());
            }

            PageBatch batch = s_Batches[page];
            int index = batch.Vertices.Count;
            batch.Vertices.Add(new Vector3(x0, y0));
            batch.Vertices.Add(new Vector3(x1, y0));
            batch.Vertices.Add(new Vector3(x1, y1));
            batch.Vertices.Add(new Vector3(x0, y1));
            batch.Uvs.Add(new Vector2(u0, vTop));
            batch.Uvs.Add(new Vector2(u1, vTop));
            batch.Uvs.Add(new Vector2(u1, vBottom));
            batch.Uvs.Add(new Vector2(u0, vBottom));
            batch.Colors.Add(color);
            batch.Colors.Add(color);
            batch.Colors.Add(color);
            batch.Colors.Add(color);
            batch.Indices.Add(index);
            batch.Indices.Add(index + 1);
            batch.Indices.Add(index + 2);
            batch.Indices.Add(index);
            batch.Indices.Add(index + 2);
            batch.Indices.Add(index + 3);
        }

        // 把收集到的四边形写进每页一个网格（覆盖层自己持有，内容不变时直接复用）。
        private static void WriteBatches(List<Mesh> meshes)
        {
            int pageCount = Mathf.Max(s_Batches.Count, meshes.Count);
            for (int page = 0; page < pageCount; page++)
            {
                PageBatch batch = page < s_Batches.Count ? s_Batches[page] : null;
                while (meshes.Count <= page)
                {
                    meshes.Add(null);
                }

                Mesh mesh = meshes[page];
                if (batch == null || batch.Vertices.Count == 0)
                {
                    if (mesh != null)
                    {
                        mesh.Clear();
                    }

                    continue;
                }

                if (mesh == null)
                {
                    mesh = AlicizaGlyphAtlas.CreateMesh();
                    meshes[page] = mesh;
                }

                mesh.Clear();
                mesh.indexFormat = batch.Vertices.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(batch.Vertices);
                mesh.SetUVs(0, batch.Uvs);
                mesh.SetColors(batch.Colors);
                mesh.SetTriangles(batch.Indices, 0, false);
                batch.Clear();
            }
        }

        private static void DrawMeshes(List<Mesh> meshes)
        {
            int pageCount = Mathf.Min(meshes.Count, AlicizaGlyphAtlas.PageCount);
            for (int page = 0; page < pageCount; page++)
            {
                Mesh mesh = meshes[page];
                if (mesh == null || mesh.vertexCount == 0)
                {
                    continue;
                }

                AlicizaGlyphAtlas.GetPage(page).Material.SetPass(0);
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
            }
        }

        private static int Combine(int hash, int value)
        {
            unchecked
            {
                return hash * 31 + value;
            }
        }

        private static int Quantize(float value)
        {
            return Mathf.RoundToInt(value * 64f);
        }

        private static int Horizontal(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    return 1;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return 2;
                default:
                    return 0;
            }
        }

        private static int Vertical(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.MiddleLeft:
                case TextAnchor.MiddleCenter:
                case TextAnchor.MiddleRight:
                    return 1;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    return 2;
                default:
                    return 0;
            }
        }

        private static int Align(int mode, int space)
        {
            return mode == 0 ? 0 : mode == 1 ? Mathf.FloorToInt(space * 0.5f) : space;
        }

        private static GUIStyle GetImguiStyle(IResolvedStyle resolved, bool richText)
        {
            FontStyle fontStyle = resolved.unityFontStyleAndWeight;
            bool bold = fontStyle == FontStyle.Bold || fontStyle == FontStyle.BoldAndItalic;
            GUIStyle style = bold
                ? s_Bold ?? (s_Bold = CreateImguiStyle(EditorStyles.boldLabel))
                : s_Regular ?? (s_Regular = CreateImguiStyle(EditorStyles.label));

            style.fontStyle = fontStyle;
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(resolved.fontSize));
            style.alignment = resolved.unityTextAlign;
            style.wordWrap = IsWrapping(resolved.whiteSpace);
            style.richText = richText;
            return style;
        }

        private static GUIStyle CreateImguiStyle(GUIStyle source)
        {
            var style = new GUIStyle(source)
            {
                padding = new RectOffset(),
                margin = new RectOffset(),
                overflow = new RectOffset(),
                border = new RectOffset(),
                contentOffset = Vector2.zero,
                fixedWidth = 0f,
                fixedHeight = 0f,
                clipping = TextClipping.Overflow,
                imagePosition = ImagePosition.TextOnly
            };
            style.normal.background = null;
            return style;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        private sealed class PageBatch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(1024);
            public readonly List<Vector2> Uvs = new List<Vector2>(1024);
            public readonly List<Color32> Colors = new List<Color32>(1024);
            public readonly List<int> Indices = new List<int>(1536);

            public void Clear()
            {
                Vertices.Clear();
                Uvs.Clear();
                Colors.Clear();
                Indices.Clear();
            }
        }

        private readonly struct ElideKey : IEquatable<ElideKey>
        {
            private readonly string _text;
            private readonly int _fontSize;
            private readonly FontStyle _fontStyle;
            private readonly int _width;

            public ElideKey(string text, float fontSize, FontStyle fontStyle, float width)
            {
                _text = text;
                _fontSize = Mathf.RoundToInt(fontSize * 10f);
                _fontStyle = fontStyle;
                _width = Mathf.RoundToInt(width * 10f);
            }

            public bool Equals(ElideKey other)
            {
                return _fontSize == other._fontSize
                       && _fontStyle == other._fontStyle
                       && _width == other._width
                       && string.Equals(_text, other._text, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ElideKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _text != null ? _text.GetHashCode() : 0;
                    hash = hash * 397 ^ _fontSize;
                    hash = hash * 397 ^ (int)_fontStyle;
                    return hash * 397 ^ _width;
                }
            }
        }

        private struct LabelState
        {
            public Label Label;
            public string Text;
            public IResolvedStyle Resolved;
            public Color Color;
            public Rect Clip;
            public Rect World;

            // 影响最终画面的全部输入；与上一帧相同则直接复用网格。
            public int Hash(int hash)
            {
                FontDefinition font = Resolved.unityFontDefinition;
                hash = Combine(hash, Text.GetHashCode());
                hash = Combine(hash, Quantize(World.x));
                hash = Combine(hash, Quantize(World.y));
                hash = Combine(hash, Quantize(World.width));
                hash = Combine(hash, Quantize(World.height));
                hash = Combine(hash, Quantize(Clip.x));
                hash = Combine(hash, Quantize(Clip.y));
                hash = Combine(hash, Quantize(Clip.width));
                hash = Combine(hash, Quantize(Clip.height));
                Color32 color = Color;
                hash = Combine(hash, color.r | color.g << 8 | color.b << 16 | color.a << 24);
                hash = Combine(hash, Quantize(Resolved.fontSize));
                hash = Combine(hash, (int)Resolved.unityFontStyleAndWeight);
                hash = Combine(hash, (int)Resolved.unityTextAlign);
                hash = Combine(hash, (int)Resolved.whiteSpace);
                hash = Combine(hash, (int)Resolved.textOverflow);
                hash = Combine(hash, Label.enableRichText ? 1 : 0);
                // 用 GetHashCode 而非 GetInstanceID：后者在 Unity 6.5 起过时并报错。
                hash = Combine(hash, font.fontAsset != null ? font.fontAsset.GetHashCode() : 0);
                return Combine(hash, font.font != null ? font.font.GetHashCode() : 0);
            }
        }

        private sealed class LabelOverlay : IMGUIContainer
        {
            private const long WatchIntervalMs = 500;

            private readonly VisualElement _host;
            private readonly List<Label> _labels = new List<Label>();
            private readonly List<LabelState> _states = new List<LabelState>();
            private readonly List<int> _imguiStates = new List<int>();
            private readonly List<Mesh> _meshes = new List<Mesh>();
            private int _signature;
            private int _builtCount = -1;
            private int _atlasVersion = -1;
            private Vector2 _origin;
            private bool _translationOnly;

            public LabelOverlay(VisualElement host)
            {
                _host = host;
                name = "alicia-imgui-text-overlay";
                pickingMode = PickingMode.Ignore;
                focusable = false;
                style.position = Position.Absolute;
                style.left = 0f;
                style.top = 0f;
                style.right = 0f;
                style.bottom = 0f;
                onGUIHandler = DrawLabels;

                // host class 决定 USS 是否隐藏原生字形：覆盖层不在、或原生已是 Bitmap 时必须撤掉。
                RegisterCallback<AttachToPanelEvent>(_ => UpdateMode(false));
                RegisterCallback<DetachFromPanelEvent>(_ => OnDetach());
                host.RegisterCallback<GeometryChangedEvent>(_ => EnsureOnTop());
                // 兜底：同尺寸重建内容、以及运行中切换偏好里的文字渲染模式。
                host.schedule.Execute(EnsureOnTop).Every(WatchIntervalMs);
            }

            // 切换只改 host class；过渡帧里 Label 仍按旧样式隐藏、覆盖层照常绘制，不会出现空白帧。
            private void UpdateMode(bool styleResolved)
            {
                if (panel == null || hierarchy.parent != _host)
                {
                    return;
                }

                if (UsesNativeBitmap(_host, styleResolved))
                {
                    _host.RemoveFromClassList(HostUssClassName);
                }
                else
                {
                    _host.AddToClassList(HostUssClassName);
                }
            }

            private void OnDetach()
            {
                _host.RemoveFromClassList(HostUssClassName);
                for (int i = 0; i < _meshes.Count; i++)
                {
                    AlicizaGlyphAtlas.ReleaseMesh(_meshes[i]);
                }

                _meshes.Clear();
                _builtCount = -1;
            }

            private void EnsureOnTop()
            {
                if (hierarchy.parent != _host)
                {
                    // host.Clear() 会连覆盖层一起移除，这里挂回去。
                    if (_host.panel != null)
                    {
                        _host.hierarchy.Add(this);
                    }

                    return;
                }

                if (_host.hierarchy.IndexOf(this) != _host.hierarchy.childCount - 1)
                {
                    BringToFront();
                }

                UpdateMode(true);
            }

            private void DrawLabels()
            {
                Event current = Event.current;
                if (current == null || current.type != EventType.Repaint)
                {
                    return;
                }

                // 每次重绘现查：周期性重建的 Label 在出现的同一帧就能画出来。
                _labels.Clear();
                _states.Clear();
                _host.Query<Label>().ToList(_labels);
                Rect bounds = contentRect;
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 origin = worldBound.position;
                Matrix4x4 transform = worldTransform;
                _origin = origin;
                _translationOnly = Mathf.Approximately(transform.m00, 1f)
                                   && Mathf.Approximately(transform.m11, 1f)
                                   && Mathf.Approximately(transform.m01, 0f)
                                   && Mathf.Approximately(transform.m10, 0f);
                int signature =Combine(Quantize(pixelsPerPoint), Combine(Quantize(origin.x), Quantize(origin.y)));
                for (int i = 0; i < _labels.Count; i++)
                {
                    Label label = _labels[i];
                    if (IsManaged(label) && TryGetState(label, bounds, out LabelState state))
                    {
                        _states.Add(state);
                        signature = state.Hash(signature);
                    }
                }

                // 绝大多数重绘（悬停、周期性重建同样的内容）文字不变，直接复用上次的网格。
                if (_states.Count != _builtCount || signature != _signature || _atlasVersion != AlicizaGlyphAtlas.Version)
                {
                    Rebuild(pixelsPerPoint, origin);
                    _signature = signature;
                    _builtCount = _states.Count;
                    _atlasVersion = AlicizaGlyphAtlas.Version;
                }

                AlicizaGlyphAtlas.Upload();
                DrawMeshes(_meshes);
                for (int i = 0; i < _imguiStates.Count; i++)
                {
                    int index = _imguiStates[i];
                    if (index < _states.Count)
                    {
                        DrawImgui(_states[index]);
                    }
                }

                _labels.Clear();
                _states.Clear();
            }

            // 覆盖层没有缩放/旋转时世界坐标到本地只差一个平移，省掉每个 Label 一次矩阵变换。
            private Rect ToLocal(Rect world)
            {
                return _translationOnly
                    ? new Rect(world.x - _origin.x, world.y - _origin.y, world.width, world.height)
                    : this.WorldToLocal(world);
            }

            private void Rebuild(float pixelsPerPoint, Vector2 origin)
            {
                _imguiStates.Clear();
                for (int i = 0; i < _states.Count; i++)
                {
                    if (!TryBatch(_states[i], pixelsPerPoint, origin))
                    {
                        _imguiStates.Add(i);
                    }
                }

                WriteBatches(_meshes);
            }

            private bool TryGetState(Label label, Rect bounds, out LabelState state)
            {
                state = default;
                string text = label.text;
                if (string.IsNullOrEmpty(text))
                {
                    return false;
                }

                IResolvedStyle resolved = label.resolvedStyle;
                if (resolved.display == DisplayStyle.None || resolved.visibility == Visibility.Hidden)
                {
                    return false;
                }

                // 一直走到面板根：覆盖层的输出不受祖先 opacity 影响（2022.3 实测），需自己乘进去。
                float alpha = 1f;
                Rect clip = bounds;
                bool underHost = false;
                for (VisualElement element = label.hierarchy.parent; element != null; element = element.hierarchy.parent)
                {
                    if (!underHost)
                    {
                        if (element == _host)
                        {
                            underHost = true;
                        }
                        else if (element.ClassListContains(RootUssClassName))
                        {
                            return false;
                        }
                    }

                    IResolvedStyle ancestor = element.resolvedStyle;
                    if (ancestor.display == DisplayStyle.None)
                    {
                        return false;
                    }

                    alpha *= ancestor.opacity;
                    if (s_WorldClip == null && element.ClassListContains(ScrollView.viewportUssClassName))
                    {
                        clip = Intersect(clip, ToLocal(element.worldBound));
                    }
                }

                Color color = resolved.color;
                color.a *= alpha;
                if (!underHost || color.a <= 0f)
                {
                    return false;
                }

                // 用父节点的裁剪区：Label 自身的 overflow:hidden 交给省略号/换行余量处理，
                // 否则 hinted 字形比 SDF 宽出的 1~2px 会被切掉。
                VisualElement parentElement = label.hierarchy.parent;
                if (s_WorldClip != null && parentElement != null)
                {
                    clip = Intersect(clip, ToLocal(s_WorldClip(parentElement)));
                }

                if (clip.width <= 0f || clip.height <= 0f)
                {
                    return false;
                }

                Rect world = label.LocalToWorld(label.contentRect);
                if (world.width <= 0f || world.height <= 0f || !ToLocal(world).Overlaps(clip))
                {
                    return false;
                }

                state.Label = label;
                state.Text = text;
                state.Resolved = resolved;
                state.Color = color;
                state.Clip = clip;
                state.World = world;
                return true;
            }

            private static bool TryBatch(LabelState state, float pixelsPerPoint, Vector2 origin)
            {
                Label label = state.Label;
                string text = state.Text;
                if (label.enableRichText && text.IndexOf('<') >= 0)
                {
                    return false;
                }

                IResolvedStyle resolved = state.Resolved;
                int pixelSize = Mathf.Max(1, Mathf.RoundToInt(resolved.fontSize * pixelsPerPoint));
                AlicizaGlyphAtlas.Face face = ResolveFace(resolved, pixelSize, out bool bold);
                if (face == null)
                {
                    return false;
                }

                // 以设备像素为单位排版和定位，保证字形纹素与屏幕像素 1:1。
                Rect world = state.World;
                int left = Mathf.RoundToInt(world.xMin * pixelsPerPoint);
                int top = Mathf.RoundToInt(world.yMin * pixelsPerPoint);
                int width = Mathf.RoundToInt(world.width * pixelsPerPoint);
                int height = Mathf.RoundToInt(world.height * pixelsPerPoint);
                int horizontal = Horizontal(resolved.unityTextAlign);
                int vertical = Vertical(resolved.unityTextAlign);

                AlicizaTextLayout layout;
                if (IsWrapping(resolved.whiteSpace))
                {
                    layout = AlicizaTextLayout.Get(text, face, bold, width, AlicizaTextWrap.Wrap);
                    if (layout.Supported && layout.Height > height + 1)
                    {
                        // hinted 字形比 SDF 略宽，按原宽度换行可能多一行；给一点余量让行数和原生一致。
                        int slack = Mathf.Max(Mathf.RoundToInt(4f * pixelsPerPoint), Mathf.RoundToInt(width * 0.08f));
                        AlicizaTextLayout wider = AlicizaTextLayout.Get(text, face, bold, width + slack, AlicizaTextWrap.Wrap);
                        if (wider.Supported && wider.Height <= height + 1)
                        {
                            layout = wider;
                            left -= Align(horizontal, slack);
                            width += slack;
                        }
                    }
                }
                else
                {
                    layout = AlicizaTextLayout.Get(text, face, bold, -1, AlicizaTextWrap.None);
                    if (layout.Supported
                        && resolved.textOverflow == TextOverflow.Ellipsis
                        && layout.Width > width + 1
                        && UitkWouldElide(label, text, resolved, world.width))
                    {
                        layout = AlicizaTextLayout.Get(text, face, bold, width, AlicizaTextWrap.Ellipsis);
                    }
                }

                if (!layout.Supported)
                {
                    return false;
                }

                Color32 color = state.Color;
                float scale = 1f / pixelsPerPoint;
                int y = top + Align(vertical, height - layout.Height);
                for (int line = 0; line < layout.LineCount; line++)
                {
                    int x = left + Align(horizontal, width - layout.LineWidths[line]);
                    int start = layout.LineQuadStart[line];
                    int end = start + layout.LineQuadCount[line];
                    for (int i = start; i < end; i++)
                    {
                        AlicizaTextLayout.Quad quad = layout.Quads[i];
                        var rect = new Rect(
                            (x + quad.X) * scale - origin.x,
                            (y + quad.Y) * scale - origin.y,
                            quad.Width * scale,
                            quad.Height * scale);
                        AddQuad(quad.Page, rect, quad.Uv, color, state.Clip);
                    }
                }

                return true;
            }

            private void DrawImgui(LabelState state)
            {
                Rect rect = ToLocal(state.World);
                GUIStyle style = GetImguiStyle(state.Resolved, state.Label.enableRichText);
                style.normal.textColor = state.Color;
                s_Content.text = state.Text;
                GUI.BeginClip(state.Clip);
                rect.position -= state.Clip.position;
                style.Draw(rect, s_Content, false, false, false, false);
                GUI.EndClip();
            }
        }
    }
}
