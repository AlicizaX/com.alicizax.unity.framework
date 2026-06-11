namespace UnityEngine
{
    /// <summary>
    /// 对 Unity 的扩展方法。
    /// </summary>
    public static class Vector3Extensions
    {
        /// <summary>
        /// 取 <see cref="Vector3" /> 的 (x, y, z) 转换为 <see cref="Vector2" /> 的 (x, z)。
        /// </summary>
        /// <param name="vector3">要转换的 Vector3。</param>
        /// <returns>转换后的 Vector2。</returns>
        public static Vector2 ToVector2(this Vector3 vector3)
        {
            return new Vector2(vector3.x, vector3.z);
        }


        /// <summary>
        /// 取 <see cref="Vector2" /> 的 (x, y) 转换为 <see cref="Vector3" /> 的 (x, 0, y)。
        /// </summary>
        /// <param name="vector3">要转换的 Vector3。</param>
        /// <returns>转换后的 Vector3。</returns>
        public static Vector3 ToVector3(this Vector3Int vector3)
        {
            return new Vector3(vector3.x, vector3.y, vector3.z);
        }

        /// <summary>
        /// 按分量相乘。
        /// </summary>
        public static Vector3 Multiply(this Vector3 lhs, Vector3 rhs)
        {
            lhs.Scale(rhs);
            return lhs;
        }

        /// <summary>
        /// 将三维向量投影到 XZ 平面。
        /// </summary>
        public static Vector2 IgnoreYAxis(this Vector3 vector3)
        {
            return new Vector2(vector3.x, vector3.z);
        }

        /// <summary>
        /// 保留 XZ 分量并清空 Y 轴。
        /// </summary>
        public static Vector3 FlattenY(this Vector3 vector3)
        {
            return new Vector3(vector3.x, 0f, vector3.z);
        }

        /// <summary>
        /// 判断目标点是否位于当前方向的左侧。
        /// </summary>
        public static bool PointOnLeftSideOfVector(this Vector3 vector3, Vector3 originPoint, Vector3 point)
        {
            Vector2 origin = originPoint.IgnoreYAxis();
            Vector2 targetDirection = (point.IgnoreYAxis() - origin).normalized;
            Vector2 direction = vector3.IgnoreYAxis();
            float verticalX = origin.x;
            float verticalY = (-verticalX * direction.x) / direction.y;
            Vector2 normalVertical = new Vector2(verticalX, verticalY).normalized;
            return Vector2.Dot(normalVertical, targetDirection) < 0f;
        }
    }
}
