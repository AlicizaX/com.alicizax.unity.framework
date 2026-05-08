using System;
using System.IO;
using System.Collections.Generic;
using AlicizaX.Editor;
using UnityEditor.IMGUI.Controls;
using UnityEditor;
using UnityEngine;


namespace AlicizaX.Localization.Editor
{
    public class LocalizationTableWindow : EditorWindow
    {
        private const float TabToolbarHeight = 24f;
        private const float TableToolbarHeight = 20f;

        private enum WindowTab
        {
            Table,
            Settings
        }

        private enum SearchTarget
        {
            Key,
            Value
        }

        private static readonly string[] WindowTabOptions = { "Table", "Settings" };
        private static readonly string[] SearchTargetOptions = { "Key", "Value" };
        private List<GameLocaizationTable> allTables = new List<GameLocaizationTable>(); // 存储所有找到的GameLocaizationTable
        private string[] tableDisplayNames; // 用于下拉框显示的名称数组
        private int selectedTableIndex = 0; // 当前选中的索引
        private string selectString = string.Empty;
        private GameLocaizationTable currentTable; // 当前选中的GameLocaizationTable


        // 原本是 const，现在改成可调的字段
        [SerializeField] private float languagesWidth = 200f;
        [SerializeField] private float tableSheetWidth = 300f;

        private const float SplitterWidth = 3f; // 拖拽条的宽度
        private bool isResizingLeft, isResizingMiddle;


        private float Spacing => EditorGUIUtility.standardVerticalSpacing * 2;

        private GUIStyle miniLabelButton => new GUIStyle(EditorStyles.miniButton)
        {
            font = EditorStyles.miniBoldLabel.font,
            fontSize = EditorStyles.miniBoldLabel.fontSize
        };

        public class WindowSelection
        {
            public TreeViewItem TreeViewItem;
        }

        public sealed class LanguageSelect : WindowSelection
        {
            public TempLanguageData Language;
        }

        public sealed class SectionSelect : WindowSelection
        {
            public SheetSectionTreeView Section;
        }

        public sealed class ItemSelect : WindowSelection
        {
            public SheetItemTreeView Item;
        }


        private LocalizationWindowData windowData;

        private SearchField searchField;
        private string searchString;
        [SerializeField] private SearchTarget searchTarget = SearchTarget.Key;
        private TempLanguageData cachedSearchLanguage;
        private string cachedSearchString;
        private SearchTarget cachedSearchTarget;
        private List<TempSheetSection> cachedSearchResult = new();
        private Vector2 scrollPosition;

        [SerializeField] private TreeViewState languagesTreeViewState;
        private LanguagesTreeView languagesTreeView;

        [SerializeField] private TreeViewState tableSheetTreeViewState;
        private TableSheetTreeView tableSheetTreeView;

        private WindowSelection selection = null;
        private bool globalExpanded = false;
        [SerializeField] private WindowTab selectedTab = WindowTab.Table;
        private LocalizationSettingsView settingsView;

        public static LocalizationTableWindow OpenTableEditor()
        {
            return Open(WindowTab.Table);
        }

        public static LocalizationTableWindow OpenSettingsEditor()
        {
            return Open(WindowTab.Settings);
        }

        private static LocalizationTableWindow Open(WindowTab tab)
        {
            LocalizationTableWindow window = GetWindow<LocalizationTableWindow>(false, "Localization Editor", true);
            window.minSize = new Vector2(1000f, 500f);
            window.selectedTab = tab;
            window.EnsureSettingsView();
            window.Show();
            window.Focus();
            return window;
        }

        private void CreateGUI()
        {
            EnsureSearchField();
            EnsureSettingsView();
        }

        private void OnDestroy()
        {
            SaveSelection();
            DisposeSettingsView();
        }

        private void OnEnable()
        {
            EnsureSearchField();
            EnsureSettingsView();
            settingsView.OnEnable();
            RefreshTableList();
        }

        private void OnFocus()
        {
            EnsureSettingsView();
            settingsView.OnFocus();
        }

        private void EnsureSearchField()
        {
            if (searchField == null)
            {
                searchField = new SearchField();
            }
        }

        private void EnsureSettingsView()
        {
            settingsView ??= new LocalizationSettingsView();
        }

        private void DisposeSettingsView()
        {
            settingsView?.Dispose();
            settingsView = null;
        }

