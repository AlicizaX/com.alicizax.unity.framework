using System;
using AlicizaX.Editor;
using AlicizaX.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Localization.Editor
{
    [CustomEditor(typeof(LocalizationComponent))]
    internal sealed class LocalizationComponentInspector : GameFrameworkInspector
    {
        private const float ToolbarHeight = 30f;
        private const float RowLabelWidth = 132f;

        private string[] _languageNames = Array.Empty<string>();
        private SerializedProperty _language;
        private GUIStyle _panelStyle;
        private GUIStyle _entryBodyStyle;
        private GUIStyle _fieldRowStyle;
        private GUIStyle _fieldLabelStyle;
        private GUIStyle _rowLabelStyle;
        private LanguageType _cachedSelectedLanguageTypes;
        private bool _languageOptionsDirty = true;

        private void OnEnable()
        {
            _language = serializedObject.FindProperty("_language");
            _languageOptionsDirty = true;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EnsureStyles();
            RefreshLanguageOptionsIfNeeded();

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
            _entryBodyStyle = AlicizaEditorGUI.Styles.EntryBody;
            _fieldRowStyle = AlicizaEditorGUI.Styles.FieldRow;
            _fieldLabelStyle = AlicizaEditorGUI.Styles.FieldLabel;
            _rowLabelStyle = AlicizaEditorGUI.Styles.RowLabel;
        }

        private void DrawComponentPanel()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            DrawToolbar("Localization Component");

            EditorGUILayout.BeginVertical(_entryBodyStyle);
            using (new EditorGUI.DisabledGroupScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawLanguageRow();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar(string title)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 16f, 20f);
            GUI.Label(labelRect, title, _rowLabelStyle);
        }

        private void DrawLanguageRow()
        {
            if (_languageNames.Length == 0)
            {
                EditorUtils.TrHelpIconText("No language is enabled in Localization Settings.", MessageType.Warning);
                return;
            }

            int languageIndex = GetLanguageIndex(_language.stringValue);
            int selectedIndex = DrawPopupRow("Language", languageIndex < 0 ? 0 : languageIndex, _languageNames);
            if (selectedIndex < 0 || selectedIndex == languageIndex)
            {
                return;
            }

            string selectedLanguage = _languageNames[selectedIndex];
            _language.stringValue = selectedLanguage;
            EditorPrefs.SetString(LocalizationComponent.PrefsKey, selectedLanguage);
        }

        private int DrawPopupRow(string label, int selectedIndex, string[] options)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));

            Rect popupRect = GUILayoutUtility.GetRect(90f, 20f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
            int nextIndex = AlicizaEditorGUI.DrawStyledPopup(popupRect, selectedIndex, options);

            EditorGUILayout.EndHorizontal();
            return nextIndex;
        }

        private void RefreshLanguageOptionsIfNeeded()
        {
            LanguageType selectedLanguageTypes = LocalizationConfiguration.Instance.SelectedLanguageTypes;
            if (!_languageOptionsDirty && selectedLanguageTypes == _cachedSelectedLanguageTypes)
            {
                return;
            }

            _languageOptionsDirty = false;
            _cachedSelectedLanguageTypes = selectedLanguageTypes;

            int count = 0;
            for (int i = 0; i < LocalizationConfiguration.AllLanguageTypes.Length; i++)
            {
                if ((selectedLanguageTypes & LocalizationConfiguration.AllLanguageTypes[i]) != 0)
                {
                    count++;
                }
            }

            _languageNames = new string[count];

            int writeIndex = 0;
            for (int i = 0; i < LocalizationConfiguration.AllLanguageTypes.Length; i++)
            {
                LanguageType languageType = LocalizationConfiguration.AllLanguageTypes[i];
                if ((selectedLanguageTypes & languageType) == 0)
                {
                    continue;
                }

                _languageNames[writeIndex] = LanguageTypeUtility.ToName(languageType);
                writeIndex++;
            }

            if (_languageNames.Length > 0)
            {
                string currentLanguage = _language.stringValue;
                if (GetLanguageIndex(currentLanguage) < 0)
                {
                    _language.stringValue = _languageNames[0];
                }
            }
        }

        private int GetLanguageIndex(string languageName)
        {
            for (int i = 0; i < _languageNames.Length; i++)
            {
                if (_languageNames[i] == languageName)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
