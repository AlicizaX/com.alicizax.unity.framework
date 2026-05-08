using System;
using AlicizaX;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Editor
{
    [CustomEditor(typeof(RootModule))]
    internal sealed class RootModuleInspector : GameFrameworkInspector
    {
        private const float ToolbarHeight = 28f;
        private const float LabelWidth = 112f;

        private SerializedProperty _frameRate = null;
        private SerializedProperty _runInBackground = null;
        private SerializedProperty _neverSleep = null;
        private GUIStyle _panelStyle;
        private GUIStyle _fieldRowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();
            EnsureStyles();

            RootModule t = (RootModule)target;

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            {
                DrawToolbar();
                DrawFrameRate(t);
                DrawToggle(_runInBackground, "Run In Background", value => t.RunInBackground = value);
                DrawToggle(_neverSleep, "Never Sleep", value => t.NeverSleep = value);
            }
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private void OnEnable()
        {
            _frameRate = serializedObject.FindProperty("frameRate");
            _runInBackground = serializedObject.FindProperty("runInBackground");
            _neverSleep = serializedObject.FindProperty("neverSleep");
        }

        private void DrawToolbar()
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect titleRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 4f, toolbarRect.width - 16f, 20f);
            GUI.Label(titleRect, "Runtime Settings", _titleStyle);
        }

        private void DrawFrameRate(RootModule rootModule)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            {
                EditorGUILayout.LabelField("Framerate", _labelStyle, GUILayout.Width(LabelWidth));

                EditorGUI.BeginChangeCheck();
                int frameRate = EditorGUILayout.IntSlider(_frameRate.intValue, 1, 120);
                if (EditorGUI.EndChangeCheck())
                {
                    if (EditorApplication.isPlaying)
                    {
                        rootModule.FrameRate = frameRate;
                    }
                    else
                    {
                        _frameRate.intValue = frameRate;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToggle(SerializedProperty property, string label, Action<bool> applyRuntimeValue)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            {
                EditorGUILayout.LabelField(label, _labelStyle, GUILayout.Width(LabelWidth));

                EditorGUI.BeginChangeCheck();
                bool value = EditorGUILayout.Toggle(property.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    if (EditorApplication.isPlaying)
                    {
                        applyRuntimeValue(value);
                    }
                    else
                    {
                        property.boolValue = value;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelStyle = AlicizaEditorGUI.Styles.Panel;
            _fieldRowStyle = AlicizaEditorGUI.Styles.FieldRow;
            _labelStyle = AlicizaEditorGUI.Styles.FieldLabel;
            _titleStyle = new GUIStyle(AlicizaEditorGUI.Styles.RowLabel)
            {
                fontStyle = FontStyle.Bold
            };
        }
    }
}
