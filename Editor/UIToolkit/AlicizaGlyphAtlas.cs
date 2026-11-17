using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using TextCoreCharacter = UnityEngine.TextCore.Text.Character;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;
using AtlasPopulationMode = UnityEngine.TextCore.Text.AtlasPopulationMode;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// 自管的 hinted 位图字形图集。
    /// TextCore FontAsset 只当 FreeType 光栅器用（SMOOTH_HINTED，按实际像素尺寸），字形拷进共享的 Alpha8 页，
    /// 拷贝时一次性套上对比度曲线（浅色字在深色底上笔画更实），绘制时零额外开销。
    /// </summary>
    internal static class AlicizaGlyphAtlas
    {
        public const int PageSize = 1024;

        private const int RasterAtlasSize = 512;
        private const int RasterPadding = 1;
        private const int GlyphSpacing = 1;

        private static readonly Dictionary<FaceKey, Face> s_Faces = new Dictionary<FaceKey, Face>();
        private static readonly Func<TextCoreFontAsset, Dictionary<uint, TextCoreCharacter>> s_CharacterLookup = CreateCharacterLookupGetter();
        private static readonly List<Page> s_Pages = new List<Page>();
        private static readonly byte[] s_Lut = new byte[256];
        private static readonly List<Mesh> s_Meshes = new List<Mesh>();
        private static float s_LutGamma = -1f;
        private static bool s_Hooked;

        public static int PageCount => s_Pages.Count;

        /// <summary>每次 Reset 递增；缓存了 UV 的网格据此判断是否需要重建。</summary>
        public static int Version { get; private set; }

        /// <summary>创建批量绘制用的网格，Reset/域重载时统一销毁。</summary>
        public static Mesh CreateMesh()
        {
            EnsureInitialized();
            var mesh = new Mesh { name = "AlicizaGlyphBatch", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            s_Meshes.Add(mesh);
            return mesh;
        }

        public static void ReleaseMesh(Mesh mesh)
        {
            if (mesh != null && s_Meshes.Remove(mesh))
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        public static Page GetPage(int index)
        {
            return s_Pages[index];
        }

        /// <summary>按 Font 资源取某个像素尺寸的字面；字体没有内嵌数据时返回 null。</summary>
        public static Face GetFace(Font font, int pixelSize)
        {
            if (font == null || s_CharacterLookup == null)
            {
                return null;
            }

            var key = new FaceKey(font, pixelSize);
            if (s_Faces.TryGetValue(key, out Face face))
            {
                return face;
            }

            EnsureInitialized();
            TextCoreFontAsset raster = TextCoreFontAsset.CreateFontAsset(
                font,
                pixelSize,
                RasterPadding,
                GlyphRenderMode.SMOOTH_HINTED,
                RasterAtlasSize,
                RasterAtlasSize,
                AtlasPopulationMode.Dynamic,
                true);
            face = raster != null ? new Face(raster, pixelSize) : null;
            s_Faces[key] = face;
            return face;
        }

        /// <summary>系统 CJK 等后备字面（按字体族名），主字体缺字时按顺序查找；未安装的族直接跳过。</summary>
        public static void GetFallbackFaces(bool bold, int pixelSize, List<Face> result)
        {
            result.Clear();
            if (s_CharacterLookup == null)
            {
                return;
            }

            for (int i = 0; i < s_FallbackFamilies.Length; i++)
            {
                FallbackFamily family = s_FallbackFamilies[i];
                // 缺粗体时退回同族常规体，总比没有后备强。
                Face face = (bold ? GetSystemFace(family.Name, family.BoldStyle, pixelSize) : null)
                            ?? GetSystemFace(family.Name, family.RegularStyle, pixelSize);
                if (face != null)
                {
                    result.Add(face);
                }
            }
        }

        private static Face GetSystemFace(string familyName, string styleName, int pixelSize)
        {
            var key = new FaceKey(familyName + "|" + styleName, pixelSize);
            if (!s_Faces.TryGetValue(key, out Face face))
            {
                EnsureInitialized();
                TextCoreFontAsset raster = SystemFontFactory.Create(familyName, styleName, pixelSize);
                face = raster != null ? new Face(raster, pixelSize) : null;
                s_Faces[key] = face;
            }

            return face;
        }

        public static bool TryGetGlyph(Face face, uint codepoint, out Glyph glyph)
        {
            if (!face.Glyphs.TryGetValue(codepoint, out glyph))
            {
                glyph = Rasterize(face, codepoint);
                face.Glyphs[codepoint] = glyph;
            }

            return glyph.Valid;
        }

        /// <summary>把新拷入的字形上传到 GPU；没有变化时什么都不做。</summary>
        public static void Upload()
        {
            for (int i = 0; i < s_Pages.Count; i++)
            {
                Page page = s_Pages[i];
                if (!page.Dirty)
                {
                    continue;
                }

                page.Texture.SetPixelData(page.Pixels, 0);
                page.Texture.Apply(false, false);
                page.Dirty = false;
            }
        }

        /// <summary>释放全部字面、图集和材质（对比度变化、域重载前调用）。</summary>
        public static void Reset()
        {
            foreach (Face face in s_Faces.Values)
            {
                face?.Destroy();
            }

            s_Faces.Clear();
            for (int i = 0; i < s_Pages.Count; i++)
            {
                s_Pages[i].Destroy();
            }

            s_Pages.Clear();
            for (int i = 0; i < s_Meshes.Count; i++)
            {
                if (s_Meshes[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(s_Meshes[i]);
                }
            }

            s_Meshes.Clear();
            s_LutGamma = -1f;
            Version++;
            AlicizaTextLayout.ClearCache();
        }

        private static void EnsureInitialized()
        {
            if (!s_Hooked)
            {
                s_Hooked = true;
                // 运行时创建的资源跨域重载不会被回收，重载前主动释放。
                AssemblyReloadEvents.beforeAssemblyReload += Reset;
            }

            float gamma = AlicizaEditorText.Contrast;
            if (Mathf.Approximately(gamma, s_LutGamma))
            {
                return;
            }

            s_LutGamma = gamma;
            float exponent = 1f / gamma;
            for (int i = 0; i < 256; i++)
            {
                s_Lut[i] = (byte)Mathf.RoundToInt(255f * Mathf.Pow(i / 255f, exponent));
            }
        }

        private static Glyph Rasterize(Face face, uint codepoint)
        {
            if (codepoint > 0x10FFFF || (codepoint >= 0xD800 && codepoint <= 0xDFFF))
            {
                return default;
            }

            TextCoreFontAsset raster = face.Raster;
            Dictionary<uint, TextCoreCharacter> lookup = s_CharacterLookup(raster);
            if (lookup == null || !lookup.TryGetValue(codepoint, out TextCoreCharacter character))
            {
                raster.TryAddCharacters(char.ConvertFromUtf32((int)codepoint), out string _);
                lookup = s_CharacterLookup(raster);
                if (lookup == null || !lookup.TryGetValue(codepoint, out character))
                {
                    return default;
                }
            }

            UnityEngine.TextCore.Glyph source = character.glyph;
            GlyphMetrics metrics = source.metrics;
            GlyphRect rect = source.glyphRect;
            int advance = Mathf.RoundToInt(metrics.horizontalAdvance);
            int bearingX = Mathf.RoundToInt(metrics.horizontalBearingX);
            int bearingY = Mathf.RoundToInt(metrics.horizontalBearingY);
            if (rect.width <= 0 || rect.height <= 0 || source.atlasIndex >= raster.atlasTextures.Length)
            {
                return new Glyph(advance);
            }

            Page page = Allocate(rect.width, rect.height, out int x, out int y, out int pageIndex);
            Texture2D atlas = raster.atlasTextures[source.atlasIndex];
            NativeArray<byte> pixels = atlas.GetPixelData<byte>(0);
            int sourceWidth = atlas.width;
            for (int row = 0; row < rect.height; row++)
            {
                int sourceRow = (rect.y + row) * sourceWidth + rect.x;
                int targetRow = (y + row) * PageSize + x;
                for (int column = 0; column < rect.width; column++)
                {
                    page.Pixels[targetRow + column] = s_Lut[pixels[sourceRow + column]];
                }
            }

            page.Dirty = true;
            const float inv = 1f / PageSize;
            var uv = new Rect(x * inv, y * inv, rect.width * inv, rect.height * inv);
            return new Glyph(advance, bearingX, bearingY, rect.width, rect.height, pageIndex, uv);
        }

        private static Page Allocate(int width, int height, out int x, out int y, out int pageIndex)
        {
            if (width + GlyphSpacing > PageSize || height + GlyphSpacing > PageSize)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Glyph larger than atlas page.");
            }

            pageIndex = s_Pages.Count - 1;
            Page page = pageIndex >= 0 ? s_Pages[pageIndex] : null;
            if (page != null && page.CursorX + width + GlyphSpacing > PageSize)
            {
                page.CursorY += page.ShelfHeight + GlyphSpacing;
                page.CursorX = 0;
                page.ShelfHeight = 0;
            }

            if (page == null || page.CursorY + height + GlyphSpacing > PageSize)
            {
                page = new Page();
                s_Pages.Add(page);
                pageIndex = s_Pages.Count - 1;
            }

            x = page.CursorX;
            y = page.CursorY;
            page.CursorX += width + GlyphSpacing;
            page.ShelfHeight = Mathf.Max(page.ShelfHeight, height);
            return page;
        }

        // Unity 6.5 起 FontAsset 的字形表（characterLookupTable 等）全部标为过时、将来会移除，且没有公开替代。
        // 用反射取而不是直接引用：被移除时这里返回 null，字面不可用、相关 Label 退回 IMGUI，不会编译失败。
        // 只在光栅化新字形时调用，开销可忽略。
        private static Func<TextCoreFontAsset, Dictionary<uint, TextCoreCharacter>> CreateCharacterLookupGetter()
        {
            PropertyInfo property = typeof(TextCoreFontAsset).GetProperty(
                "characterLookupTable",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getter = property != null && property.PropertyType == typeof(Dictionary<uint, TextCoreCharacter>)
                ? property.GetGetMethod(true)
                : null;
            if (getter == null)
            {
                return null;
            }

            try
            {
                return (Func<TextCoreFontAsset, Dictionary<uint, TextCoreCharacter>>)Delegate.CreateDelegate(
                    typeof(Func<TextCoreFontAsset, Dictionary<uint, TextCoreCharacter>>),
                    getter);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// 按字体族名创建系统字体的 hinted 位图字面。走的是 Unity 自己的 DynamicOS 机制：按族名解析系统字体、
        /// 按族名加载字面，所以不依赖文件路径，也不会触发按路径创建时那条 "Unable to load font face" 警告。
        /// </summary>
        private static class SystemFontFactory
        {
#if UNITY_6000_5_OR_NEWER
            public static TextCoreFontAsset Create(string familyName, string styleName, int pixelSize)
            {
                return TextCoreFontAsset.CreateFontAsset(familyName, styleName, pixelSize, RasterPadding, GlyphRenderMode.SMOOTH_HINTED);
            }
#else
            // 2022.3 ~ 6.4 的公开重载只能建 SDF；同一实现（TryGetSystemFontReference + DynamicOS 版 CreateFontAsset）
            // 还是 internal，6.5 起才公开了可指定渲染模式的重载。这里反射调用；取不到就返回 null（不提供后备，
            // 缺字的 Label 交给 IMGUI），不会编译失败也不会打日志。
            private const BindingFlags Internal = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

            private static readonly MethodInfo s_TryGetReference = typeof(FontEngine).GetMethod("TryGetSystemFontReference", Internal);
            private static readonly Type s_ReferenceType = typeof(FontEngine).Assembly.GetType("UnityEngine.TextCore.LowLevel.FontReference");
            private static readonly FieldInfo s_PathField = s_ReferenceType?.GetField("filePath");
            private static readonly FieldInfo s_FaceIndexField = s_ReferenceType?.GetField("faceIndex");
            private static readonly MethodInfo s_CreateFromReference = FindCreateFromReference();

            public static TextCoreFontAsset Create(string familyName, string styleName, int pixelSize)
            {
                if (s_TryGetReference == null || s_PathField == null || s_FaceIndexField == null || s_CreateFromReference == null)
                {
                    return null;
                }

                try
                {
                    var lookup = new object[] { familyName, styleName, null };
                    if (!(bool)s_TryGetReference.Invoke(null, lookup))
                    {
                        return null;
                    }

                    return (TextCoreFontAsset)s_CreateFromReference.Invoke(null, new object[]
                    {
                        (string)s_PathField.GetValue(lookup[2]),
                        (int)s_FaceIndexField.GetValue(lookup[2]),
                        pixelSize,
                        RasterPadding,
                        GlyphRenderMode.SMOOTH_HINTED,
                        RasterAtlasSize,
                        RasterAtlasSize,
                        AtlasPopulationMode.DynamicOS,
                        true
                    });
                }
                catch (TargetInvocationException)
                {
                    return null;
                }
            }

            private static MethodInfo FindCreateFromReference()
            {
                foreach (MethodInfo method in typeof(TextCoreFontAsset).GetMethods(Internal))
                {
                    if (method.Name != nameof(TextCoreFontAsset.CreateFontAsset))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 9
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(int)
                        && parameters[4].ParameterType == typeof(GlyphRenderMode)
                        && parameters[7].ParameterType == typeof(AtlasPopulationMode))
                    {
                        return method;
                    }
                }

                return null;
            }
#endif
        }

        // 系统 CJK 后备字体栈（按族名，类似 CSS font-family）；各平台只会命中自己装了的那几个。
        private static readonly FallbackFamily[] s_FallbackFamilies =
        {
            new FallbackFamily("Microsoft YaHei UI", "Regular", "Bold"),
            new FallbackFamily("Malgun Gothic", "Regular", "Bold"),
            new FallbackFamily("PingFang SC", "Regular", "Semibold"),
            new FallbackFamily("Apple SD Gothic Neo", "Regular", "Bold"),
            new FallbackFamily("Noto Sans CJK SC", "Regular", "Bold")
        };

        internal readonly struct Glyph
        {
            public readonly bool Valid;
            public readonly int Advance;
            public readonly int BearingX;
            public readonly int BearingY;
            public readonly int Width;
            public readonly int Height;
            public readonly int Page;
            public readonly Rect Uv;

            public Glyph(int advance)
            {
                Valid = true;
                Advance = advance;
                BearingX = 0;
                BearingY = 0;
                Width = 0;
                Height = 0;
                Page = -1;
                Uv = default;
            }

            public Glyph(int advance, int bearingX, int bearingY, int width, int height, int page, Rect uv)
            {
                Valid = true;
                Advance = advance;
                BearingX = bearingX;
                BearingY = bearingY;
                Width = width;
                Height = height;
                Page = page;
                Uv = uv;
            }
        }

        internal sealed class Face
        {
            private static int s_NextId;

            public readonly int Id;
            public readonly int PixelSize;
            public readonly int Ascent;
            public readonly int LineHeight;
            public readonly Dictionary<uint, Glyph> Glyphs = new Dictionary<uint, Glyph>();
            internal readonly TextCoreFontAsset Raster;

            public Face(TextCoreFontAsset raster, int pixelSize)
            {
                Id = ++s_NextId;
                Raster = raster;
                PixelSize = pixelSize;
                raster.hideFlags = HideFlags.HideAndDontSave;
                Texture2D[] textures = raster.atlasTextures;
                for (int i = 0; textures != null && i < textures.Length; i++)
                {
                    if (textures[i] != null)
                    {
                        textures[i].hideFlags = HideFlags.HideAndDontSave;
                    }
                }
                Ascent = Mathf.RoundToInt(raster.faceInfo.ascentLine);
                LineHeight = Mathf.Max(1, Mathf.RoundToInt(raster.faceInfo.lineHeight));
            }

            public void Destroy()
            {
                if (Raster == null)
                {
                    return;
                }

                Texture2D[] textures = Raster.atlasTextures;
                if (textures != null)
                {
                    for (int i = 0; i < textures.Length; i++)
                    {
                        if (textures[i] != null)
                        {
                            UnityEngine.Object.DestroyImmediate(textures[i]);
                        }
                    }
                }

                if (Raster.material != null)
                {
                    UnityEngine.Object.DestroyImmediate(Raster.material);
                }

                UnityEngine.Object.DestroyImmediate(Raster);
            }
        }

        internal sealed class Page
        {
            public readonly Texture2D Texture;
            public readonly Material Material;
            public readonly byte[] Pixels = new byte[PageSize * PageSize];
            public int CursorX;
            public int CursorY;
            public int ShelfHeight;
            public bool Dirty;

            public Page()
            {
                // 绘制时与屏幕像素 1:1 对齐，Point 采样保证不被插值糊掉。
                Texture = new Texture2D(PageSize, PageSize, TextureFormat.Alpha8, false)
                {
                    name = "AlicizaGlyphAtlas",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                Material = new Material(Shader.Find("GUI/Text Shader"))
                {
                    name = "AlicizaGlyphAtlas",
                    hideFlags = HideFlags.HideAndDontSave,
                    mainTexture = Texture
                };
                Dirty = true;
            }

            public void Destroy()
            {
                UnityEngine.Object.DestroyImmediate(Material);
                UnityEngine.Object.DestroyImmediate(Texture);
            }
        }

        private readonly struct FallbackFamily
        {
            public readonly string Name;
            public readonly string RegularStyle;
            public readonly string BoldStyle;

            public FallbackFamily(string name, string regularStyle, string boldStyle)
            {
                Name = name;
                RegularStyle = regularStyle;
                BoldStyle = boldStyle;
            }
        }

        // 字面缓存键：Font 资源本身或系统字体的 "族名|样式"，加像素尺寸。
        // 不用 GetInstanceID()：Unity 6.5 起它已标为过时并报错，而 Equals/GetHashCode 各版本都可用。
        private readonly struct FaceKey : IEquatable<FaceKey>
        {
            private readonly object _source;
            private readonly int _pixelSize;

            public FaceKey(object source, int pixelSize)
            {
                _source = source;
                _pixelSize = pixelSize;
            }

            public bool Equals(FaceKey other)
            {
                return _pixelSize == other._pixelSize && Equals(_source, other._source);
            }

            public override bool Equals(object obj)
            {
                return obj is FaceKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_source != null ? _source.GetHashCode() : 0) * 397 ^ _pixelSize;
                }
            }
        }
    }
}
