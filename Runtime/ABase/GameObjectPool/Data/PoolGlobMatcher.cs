using System;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    internal readonly struct PoolGlobMatcher
    {
        private const byte KindLiteral = 0;
        private const byte KindWild = 1;
        private const byte KindRecursiveWild = 2;
        private const byte KindPattern = 3;

        private readonly byte[] _kinds;
        private readonly string[] _texts;
        private readonly int _segmentCount;
        private readonly bool _implicitPrefix;

        private PoolGlobMatcher(byte[] kinds, string[] texts, int segmentCount, bool implicitPrefix)
        {
            _kinds = kinds;
            _texts = texts;
            _segmentCount = segmentCount;
            _implicitPrefix = implicitPrefix;
        }

        public bool IsValid => _kinds != null && _segmentCount > 0;

        public bool IsLiteralPattern => _implicitPrefix;

        public static PoolGlobMatcher Compile(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return default;
            }

            int len = pattern.Length;
            int maxSegments = 1;
            for (int i = 0; i < len; i++)
            {
                if (pattern[i] == '/')
                {
                    maxSegments++;
                }
            }

            var kinds = new byte[maxSegments];
            var texts = new string[maxSegments];
            int count = 0;
            bool hasWildcard = false;

            int segStart = 0;
            for (int i = 0; i <= len; i++)
            {
                if (i < len && pattern[i] != '/')
                {
                    continue;
                }

                if (i == segStart)
                {
                    segStart = i + 1;
                    continue;
                }

                string seg = pattern.Substring(segStart, i - segStart);
                segStart = i + 1;

                if (seg == "**")
                {
                    if (count > 0 && kinds[count - 1] == KindRecursiveWild)
                    {
                        continue;
                    }

                    kinds[count] = KindRecursiveWild;
                    texts[count] = null;
                    count++;
                    hasWildcard = true;
                }
                else if (seg == "*")
                {
                    kinds[count] = KindWild;
                    texts[count] = null;
                    count++;
                    hasWildcard = true;
                }
                else if (ContainsWildcard(seg))
                {
                    kinds[count] = KindPattern;
                    texts[count] = seg;
                    count++;
                    hasWildcard = true;
                }
                else
                {
                    kinds[count] = KindLiteral;
                    texts[count] = seg;
                    count++;
                }
            }

            if (count == 0)
            {
                return default;
            }

            return new PoolGlobMatcher(kinds, texts, count, !hasWildcard);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMatch(string path)
        {
            if (_kinds == null || _segmentCount == 0 || string.IsNullOrEmpty(path))
            {
                return false;
            }

            return MatchCore(path);
        }

        private bool MatchCore(string path)
        {
            int pathLen = path.Length;
            int patIdx = 0;
            int pathSegStart = 0;

            int starPatIdx = -1;
            int starPathSegStart = -1;

            while (pathSegStart <= pathLen)
            {
                int pathSegEnd = IndexOfSlash(path, pathSegStart);

                if (patIdx < _segmentCount)
                {
                    byte kind = _kinds[patIdx];

                    if (kind == KindRecursiveWild)
                    {
                        starPatIdx = patIdx;
                        starPathSegStart = pathSegStart;
                        patIdx++;
                        continue;
                    }

                    if (pathSegStart >= pathLen)
                    {
                        break;
                    }

                    if (MatchSegment(kind, _texts[patIdx], path, pathSegStart, pathSegEnd))
                    {
                        patIdx++;
                        pathSegStart = pathSegEnd < pathLen ? pathSegEnd + 1 : pathLen + 1;
                        continue;
                    }
                }

                if (starPatIdx >= 0)
                {
                    if (starPathSegStart >= pathLen)
                    {
                        return false;
                    }

                    int nextSeg = IndexOfSlash(path, starPathSegStart);
                    starPathSegStart = nextSeg < pathLen ? nextSeg + 1 : pathLen + 1;
                    pathSegStart = starPathSegStart;
                    patIdx = starPatIdx + 1;
                    continue;
                }

                break;
            }

            while (patIdx < _segmentCount && _kinds[patIdx] == KindRecursiveWild)
            {
                patIdx++;
            }

            if (patIdx < _segmentCount)
            {
                return false;
            }

            if (_implicitPrefix)
            {
                return true;
            }

            return pathSegStart > pathLen;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchSegment(byte kind, string pattern, string path, int start, int end)
        {
            switch (kind)
            {
                case KindLiteral:
                    return MatchLiteral(pattern, path, start, end);
                case KindWild:
                    return true;
                case KindPattern:
                    return MatchPattern(pattern, path, start, end);
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchLiteral(string literal, string path, int start, int end)
        {
            int segLen = end - start;
            if (segLen != literal.Length)
            {
                return false;
            }

            for (int i = 0; i < segLen; i++)
            {
                if (path[start + i] != literal[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchPattern(string pattern, string path, int start, int end)
        {
            int pLen = pattern.Length;
            int sLen = end - start;

            int pi = 0;
            int si = 0;
            int starPi = -1;
            int starSi = -1;

            while (si < sLen)
            {
                if (pi < pLen && (pattern[pi] == '?' || pattern[pi] == path[start + si]))
                {
                    pi++;
                    si++;
                }
                else if (pi < pLen && pattern[pi] == '*')
                {
                    starPi = pi;
                    starSi = si;
                    pi++;
                }
                else if (starPi >= 0)
                {
                    pi = starPi + 1;
                    si = ++starSi;
                }
                else
                {
                    return false;
                }
            }

            while (pi < pLen && pattern[pi] == '*')
            {
                pi++;
            }

            return pi == pLen;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int IndexOfSlash(string path, int start)
        {
            int len = path.Length;
            for (int i = start; i < len; i++)
            {
                if (path[i] == '/')
                {
                    return i;
                }
            }

            return len;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsWildcard(string segment)
        {
            for (int i = 0; i < segment.Length; i++)
            {
                char c = segment[i];
                if (c == '*' || c == '?')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
