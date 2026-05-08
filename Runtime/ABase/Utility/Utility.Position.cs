using UnityEngine;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// 位置与几何相关的实用函数。
        /// </summary>
        public static class Position
        {
            public static Vector3 RayCastV2ToV3(Vector2 position)
            {
                return position.ToVector3();
            }

            public static Vector3 RayCastXYToV3(float x, float y)
            {
                return new Vector3(x, 0f, y);
            }

            public static Vector3 RayCastV3ToV3(Vector3 position)
            {
                return position.FlattenY();
            }

            public static Quaternion AngleToQuaternion(int angle)
            {
                return GetAngleToQuaternion(angle);
            }

            public static Quaternion GetAngleToQuaternion(float angle)
            {
                return Quaternion.AngleAxis(-angle, Vector3.up) * Quaternion.AngleAxis(90f, Vector3.up);
            }

            public static Quaternion GetVector3ToQuaternion(Vector3 source, Vector3 direction)
            {
                if (source == direction)
                {
                    return Quaternion.identity;
                }

                return Quaternion.LookRotation((direction - source).normalized, Vector3.up);
            }

            public static float Distance2D(Vector3 a, Vector3 b)
            {
                return Vector2.Distance(a.IgnoreYAxis(), b.IgnoreYAxis());
            }

            public static float Vector3ToAngle360(Vector3 from, Vector3 to)
            {
                float angle = Vector3.Angle(from, to);
                Vector3 cross = Vector3.Cross(from, to);
                return cross.y > 0f ? angle : 360f - angle;
            }

            public static float DistanceOfPointToVector(Vector3 startPoint, Vector3 endPoint, Vector3 point)
            {
                Vector2 start = startPoint.IgnoreYAxis();
                Vector2 end = endPoint.IgnoreYAxis();
                float a = end.y - start.y;
                float b = start.x - end.x;
                float c = end.x * start.y - start.x * end.y;
                float denominator = Mathf.Sqrt(a * a + b * b);
                Vector2 point2 = point.IgnoreYAxis();
                return Mathf.Abs((a * point2.x + b * point2.y + c) / denominator);
            }

            public static bool RayCastSphere(Ray ray, Vector3 center, float radius, out float distance)
            {
                distance = 0f;
                Vector3 originToCenter = center - ray.origin;
                float perpendicularDistance = Vector3.Cross(originToCenter, ray.direction).magnitude / ray.direction.magnitude;
                if (perpendicularDistance >= radius)
                {
                    return false;
                }

                float centerDistance = PythagoreanTheorem(Vector3.Distance(center, ray.origin), perpendicularDistance);
                float radiusDistance = PythagoreanTheorem(radius, perpendicularDistance);
                distance = centerDistance - radiusDistance;
                return true;
            }

            public static float PythagoreanTheorem(float x, float y)
            {
                return Mathf.Sqrt(x * x + y * y);
            }
        }
    }
}
