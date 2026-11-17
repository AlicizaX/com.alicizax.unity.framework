using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX.Editor.UIToolkit
{
    internal enum AlicizaTextWrap
    {
        None,
        Wrap,
        Ellipsis
    }

    /// <summary>
    /// 用 hinted 整数步进排版（单位：设备像素），结果按内容缓存。
    /// 主字体缺字时依次找系统后备字体；全都缺时 Supported 为 false，由调用方退回 IMGUI。
    /// </summary>
    internal sealed class AlicizaTextLayout
    {
        private const int CacheCapacity = 2048;
        private const uint EllipsisCodepoint = 0x2026;
        private const int TabSpaces = 4;

        private static readonly Dictionary<Key, AlicizaTextLayout> s_Cache = new Dictionary<Key, AlicizaTextLayout>();
        private static readonly List<Run> s_Runs = new List<Run>(256);
        private static readonly List<int> s_LineStarts = new List<int>(16);
        private static readonly List<int> s_LineEnds = new List<int>(16);
        private static readonly List<Quad> s_Quads = new List<Quad>(256);
        private static readonly List<AlicizaGlyphAtlas.Face> s_Fallbacks = new List<AlicizaGlyphAtlas.Face>(4);
        private static readonly AlicizaTextLayout s_Unsupported = new AlicizaTextLayout();

        public readonly bool Supported;
        public readonly int Width;
        public readonly int Height;
        public readonly int[] LineWidths;
        public readonly int[] LineQuadStart;
        public readonly int[] LineQuadCount;
        public readonly Quad[] Quads;

        private AlicizaTextLayout()
        {
            Supported = false;
            LineWidths = LineQuadStart = LineQuadCount = Array.Empty<int>();
            Quads = Array.Empty<Quad>();
        }

        private AlicizaTextLayout(int width, int height, int[] lineWidths, int[] lineQuadStart, int[] lineQuadCount, Quad[] quads)
        {
            Supported = true;
            Width = width;
            Height = height;
            LineWidths = lineWidths;
            LineQuadStart = lineQuadStart;
            LineQuadCount = lineQuadCount;
            Quads = quads;
        }

        public int LineCount => LineWidths.Length;

        public static AlicizaTextLayout Get(string text, AlicizaGlyphAtlas.Face face, bool bold, int maxWidth, AlicizaTextWrap wrap)
        {
            if (wrap == AlicizaTextWrap.None)
            {
                maxWidth = -1;
            }

            var key = new Key(text, face.Id, maxWidth, wrap);
            if (s_Cache.TryGetValue(key, out AlicizaTextLayout layout))
            {
                return layout;
            }

            layout = Build(text, face, bold, maxWidth, wrap);
            if (s_Cache.Count >= CacheCapacity)
            {
                s_Cache.Clear();
            }

            s_Cache[key] = layout;
            return layout;
        }

        public static void ClearCache()
        {
            s_Cache.Clear();
        }

        private static AlicizaTextLayout Build(string text, AlicizaGlyphAtlas.Face face, bool bold, int maxWidth, AlicizaTextWrap wrap)
        {
            s_Fallbacks.Clear();
            bool fallbacksLoaded = false;
            if (!Decode(text, face, bold, ref fallbacksLoaded))
            {
                return s_Unsupported;
            }

            BreakLines(wrap == AlicizaTextWrap.Wrap ? maxWidth : -1);

            Run ellipsis = default;
            bool hasEllipsis = wrap == AlicizaTextWrap.Ellipsis
                               && TryResolve(EllipsisCodepoint, face, bold, ref fallbacksLoaded, out ellipsis);

            int lineCount = s_LineStarts.Count;
            var lineWidths = new int[lineCount];
            var lineQuadStart = new int[lineCount];
            var lineQuadCount = new int[lineCount];
            s_Quads.Clear();
            int width = 0;
            for (int line = 0; line < lineCount; line++)
            {
                int start = s_LineStarts[line];
                int end = TrimTrailingSpaces(start, s_LineEnds[line]);
                bool elide = hasEllipsis && maxWidth >= 0 && Measure(start, end) > maxWidth;
                if (elide)
                {
                    end = TrimTrailingSpaces(start, FitPrefix(start, end, maxWidth - ellipsis.Glyph.Advance));
                }

                lineQuadStart[line] = s_Quads.Count;
                int top = line * face.LineHeight;
                int x = 0;
                for (int i = start; i < end; i++)
                {
                    x = Emit(s_Runs[i], x, top, face.Ascent);
                }

                if (elide)
                {
                    x = Emit(ellipsis, x, top, face.Ascent);
                }

                lineQuadCount[line] = s_Quads.Count - lineQuadStart[line];
                lineWidths[line] = x;
                width = Mathf.Max(width, x);
            }

            return new AlicizaTextLayout(width, lineCount * face.LineHeight, lineWidths, lineQuadStart, lineQuadCount, s_Quads.ToArray());
        }

        private static bool Decode(string text, AlicizaGlyphAtlas.Face face, bool bold, ref bool fallbacksLoaded)
        {
            s_Runs.Clear();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    continue;
                }

                if (c == '\n')
                {
                    s_Runs.Add(Run.NewLine);
                    continue;
                }

                uint codepoint = c;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codepoint = (uint)char.ConvertToUtf32(c, text[i + 1]);
                    i++;
                }

                if (c == '\t')
                {
                    if (!TryResolve(' ', face, bold, ref fallbacksLoaded, out Run space))
                    {
                        return false;
                    }

                    s_Runs.Add(space.WithAdvance(space.Glyph.Advance * TabSpaces));
                    continue;
                }

                if (!TryResolve(codepoint, face, bold, ref fallbacksLoaded, out Run run))
                {
                    return false;
                }

                s_Runs.Add(run);
            }

            return true;
        }

        private static bool TryResolve(uint codepoint, AlicizaGlyphAtlas.Face face, bool bold, ref bool fallbacksLoaded, out Run run)
        {
            if (AlicizaGlyphAtlas.TryGetGlyph(face, codepoint, out AlicizaGlyphAtlas.Glyph glyph))
            {
                run = new Run(codepoint, glyph);
                return true;
            }

            if (!fallbacksLoaded)
            {
                fallbacksLoaded = true;
                AlicizaGlyphAtlas.GetFallbackFaces(bold, face.PixelSize, s_Fallbacks);
            }

            for (int i = 0; i < s_Fallbacks.Count; i++)
            {
                if (AlicizaGlyphAtlas.TryGetGlyph(s_Fallbacks[i], codepoint, out glyph))
                {
                    run = new Run(codepoint, glyph);
                    return true;
                }
            }

            run = default;
            return false;
        }

        private static void BreakLines(int maxWidth)
        {
            s_LineStarts.Clear();
            s_LineEnds.Clear();
            int count = s_Runs.Count;
            int start = 0;
            while (true)
            {
                int x = 0;
                int lastBreak = -1;
                int end = count;
                int next = count;
                for (int i = start; i < count; i++)
                {
                    Run run = s_Runs[i];
                    if (run.IsNewLine)
                    {
                        end = i;
                        next = i + 1;
                        break;
                    }

                    if (maxWidth >= 0 && !run.IsSpace && i > start && x + run.Glyph.Advance > maxWidth)
                    {
                        end = lastBreak >= start ? lastBreak + 1 : i;
                        next = end;
                        while (next < count && s_Runs[next].IsSpace)
                        {
                            next++;
                        }

                        break;
                    }

                    x += run.Glyph.Advance;
                    if (run.IsSpace || run.IsCjk || (i + 1 < count && s_Runs[i + 1].IsCjk))
                    {
                        lastBreak = i;
                    }
                }

                s_LineStarts.Add(start);
                s_LineEnds.Add(end);
                if (next >= count)
                {
                    if (count > 0 && next == count && end < count && s_Runs[count - 1].IsNewLine)
                    {
                        // 以换行结尾时补一个空行，和 UI Toolkit 的行数保持一致。
                        s_LineStarts.Add(count);
                        s_LineEnds.Add(count);
                    }

                    return;
                }

                start = next;
            }
        }

        private static int TrimTrailingSpaces(int start, int end)
        {
            while (end > start && s_Runs[end - 1].IsSpace)
            {
                end--;
            }

            return end;
        }

        private static int Measure(int start, int end)
        {
            int width = 0;
            for (int i = start; i < end; i++)
            {
                width += s_Runs[i].Glyph.Advance;
            }

            return width;
        }

        private static int FitPrefix(int start, int end, int width)
        {
            int x = 0;
            for (int i = start; i < end; i++)
            {
                x += s_Runs[i].Glyph.Advance;
                if (x > width)
                {
                    return i;
                }
            }

            return end;
        }

        private static int Emit(Run run, int x, int top, int ascent)
        {
            AlicizaGlyphAtlas.Glyph glyph = run.Glyph;
            if (glyph.Page >= 0)
            {
                s_Quads.Add(new Quad(
                    x + glyph.BearingX,
                    top + ascent - glyph.BearingY,
                    glyph.Width,
                    glyph.Height,
                    glyph.Page,
                    glyph.Uv));
            }

            return x + glyph.Advance;
        }

        private static bool IsCjk(uint codepoint)
        {
            return (codepoint >= 0x2E80 && codepoint <= 0x9FFF)
                   || (codepoint >= 0xAC00 && codepoint <= 0xD7AF)
                   || (codepoint >= 0xF900 && codepoint <= 0xFAFF)
                   || (codepoint >= 0xFF00 && codepoint <= 0xFFEF)
                   || (codepoint >= 0x20000 && codepoint <= 0x2FFFF);
        }

        internal readonly struct Quad
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Width;
            public readonly int Height;
            public readonly int Page;
            public readonly Rect Uv;

            public Quad(int x, int y, int width, int height, int page, Rect uv)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
                Page = page;
                Uv = uv;
            }
        }

        private readonly struct Run
        {
            public static readonly Run NewLine = new Run(true);

            public readonly AlicizaGlyphAtlas.Glyph Glyph;
            public readonly bool IsNewLine;
            public readonly bool IsSpace;
            public readonly bool IsCjk;

            private Run(bool newLine)
            {
                Glyph = default;
                IsNewLine = newLine;
                IsSpace = false;
                IsCjk = false;
            }

            public Run(uint codepoint, AlicizaGlyphAtlas.Glyph glyph)
            {
                Glyph = glyph;
                IsNewLine = false;
                IsSpace = codepoint == ' ' || codepoint == 0x3000;
                IsCjk = AlicizaTextLayout.IsCjk(codepoint);
            }

            private Run(Run source, AlicizaGlyphAtlas.Glyph glyph)
            {
                Glyph = glyph;
                IsNewLine = false;
                IsSpace = source.IsSpace;
                IsCjk = source.IsCjk;
            }

            public Run WithAdvance(int advance)
            {
                return new Run(this, new AlicizaGlyphAtlas.Glyph(advance));
            }
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly string _text;
            private readonly int _faceId;
            private readonly int _maxWidth;
            private readonly AlicizaTextWrap _wrap;

            public Key(string text, int faceId, int maxWidth, AlicizaTextWrap wrap)
            {
                _text = text;
                _faceId = faceId;
                _maxWidth = maxWidth;
                _wrap = wrap;
            }

            public bool Equals(Key other)
            {
                return _faceId == other._faceId
                       && _maxWidth == other._maxWidth
                       && _wrap == other._wrap
                       && string.Equals(_text, other._text, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _text != null ? _text.GetHashCode() : 0;
                    hash = hash * 397 ^ _faceId;
                    hash = hash * 397 ^ _maxWidth;
                    return hash * 397 ^ (int)_wrap;
                }
            }
        }
    }
}
