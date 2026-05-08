// using System;
// using AlicizaX.Editor;
// using AlicizaX.Resource.Runtime;
// using UnityEditor;
// using UnityEngine;
//
// namespace AlicizaX.Resource.Editor
// {
//     [CustomEditor(typeof(ResourceExtComponent))]
//     internal sealed class ResourceExtComponentInspector : GameFrameworkInspector
//     {
//         private SerializedProperty m_CheckCanReleaseInterval = null;
//         private SerializedProperty m_AutoReleaseInterval = null;
//
//         public override void OnInspectorGUI()
//         {
//             base.OnInspectorGUI();
//             serializedObject.Update();
//
//             ResourceExtComponent t = (ResourceExtComponent)target;
//
//             EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
//             {
//                 EditorGUILayout.BeginVertical("box");
//                 {
//                     float checkCanReleaseInterval = EditorGUILayout.Slider("检查释放间隔", m_CheckCanReleaseInterval.floatValue, 30, 460);
//                     if (m_CheckCanReleaseInterval.floatValue != checkCanReleaseInterval)
//                     {
//                         m_CheckCanReleaseInterval.floatValue = checkCanReleaseInterval;
//                     }
//
//
//                     float autoReleaseInterval = EditorGUILayout.Slider("对象池释放间隔", m_AutoReleaseInterval.floatValue, 60, 1800);
//                     if (m_AutoReleaseInterval.floatValue != autoReleaseInterval)
//                     {
//                         m_AutoReleaseInterval.floatValue = autoReleaseInterval;
//                     }
//                 }
//
//                 if (GUILayout.Button("ReleaseUnused"))
//                 {
//                     t.ReleaseUnused();
//                 }
//
//
//                 EditorGUILayout.EndVertical();
//             }
//             EditorGUI.EndDisabledGroup();
//
//             serializedObject.ApplyModifiedProperties();
//         }
//
//         private void OnEnable()
//         {
//             m_CheckCanReleaseInterval = serializedObject.FindProperty("m_CheckCanReleaseInterval");
//             m_AutoReleaseInterval = serializedObject.FindProperty("m_AutoReleaseInterval");
//         }
//     }
// }