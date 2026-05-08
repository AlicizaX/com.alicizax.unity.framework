using System.Collections.Generic;
using AlicizaX.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX
{
    public sealed class PoolConfigEditorWindow : EditorWindow
    {
        private const float MinLeftWidth = 240f;
        private const float InitialLeftWidth = 300f;
        private const int ListItemHeight = 24;
        private const string WindowTitle = "对象池配置编辑器";

        private static readonly Color LeftPanelColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color RightPanelColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color DescriptionColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly List<string> LoaderTypeOptions = new List<string> { "AssetBundle", "Resources" };

        [SerializeField]
        private PoolConfigScriptableObject _asset;
        [SerializeField]
        private string _assetGuid;
        private SerializedObject _serializedObject;
        private SerializedProperty _entriesProperty;
        private readonly List<int> _entryIndices = new List<int>();

        [SerializeField]
        private int _selectedIndex;
        [SerializeField]
        private bool _hasUnsavedChanges;
        [SerializeField]
        private Vector2 _entryListScrollPosition;

        private ToolbarButton _saveButton;
        private Label _titleLabel;
        private VisualElement _leftPane;
        private IMGUIContainer _entryListContainer;
        private ReorderableList _entryList;
        private ScrollView _detailScrollView;
        private Label _detailTitleLabel;
        private VisualElement _detailFieldsContainer;
        private VisualElement _emptyContainer;

        private static void OpenForAsset(PoolConfigScriptableObject asset)
        {
            if (asset == null)
            {
                return;
            }

            PoolConfigEditorWindow window = GetWindow<PoolConfigEditorWindow>(false, WindowTitle, true);
            window.minSize = new Vector2(920f, 560f);
            window.SetAsset(asset);
            window.Show();
        }

        [OnOpenAsset(0)]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            Object obj = EditorUtility.InstanceIDToObject(instanceId);
            if (obj is not PoolConfigScriptableObject asset)
            {
                return false;
            }

            OpenForAsset(asset);
            return true;
        }

        private void CreateGUI()
        {
            titleContent = new GUIContent(WindowTitle, EditorGUIUtility.IconContent("ScriptableObject Icon").image);
            BuildUi();
            RestoreWindowState();
            RefreshUi();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle, EditorGUIUtility.IconContent("ScriptableObject Icon").image);
            if (rootVisualElement.childCount == 0)
            {
                BuildUi();
            }

            RestoreWindowState();
            RefreshUi();
        }

        private void BuildUi()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            Toolbar toolbar = new Toolbar();
            toolbar.style.flexShrink = 0f;

            _saveButton = new ToolbarButton(SaveAsset)
            {
                tooltip = "保存当前 PoolConfig 配置"
            };
            _saveButton.Add(new Image
            {
                image = EditorGUIUtility.IconContent("SaveActive").image,
                scaleMode = ScaleMode.ScaleToFit
            });

            _titleLabel = new Label();
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.flexGrow = 1f;
            _titleLabel.style.marginLeft = 6f;
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            toolbar.Add(_saveButton);
            toolbar.Add(_titleLabel);
            rootVisualElement.Add(toolbar);

            TwoPaneSplitView splitView = new TwoPaneSplitView(0, InitialLeftWidth, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;
            rootVisualElement.Add(splitView);

            _leftPane = new VisualElement();
            _leftPane.style.flexGrow = 1f;
            _leftPane.style.minWidth = MinLeftWidth;
            _leftPane.style.backgroundColor = LeftPanelColor;
            _leftPane.style.paddingLeft = 4f;
            _leftPane.style.paddingRight = 4f;
            _leftPane.style.paddingTop = 4f;
            _leftPane.style.paddingBottom = 4f;

            VisualElement rightPane = new VisualElement();
            rightPane.style.flexGrow = 1f;
            rightPane.style.backgroundColor = RightPanelColor;
            rightPane.style.paddingLeft = 10f;
            rightPane.style.paddingRight = 10f;
            rightPane.style.paddingTop = 8f;
            rightPane.style.paddingBottom = 8f;

            splitView.Add(_leftPane);
            splitView.Add(rightPane);

            BuildLeftPane();
            BuildRightPane(rightPane);
        }

        private void BuildLeftPane()
        {
            _entryListContainer = new IMGUIContainer(DrawEntryList);
            _entryListContainer.style.flexGrow = 1f;
            _entryListContainer.style.marginBottom = 4f;
            _leftPane.Add(_entryListContainer);
        }

        private void BuildRightPane(VisualElement rightPane)
        {
            _emptyContainer = new VisualElement
            {
                style =
                {
                    flexGrow = 1f,
                    justifyContent = Justify.Center
                }
            };
            _emptyContainer.Add(new HelpBox("当前没有选中任何规则。", HelpBoxMessageType.Info));

            _detailScrollView = new ScrollView
            {
                style =
                {
                    flexGrow = 1f
                }
            };

            _detailTitleLabel = new Label();
            _detailTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _detailTitleLabel.style.marginBottom = 6f;
            _detailTitleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            _detailFieldsContainer = new VisualElement();
            _detailFieldsContainer.style.flexDirection = FlexDirection.Column;
            _detailFieldsContainer.style.flexGrow = 1f;

            _detailScrollView.Add(_detailTitleLabel);
            _detailScrollView.Add(_detailFieldsContainer);

            rightPane.Add(_emptyContainer);
            rightPane.Add(_detailScrollView);
        }

        private void SetAsset(PoolConfigScriptableObject asset)
        {
            _asset = asset;
            _assetGuid = GetAssetGuid(asset);
            _selectedIndex = 0;
            _hasUnsavedChanges = false;
            _entryListScrollPosition = Vector2.zero;
            RebindAssetState();
            RefreshUi();
        }

        private void RestoreWindowState()
        {
            if (_asset == null && !string.IsNullOrEmpty(_assetGuid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(_assetGuid);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    _asset = AssetDatabase.LoadAssetAtPath<PoolConfigScriptableObject>(assetPath);
                }
            }
            else if (_asset != null)
            {
                _assetGuid = GetAssetGuid(_asset);
            }

            RebindAssetState();
        }

        private void RebindAssetState()
        {
            _entryList = null;

            if (_asset == null)
            {
                _assetGuid = string.Empty;
                _serializedObject = null;
                _entriesProperty = null;
                _entryIndices.Clear();
                return;
            }

            _asset.Normalize();
            _serializedObject = new SerializedObject(_asset);
            _entriesProperty = _serializedObject.FindProperty("entries");
            RebuildEntryIndices();
            ClampSelection();
        }

        private void RefreshUi()
        {
            if (_titleLabel == null)
            {
                return;
            }

            RefreshTitle();
            _saveButton?.SetEnabled(_asset != null);

            if (_asset == null || _serializedObject == null)
            {
                _entryIndices.Clear();
                _entryList = null;
                _entryListContainer?.MarkDirtyRepaint();
                _emptyContainer.Clear();
                _emptyContainer.Add(new HelpBox("请选择或双击 PoolConfig 资源，然后在这个窗口里编辑对象池规则。", HelpBoxMessageType.Info));
                _emptyContainer.style.display = DisplayStyle.Flex;
                _detailScrollView.style.display = DisplayStyle.None;
                return;
            }

            RebuildEntryIndices();
            ClampSelection();
            EnsureEntryReorderableList();
            SyncEntryListSelection();
            _entryListContainer?.MarkDirtyRepaint();

            if (_entryIndices.Count > 0)
            {
                _emptyContainer.style.display = DisplayStyle.None;
                _detailScrollView.style.display = DisplayStyle.Flex;
                RebuildDetailFields();
            }
            else
            {
                _emptyContainer.Clear();
                _emptyContainer.Add(new HelpBox("当前没有可编辑的规则，请先新增一条对象池规则。", HelpBoxMessageType.Info));
                _emptyContainer.style.display = DisplayStyle.Flex;
                _detailScrollView.style.display = DisplayStyle.None;
            }
        }

        private void RebuildEntryIndices()
        {
            _entryIndices.Clear();
            int count = _entriesProperty?.arraySize ?? 0;
            for (int i = 0; i < count; i++)
            {
                _entryIndices.Add(i);
            }
        }

        private void DrawEntryList()
        {
            if (_asset == null || _serializedObject == null || _entriesProperty == null)
            {
                return;
            }

            EnsureEntryReorderableList();
            SyncEntryListSelection();

            _serializedObject.Update();
            _entryListScrollPosition = EditorGUILayout.BeginScrollView(_entryListScrollPosition, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            Rect listRect = GUILayoutUtility.GetRect(0f, _entryList.GetHeight(), GUILayout.ExpandWidth(true));
            _entryList.DoList(listRect);
            EditorGUILayout.EndScrollView();
        }

        private void EnsureEntryReorderableList()
        {
            if (_serializedObject == null || _entriesProperty == null)
            {
                _entryList = null;
                return;
            }

            if (_entryList != null && _entryList.serializedProperty == _entriesProperty)
            {
                return;
            }

            _entryList = new ReorderableList(_serializedObject, _entriesProperty, true, true, true, true)
            {
                elementHeight = ListItemHeight + 6f
            };

            _entryList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, Utility.Text.Format("对象池规则 ({0})", _entriesProperty.arraySize), EditorStyles.boldLabel);
            };
            _entryList.drawElementCallback = DrawEntryListElement;
            _entryList.onSelectCallback = OnEntryListSelected;
            _entryList.onAddCallback = OnEntryListAdd;
            _entryList.onRemoveCallback = OnEntryListRemove;
            _entryList.onReorderCallback = OnEntryListReordered;
        }

        private void DrawEntryListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty entry = GetEntryAt(index);
            if (entry == null)
            {
                return;
            }

            rect.y += 5f;

            string primaryLabel = GetPrimaryLabel(entry);
            string assetPath = entry.FindPropertyRelative("assetPath").stringValue;
            string tooltip = string.IsNullOrWhiteSpace(assetPath) || string.Equals(primaryLabel, assetPath, System.StringComparison.Ordinal)
                ? primaryLabel
                : assetPath;

            Rect primaryRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(primaryRect, new GUIContent(primaryLabel, tooltip), EditorStyles.boldLabel);
        }

        private void OnEntryListSelected(ReorderableList list)
        {
            if (list.index < 0 || list.index >= _entryIndices.Count)
            {
                return;
            }

            _selectedIndex = list.index;
            RebuildDetailFields();
        }

        private void OnEntryListAdd(ReorderableList list)
        {
            AddEntry();
        }

        private void OnEntryListRemove(ReorderableList list)
        {
            RemoveEntry();
        }

        private void OnEntryListReordered(ReorderableList list)
        {
            if (_asset == null || _serializedObject == null || _entriesProperty == null)
            {
                return;
            }

            RefreshEntryPriorities();
            _serializedObject.ApplyModifiedPropertiesWithoutUndo();
            _asset.Normalize();
            RebuildEntryIndices();
            _selectedIndex = Mathf.Clamp(list.index, 0, _entryIndices.Count - 1);
            _hasUnsavedChanges = true;
            RefreshTitle();
            RebuildDetailFields();
            _entryListContainer?.MarkDirtyRepaint();
        }

        private void SyncEntryListSelection()
        {
            if (_entryList == null)
            {
                return;
            }

            _entryList.index = _entryIndices.Count == 0 ? -1 : _selectedIndex;
        }

        private void RebuildDetailFields()
        {
            if (_asset == null || _serializedObject == null)
            {
                return;
            }

            SerializedProperty selectedProperty = GetSelectedProperty();
            if (selectedProperty == null)
            {
                return;
            }

            _serializedObject.Update();
            _detailTitleLabel.text = GetPrimaryLabel(selectedProperty);

            _detailFieldsContainer.Unbind();
            _detailFieldsContainer.Clear();

            SerializedProperty iterator = selectedProperty.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                SerializedProperty currentProperty = iterator.Copy();
                if (!ShouldDisplayField(currentProperty.name))
                {
                    enterChildren = false;
                    continue;
                }

                _detailFieldsContainer.Add(CreateDetailField(currentProperty));
                enterChildren = false;
            }

            _detailFieldsContainer.Bind(_serializedObject);
        }

        private VisualElement CreateDetailField(SerializedProperty property)
        {
            if (TryCreateLocalizedEnumField(property, out VisualElement enumFieldContainer))
            {
                return enumFieldContainer;
            }

            VisualElement container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.marginBottom = 4f;

            string label = property.name == "group" ? "\u5206\u7ec4" : GetFieldLabel(property.name);
            PropertyField field = new PropertyField(property, label);
            if (IsReadOnlyField(property.name))
            {
                field.SetEnabled(false);
            }

            field.RegisterCallback<SerializedPropertyChangeEvent>(OnDetailPropertyChanged);
            container.Add(field);

            string description = property.name == "group"
                ? "\u7528\u4e8e GameObjectPoolManager \u4e0b\u7684\u7a7a\u95f2\u8282\u70b9\u5f52\u7c7b\uff0c\u4e0d\u586b\u6216\u7a7a\u503c\u4f1a\u81ea\u52a8\u56de\u843d\u5230 DefaultGroup\u3002"
                : GetFieldDescription(property.name);
            if (!string.IsNullOrWhiteSpace(description))
            {
                Label descriptionLabel = new Label(description);
                descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
                descriptionLabel.style.fontSize = 11f;
                descriptionLabel.style.color = DescriptionColor;
                descriptionLabel.style.marginLeft = 4f;
                descriptionLabel.style.marginRight = 4f;
                descriptionLabel.style.marginTop = -2f;
                descriptionLabel.style.marginBottom = 6f;
                container.Add(descriptionLabel);
            }

            return container;
        }

        private bool TryCreateLocalizedEnumField(SerializedProperty property, out VisualElement container)
        {
            List<string> options = GetEnumOptions(property.name);
            if (options == null)
            {
                container = null;
                return false;
            }

            container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.marginBottom = 4f;

            int currentIndex = Mathf.Clamp(property.enumValueIndex, 0, options.Count - 1);
            PopupField<string> popupField = new PopupField<string>(GetFieldLabel(property.name), options, currentIndex);
            string propertyPath = property.propertyPath;
            popupField.RegisterValueChangedCallback(_ =>
            {
                if (_serializedObject == null)
                {
                    return;
                }

                int selectedIndex = options.IndexOf(popupField.value);
                if (selectedIndex < 0)
                {
                    return;
                }

                _serializedObject.Update();
                SerializedProperty targetProperty = _serializedObject.FindProperty(propertyPath);
                if (targetProperty == null || targetProperty.enumValueIndex == selectedIndex)
                {
                    return;
                }

                targetProperty.enumValueIndex = selectedIndex;
                ApplyDetailPropertyChanges();
            });
            container.Add(popupField);

            string description = GetFieldDescription(property.name);
            if (!string.IsNullOrWhiteSpace(description))
            {
                Label descriptionLabel = new Label(description);
                descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
                descriptionLabel.style.fontSize = 11f;
                descriptionLabel.style.color = DescriptionColor;
                descriptionLabel.style.marginLeft = 4f;
                descriptionLabel.style.marginRight = 4f;
                descriptionLabel.style.marginTop = -2f;
                descriptionLabel.style.marginBottom = 6f;
                container.Add(descriptionLabel);
            }

            return true;
        }

        private void OnDetailPropertyChanged(SerializedPropertyChangeEvent evt)
        {
            ApplyDetailPropertyChanges();
        }

        private void ApplyDetailPropertyChanges()
        {
            if (_asset == null || _serializedObject == null)
            {
                return;
            }

            _serializedObject.ApplyModifiedPropertiesWithoutUndo();
            _asset.Normalize();
            _hasUnsavedChanges = true;
            _entryListContainer?.MarkDirtyRepaint();
            RefreshTitle();
            _detailTitleLabel.text = GetPrimaryLabel(GetSelectedProperty());
        }

        private void AddEntry()
        {
            if (_entriesProperty == null)
            {
                return;
            }

            _serializedObject.Update();

            int index = _entriesProperty.arraySize;
            _entriesProperty.InsertArrayElementAtIndex(index);
            SerializedProperty property = _entriesProperty.GetArrayElementAtIndex(index);
            InitializeNewEntry(property, index);
            RefreshEntryPriorities();

            _serializedObject.ApplyModifiedPropertiesWithoutUndo();
            _asset.Normalize();
            RebuildEntryIndices();
            _selectedIndex = index;
            _hasUnsavedChanges = true;
            RefreshUi();
        }

        private void RemoveEntry()
        {
            if (_entriesProperty == null || _entryIndices.Count == 0)
            {
                return;
            }

            _serializedObject.Update();
            _entriesProperty.DeleteArrayElementAtIndex(_selectedIndex);
            RefreshEntryPriorities();
            _serializedObject.ApplyModifiedPropertiesWithoutUndo();
            _asset.Normalize();
            RebuildEntryIndices();

            if (_selectedIndex >= _entryIndices.Count)
            {
                _selectedIndex = Mathf.Max(0, _entryIndices.Count - 1);
            }

            _hasUnsavedChanges = true;
            RefreshUi();
        }

        private void InitializeNewEntry(SerializedProperty property, int index)
        {
            property.FindPropertyRelative("entryName").stringValue = Utility.Text.Format("对象池规则{0}", index + 1);
            property.FindPropertyRelative("group").stringValue = PoolEntry.DefaultGroup;
            property.FindPropertyRelative("assetPath").stringValue = string.Empty;
            property.FindPropertyRelative("loaderType").enumValueIndex = (int)PoolResourceLoaderType.AssetBundle;
            property.FindPropertyRelative("category").enumValueIndex = (int)PoolCategory.Default;
            property.FindPropertyRelative("softCapacity").intValue = 8;
            property.FindPropertyRelative("hardCapacity").intValue = 16;
            property.FindPropertyRelative("priority").intValue = index;
        }

        private void RefreshEntryPriorities()
        {
            if (_entriesProperty == null)
            {
                return;
            }

            int count = _entriesProperty.arraySize;
            for (int i = 0; i < count; i++)
            {
                SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("priority").intValue = count - i;
            }
        }

        private static string GetAssetGuid(PoolConfigScriptableObject asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
        }

        private void SaveAsset()
        {
            if (_asset == null || _serializedObject == null)
            {
                return;
            }

            _serializedObject.ApplyModifiedProperties();
            _asset.Normalize();
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            _serializedObject.Update();
            _hasUnsavedChanges = false;
            RefreshUi();
        }

        private void RefreshTitle()
        {
            string assetLabel = _asset == null ? "未选择 PoolConfig" : _asset.name;
            if (_hasUnsavedChanges)
            {
                assetLabel = Utility.Text.Format("{0} *", assetLabel);
            }

            _titleLabel.text = assetLabel;
        }

        private SerializedProperty GetEntryAt(int index)
        {
            return _entriesProperty == null || index < 0 || index >= _entriesProperty.arraySize
                ? null
                : _entriesProperty.GetArrayElementAtIndex(index);
        }

        private SerializedProperty GetSelectedProperty()
        {
            if (_entryIndices.Count == 0)
            {
                return null;
            }

            ClampSelection();
            return GetEntryAt(_selectedIndex);
        }

        private string GetPrimaryLabel(SerializedProperty property)
        {
            if (property == null)
            {
                return "<规则缺失>";
            }

            string entryName = property.FindPropertyRelative("entryName").stringValue;
            string assetPath = property.FindPropertyRelative("assetPath").stringValue;
            if (!string.IsNullOrWhiteSpace(entryName))
            {
                return entryName;
            }

            return string.IsNullOrWhiteSpace(assetPath) ? "<未命名规则>" : assetPath;
        }

        private static List<string> GetEnumOptions(string propertyName)
        {
            return propertyName switch
            {
                "loaderType" => LoaderTypeOptions,
                _ => null
            };
        }

        private static bool ShouldDisplayField(string propertyName)
        {
            return propertyName != "category";
        }

        private static bool IsReadOnlyField(string propertyName)
        {
            return propertyName == "priority";
        }

        private static string GetFieldLabel(string propertyName)
        {
            return propertyName switch
            {
                "entryName" => "规则名称",
                "group" => "分组",
                "assetPath" => "资源路径",
                "matchMode" => "匹配模式",
                "loaderType" => "加载器类型",
                "softCapacity" => "软容量",
                "hardCapacity" => "容量",
                "priority" => "优先级",
                _ => propertyName
            };
        }

        private static string GetFieldDescription(string propertyName)
        {
            return propertyName switch
            {
                "entryName" => "规则名称就是主定位信息。列表、调试和问题排查都直接看这个名字。",
                "group" => "用于 GameObjectPoolManager 下的空闲节点归类。不填或空值会自动回落到 DefaultGroup。",
                "assetPath" => "要匹配的资源路径。精确匹配填完整路径，前缀匹配可填写目录前缀。",
                "matchMode" => "精确匹配只命中单一路径，前缀匹配适合同目录或同类资源共用规则。",
                "loaderType" => "决定 Prefab 从哪个资源通道加载。AssetBundle 走包体资源，Resources 走内置目录。",
                "softCapacity" => "超过该值后，维护阶段会优先回收空闲实例。",
                "hardCapacity" => "基础容量。超过这个值会自动扩容并输出警告，后续维护回收会再收回到这个基准。",
                "priority" => "由左侧拖拽顺序自动维护，越靠上优先级越高。",
                _ => string.Empty
            };
        }

        private void ClampSelection()
        {
            if (_entryIndices.Count == 0)
            {
                _selectedIndex = 0;
                return;
            }

            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _entryIndices.Count - 1);
        }
    }
}
