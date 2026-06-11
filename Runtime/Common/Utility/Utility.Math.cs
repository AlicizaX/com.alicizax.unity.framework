using UnityEngine;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// 数学和插值相关工具。
        /// </summary>
        public static class Math
        {
            public static bool IsApproximate(float valueA, float valueB, float tolerance)
            {
                return Mathf.Abs(valueA - valueB) < tolerance;
            }

            public static float PingPong(float min, float max, float speed = 1f)
            {
                return Mathf.PingPong(UnityEngine.Time.time * speed, max - min) + min;
            }

            public static int Wrap(int value, int min, int max)
            {
                int newValue = value % max;
                if (newValue < min)
                {
                    newValue = max - 1;
                }

                return newValue;
            }

            public static float InverseLerp3(float min, float mid, float max, float t)
            {
                if (t <= min)
                {
                    return 0f;
                }

                if (t >= max)
                {
                    return 0f;
                }

                return t <= mid ? Mathf.InverseLerp(min, mid, t) : 1f - Mathf.InverseLerp(mid, max, t);
            }

            public static float Remap(float minA, float maxA, float minB, float maxB, float t)
            {
                return minB + (t - minA) * (maxB - minB) / (maxA - minA);
            }

            public static float EaseOut(float start, float end, float t)
            {
                t = Mathf.Clamp01(t);
                t = Mathf.Sin(t * Mathf.PI * 0.5f);
                return Mathf.Lerp(start, end, t);
            }

            public static float EaseIn(float start, float end, float t)
            {
                t = Mathf.Clamp01(t);
                t = 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                return Mathf.Lerp(start, end, t);
            }

            public static float SmootherStep(float start, float end, float t)
            {
                t = Mathf.Clamp01(t);
                t = t * t * t * (t * (6f * t - 15f) + 10f);
                return Mathf.Lerp(start, end, t);
            }

            public static float InverseLerp(Vector3 a, Vector3 b, Vector3 value)
            {
                if (a != b)
                {
                    Vector3 ab = b - a;
                    Vector3 av = value - a;
                    float t = Vector3.Dot(av, ab) / Vector3.Dot(ab, ab);
                    return Mathf.Clamp01(t);
                }

                return 0f;
            }

            public static Vector3 QuadraticBezier(Vector3 p1, Vector3 p2, Vector3 cp, float t)
            {
                t = Mathf.Clamp01(t);
                Vector3 m1 = Vector3.LerpUnclamped(p1, cp, t);
                Vector3 m2 = Vector3.LerpUnclamped(cp, p2, t);
                return Vector3.LerpUnclamped(m1, m2, t);
            }

            public static Vector3 BezierCurve(float t, params Vector3[] points)
            {
                if (points.Length < 1)
                {
                    return Vector3.zero;
                }

                if (points.Length == 1)
                {
                    return points[0];
                }

                t = Mathf.Clamp01(t);
                Vector3[] cp = points;
                int n = points.Length - 1;
                while (n > 1)
                {
                    Vector3[] rp = new Vector3[n];
                    for (int i = 0; i < rp.Length; i++)
                    {
                        rp[i] = Vector3.LerpUnclamped(cp[i], cp[i + 1], t);
                    }

                    cp = rp;
                    n--;
                }

                return Vector3.LerpUnclamped(cp[0], cp[1], t);
            }

            public static Vector3 Lerp3(Vector3 a, Vector3 b, Vector3 c, float t)
            {
                t = Mathf.Clamp01(t);
                if (t <= 0.5f)
                {
                    return Vector3.LerpUnclamped(a, b, t * 2f);
                }

                return Vector3.LerpUnclamped(b, c, (t * 2f) - 1f);
            }

            public static Vector3 RangeLerp(float t, params Vector3[] points)
            {
                if (points.Length < 1)
                {
                    return Vector3.zero;
                }

                if (points.Length == 1)
                {
                    return points[0];
                }

                t = Mathf.Clamp01(t);
                int pointsCount = points.Length - 1;
                float scale = 1f / pointsCount;
                float remap = Remap(0f, 1f, 0f, pointsCount, t);
                int index = Mathf.Clamp(Mathf.FloorToInt(remap), 0, pointsCount - 1);
                float indexT = Mathf.InverseLerp(index * scale, (index + 1) * scale, t);
                return Vector3.LerpUnclamped(points[index], points[index + 1], indexT);
            }

            public static Vector3 Lerp(float t, Vector3[] points)
            {
                if (points.Length > 3)
                {
                    return RangeLerp(t, points);
                }

                if (points.Length == 3)
                {
                    return Lerp3(points[0], points[1], points[2], t);
                }

                if (points.Length == 2)
                {
                    return Vector3.Lerp(points[0], points[1], t);
                }

                if (points.Length == 1)
                {
                    return points[0];
                }

                return Vector3.zero;
            }

            /// <summary>
            /// 检查两个矩形是否相交
            /// </summary>
            /// <param name="src"></param>
            /// <param name="target"></param>
            /// <returns></returns>
            public static bool CheckIntersect(RectInt src, RectInt target)
            {
                int minX = System.Math.Max(src.x, target.x);
                int minY = System.Math.Max(src.y, target.y);
                int maxX = System.Math.Min(src.x + src.width, target.x + target.width);
                int maxY = System.Math.Min(src.y + src.height, target.y + target.height);
                if (minX >= maxX || minY >= maxY)
                {
                    return false;
                }

                return true;
            }

            /// <summary>
            /// 检查两个矩形是否相交
            /// </summary>
            /// <param name="x1"></param>
            /// <param name="y1"></param>
            /// <param name="w1"></param>
            /// <param name="h1"></param>
            /// <param name="x2"></param>
            /// <param name="y2"></param>
            /// <param name="w2"></param>
            /// <param name="h2"></param>
            /// <returns></returns>
            public static bool CheckIntersect(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2)
            {
                int minX = System.Math.Max(x1, x2);
                int minY = System.Math.Max(y1, y2);
                int maxX = System.Math.Min(x1 + w1, x2 + w2);
                int maxY = System.Math.Min(y1 + h1, y2 + h2);
                if (minX >= maxX || minY >= maxY)
                {
                    return false;
                }

                return true;
            }

            /// <summary>
            /// 检查两个矩形是否相交，并返回相交的区域
            /// </summary>
            /// <param name="x1"></param>
            /// <param name="y1"></param>
            /// <param name="w1"></param>
            /// <param name="h1"></param>
            /// <param name="x2"></param>
            /// <param name="y2"></param>
            /// <param name="w2"></param>
            /// <param name="h2"></param>
            /// <param name="rect"></param>
            /// <returns></returns>
            private static bool CheckIntersect(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2, out RectInt rect)
            {
                rect = default;
                int minX = System.Math.Max(x1, x2);
                int minY = System.Math.Max(y1, y2);
                int maxX = System.Math.Min(x1 + w1, x2 + w2);
                int maxY = System.Math.Min(y1 + h1, y2 + h2);
                if (minX >= maxX || minY >= maxY)
                {
                    return false;
                }

                rect.x = minX;
                rect.y = minY;
                rect.width = System.Math.Abs(maxX - minX);
                rect.height = System.Math.Abs(maxY - minY);
                return true;
            }

            /// <summary>
            /// 检查两个矩形相交的点
            /// </summary>
            /// <param name="x1">A 坐标X</param>
            /// <param name="y1">A 坐标Y</param>
            /// <param name="w1">A 宽度</param>
            /// <param name="h1">A 高度</param>
            /// <param name="x2">B 坐标X</param>
            /// <param name="y2">B 坐标Y</param>
            /// <param name="w2">B 宽度</param>
            /// <param name="h2">B 高度</param>
            /// <param name="intersectPoints">交叉点列表</param>
            /// <returns>返回是否相交</returns>
            public static bool CheckIntersectPoints(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2, int[] intersectPoints)
            {
                Vector2Int dPt = new Vector2Int();

                if (false == CheckIntersect(x1, y1, w1, h1, x2, y2, w2, h2, out var rectInt))
                {
                    return false;
                }

                for (var i = 0; i < w1; i++)
                {
                    for (var n = 0; n < h1; n++)
                    {
                        if (intersectPoints[i * h1 + n] == 1)
                        {
                            dPt.x = x1 + i;
                            dPt.y = y1 + n;
                            if (rectInt.Contains(dPt))
                            {
                                intersectPoints[i * h1 + n] = 0;
                            }
                        }
                    }
                }

                return true;
            }
        }
    }
}
