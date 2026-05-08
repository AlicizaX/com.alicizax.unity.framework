namespace UnityEngine
{
    public static class Vector2Extensions
    {
        /// <summary>
        /// 取 <see cref="Vector2" /> 的 (x, y) 转换为 <see cref="Vector3" /> 的 (x, 0, y)。
        /// </summary>
        /// <param name="vector2">要转换的 Vector2。</param>
        /// <returns>转换后的 Vector3。</returns>
        public static Vector3 ToVector3(this Vector2 vector2)
        {
            return new Vector3(vector2.x, 0f, vector2.y);
        }

        /// <summary>
        /// 取 <see cref="Vector2" /> 的 (x, y) 和给定参数 y 转换为 <see cref="Vector3" /> 的 (x, 参数 y, y)。
        /// </summary>
        /// <param name="vector2">要转换的 Vector2。</param>
        /// <param name="y">Vector3 的 y 值。</param>
        /// <returns>转换后的 Vector3。</returns>
        public static Vector3 ToVector3(this Vector2 vector2, float y)
        {
            return new Vector3(vector2.x, y, vector2.y);
        }

        /// <summary>
        /// 判断值是否在向量区间内。
        /// </summary>
        public static bool InRange(this Vector2 vector, float value, bool equal = false)
        {
            return equal ? value >= vector.x && value <= vector.y : value > vector.x && value < vector.y;
        }

        /// <summary>
        /// 判断角度是否在向量表示的角度区间内。
        /// </summary>
        public static bool InDegrees(this Vector2 vector, float value, bool equal = false)
        {
            if (vector.x > vector.y)
            {
                return equal ? value >= (vector.x - 360f) && value <= vector.y : value > (vector.x - 360f) && value < vector.y;
            }

            return equal ? value >= vector.x && value <= vector.y : value > vector.x && value < vector.y;
        }

        /// <summary>
        /// 从区间内取随机值。
        /// </summary>
        public static float Random(this Vector2 vector)
        {
            return UnityEngine.Random.Range(vector.x, vector.y);
        }
    }
}
