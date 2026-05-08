using AlicizaX.Debugger.Runtime;
using AlicizaX.Editor;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Debugger.Editor
{
    [CustomEditor(typeof(DebuggerComponent))]
    internal sealed class DebuggerComponentInspector : UnityEditor.Editor
    {
        private const float ToolbarHeight = 30f;
        private const float RowLabelWidth = 132f;
        private const float SliderValueWidth = 46f;
        private const float SliderMinValue = 0.2f;
        private const float SliderMaxValue = 1f;

        private SerializedProperty _activeWindowProperty;
        private SerializedProperty _enableFloatingToggleSnapProperty;
        private SerializedProperty _windowOpacityProperty;
        private GUIStyle _panelStyle;
        private GUIStyle _fieldRowStyle;
        private GUIStyle _fieldLabelStyle;
        private GUIStyle _rowLabelStyle;
        private string[] _activeWindowOptions;
        private void OnEnable()
        {
            _activeWindowProperty = serializedObject.FindProperty("m_ActiveWindow");
            _enableFloatingToggleSnapProperty = serializedObject.FindProperty("m_EnableFloatingToggleSnap");
            _windowOpacityProperty = serializedObject.FindProperty("m_WindowOpacity");
            _activeWindowOptions = _activeWindowProperty.enumDisplayNames;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EnsureStyles();

            DrawComponentPanel();

            serializedObject.ApplyModifiedProperties();
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelStyle = AlicizaEditorGUI.Styles.Panel;
            _fieldRowStyle = AlicizaEditorGUI.Styles.FieldRow;
            _fieldLabelStyle = AlicizaEditorGUI.Styles.FieldLabel;
            _rowLabelStyle = AlicizaEditorGUI.Styles.RowLabel;
        }

        private void DrawComponentPanel()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawToolbar("Debugger Component");

            DrawActiveWindowRow();
            DrawToggleRow("Enable Snap", _enableFloatingToggleSnapProperty);
            DrawOpacityRow();

            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar(string title)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 16f, 20f);
            GUI.Label(labelRect, title, _rowLabelStyle);
        }

        private void DrawActiveWindowRow()
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField("Active Window", _fieldLabelStyle, GUILayout.Width(RowLabelWidth));

            Rect popupRect = GUILayoutUtility.GetRect(90f, 20f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
            _activeWindowProperty.enumValueIndex = AlicizaEditorGUI.DrawStyledPopup(
                popupRect,
                _activeWindowProperty.enumValueIndex,
                _activeWindowOptions);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToggleRow(string label, SerializedProperty property)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
            property.boolValue = GUILayout.Toggle(
                property.boolValue,
                property.boolValue ? "Enabled" : "Disabled",
                property.boolValue ? AlicizaEditorGUI.Styles.PillOn : AlicizaEditorGUI.Styles.PillOff,
                GUILayout.Width(78f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOpacityRow()
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField("Window Opacity", _fieldLabelStyle, GUILayout.Width(RowLabelWidth));

            float value = Mathf.Clamp(_windowOpacityProperty.floatValue, SliderMinValue, SliderMaxValue);
            value = GUILayout.HorizontalSlider(value, SliderMinValue, SliderMaxValue, GUILayout.MinWidth(90f));
            value = Mathf.Clamp(EditorGUILayout.FloatField(value, GUILayout.Width(SliderValueWidth)), SliderMinValue, SliderMaxValue);
            _windowOpacityProperty.floatValue = value;

            EditorGUILayout.EndHorizontal();
        }
    }
}