        private void OnDisable()
        {
            SaveSelection();
            DisposeSettingsView();
        }

        private void RefreshTableList()
        {
            allTables.Clear();
            string[] guids = AssetDatabase.FindAssets("t:GameLocaizationTable");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                GameLocaizationTable table = AssetDatabase.LoadAssetAtPath<GameLocaizationTable>(assetPath);
                if (table != null)
                {
                    allTables.Add(table);
                }
            }

            tableDisplayNames = new string[allTables.Count];
            for (int i = 0; i < allTables.Count; i++)
            {
                tableDisplayNames[i] = allTables[i].name;
            }

            if (allTables.Count > 0 && selectedTableIndex >= allTables.Count)
            {
                selectedTableIndex = 0;
            }


            var selectIndex = selectedTableIndex;
            var lastSelect = EditorPrefs.GetString("LastSelectedGameLocaizationTable", string.Empty);
            if (!string.IsNullOrEmpty(lastSelect))
            {
                for (int i = 0; i < allTables.Count; i++)
                {
                    var path = AssetDatabase.GetAssetPath(allTables[i]);
                    if (path.Equals(lastSelect))
                    {
                        selectIndex = i;
                    }
                }
            }

            if (selectIndex <= tableDisplayNames.Length - 1 && selectIndex >= 0)
            {
                selectString = tableDisplayNames[selectIndex];
            }

            UpdateCurrentTable();
        }

        private void UpdateCurrentTable()
        {
            if (allTables.Count > 0 && selectedTableIndex >= 0 && selectedTableIndex < allTables.Count)
            {
                currentTable = allTables[selectedTableIndex];
                SaveSelection();
            }
            else
            {
                currentTable = null;
            }

            InitializeTreeView();
        }

        private void SaveSelection()
        {
            if (currentTable != null)
            {
                string path = AssetDatabase.GetAssetPath(currentTable);
                EditorPrefs.SetString("LastSelectedGameLocaizationTable", path);
            }
        }

        private void InitializeTreeView()
        {
            if (!currentTable) return;

            LocalizationWindowUtility.BuildWindowData(currentTable, out windowData);
            InvalidateSearchCache();


            foreach (var section in windowData.TableSheet)
            {
                section.IsExpanded = globalExpanded;
            }


            languagesTreeViewState = new TreeViewState();
            languagesTreeView = new(languagesTreeViewState, windowData, currentTable)
            {
                OnLanguageSelect = (s) => selection = s
            };


            tableSheetTreeViewState = new TreeViewState();
            tableSheetTreeView = new(tableSheetTreeViewState, windowData)
            {
                OnTableSheetSelect = (s) => selection = s
            };
        }

        private void OnGUI()
        {
            EnsureSearchField();
            EnsureSettingsView();

            Rect tabToolbarRect = new(0, 0, position.width, TabToolbarHeight);
            GUI.Box(tabToolbarRect, GUIContent.none, EditorStyles.toolbar);
            Rect tabRect = new(tabToolbarRect.xMin + 3f, tabToolbarRect.yMin + -2f, 200f, 20f);
            WindowTab nextTab = (WindowTab)GUI.Toolbar(tabRect, (int)selectedTab, WindowTabOptions, EditorStyles.toolbarButton);
            if (nextTab != selectedTab)
            {
                selectedTab = nextTab;
                GUI.FocusControl(null);
                if (selectedTab == WindowTab.Settings)
                {
                    settingsView.OnFocus();
                }
            }

            Rect contentRect = new(0, TabToolbarHeight, position.width, Mathf.Max(0f, position.height - TabToolbarHeight));
            if (selectedTab == WindowTab.Settings)
            {
                settingsView.OnGUI(contentRect);
                return;
            }

            DrawTableEditor(contentRect);
        }

