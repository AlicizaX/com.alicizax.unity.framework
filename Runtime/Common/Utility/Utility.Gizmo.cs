using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// Gizmos 绘制辅助。
        /// </summary>
        public static class Gizmo
        {
            public static void DrawArrow(Vector3 position, Vector3 direction, float arrowHeadLength = 0.25f, float arrowHeadAngle = 20f)
            {
                Gizmos.DrawRay(position, direction);
                Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + arrowHeadAngle, 0) * new Vector3(0, 0, 1);
                Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - arrowHeadAngle, 0) * new Vector3(0, 0, 1);
                Gizmos.DrawRay(position + direction, right * arrowHeadLength);
                Gizmos.DrawRay(position + direction, left * arrowHeadLength);
            }

            public static void DrawWireCapsule(Vector3 p1, Vector3 p2, float radius)
            {
#if UNITY_EDITOR
                if (p1 == p2)
                {
                    Gizmos.DrawWireSphere(p1, radius);
                    return;
                }

                using (new Handles.DrawingScope(Gizmos.color, Gizmos.matrix))
                {
                    Quaternion p1Rotation = Quaternion.LookRotation(p1 - p2);
                    Quaternion p2Rotation = Quaternion.LookRotation(p2 - p1);
                    float c = Vector3.Dot((p1 - p2).normalized, Vector3.up);
                    if (c == 1f || c == -1f)
                    {
                        p2Rotation = Quaternion.Euler(p2Rotation.eulerAngles.x, p2Rotation.eulerAngles.y + 180f, p2Rotation.eulerAngles.z);
                    }

                    Handles.DrawWireArc(p1, p1Rotation * Vector3.left, p1Rotation * Vector3.down, 180f, radius);
                    Handles.DrawWireArc(p1, p1Rotation * Vector3.up, p1Rotation * Vector3.left, 180f, radius);
                    Handles.DrawWireDisc(p1, (p2 - p1).normalized, radius);
                    Handles.DrawWireArc(p2, p2Rotation * Vector3.left, p2Rotation * Vector3.down, 180f, radius);
                    Handles.DrawWireArc(p2, p2Rotation * Vector3.up, p2Rotation * Vector3.left, 180f, radius);
                    Handles.DrawWireDisc(p2, (p1 - p2).normalized, radius);
                    Handles.DrawLine(p1 + p1Rotation * Vector3.down * radius, p2 + p2Rotation * Vector3.down * radius);
                    Handles.DrawLine(p1 + p1Rotation * Vector3.left * radius, p2 + p2Rotation * Vector3.right * radius);
                    Handles.DrawLine(p1 + p1Rotation * Vector3.up * radius, p2 + p2Rotation * Vector3.up * radius);
                    Handles.DrawLine(p1 + p1Rotation * Vector3.right * radius, p2 + p2Rotation * Vector3.left * radius);
                }
#endif
            }

            public static void DrawWireCapsule(Vector3 position, Quaternion rotation, float radius, float height)
            {
#if UNITY_EDITOR
                Matrix4x4 angleMatrix = Matrix4x4.TRS(position, rotation, Handles.matrix.lossyScale);
                using (new Handles.DrawingScope(angleMatrix))
                {
                    float pointOffset = (height - (radius * 2)) / 2;
                    Handles.DrawWireArc(Vector3.up * pointOffset, Vector3.left, Vector3.back, -180, radius);
                    Handles.DrawLine(new Vector3(0, pointOffset, -radius), new Vector3(0, -pointOffset, -radius));
                    Handles.DrawLine(new Vector3(0, pointOffset, radius), new Vector3(0, -pointOffset, radius));
                    Handles.DrawWireArc(Vector3.down * pointOffset, Vector3.left, Vector3.back, 180, radius);
                    Handles.DrawWireArc(Vector3.up * pointOffset, Vector3.back, Vector3.left, 180, radius);
                    Handles.DrawLine(new Vector3(-radius, pointOffset, 0), new Vector3(-radius, -pointOffset, 0));
                    Handles.DrawLine(new Vector3(radius, pointOffset, 0), new Vector3(radius, -pointOffset, 0));
                    Handles.DrawWireArc(Vector3.down * pointOffset, Vector3.back, Vector3.left, -180, radius);
                    Handles.DrawWireDisc(Vector3.up * pointOffset, Vector3.up, radius);
                    Handles.DrawWireDisc(Vector3.down * pointOffset, Vector3.up, radius);
                }
#endif
            }

            public static void DrawCenteredLabel(Vector3 position, string labelText, GUIStyle style = null)
            {
#if UNITY_EDITOR
                if (style == null)
                {
                    style = new GUIStyle(GUI.skin.label);
                }

                GUIContent content = new GUIContent(labelText);
                Vector2 labelSize = style.CalcSize(content);
                Vector3 screenPosition = HandleUtility.WorldToGUIPoint(position);
                screenPosition.x -= labelSize.x / 2;
                screenPosition.y -= labelSize.y / 2;
                Vector3 worldPosition = HandleUtility.GUIPointToWorldRay(screenPosition).origin;
                Handles.Label(worldPosition, labelText, style);
#endif
            }

            public static void DrawDisc(Vector3 position, float radius, Color outerColor, Color innerColor)
            {
#if UNITY_EDITOR
                Handles.color = innerColor;
                Handles.DrawSolidDisc(position, Vector3.up, radius);
                Handles.color = outerColor;
                Handles.DrawWireDisc(position, Vector3.up, radius);
#endif
            }
        }
    }
}
