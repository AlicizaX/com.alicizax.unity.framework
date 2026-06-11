using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlicizaX.UI.Runtime;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AlicizaX.UI.Editor
{
    public class UISettingEditorWindow : EditorWindow
    {
        private const float ToolbarButtonWidth = 36f;

        private static List<string> cacheFilterType;

        [MenuItem("AlicizaX/UISetting Window", false, 211)]
        private static void OpenWindow()
        {
            var window = GetWindow<UISettingEditorWindow>("UI Setting");
            window.minSize = new Vector2(760, 520);
            window.Show();
        }

        private readonly string[] toolbarTitles = { "General", "Script Generation", "Element Mapping" };

        private UIGenerateConfiguration uiGenerateConfiguration;
        private SerializedObject serializedConfig;
        private SerializedProperty commonDataProperty;
        private SerializedProperty regexConfigsProperty;
        private SerializedProperty scriptGenerateConfigsProperty;
        private SerializedProperty identifierFormatterTypeProperty;
        private SerializedProperty resourcePathResolverTypeProperty;
        private SerializedProperty scriptCodeEmitterTypeProperty;
        private SerializedProperty scriptFileWriterTypeProperty;
        private SerializedProperty excludeKeywordsProperty;

        private ReorderableList regexList;
        private ReorderableList projectList;
        private ReorderableList excludeList;

        private Vector2 scroll;
        private int toolbarTab;
        private TextAsset importText;
        private string previewLabel;
        private string previewCompLabel;
        private List<string> identifierFormatterTypes = new();
        private List<string> resourcePathResolverTypes = new();
        private List<string> scriptCodeEmitterTypes = new();
        private List<string> scriptFileWriterTypes = new();
        private int identifierFormatterSelectIndex;
        private int resourcePathResolverSelectIndex;
        private int scriptCodeEmitterSelectIndex;
        private int scriptFileWriterSelectIndex;

        private void OnEnable()
        {
            BindConfiguration();
            SetupLists();
            RefreshGeneratorServiceTypes();
            RefreshPreview();
            CollectComponentTypeNamesFallback();
        }

        private void OnDisable()
        {
            SaveConfig(false);
        }

        private void BindConfiguration()
        {
            uiGenerateConfiguration = UIGenerateConfiguration.LoadOrCreate();
            if (uiGenerateConfiguration == null)
            {
                uiGenerateConfiguration = CreateInstance<UIGenerateConfiguration>();
            }

            uiGenerateConfiguration.UIGenerateCommonData ??= new UIGenerateCommonData();
            uiGenerateConfiguration.UIElementRegexConfigs ??= new List<UIEelementRegexData>();
            uiGenerateConfiguration.UIScriptGenerateConfigs ??= new List<UIScriptGenerateData>();
            uiGenerateConfiguration.UIGenerateCommonData.ExcludeKeywords ??= Array.Empty<string>();

            serializedConfig = new SerializedObject(uiGenerateConfiguration);
            commonDataProperty = serializedConfig.FindProperty(nameof(UIGenerateConfiguration.UIGenerateCommonData));
            regexConfigsProperty = serializedConfig.FindProperty(nameof(UIGenerateConfiguration.UIElementRegexConfigs));
            scriptGenerateConfigsProperty = serializedConfig.FindProperty(nameof(UIGenerateConfiguration.UIScriptGenerateConfigs));
            identifierFormatterTypeProperty = serializedConfig.FindProperty(nameof(UIGenerateConfiguration.UIIdentifierFormatterTypeName));
            resourcePathResolverTypeProperty = serializedConfig.FindProperty(nameof(UIGenerateConfiguration.UIResourcePathResolverTypeName));
            scriptCodeEmitterTypeProperty = serializedConfig.FindProperty(nameof(UIGenerateConfiguration.UIScriptCodeEmitterTypeName));
            scriptFileWriterTypeProperty = serializedConfig.FindProperty(nameof(UIGenerateConfiguration.UIScriptFileWriterTypeName));
            excludeKeywordsProperty = commonDataProperty?.FindPropertyRelative(nameof(UIGenerateCommonData.ExcludeKeywords));
        }

        private void SetupLists()
        {
            SetupExcludeList();
            SetupRegexList();
            SetupProjectList();
        }

        private void SetupExcludeList()
        {
            excludeList = new ReorderableList(serializedConfig, excludeKeywordsProperty, true, true, true, true);
            excludeList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Exclude Keywords");
            excludeList.drawElementCallback = (rect, index, active, focused) =>
            {
                if (!IsValidArrayIndex(excludeKeywordsProperty, index))
                {
                    return;
                }

                rect.y += 2f;
                var itemProperty = excludeKeywordsProperty.GetArrayElementAtIndex(index);
                itemProperty.stringValue = EditorGUI.TextField(
                    new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                    itemProperty.stringValue);
            };
            excludeList.onAddCallback = _ =>
            {
                int index = excludeKeywordsProperty.arraySize;
                excludeKeywordsProperty.InsertArrayElementAtIndex(index);
                excludeKeywordsProperty.GetArrayElementAtIndex(index).stringValue = string.Empty;
                ApplyConfigChanges();
            };
            excludeList.onRemoveCallback = list =>
            {
                ReorderableList.defaultBehaviours.DoRemoveButton(list);
                ApplyConfigChanges();
            };
        }

        private void SetupRegexList()
        {
            regexList = new ReorderableList(serializedConfig, regexConfigsProperty, true, true, true, true);
            regexList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "UI Element Mapping (Prefix -> Component)");
            regexList.elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 6f;
            regexList.drawElementCallback = (rect, index, active, focused) =>
            {
                if (!IsValidArrayIndex(regexConfigsProperty, index))
                {
                    return;
                }

                var itemProperty = regexConfigsProperty.GetArrayElementAtIndex(index);
                var regexProperty = itemProperty.FindPropertyRelative(nameof(UIEelementRegexData.uiElementRegex));
                var componentTypeProperty = itemProperty.FindPropertyRelative(nameof(UIEelementRegexData.componentType));

                rect.y += 2f;
                float lineHeight = EditorGUIUtility.singleLineHeight;
                float leftWidth = rect.width * 0.8f;
                Rect textRect = new Rect(rect.x, rect.y, leftWidth - 8f, lineHeight);
                Rect buttonRect = new Rect(rect.x + leftWidth + 8f, rect.y, Mathf.Min(180f, rect.width - leftWidth - 8f), lineHeight);

                regexProperty.stringValue = EditorGUI.TextField(textRect, regexProperty.stringValue);

                string buttonLabel = string.IsNullOrEmpty(componentTypeProperty.stringValue)
                    ? "(Select Type)"
                    : componentTypeProperty.stringValue;

                if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
                {
                    var options = CollectComponentTypeNamesFallback();
                    Rect anchor = new Rect(
                        buttonRect.x,
                        buttonRect.y + buttonRect.height,
                        Mathf.Min(360f, Mathf.Max(buttonRect.width, 200f)),
                        buttonRect.height);

                    SearchablePopup.Show(
                        anchor,
                        options,
                        Mathf.Max(0, options.IndexOf(componentTypeProperty.stringValue)),
                        selectedIndex => UpdateRegexComponentType(index, options, selectedIndex));
                }
            };
            regexList.onAddCallback = _ =>
            {
                int index = regexConfigsProperty.arraySize;
                regexConfigsProperty.InsertArrayElementAtIndex(index);
                var itemProperty = regexConfigsProperty.GetArrayElementAtIndex(index);
                itemProperty.FindPropertyRelative(nameof(UIEelementRegexData.uiElementRegex)).stringValue = string.Empty;
                itemProperty.FindPropertyRelative(nameof(UIEelementRegexData.componentType)).stringValue = string.Empty;
                ApplyConfigChanges();
            };
            regexList.onRemoveCallback = list =>
            {
                ReorderableList.defaultBehaviours.DoRemoveButton(list);
                ApplyConfigChanges();
            };
        }

        private void SetupProjectList()
        {
            projectList = new ReorderableList(serializedConfig, scriptGenerateConfigsProperty, true, true, true, true);
            projectList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "UI Script Generation Config");
            projectList.elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 5f + 10f;
            projectList.drawElementCallback = (rect, index, active, focused) =>
            {
                if (!IsValidArrayIndex(scriptGenerateConfigsProperty, index))
                {
                    return;
                }

                var itemProperty = scriptGenerateConfigsProperty.GetArrayElementAtIndex(index);
                var projectNameProperty = itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.ProjectName));
                var namespaceProperty = itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.NameSpace));
                var generatePathProperty = itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.GenerateHolderCodePath));
                var prefabRootProperty = itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.UIPrefabRootPath));
                var loadTypeProperty = itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.LoadType));

                float lineHeight = EditorGUIUtility.singleLineHeight;
                float padding = 2f;

                projectNameProperty.stringValue = EditorGUI.TextField(
                    new Rect(rect.x, rect.y, rect.width, lineHeight),
                    "Project Name",
                    projectNameProperty.stringValue);

                namespaceProperty.stringValue = EditorGUI.TextField(
                    new Rect(rect.x, rect.y + (lineHeight + padding), rect.width, lineHeight),
                    "Namespace",
                    namespaceProperty.stringValue);

                DrawFolderField(
                    "Holder Code Path",
                    generatePathProperty,
                    rect.x,
                    rect.y + 2f * (lineHeight + padding),
                    rect.width,
                    lineHeight);

                DrawFolderField(
                    "Prefab Root Path",
                    prefabRootProperty,
                    rect.x,
                    rect.y + 3f * (lineHeight + padding),
                    rect.width,
                    lineHeight);

                loadTypeProperty.enumValueIndex = (int)(EUIResLoadType)EditorGUI.EnumPopup(
                    new Rect(rect.x, rect.y + 4f * (lineHeight + padding), rect.width, lineHeight),
                    "Load Type",
                    (EUIResLoadType)loadTypeProperty.enumValueIndex);
            };
            projectList.onAddCallback = _ =>
            {
                int index = scriptGenerateConfigsProperty.arraySize;
                scriptGenerateConfigsProperty.InsertArrayElementAtIndex(index);
                var itemProperty = scriptGenerateConfigsProperty.GetArrayElementAtIndex(index);
                itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.ProjectName)).stringValue = "NewProject";
                itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.NameSpace)).stringValue = "Game.UI";
                itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.GenerateHolderCodePath)).stringValue = "Assets/Scripts/UI/Generated";
                itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.UIPrefabRootPath)).stringValue = "Assets/Resources/UI";
                itemProperty.FindPropertyRelative(nameof(UIScriptGenerateData.LoadType)).enumValueIndex = (int)EUIResLoadType.Resources;
                ApplyConfigChanges();
            };
            projectList.onRemoveCallback = list =>
            {
                ReorderableList.defaultBehaviours.DoRemoveButton(list);
                ApplyConfigChanges();
            };
        }

        private void OnGUI()
        {
            if (serializedConfig == null)
            {
                BindConfiguration();
                SetupLists();
                RefreshGeneratorServiceTypes();
                RefreshPreview();
            }

            serializedConfig.Update();

            GUILayout.Space(6f);
            DrawToolbar();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Space(8f);

            switch (toolbarTab)
            {
                case 0:
                    DrawCommonPane();
                    break;
                case 1:
                    DrawScriptPane();
                    break;
                case 2:
                    DrawElementPane();
                    break;
            }

            EditorGUILayout.EndScrollView();
            GUILayout.Space(8f);

            if (serializedConfig.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(uiGenerateConfiguration);
                RefreshPreview();
            }
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            for (int i = 0; i < toolbarTitles.Length; i++)
            {
                bool isActive = toolbarTab == i;
                bool toggled = GUILayout.Toggle(isActive, toolbarTitles[i], EditorStyles.toolbarButton, GUILayout.Height(22f));
                if (toggled && toolbarTab != i)
                {
                    toolbarTab = i;
                    Repaint();
                }
            }

            GUILayout.FlexibleSpace();

            var saveIcon = EditorGUIUtility.IconContent("SaveActive");
            var refreshIcon = EditorGUIUtility.IconContent("Refresh");
            var reloadIcon = EditorGUIUtility.IconContent("RotateTool");

            if (GUILayout.Button(new GUIContent(saveIcon.image, "Save configuration"), EditorStyles.toolbarButton, GUILayout.Width(ToolbarButtonWidth)))
            {
                SaveConfig(true);
            }

            if (GUILayout.Button(new GUIContent(refreshIcon.image, "Refresh preview"), EditorStyles.toolbarButton, GUILayout.Width(ToolbarButtonWidth)))
            {
                RefreshPreview();
            }

            if (GUILayout.Button(new GUIContent(reloadIcon.image, "Reload configuration"), EditorStyles.toolbarButton, GUILayout.Width(ToolbarButtonWidth)))
            {
                ReloadConfiguration();
            }

            GUILayout.EndHorizontal();
        }

        private void DrawCommonPane()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.PropertyField(
                commonDataProperty.FindPropertyRelative(nameof(UIGenerateCommonData.ComCheckSplitName)),
                new GUIContent("组件分割符", "例如: Button#Close"));
            EditorGUILayout.PropertyField(
                commonDataProperty.FindPropertyRelative(nameof(UIGenerateCommonData.ComCheckEndName)),
                new GUIContent("组件结尾符", "例如: @End"));
            EditorGUILayout.PropertyField(
                commonDataProperty.FindPropertyRelative(nameof(UIGenerateCommonData.ArrayComSplitName)),
                new GUIContent("数组分割", "例如: *Item"));
            EditorGUILayout.PropertyField(
                commonDataProperty.FindPropertyRelative(nameof(UIGenerateCommonData.GeneratePrefix)),
                new GUIContent("生成脚本前缀"));

            DrawTypePopup(
                "Identifier Formatter",
                identifierFormatterTypeProperty,
                identifierFormatterTypes,
                ref identifierFormatterSelectIndex,
                typeof(DefaultUIIdentifierFormatter).FullName);
            DrawTypePopup(
                "Resource Path Resolver",
                resourcePathResolverTypeProperty,
                resourcePathResolverTypes,
                ref resourcePathResolverSelectIndex,
                typeof(DefaultUIResourcePathResolver).FullName);
            DrawTypePopup(
                "Script Code Emitter",
                scriptCodeEmitterTypeProperty,
                scriptCodeEmitterTypes,
                ref scriptCodeEmitterSelectIndex,
                typeof(DefaultUIScriptCodeEmitter).FullName);
            DrawTypePopup(
                "Script File Writer",
                scriptFileWriterTypeProperty,
                scriptFileWriterTypes,
                ref scriptFileWriterSelectIndex,
                typeof(DefaultUIScriptFileWriter).FullName);

            GUILayout.Space(8f);
            excludeList.DoLayoutList();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Script Preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(previewLabel ?? string.Empty, MessageType.None);
            EditorGUILayout.LabelField("Component Preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(previewCompLabel ?? string.Empty, MessageType.None);
            EditorGUILayout.EndVertical();

            GUILayout.Space(8f);
        }

        private void DrawScriptPane()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("UI Script Generation Config", EditorStyles.boldLabel);
            GUILayout.Space(6f);
            projectList.DoLayoutList();
            EditorGUILayout.EndVertical();

            GUILayout.Space(8f);
        }

        private void DrawElementPane()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("UI Element Mapping", EditorStyles.boldLabel);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Load Default", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                LoadDefault();
            }

            if (GUILayout.Button("Export", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                ExportConfig();
            }

            GUILayout.Space(8f);

            importText = (TextAsset)EditorGUILayout.ObjectField(
                importText,
                typeof(TextAsset),
                false,
                GUILayout.Height(18f),
                GUILayout.MinWidth(200f));

            GUI.enabled = importText != null;
            if (GUILayout.Button("Import", EditorStyles.toolbarButton, GUILayout.Width(84f)) && importText != null)
            {
                ImportConfig(importText);
                importText = null;
            }

            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            regexList.DoLayoutList();
            EditorGUILayout.EndVertical();
        }

        private void DrawFolderField(string label, SerializedProperty property, float x, float y, float width, float height)
        {
            if (property == null)
            {
                return;
            }

            Rect textRect = new Rect(x, y, width - 76f, height);
            Rect buttonRect = new Rect(x + width - 72f, y, 68f, height);
            property.stringValue = EditorGUI.TextField(textRect, label, property.stringValue);

            if (!GUI.Button(buttonRect, "Select"))
            {
                return;
            }

            string selectedPath = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(selectedPath))
            {
                return;
            }

            if (!selectedPath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Please select a folder under Assets.", "OK");
                return;
            }

            Undo.RecordObject(uiGenerateConfiguration, "Change UI Generation Folder");
            property.stringValue = "Assets" + selectedPath.Substring(Application.dataPath.Length);
            ApplyConfigChanges();
        }

        private void RefreshGeneratorServiceTypes()
        {
            RefreshTypeOptions(
                typeof(IUIIdentifierFormatter),
                identifierFormatterTypeProperty,
                typeof(DefaultUIIdentifierFormatter).FullName,
                ref identifierFormatterTypes,
                ref identifierFormatterSelectIndex);
            RefreshTypeOptions(
                typeof(IUIResourcePathResolver),
                resourcePathResolverTypeProperty,
                typeof(DefaultUIResourcePathResolver).FullName,
                ref resourcePathResolverTypes,
                ref resourcePathResolverSelectIndex);
            RefreshTypeOptions(
                typeof(IUIScriptCodeEmitter),
                scriptCodeEmitterTypeProperty,
                typeof(DefaultUIScriptCodeEmitter).FullName,
                ref scriptCodeEmitterTypes,
                ref scriptCodeEmitterSelectIndex);
            RefreshTypeOptions(
                typeof(IUIScriptFileWriter),
                scriptFileWriterTypeProperty,
                typeof(DefaultUIScriptFileWriter).FullName,
                ref scriptFileWriterTypes,
                ref scriptFileWriterSelectIndex);
        }

        private static void RefreshTypeOptions(
            Type interfaceType,
            SerializedProperty property,
            string defaultTypeName,
            ref List<string> options,
            ref int selectedIndex)
        {
            options = AlicizaX.Utility.Assembly
                .GetRuntimeTypeNames(interfaceType)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(typeName => typeName, StringComparer.Ordinal)
                .ToList();

            if (!options.Contains(defaultTypeName))
            {
                options.Insert(0, defaultTypeName);
            }

            string currentType = string.IsNullOrWhiteSpace(property?.stringValue) ? defaultTypeName : property.stringValue;
            if (!string.IsNullOrEmpty(currentType) && !options.Contains(currentType))
            {
                options.Insert(0, currentType);
            }

            selectedIndex = Mathf.Max(0, options.IndexOf(currentType));
        }

        private static void DrawTypePopup(
            string label,
            SerializedProperty property,
            List<string> options,
            ref int selectedIndex,
            string defaultTypeName)
        {
            if (property == null || options == null || options.Count == 0)
            {
                return;
            }

            int currentIndex = selectedIndex;
            if (currentIndex < 0 || currentIndex >= options.Count)
            {
                string currentType = string.IsNullOrWhiteSpace(property.stringValue) ? defaultTypeName : property.stringValue;
                currentIndex = Mathf.Max(0, options.IndexOf(currentType));
                selectedIndex = currentIndex;
            }

            int nextIndex = EditorGUILayout.Popup(label, currentIndex, options.ToArray());
            if (nextIndex >= 0 && nextIndex < options.Count && nextIndex != currentIndex)
            {
                selectedIndex = nextIndex;
                property.stringValue = options[nextIndex];
            }
        }

        private List<string> CollectComponentTypeNamesFallback()
        {
            if (cacheFilterType == null)
            {
                cacheFilterType = AlicizaX.Utility.Assembly.GetTypes()
                    .Where(type => !string.IsNullOrEmpty(type.FullName) && !type.FullName.Contains("Editor"))
                    .Where(type => !type.IsAbstract && !type.IsInterface)
                    .Where(type => !type.IsGenericTypeDefinition)
                    .Where(type => !type.IsSubclassOf(typeof(UIHolderObjectBase)))
                    .Where(type => type.IsSubclassOf(typeof(Component)))
                    .Where(type => !type.FullName.Contains("YooAsset"))
                    .Where(type => !type.FullName.Contains("Unity.VisualScripting"))
                    .Where(type => !type.FullName.Contains("Cysharp.Threading"))
                    .Where(type => !type.FullName.Contains("UnityEngine.Rendering.UI.Debug"))
                    .Where(type => !type.FullName.Contains("Unity.PerformanceTesting"))
                    .Where(type => !type.FullName.Contains("UnityEngine.TestTools"))
                    .Select(type => type.FullName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(typeName => typeName, StringComparer.Ordinal)
                    .ToList();

                cacheFilterType.Add(typeof(GameObject).Name);
            }

            return cacheFilterType;
        }

        private void UpdateRegexComponentType(int index, List<string> options, int selectedIndex)
        {
            if (!IsValidArrayIndex(regexConfigsProperty, index) ||
                selectedIndex < 0 ||
                selectedIndex >= options.Count)
            {
                return;
            }

            Undo.RecordObject(uiGenerateConfiguration, "Change UI Element Component Type");
            serializedConfig.Update();
            regexConfigsProperty
                .GetArrayElementAtIndex(index)
                .FindPropertyRelative(nameof(UIEelementRegexData.componentType))
                .stringValue = options[selectedIndex];
            ApplyConfigChanges();
            Repaint();
        }

        private void LoadDefault()
        {
            string defaultPath = UIGlobalPath.DefaultComPath;
            if (!File.Exists(defaultPath))
            {
                EditorUtility.DisplayDialog("Load Default", $"Default file not found: {defaultPath}", "OK");
                return;
            }

            string text = File.ReadAllText(defaultPath);
            var list = JsonConvert.DeserializeObject<List<UIEelementRegexData>>(text);
            ReplaceRegexConfigs(list, "Load Default UI Element Config");
        }

        private void ImportConfig(TextAsset text)
        {
            try
            {
                var list = JsonConvert.DeserializeObject<List<UIEelementRegexData>>(text.text);
                ReplaceRegexConfigs(list, "Import UI Element Config");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Import Failed", "Import failed. Check the console for details.", "OK");
            }
        }

        private void ReplaceRegexConfigs(IReadOnlyList<UIEelementRegexData> list, string undoName)
        {
            if (list == null)
            {
                return;
            }

            Undo.RecordObject(uiGenerateConfiguration, undoName);
            serializedConfig.Update();
            regexConfigsProperty.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var itemProperty = regexConfigsProperty.GetArrayElementAtIndex(i);
                itemProperty.FindPropertyRelative(nameof(UIEelementRegexData.uiElementRegex)).stringValue = list[i]?.uiElementRegex ?? string.Empty;
                itemProperty.FindPropertyRelative(nameof(UIEelementRegexData.componentType)).stringValue = list[i]?.componentType ?? string.Empty;
            }

            ApplyConfigChanges();
        }

        private void ExportConfig()
        {
            serializedConfig.ApplyModifiedProperties();

            string json = JsonConvert.SerializeObject(uiGenerateConfiguration.UIElementRegexConfigs, Formatting.Indented);
            string path = EditorUtility.SaveFilePanel("Export UI Element Config", Application.dataPath, "uielementconfig", "txt");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Export Complete", "Config exported.", "OK");
        }

        private void ReloadConfiguration()
        {
            BindConfiguration();
            SetupLists();
            RefreshGeneratorServiceTypes();
            RefreshPreview();
            Repaint();
        }

        private void RefreshPreview()
        {
            string prefix = commonDataProperty?.FindPropertyRelative(nameof(UIGenerateCommonData.GeneratePrefix))?.stringValue ?? "ui";
            string arraySplit = commonDataProperty?.FindPropertyRelative(nameof(UIGenerateCommonData.ArrayComSplitName))?.stringValue ?? "*";
            string componentSplit = commonDataProperty?.FindPropertyRelative(nameof(UIGenerateCommonData.ComCheckSplitName))?.stringValue ?? "#";
            string componentEnd = commonDataProperty?.FindPropertyRelative(nameof(UIGenerateCommonData.ComCheckEndName))?.stringValue ?? "@";

            previewLabel = $"{prefix}_UITestWindow";
            previewCompLabel = $"{arraySplit}Text{componentSplit}Img{componentEnd}Test{arraySplit}0";
            Repaint();
        }

        private void ApplyConfigChanges()
        {
            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(uiGenerateConfiguration);
            RefreshPreview();
        }

        private void SaveConfig(bool logSave)
        {
            if (serializedConfig == null || uiGenerateConfiguration == null)
            {
                return;
            }

            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(uiGenerateConfiguration);
            UIGenerateConfiguration.Save();

            if (logSave)
            {
                Debug.Log("UIGenerateConfiguration saved.");
            }
        }

        private static bool IsValidArrayIndex(SerializedProperty property, int index)
        {
            return property != null && index >= 0 && index < property.arraySize;
        }
    }

    internal class SearchablePopup : PopupWindowContent
    {
        private const float RowHeight = 20f;

        private static GUIStyle searchFieldStyle;
        private static GUIStyle cancelStyle;
        private static GUIStyle rowStyle;
        private static GUIStyle selectedRowStyle;

        private readonly List<string> allItems;
        private readonly Action<int> onSelect;
        private List<string> filtered;
        private int currentIndex;
        private string search = string.Empty;
        private Vector2 scroll;

        private SearchablePopup(List<string> items, int currentIndex, Action<int> onSelect)
        {
            allItems = items ?? new List<string>();
            filtered = new List<string>(allItems);
            this.currentIndex = Mathf.Clamp(currentIndex, -1, allItems.Count - 1);
            this.onSelect = onSelect;
        }

        public static void Show(Rect anchorRect, List<string> items, int currentIndex, Action<int> onSelect)
        {
            PopupWindow.Show(anchorRect, new SearchablePopup(items, currentIndex, onSelect));
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(360f, 320f);
        }

        public override void OnOpen()
        {
            EditorApplication.delayCall += () => EditorGUI.FocusTextInControl("SearchField");
        }

        public override void OnGUI(Rect rect)
        {
            InitStyles();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUI.SetNextControlName("SearchField");
            search = EditorGUILayout.TextField(search, searchFieldStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(string.Empty, cancelStyle, GUILayout.Width(18f)))
            {
                search = string.Empty;
                GUI.FocusControl("SearchField");
            }

            EditorGUILayout.EndHorizontal();

            FilterList(search);
            HandleKeyboard();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < filtered.Count; i++)
            {
                bool selected = i == currentIndex;
                var style = selected ? selectedRowStyle : rowStyle;
                Rect rowRect = GUILayoutUtility.GetRect(
                    new GUIContent(filtered[i]),
                    style,
                    GUILayout.Height(RowHeight),
                    GUILayout.ExpandWidth(true));

                if (Event.current.type == EventType.Repaint)
                {
                    style.Draw(rowRect, filtered[i], false, false, selected, false);
                }

                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                {
                    Select(filtered[i]);
                    Event.current.Use();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void HandleKeyboard()
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            if (currentEvent.keyCode == KeyCode.DownArrow)
            {
                currentIndex = Mathf.Min(currentIndex + 1, filtered.Count - 1);
                currentEvent.Use();
                editorWindow.Repaint();
                return;
            }

            if (currentEvent.keyCode == KeyCode.UpArrow)
            {
                currentIndex = Mathf.Max(currentIndex - 1, 0);
                currentEvent.Use();
                editorWindow.Repaint();
                return;
            }

            if ((currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter) &&
                filtered.Count > 0 &&
                currentIndex >= 0 &&
                currentIndex < filtered.Count)
            {
                Select(filtered[currentIndex]);
                currentEvent.Use();
            }
        }

        private void FilterList(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                filtered = new List<string>(allItems);
            }
            else
            {
                string lowerKeyword = keyword.ToLowerInvariant();
                filtered = allItems
                    .Where(item => item != null && item.ToLowerInvariant().Contains(lowerKeyword))
                    .ToList();
            }

            currentIndex = filtered.Count == 0 ? -1 : Mathf.Clamp(currentIndex, 0, filtered.Count - 1);
        }

        private void Select(string item)
        {
            int originalIndex = allItems.IndexOf(item);
            if (originalIndex >= 0)
            {
                onSelect?.Invoke(originalIndex);
            }

            editorWindow.Close();
            GUIUtility.ExitGUI();
        }

        private void InitStyles()
        {
            if (searchFieldStyle == null)
            {
                searchFieldStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarSearchField;
            }

            if (cancelStyle == null)
            {
                cancelStyle = GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? EditorStyles.toolbarButton;
            }

            if (rowStyle == null)
            {
                rowStyle = new GUIStyle("PR Label")
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(6, 6, 2, 2),
                };
            }

            if (selectedRowStyle == null)
            {
                selectedRowStyle = new GUIStyle(rowStyle);
                selectedRowStyle.normal.background = Texture2D.grayTexture;
            }
        }
    }
}