        private void DrawTableEditor(Rect contentRect)
        {
            // 顶部工具栏
            Rect toolbarRect = new(contentRect.x, contentRect.y, contentRect.width, TableToolbarHeight);
            GUI.Box(toolbarRect, GUIContent.none, EditorStyles.toolbar);

            float buttonWidth = 100f;
            float spacing = 5f;
            float searchTypeWidth = 65f;

            Rect leftTitle = new(toolbarRect.xMin, toolbarRect.yMin, 40, TableToolbarHeight);
            Rect leftPop = new(leftTitle.xMin + 40, toolbarRect.yMin, 200, TableToolbarHeight);
            Rect saveBtn = new(toolbarRect.xMax - buttonWidth - spacing, toolbarRect.yMin, buttonWidth, TableToolbarHeight);
            Rect genBtn = new(saveBtn.xMin - buttonWidth - spacing, toolbarRect.yMin, buttonWidth, TableToolbarHeight);
            Rect importBtn = new(genBtn.xMin - buttonWidth - spacing, toolbarRect.yMin, buttonWidth, TableToolbarHeight);
            Rect exportBtn = new(importBtn.xMin - buttonWidth - spacing, toolbarRect.yMin, buttonWidth, TableToolbarHeight);
            Rect searchTypePopup = new(leftPop.xMax + spacing, toolbarRect.yMin, searchTypeWidth, TableToolbarHeight);
            Rect searchRect = new(searchTypePopup.xMax + spacing, toolbarRect.yMin, Mathf.Max(80f, exportBtn.xMin - spacing - (searchTypePopup.xMax + spacing)), TableToolbarHeight);

            EditorGUI.LabelField(leftTitle, "Table", EditorStyles.boldLabel);
            EditorDrawing.DrawStringSelectPopup(leftPop, tableDisplayNames, selectString, (e) =>
            {
                selectString = e;
                selectedTableIndex = allTables.FindIndex(table => table.name == e);
                UpdateCurrentTable();
            });

            using (new EditorGUI.DisabledGroupScope(currentTable == null || !(selection is LanguageSelect)))
            {
                SearchTarget nextSearchTarget = (SearchTarget)EditorGUI.Popup(searchTypePopup, (int)searchTarget, SearchTargetOptions, EditorStyles.toolbarPopup);
                string nextSearchString = searchField.OnToolbarGUI(searchRect, searchString);
                if (nextSearchTarget != searchTarget || nextSearchString != searchString)
                {
                    searchTarget = nextSearchTarget;
                    searchString = nextSearchString;
                    InvalidateSearchCache();
                }
            }

            if (currentTable == null) return;

            if (GUI.Button(exportBtn, "Export CSV", EditorStyles.toolbarButton))
            {
                string path = EditorUtility.SaveFilePanel("Export CSV", "", "Localization", "csv");
                LocalizationExporter.ExportLocalizationToCSV(windowData, path);
            }

            if (GUI.Button(importBtn, "Import CSV", EditorStyles.toolbarButton))
            {
                string path = EditorUtility.OpenFilePanel("Import CSV", "", "csv");
                LocalizationExporter.ImportLocalizationFromCSV(windowData, path);
                InvalidateSearchCache();
            }

            if (GUI.Button(genBtn, "Gen Code", EditorStyles.toolbarButton))
            {
                if (!ValidateWindowData())
                {
                    return;
                }

                BuildLocalizationTable();
                EditorUtility.SetDirty(currentTable);
                AssetDatabase.SaveAssets();
                LocalizationWindowUtility.GenerateCode(currentTable);
            }

            if (GUI.Button(saveBtn, "Save Asset", EditorStyles.toolbarButton))
            {
                if (!ValidateWindowData())
                {
                    return;
                }

                BuildLocalizationTable();
                EditorUtility.SetDirty(currentTable);
                AssetDatabase.SaveAssets();
            }

            // ---------------- 这里开始是 SplitView ----------------
            Rect fullRect = new Rect(contentRect.x, contentRect.y + TableToolbarHeight, contentRect.width, contentRect.height - TableToolbarHeight);

            // 左侧 Languages
            Rect languagesRect = new Rect(fullRect.x, fullRect.y, languagesWidth, fullRect.height);
            languagesTreeView?.OnGUI(languagesRect);

            // 左侧拖拽条
            Rect leftSplitter = new Rect(languagesRect.xMax, fullRect.y, SplitterWidth, fullRect.height);
            EditorGUIUtility.AddCursorRect(leftSplitter, MouseCursor.ResizeHorizontal);
            HandleResize(ref languagesWidth, leftSplitter, ref isResizingLeft);

            // 中间 TableSheet
            Rect tableSheetRect = new Rect(leftSplitter.xMax, fullRect.y, tableSheetWidth, fullRect.height);
            tableSheetTreeView?.OnGUI(tableSheetRect);

            // 中间拖拽条
            Rect midSplitter = new Rect(tableSheetRect.xMax, fullRect.y, SplitterWidth, fullRect.height);
            EditorGUIUtility.AddCursorRect(midSplitter, MouseCursor.ResizeHorizontal);
            HandleResize(ref tableSheetWidth, midSplitter, ref isResizingMiddle);

            // 右侧 Inspector（剩余空间）
            float inspectorWidth = fullRect.width - (languagesWidth + tableSheetWidth + SplitterWidth * 2);
            Rect inspectorRect = new Rect(midSplitter.xMax, fullRect.y, inspectorWidth, fullRect.height);

            if (selection != null)
            {
                GUILayout.BeginArea(inspectorRect);
                {
                    if (selection is LanguageSelect lang) OnDrawLanguageInspector(lang);
                    else if (selection is SectionSelect sec) OnDrawSectionInspector(sec);
                    else if (selection is ItemSelect item) OnDrawSectionItemInspector(item);
                }
                GUILayout.EndArea();
            }
        }

