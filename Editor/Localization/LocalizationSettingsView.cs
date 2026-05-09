using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AlicizaX.Editor;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Localization.Editor
{
    internal sealed class LocalizationSettingsView : IDisposable
    {
        private const string MenuPath = "AlicizaX/Localization/Open Localization Settings";
        private const string DefaultTableFileName = "LocalizationTable.asset";

        private SerializedObject _serializedObject;
        private SerializedProperty _selectedLanguageTypes;
        private SerializedProperty _languageTypesPath;
        private SerializedProperty _languageTypesClassName;
        private SerializedProperty _commentLanguage;
        private SerializedProperty _generateLanguageTypesNamespace;

        private readonly List<string> _languagePopupOptions = new();
        private readonly List<LanguageType> _enabledLanguageTypes = new();
        private readonly List<GameLocaizationTable> _localizationTables = new();
        private Vector2 _scrollPosition;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            LocalizationTableWindow.OpenSettingsEditor();
        }

        public void OnEnable()
        {
            InitGUI();
            RefreshLocalizationTables();
        }

        public void OnFocus()
        {
            RefreshLocalizationTables();
        }

        public void Dispose()
        {
            _serializedObject?.Dispose();
            _serializedObject = null;
            LocalizationConfiguration.Save();
        }

        public void OnGUI(Rect rect)
        {
            if (_serializedObject == null || !_serializedObject.targetObject)
            {
                InitGUI();
            }

            GUILayout.BeginArea(rect);
            {
                _serializedObject.Update();
                RefreshLanguagePopupOptions();

                EditorGUILayout.Space();
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

                DrawLanguageTypesSection();
                EditorGUILayout.Space(8f);
                DrawLanguageTypesGenerateSection();
                EditorGUILayout.Space(8f);
                DrawCreateLocalizationTableSection();
                EditorGUILayout.Space(8f);
                DrawLocalizationTablesSection();

                EditorGUILayout.EndScrollView();

                if (_serializedObject.ApplyModifiedProperties())
                {
                    LocalizationConfiguration.Save();
                }
            }
            GUILayout.EndArea();
        }

        private void InitGUI()
        {
            LocalizationConfiguration setting = LocalizationConfiguration.Instance;
            _serializedObject?.Dispose();
            _serializedObject = new SerializedObject(setting);
            _selectedLanguageTypes = _serializedObject.FindProperty("selectedLanguageTypes");
            _languageTypesPath = _serializedObject.FindProperty("generateLanguageTypesPath");
            _languageTypesClassName = _serializedObject.FindProperty("generateLanguageTypesClassName");
            _commentLanguage = _serializedObject.FindProperty("generateScriptCodeFirstConfig");
            _generateLanguageTypesNamespace = _serializedObject.FindProperty("generateLanguageTypesNamespace");
            EnsureSelectedLanguages();
            RefreshLanguagePopupOptions();
        }

        private void RefreshLanguagePopupOptions()
        {
            _languagePopupOptions.Clear();
            _enabledLanguageTypes.Clear();
            LanguageType selectedLanguageTypes = LocalizationConfiguration.Instance.SelectedLanguageTypes;
            for (int i = 0; i < LocalizationConfiguration.AllLanguageTypes.Length; i++)
            {
                LanguageType languageType = LocalizationConfiguration.AllLanguageTypes[i];
                if ((selectedLanguageTypes & languageType) == 0)
                {
                    continue;
                }

                _enabledLanguageTypes.Add(languageType);
                _languagePopupOptions.Add(LanguageTypeUtility.ToName(languageType));
            }
        }

        private void SanitizeCommentLanguage()
        {
            LanguageType commentLanguage = (LanguageType)_commentLanguage.intValue;
            LanguageType selectedLanguageTypes = (LanguageType)_selectedLanguageTypes.intValue;
            if (commentLanguage == LanguageType.None || (selectedLanguageTypes & commentLanguage) != 0)
            {
                return;
            }

            _commentLanguage.intValue = (int)LanguageType.None;
            _serializedObject.ApplyModifiedProperties();
        }

        private void EnsureSelectedLanguages()
        {
            LanguageType selectedLanguageTypes = (LanguageType)_selectedLanguageTypes.intValue & LanguageType.All;
            if (selectedLanguageTypes == LanguageType.None)
            {
                selectedLanguageTypes = LanguageTypeUtility.Default;
            }

            if (_selectedLanguageTypes.intValue == (int)selectedLanguageTypes)
            {
                return;
            }

            _selectedLanguageTypes.intValue = (int)selectedLanguageTypes;
            _serializedObject.ApplyModifiedProperties();
            LocalizationConfiguration.Save();
        }

        private void RefreshLocalizationTables()
        {
            _localizationTables.Clear();
            string[] guids = AssetDatabase.FindAssets("t:GameLocaizationTable");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameLocaizationTable table = AssetDatabase.LoadAssetAtPath<GameLocaizationTable>(assetPath);
                if (table != null)
                {
                    _localizationTables.Add(table);
                }
            }
        }

        private void DrawLanguageTypesSection()
        {
            using (new EditorDrawing.BorderBoxScope(new GUIContent("Language Types"), roundedBox: false))
            {
                EditorGUI.BeginChangeCheck();
                LanguageType selectedLanguages = (LanguageType)EditorGUILayout.EnumFlagsField("Enabled Languages", (LanguageType)_selectedLanguageTypes.intValue);
                if (EditorGUI.EndChangeCheck())
                {
                    selectedLanguages &= LanguageType.All;
                    if (selectedLanguages == LanguageType.None)
                    {
                        EditorUtility.DisplayDialog("Invalid Language Selection", "At least one language must be enabled.", "OK");
                        return;
                    }

                    _selectedLanguageTypes.intValue = (int)selectedLanguages;
                    _serializedObject.ApplyModifiedProperties();
                    SanitizeCommentLanguage();
                    LocalizationConfiguration.Save();
                    RefreshLanguagePopupOptions();
                    ApplyLanguageSelectionToTables();
                }
            }
        }

        private void DrawLanguageTypesGenerateSection()
        {
            using (new EditorDrawing.BorderBoxScope(new GUIContent("Generate LanguageTypes"), roundedBox: false))
            {
                EditorGUILayout.PropertyField(_languageTypesPath, new GUIContent("Folder Path"));
                EditorGUILayout.PropertyField(_languageTypesClassName, new GUIContent("Class Name"));
                EditorGUILayout.PropertyField(_generateLanguageTypesNamespace, new GUIContent("Namespace"));

                EditorDrawing.DrawStringSelectPopup(
                    new GUIContent("Comment Language"),
                    new GUIContent("None"),
                    _languagePopupOptions.ToArray(),
                    LanguageTypeUtility.ToName((LanguageType)_commentLanguage.intValue),
                    selected =>
                    {
                        if (TryGetLanguageType(selected, out LanguageType languageType))
                        {
                            _commentLanguage.intValue = (int)languageType;
                        }
                        _serializedObject.ApplyModifiedProperties();
                        LocalizationConfiguration.Save();
                    });

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Generate LanguageTypes", GUILayout.Width(180f)))
                {
                    RegenerateLanguageTypes();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawCreateLocalizationTableSection()
        {
            using (new EditorDrawing.BorderBoxScope(new GUIContent("Create LocalizationTable"), roundedBox: false))
            {
                if (GUILayout.Button("Create Localization Table", GUILayout.Width(220f), GUILayout.Height(26f)))
                {
                    CreateLocalizationTable();
                }
            }
        }

        private void DrawLocalizationTablesSection()
        {
            using (new EditorDrawing.BorderBoxScope(new GUIContent("Localization Tables"), roundedBox: false))
            {
                if (GUILayout.Button("Refresh", GUILayout.Width(120f)))
                {
                    RefreshLocalizationTables();
                }

                EditorGUILayout.Space(4f);

                if (_localizationTables.Count == 0)
                {
                    EditorGUILayout.HelpBox("No LocalizationTable assets found in the project.", MessageType.Info);
                    return;
                }

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    for (int i = 0; i < _localizationTables.Count; i++)
                    {
                        EditorGUILayout.ObjectField(_localizationTables[i], typeof(GameLocaizationTable), false);
                    }
                }
            }
        }

        private void RegenerateLanguageTypes()
        {
            string folderPath = _languageTypesPath.stringValue;
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                EditorUtility.DisplayDialog("Invalid Path", "LanguageTypes output path cannot be empty.", "OK");
                return;
            }

            string className = _languageTypesClassName.stringValue;
            if (!LocalizationWindowUtility.IsValidIdentifierName(className))
            {
                EditorUtility.DisplayDialog("Invalid Class Name", "LanguageTypes class name must be a valid C# identifier.", "OK");
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string generatedCode = BuildLanguageTypesCode(className, _generateLanguageTypesNamespace.stringValue, LocalizationConfiguration.Instance.LanguageTypeNames);
            string finalPath = Path.Combine(folderPath, className + ".cs");
            File.WriteAllText(finalPath, generatedCode, Encoding.UTF8);
            AssetDatabase.Refresh();
        }

        private static string BuildLanguageTypesCode(string className, string namespaceName, IReadOnlyList<string> languages)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            string indent = string.IsNullOrWhiteSpace(namespaceName) ? string.Empty : "    ";
            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// AutoGenerate");
            sb.AppendLine($"{indent}/// </summary>");
            sb.AppendLine($"{indent}public static class {className}");
            sb.AppendLine($"{indent}{{");

            string memberIndent = indent + "    ";
            for (int i = 0; i < languages.Count; i++)
            {
                string languageName = languages[i];
                if (!LocalizationWindowUtility.IsValidIdentifierName(languageName))
                {
                    Debug.LogError($"Invalid language name for code generation: {languageName}");
                    continue;
                }

                sb.AppendLine($"{memberIndent}public const string {languageName} = \"{languageName}\";");
            }


            sb.AppendLine();
            sb.AppendLine($"{memberIndent}public static readonly IReadOnlyList<string> Languages = new List<string>");
            sb.AppendLine($"{memberIndent}{{");
            for (int i = 0; i < languages.Count; i++)
            {
                string languageName = languages[i];
                if (!LocalizationWindowUtility.IsValidIdentifierName(languageName))
                {
                    continue;
                }

                sb.AppendLine($"{memberIndent}    \"{languageName}\",");
            }

            sb.AppendLine($"{memberIndent}}};");
            sb.AppendLine();
            sb.AppendLine($"{memberIndent}public static string IndexToString(int index)");
            sb.AppendLine($"{memberIndent}{{");
            sb.AppendLine($"{memberIndent}    if (index < 0 || index >= Languages.Count) return \"Unknown\";");
            sb.AppendLine($"{memberIndent}    return Languages[index];");
            sb.AppendLine($"{memberIndent}}}");
            sb.AppendLine();
            sb.AppendLine($"{memberIndent}public static int StringToIndex(string s)");
            sb.AppendLine($"{memberIndent}{{");
            sb.AppendLine($"{memberIndent}    int index = -1;");
            sb.AppendLine($"{memberIndent}    for (int i = 0; i < Languages.Count; i++)");
            sb.AppendLine($"{memberIndent}    {{");
            sb.AppendLine($"{memberIndent}        if (Languages[i] == s)");
            sb.AppendLine($"{memberIndent}        {{");
            sb.AppendLine($"{memberIndent}            index = i;");
            sb.AppendLine($"{memberIndent}            break;");
            sb.AppendLine($"{memberIndent}        }}");
            sb.AppendLine($"{memberIndent}    }}");
            sb.AppendLine();
            sb.AppendLine($"{memberIndent}    return index;");
            sb.AppendLine($"{memberIndent}}}");
            sb.AppendLine($"{indent}}}");


            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private void CreateLocalizationTable()
        {
            string path = EditorUtility.SaveFilePanel("Create Localization Table", "Assets", DefaultTableFileName, "asset");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!TryConvertToAssetPath(path, out string assetPath))
            {
                EditorUtility.DisplayDialog("Error", "Localization table must be saved within the project's Assets folder.", "OK");
                return;
            }

            if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                assetPath += ".asset";
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                bool overwrite = EditorUtility.DisplayDialog("File Exists",
                    "A file with the same name already exists. Do you want to overwrite it?",
                    "Yes", "No");
                if (!overwrite)
                {
                    return;
                }
            }

            string directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            GameLocaizationTable table = ScriptableObject.CreateInstance<GameLocaizationTable>();
            table.TableSheet = new List<GameLocaizationTable.TableData>();
            table.Languages = new List<LocalizationLanguage>();

            AssetDatabase.CreateAsset(table, assetPath);
            RefreshLanguagePopupOptions();
            for (int i = 0; i < _enabledLanguageTypes.Count; i++)
            {
                LanguageType languageType = _enabledLanguageTypes[i];
                string languageName = LanguageTypeUtility.ToName(languageType);
                LocalizationLanguage asset = ScriptableObject.CreateInstance<LocalizationLanguage>();
                asset.name = languageName;
                asset.LanguageName = languageName;
                asset.Strings = new List<LocalizationLanguage.LocalizationString>();
                table.Languages.Add(asset);
                AssetDatabase.AddObjectToAsset(asset, table);
            }

            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshLocalizationTables();

            Selection.activeObject = table;
            EditorGUIUtility.PingObject(table);
        }

        private static bool TryConvertToAssetPath(string absolutePath, out string assetPath)
        {
            assetPath = null;
            string dataPath = Application.dataPath.Replace('\\', '/');
            string normalizedPath = absolutePath.Replace('\\', '/');
            if (!normalizedPath.StartsWith(dataPath, StringComparison.Ordinal))
            {
                return false;
            }

            assetPath = "Assets" + normalizedPath.Substring(dataPath.Length);
            return true;
        }

        private void ApplyLanguageSelectionToTables()
        {
            string[] guids = AssetDatabase.FindAssets("t:GameLocaizationTable");
            int tablesUpdated = 0;
            RefreshLanguagePopupOptions();

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                GameLocaizationTable table = AssetDatabase.LoadAssetAtPath<GameLocaizationTable>(assetPath);
                if (table == null)
                {
                    continue;
                }

                bool tableModified = false;

                for (int i = 0; i < _enabledLanguageTypes.Count; i++)
                {
                    LanguageType newLanguageType = _enabledLanguageTypes[i];
                    string newLanguageName = LanguageTypeUtility.ToName(newLanguageType);
                    if (table.Languages.Exists(lang => lang != null && lang.LanguageName == newLanguageName))
                    {
                        continue;
                    }

                    LocalizationLanguage newLanguage = ScriptableObject.CreateInstance<LocalizationLanguage>();
                    newLanguage.name = newLanguageName;
                    newLanguage.LanguageName = newLanguageName;
                    newLanguage.Strings = new List<LocalizationLanguage.LocalizationString>();

                    foreach (GameLocaizationTable.TableData section in table.TableSheet)
                    {
                        foreach (GameLocaizationTable.SheetItem item in section.SectionSheet)
                        {
                            string sectionKey = section.SectionName.Replace(" ", string.Empty);
                            string itemKey = item.Key.Replace(" ", string.Empty);
                            string fullKey = sectionKey + "." + itemKey;

                            newLanguage.Strings.Add(new LocalizationLanguage.LocalizationString
                            {
                                SectionId = section.Id,
                                EntryId = item.Id,
                                Key = fullKey,
                                Value = string.Empty
                            });
                        }
                    }

                    AssetDatabase.AddObjectToAsset(newLanguage, table);
                    table.Languages.Add(newLanguage);
                    tableModified = true;
                }

                for (int i = table.Languages.Count - 1; i >= 0; i--)
                {
                    LocalizationLanguage languageToDelete = table.Languages[i];
                    if (languageToDelete == null || !ContainsLanguage(_enabledLanguageTypes, languageToDelete.LanguageName))
                    {
                        table.Languages.RemoveAt(i);
                        if (languageToDelete != null)
                        {
                            UnityEngine.Object.DestroyImmediate(languageToDelete, true);
                        }

                        tableModified = true;
                    }
                }

                if (tableModified)
                {
                    EditorUtility.SetDirty(table);
                    tablesUpdated++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RefreshLanguagePopupOptions();
            RefreshLocalizationTables();

            Debug.Log($"Language selection applied to {tablesUpdated} GameLocalizationTable(s).");
        }

        private static bool ContainsLanguage(IReadOnlyList<LanguageType> languages, string languageName)
        {
            for (int i = 0; i < languages.Count; i++)
            {
                if (LanguageTypeUtility.ToName(languages[i]) == languageName)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetLanguageType(string languageName, out LanguageType languageType)
        {
            for (int i = 0; i < _enabledLanguageTypes.Count; i++)
            {
                LanguageType candidate = _enabledLanguageTypes[i];
                if (LanguageTypeUtility.ToName(candidate) == languageName)
                {
                    languageType = candidate;
                    return true;
                }
            }

            languageType = LanguageType.None;
            return false;
        }
    }
}