        private void HandleResize(ref float targetWidth, Rect splitterRect, ref bool isResizing)
        {
            Event e = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(e.mousePosition))
                    {
                        isResizing = true;
                        e.Use();
                    }

                    break;

                case EventType.MouseDrag:
                    if (isResizing)
                    {
                        targetWidth += e.delta.x;
                        targetWidth = Mathf.Max(100f, targetWidth); // 设置最小宽度
                        e.Use();
                        Repaint();
                    }

                    break;

                case EventType.MouseUp:
                    if (isResizing)
                    {
                        isResizing = false;
                        e.Use();
                    }

                    break;
            }
        }

        private void OnDrawSectionInspector(SectionSelect section)
        {
            // section name change
            EditorGUI.BeginChangeCheck();
            {
                string nextName = EditorGUILayout.DelayedTextField("Name", section.Section.Name);
                if (nextName != section.Section.Name)
                {
                    if (LocalizationWindowUtility.TryValidateSectionName(windowData, section.Section, nextName, out string error))
                    {
                        section.Section.Name = nextName;
                        InvalidateSearchCache();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Invalid Section", error, "OK");
                    }
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                section.TreeViewItem.displayName = section.Section.Name;
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                int childerCount = section.TreeViewItem.children?.Count ?? 0;
                EditorGUILayout.IntField(new GUIContent("Keys"), childerCount);
            }

            EditorGUILayout.Space(2);
            EditorDrawing.Separator();
            EditorGUILayout.Space(1);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.LabelField("Id: " + section.Section.Id, EditorStyles.miniBoldLabel);
            }
        }

        private void OnDrawSectionItemInspector(ItemSelect itemSelect)
        {
            var item = itemSelect.Item;

            EditorGUILayout.LabelField("Editing Key Across Languages", EditorStyles.boldLabel);

            // 显示 Key 和 ID（只读）
            string nextKey = EditorGUILayout.DelayedTextField("Key", item.Key);
            if (nextKey != item.Key)
            {
                if (LocalizationWindowUtility.TryValidateItemKey(item.Parent, item, nextKey, out string error))
                {
                    item.Key = nextKey;
                    itemSelect.TreeViewItem.displayName = nextKey;
                    InvalidateSearchCache();
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Key", error, "OK");
                }
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.LabelField("Id: " + item.Id);
            }

            EditorGUILayout.Space(4);
            EditorDrawing.Separator();
            EditorGUILayout.Space(4);

            // 遍历所有语言
            foreach (var lang in windowData.Languages)
            {
                if (lang.Entry.Asset == null)
                    continue;

                var languageName = lang.Entry.LanguageName;
                TempSheetItem targetItem = null;

                // 在语言的 TableSheet 里找到对应的 Item
                foreach (var section in lang.TableSheet)
                {
                    targetItem = section.Items.Find(i => i.Id == item.Id);
                    if (targetItem != null)
                        break;
                }

                if (targetItem == null)
                {
                    EditorGUILayout.HelpBox($"Language [{languageName}] does not contain this key.", MessageType.Info);
                    continue;
                }

                // 绘制语言名标题
                EditorGUILayout.LabelField(languageName, EditorStyles.miniBoldLabel);

                // 多行文本框
                EditorGUI.BeginChangeCheck();
                targetItem.Value = EditorGUILayout.TextArea(targetItem.Value, GUILayout.MinHeight(40));
                if (EditorGUI.EndChangeCheck())
                {
                    // 标记已修改
                    EditorUtility.SetDirty(lang.Entry.Asset);
                    InvalidateSearchCache();
                }

                EditorGUILayout.Space(6);
            }
        }


        private void OnDrawLanguageInspector(LanguageSelect selection)
        {
            var language = selection.Language;
            var entry = language.Entry;

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.TextField("Language", entry.LanguageName);
                EditorGUILayout.ObjectField("Asset", entry.Asset, typeof(LocalizationLanguage), false);
            }

            using (new EditorGUI.DisabledGroupScope(entry.Asset == null))
            {
                EditorGUILayout.Space();

                GUIContent expandText = new GUIContent("Expand");
                float expandWidth = miniLabelButton.CalcSize(expandText).x;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    using (new EditorDrawing.BackgroundColorScope("#F7E987"))
                    {
                        if (GUILayout.Button(expandText, miniLabelButton, GUILayout.Width(expandWidth)))
                        {
                            globalExpanded = !globalExpanded;
                            foreach (var section in language.TableSheet)
                            {
                                section.Reference.IsExpanded = globalExpanded;
                            }
                        }
                    }
                }

                if (entry.Asset != null)
                {
                    bool forceExpanded = !string.IsNullOrWhiteSpace(searchString);

                    // Draw localization data
                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                    {
                        IList<TempSheetSection> sections = GetSearchResult(language, searchString, searchTarget);
                        for (int i = 0; i < sections.Count; i++)
                        {
                            DrawLocalizationKey(sections[i], forceExpanded);
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    EditorGUILayout.HelpBox("To begin editing localization data, you must first assign a localization asset.", MessageType.Warning);
                }
            }
        }

        private void DrawLocalizationKey(TempSheetSection section, bool forceExpanded = false)
        {
            if (section.Items == null || section.Items.Count == 0)
                return;

            using (new EditorDrawing.BorderBoxScope(false))
            {
                string sectionName = section.Name.Replace(" ", "");
                bool isExpanded = forceExpanded || section.Reference.IsExpanded;
                bool nextExpanded = EditorGUILayout.Foldout(isExpanded, new GUIContent(sectionName), true, EditorDrawing.Styles.miniBoldLabelFoldout);

                if (!forceExpanded)
                {
                    section.Reference.IsExpanded = nextExpanded;
                }

                // Show section keys when expanded
                if (forceExpanded || nextExpanded)
                {
                    foreach (var item in section.Items)
                    {
                        string key = GetItemDisplayKey(section, item);

                        if (IsMultiline(item.Value))
                            key += " (Multiline)";

                        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                        {
                            // Display the expandable toggle
                            using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
                            {
                                item.IsExpanded = EditorGUILayout.Foldout(item.IsExpanded, new GUIContent(key), true, EditorDrawing.Styles.miniBoldLabelFoldout);
                            }

                            if (item.IsExpanded)
                            {
                                // Show TextArea when expanded
                                float height = (EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight) * 3;
                                height += EditorGUIUtility.standardVerticalSpacing;

                                item.Scroll = EditorGUILayout.BeginScrollView(item.Scroll, GUILayout.Height(height));
                                EditorGUI.BeginChangeCheck();
                                item.Value = EditorGUILayout.TextArea(item.Value, GUILayout.ExpandHeight(true));
                                if (EditorGUI.EndChangeCheck())
                                {
                                    InvalidateSearchCache();
                                }

                                EditorGUILayout.EndScrollView();
                            }
                            else
                            {
                                // Show TextField when collapsed
                                EditorGUI.BeginChangeCheck();
                                item.Value = EditorGUILayout.TextField(item.Value);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    InvalidateSearchCache();
                                }
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space(1f);
        }

        private IList<TempSheetSection> GetSearchResult(TempLanguageData languageData, string search, SearchTarget target)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return languageData.TableSheet;
            }

            if (ReferenceEquals(cachedSearchLanguage, languageData) &&
                string.Equals(cachedSearchString, search, StringComparison.Ordinal) &&
                cachedSearchTarget == target)
            {
                return cachedSearchResult;
            }

            cachedSearchLanguage = languageData;
            cachedSearchString = search;
            cachedSearchTarget = target;
            cachedSearchResult.Clear();

            for (int i = 0; i < languageData.TableSheet.Count; i++)
            {
                TempSheetSection section = languageData.TableSheet[i];
                List<TempSheetItem> sectionItems = null;

                for (int j = 0; j < section.Items.Count; j++)
                {
                    TempSheetItem item = section.Items[j];
                    string source = target == SearchTarget.Value ? item.Value : GetItemDisplayKey(section, item);
                    if (!ContainsSearch(source, search))
                    {
                        continue;
                    }

                    sectionItems ??= new List<TempSheetItem>();
                    sectionItems.Add(item);
                }

                if (sectionItems != null)
                {
                    cachedSearchResult.Add(new TempSheetSection()
                    {
                        Items = sectionItems,
                        Reference = section.Reference
                    });
                }
            }

            return cachedSearchResult;
        }

        private void InvalidateSearchCache()
        {
            cachedSearchLanguage = null;
            cachedSearchString = null;
            cachedSearchResult.Clear();
        }

        private bool IsMultiline(string text)
        {
            return !string.IsNullOrEmpty(text) && (text.Contains("\n") || text.Contains("\r"));
        }

        private string GetItemDisplayKey(TempSheetSection section, TempSheetItem item)
        {
            string sectionName = section.Name.Replace(" ", "");
            string keyName = item.Key.Replace(" ", "");
            return sectionName + "." + keyName;
        }

        private bool ContainsSearch(string source, string search)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ValidateLanguageName(string languageName, TempLanguageData self, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(languageName))
            {
                error = "Language name cannot be empty.";
                return false;
            }

            if (!LocalizationConfiguration.Instance.IsLanguageSelected(languageName))
            {
                error = "Language is not enabled in Localization Settings.";
                return false;
            }

            for (int i = 0; i < windowData.Languages.Count; i++)
            {
                TempLanguageData languageData = windowData.Languages[i];
                if (!ReferenceEquals(languageData, self) && string.Equals(languageData.Entry.LanguageName, languageName, StringComparison.Ordinal))
                {
                    error = "Language name is duplicated.";
                    return false;
                }
            }

            return true;
        }

        private bool ValidateWindowData()
        {
            for (int i = 0; i < windowData.TableSheet.Count; i++)
            {
                SheetSectionData section = windowData.TableSheet[i];
                if (!LocalizationWindowUtility.TryValidateSectionName(windowData, section, section.Name, out string sectionError))
                {
                    EditorUtility.DisplayDialog("Invalid Section", sectionError, "OK");
                    return false;
                }

                for (int j = 0; j < section.Items.Count; j++)
                {
                    SheetItemTreeView item = section.Items[j];
                    if (!LocalizationWindowUtility.TryValidateItemKey(section, item, item.Key, out string itemError))
                    {
                        EditorUtility.DisplayDialog("Invalid Key", itemError, "OK");
                        return false;
                    }
                }
            }

            for (int i = 0; i < windowData.Languages.Count; i++)
            {
                TempLanguageData language = windowData.Languages[i];
                if (!ValidateLanguageName(language.Entry.LanguageName, language, out string languageError))
                {
                    EditorUtility.DisplayDialog("Invalid Language", languageError, "OK");
                    return false;
                }

            }

            if (!ValidateFormatStrings(out string formatError))
            {
                EditorUtility.DisplayDialog("Invalid Localization Format", formatError, "OK");
                return false;
            }

            return true;
        }

        private bool ValidateFormatStrings(out string error)
        {
            error = null;
            List<int> basePlaceholders = new();
            List<int> languagePlaceholders = new();
            TempLanguageData commentLanguage = null;
            string commentLanguageName = LocalizationConfiguration.Instance.GenerateScriptCodeFirstConfigName;

            for (int i = 0; i < windowData.Languages.Count; i++)
            {
                TempLanguageData language = windowData.Languages[i];
                if (string.Equals(language.Entry.LanguageName, commentLanguageName, StringComparison.Ordinal))
                {
                    commentLanguage = language;
                    break;
                }
            }

            if (commentLanguage == null)
            {
                error = $"Comment language [{commentLanguageName}] is not available.";
                return false;
            }

            for (int sectionIndex = 0; sectionIndex < commentLanguage.TableSheet.Count; sectionIndex++)
            {
                TempSheetSection commentSection = commentLanguage.TableSheet[sectionIndex];
                for (int itemIndex = 0; itemIndex < commentSection.Items.Count; itemIndex++)
                {
                    TempSheetItem commentItem = commentSection.Items[itemIndex];
                    string itemKey = GetItemDisplayKey(commentSection, commentItem);
                    if (!LocalizationWindowUtility.TryGetFormatPlaceholderSequence(commentItem.Value, basePlaceholders, out string commentError))
                    {
                        error = $"Language [{commentLanguage.Entry.LanguageName}] key [{itemKey}] has invalid format placeholders: {commentError}";
                        return false;
                    }

                    if (!LocalizationWindowUtility.IsContinuousFormatPlaceholderSequence(basePlaceholders))
                    {
                        error = $"Language [{commentLanguage.Entry.LanguageName}] key [{itemKey}] format placeholders must be ordered as {{0}}, {{1}}, ...";
                        return false;
                    }

                    int argumentCount = LocalizationWindowUtility.GetFormatPlaceholderArgumentCount(basePlaceholders);
                    if (argumentCount > 16)
                    {
                        error = $"Language [{commentLanguage.Entry.LanguageName}] key [{itemKey}] uses {argumentCount} arguments, but generated localization keys support at most 16.";
                        return false;
                    }

                    for (int languageIndex = 0; languageIndex < windowData.Languages.Count; languageIndex++)
                    {
                        TempLanguageData language = windowData.Languages[languageIndex];
                        TempSheetItem targetItem = FindTempItem(language, commentSection.Reference.Id, commentItem.Id);
                        if (targetItem == null)
                        {
                            continue;
                        }

                        if (!LocalizationWindowUtility.TryGetFormatPlaceholderSequence(targetItem.Value, languagePlaceholders, out string languageError))
                        {
                            error = $"Language [{language.Entry.LanguageName}] key [{itemKey}] has invalid format placeholders: {languageError}";
                            return false;
                        }

                        if (!LocalizationWindowUtility.FormatPlaceholderSequenceEquals(basePlaceholders, languagePlaceholders))
                        {
                            error = $"Language [{language.Entry.LanguageName}] key [{itemKey}] format placeholders must match comment language sequence.";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private TempSheetItem FindTempItem(TempLanguageData language, int sectionId, int itemId)
        {
            for (int i = 0; i < language.TableSheet.Count; i++)
            {
                TempSheetSection section = language.TableSheet[i];
                if (section.Reference.Id != sectionId)
                {
                    continue;
                }

                for (int j = 0; j < section.Items.Count; j++)
                {
                    TempSheetItem item = section.Items[j];
                    if (item.Id == itemId)
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        private void BuildLocalizationTable()
        {
            // 1. build table sheet
            currentTable.TableSheet = new();
            foreach (var section in windowData.TableSheet)
            {
                GameLocaizationTable.TableData tableData = new GameLocaizationTable.TableData(section.Name, section.Id);

                foreach (var item in section.Items)
                {
                    GameLocaizationTable.SheetItem sheetItem = new GameLocaizationTable.SheetItem(item.Key, item.Id, item.isGen);
                    tableData.SectionSheet.Add(sheetItem);
                }

                currentTable.TableSheet.Add(tableData);
            }

            // 2. build table sheet for each language
            IList<LocalizationLanguage> languages = new List<LocalizationLanguage>();
            foreach (var language in windowData.Languages)
            {
                if (language.Entry.Asset == null)
                    continue;

                LocalizationLanguage asset = language.Entry.Asset;
                IList<LocalizationLanguage.LocalizationString> strings = new List<LocalizationLanguage.LocalizationString>();

                foreach (var section in language.TableSheet)
                {
                    string sectionKey = section.Name.Replace(" ", "");
                    foreach (var item in section.Items)
                    {
                        string itemKey = item.Key.Replace(" ", "");
                        string key = sectionKey + "." + itemKey;

                        strings.Add(new()
                        {
                            SectionId = section.Id,
                            EntryId = item.Id,
                            Key = key,
                            Value = item.Value
                        });
                    }
                }

                asset.LanguageName = language.Entry.LanguageName;
                asset.name = language.Entry.LanguageName;
                asset.Strings = new(strings);

                languages.Add(asset);
                EditorUtility.SetDirty(asset);
            }

            currentTable.Languages = new(languages);
            currentTable.InvalidateLanguageLookup();
        }
    }
}
