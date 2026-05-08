using System;
using System.Linq;
using System.Collections.Generic;
using AlicizaX.Editor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEditor;


namespace AlicizaX.Localization.Editor
{
    public class TableSheetTreeView : TreeView
    {
        private const string k_DeleteCommand = "Delete";
        private const string k_SoftDeleteCommand = "SoftDelete";
        private const string k_NewSection = "New Section";
        private const string k_NewEntry = "New Entry";
        private const int k_TreeViewStartIndex = 100;

        public Action<LocalizationTableWindow.WindowSelection> OnTableSheetSelect;

        private readonly LocalizationWindowData windowData;

        private bool InitiateContextMenuOnNextRepaint = false;
        private int ContextSelectedID = -1;

        internal class LstrSectionTreeViewItem : TreeViewItem
        {
            public SheetSectionTreeView Section;

            public int Id => Section.Id;

            public LstrSectionTreeViewItem(int id, int depth, SheetSectionTreeView tableData) : base(id, depth, tableData.Name)
            {
                Section = tableData;
            }
        }

        internal class LstrEntryTreeViewItem : TreeViewItem
        {
            public SheetItemTreeView Item;

            public int Id => Item.Id;

            public LstrEntryTreeViewItem(int id, int depth, SheetItemTreeView item) : base(id, depth, item.Key)
            {
                Item = item;
            }
        }

        public TableSheetTreeView(TreeViewState state, LocalizationWindowData data) : base(state)
        {
            windowData = data;
            rowHeight = 20f;
            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            int id = k_TreeViewStartIndex;
            var root = new TreeViewItem { id = id++, depth = -1, displayName = "TableSheet" };

            foreach (var section in windowData.TableSheet)
            {
                string sectionName = section.Name;
                var sectionItem = new LstrSectionTreeViewItem(id++, 0, section);

                root.AddChild(sectionItem);

                // Add items within each section as children of the section.
                foreach (var key in section.Items)
                {
                    sectionItem.AddChild(new LstrEntryTreeViewItem(id++, 1, key));
                }
            }

            if (root.children == null)
                root.children = new List<TreeViewItem>();

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item;
            var rect = args.rowRect;

            GUIContent labelIcon = new GUIContent(item.displayName);
            if (item is LstrSectionTreeViewItem) labelIcon = EditorGUIUtility.TrTextContentWithIcon(" " + item.displayName, "Folder Icon");

            Rect labelRect = new(rect.x + GetContentIndent(item), rect.y, rect.width - GetContentIndent(item), rect.height);
            Rect toggleRect = new Rect(rect.xMax - EditorGUIUtility.singleLineHeight - 4f, rect.y, EditorGUIUtility.singleLineHeight, rect.height);

            EditorGUI.LabelField(labelRect, labelIcon);
            if (item is LstrEntryTreeViewItem entryTreeViewItem)
            {
                entryTreeViewItem.Item.isGen = EditorGUI.Toggle(toggleRect, entryTreeViewItem.Item.isGen);
            }
        }

        public override void OnGUI(Rect rect)
        {
            Rect headerRect = EditorDrawing.DrawHeaderWithBorder(ref rect, new GUIContent("TABLE SHEET"), 20f, false);
            headerRect.xMin = headerRect.xMax - EditorGUIUtility.singleLineHeight - EditorGUIUtility.standardVerticalSpacing;
            headerRect.width = EditorGUIUtility.singleLineHeight;
            headerRect.y += EditorGUIUtility.standardVerticalSpacing;

            if (GUI.Button(headerRect, EditorUtils.Styles.PlusIcon, EditorStyles.iconButton))
            {
                OnAddNewSection();
                Reload();
            }

            if (InitiateContextMenuOnNextRepaint)
            {
                InitiateContextMenuOnNextRepaint = false;
                PopUpContextMenu();
            }

            // --- 新增：Ctrl/Cmd + D 快捷键处理（在 TreeView 获得焦点且选中 entry 时新增 entry） ---
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.control || e.command) && e.keyCode == KeyCode.D)
            {
                if (HasFocus())
                {
                    var sel = GetSelection();
                    if (sel != null && sel.Count > 0)
                    {
                        var selectedItem = FindItem(sel[0], rootItem);
                        if (selectedItem is LstrEntryTreeViewItem entry)
                        {
                            var parentSection = selectedItem.parent as LstrSectionTreeViewItem;
                            if (parentSection != null)
                            {
                                OnAddNewSectionEntry(parentSection.Section.Id);
                                Reload();
                                SelectLastChildOfSectionAfterReload(parentSection); // 选中新加的条目
                                e.Use();
                            }
                        }
                        else if (selectedItem is LstrSectionTreeViewItem section)
                        {
                            OnAddNewSectionEntry(section.Section.Id);
                            Reload();
                            SelectLastChildOfSectionAfterReload(section);
                            e.Use();
                        }
                    }
                }
            }
            // --- 结束新增 ---

            HandleCommandEvent(Event.current);
            base.OnGUI(rect);
        }

        private void PopUpContextMenu()
        {
            var selectedItem = FindItem(ContextSelectedID, rootItem);
            var menu = new GenericMenu();

            if (selectedItem is LstrSectionTreeViewItem section)
            {
                menu.AddItem(new GUIContent("Add Entry"), false, () =>
                {
                    OnAddNewSectionEntry(section.Section.Id);
                    ContextSelectedID = -1;
                    Reload();
                    SelectLastChildOfSectionAfterReload(section);
                });

                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    DeleteLstrSection(section.Section);
                    ContextSelectedID = -1;
                    Reload();
                });
            }
            else if (selectedItem is LstrEntryTreeViewItem item)
            {
                LstrSectionTreeViewItem parentSection = item.parent as LstrSectionTreeViewItem;

                // 新增：对 entry 的右键也显示 Add Entry（在同一个 section 下新增）
                menu.AddItem(new GUIContent("Add Entry"), false, () =>
                {
                    if (parentSection != null)
                        OnAddNewSectionEntry(parentSection.Section.Id);

                    ContextSelectedID = -1;
                    Reload();

                    if (parentSection != null)
                        SelectLastChildOfSectionAfterReload(parentSection);
                });

                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    if (parentSection != null)
                        DeleteLstrEntry(parentSection.Section, item.Item);

                    ContextSelectedID = -1;
                    Reload();
                });
                menu.AddItem(new GUIContent("Copy"), false, () =>
                {
                    if (parentSection != null)
                        CopyEntryToBiliboard(parentSection.Section, item.Item);

                    ContextSelectedID = -1;
                    Reload();
                });
            }

            menu.ShowAsContext();
        }

        private void SelectLastChildOfSectionAfterReload(LstrSectionTreeViewItem originalSection)
        {
            if (rootItem == null || rootItem.children == null) return;

            // 找到 reload 后仍然绑定到同一 section object 的节点
            var newSectionNode = rootItem.children
                .OfType<LstrSectionTreeViewItem>()
                .FirstOrDefault(s => s.Section != null && originalSection.Section != null && s.Section.Id == originalSection.Section.Id);

            if (newSectionNode != null && newSectionNode.children != null && newSectionNode.children.Count > 0)
            {
                var newEntryNode = newSectionNode.children.Last();
                SetSelection(new int[] { newEntryNode.id });
                FrameItem(newEntryNode.id);
            }
        }


        private void OnAddNewSection()
        {
            windowData.AddSection(k_NewSection);
        }

        private void OnAddNewSectionEntry(int sectionId)
        {
            foreach (var section in windowData.TableSheet)
            {
                if (section.Id == sectionId)
                {
                    windowData.AddItem(section, k_NewEntry);
                    break;
                }
            }
        }

        protected override bool CanRename(TreeViewItem item) => true;
        protected override bool CanMultiSelect(TreeViewItem item) => true;

        protected override void ContextClickedItem(int id)
        {
            InitiateContextMenuOnNextRepaint = true;
            ContextSelectedID = id;
            Repaint();
        }

        protected override void RenameEnded(RenameEndedArgs args)
        {
            if (!args.acceptedRename)
                return;

            var renamedItem = FindItem(args.itemID, rootItem);
            if (renamedItem == null) return;

            if (renamedItem is LstrEntryTreeViewItem item)
            {
                if (!LocalizationWindowUtility.TryValidateItemKey((SheetSectionTreeView)item.Item.Parent, item.Item, args.newName, out string error))
                {
                    EditorUtility.DisplayDialog("Invalid Key", error, "OK");
                    return;
                }

                renamedItem.displayName = args.newName;
                item.Item.Key = args.newName;
            }
            else if (renamedItem is LstrSectionTreeViewItem section)
            {
                if (!LocalizationWindowUtility.TryValidateSectionName(windowData, section.Section, args.newName, out string error))
                {
                    EditorUtility.DisplayDialog("Invalid Section", error, "OK");
                    return;
                }

                renamedItem.displayName = args.newName;
                section.Section.Name = args.newName;
            }
        }

        protected override void SingleClickedItem(int id)
        {
            var selectedItem = FindItem(id, rootItem);
            if (selectedItem != null)
            {
                if (selectedItem is LstrSectionTreeViewItem section)
                {
                    OnTableSheetSelect?.Invoke(new LocalizationTableWindow.SectionSelect()
                    {
                        Section = section.Section,
                        TreeViewItem = section
                    });
                }
                else if (selectedItem is LstrEntryTreeViewItem entry)
                {
                    OnTableSheetSelect?.Invoke(new LocalizationTableWindow.ItemSelect()
                    {
                        Item = entry.Item,
                        TreeViewItem = entry
                    });
                }
            }
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectedIds.Count > 1)
                OnTableSheetSelect?.Invoke(null);
        }

        protected override bool CanStartDrag(CanStartDragArgs args)
        {
            var firstItem = FindItem(args.draggedItemIDs[0], rootItem);
            return args.draggedItemIDs.All(id => FindItem(id, rootItem).parent == firstItem.parent);
        }

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData("IDs", args.draggedItemIDs.ToArray());
            DragAndDrop.SetGenericData("Type", "TableSheet");
            DragAndDrop.StartDrag("TableSheet");
        }

        protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
        {
            int[] draggedIDs = (int[])DragAndDrop.GetGenericData("IDs");
            string type = (string)DragAndDrop.GetGenericData("Type");

            if (!type.Equals("TableSheet"))
                return DragAndDropVisualMode.Rejected;

            switch (args.dragAndDropPosition)
            {
                case DragAndDropPosition.BetweenItems:
                    if (args.parentItem is LstrSectionTreeViewItem section1)
                    {
                        bool acceptDrag = false;
                        foreach (var draggedId in draggedIDs)
                        {
                            var draggedItem = FindItem(draggedId, rootItem);
                            if (draggedItem != null && draggedItem is LstrEntryTreeViewItem item)
                            {
                                if (args.performDrop)
                                {
                                    if (draggedItem.parent == section1)
                                    {
                                        OnMoveItemWithinSection(section1.Section, item.Item, args.insertAtIndex);
                                    }
                                    else
                                    {
                                        var parentSection = (LstrSectionTreeViewItem)draggedItem.parent;
                                        OnMoveItemToSectionAt(parentSection.Section, section1.Section, item.Item, args.insertAtIndex);
                                    }
                                }

                                acceptDrag = true;
                            }
                        }

                        if (args.performDrop && acceptDrag)
                        {
                            Reload();
                            SetSelection(new int[0]);
                        }

                        return acceptDrag
                            ? DragAndDropVisualMode.Move
                            : DragAndDropVisualMode.Rejected;
                    }
                    else
                    {
                        bool acceptDrag = false;
                        foreach (var draggedId in draggedIDs)
                        {
                            var draggedItem = FindItem(draggedId, rootItem);
                            if (draggedItem != null && draggedItem is LstrSectionTreeViewItem section)
                            {
                                if (args.performDrop)
                                    OnMoveSection(section.Section, args.insertAtIndex);

                                acceptDrag = true;
                            }
                        }

                        if (args.performDrop && acceptDrag)
                        {
                            Reload();
                            SetSelection(new int[0]);
                        }

                        return acceptDrag
                            ? DragAndDropVisualMode.Move
                            : DragAndDropVisualMode.Rejected;
                    }

                case DragAndDropPosition.UponItem:
                    if (args.parentItem is LstrSectionTreeViewItem section2)
                    {
                        bool acceptDrag = false;
                        foreach (var draggedId in draggedIDs)
                        {
                            var draggedItem = FindItem(draggedId, rootItem);
                            if (draggedItem != null && draggedItem is LstrEntryTreeViewItem item)
                            {
                                if (args.performDrop && draggedItem.parent != section2)
                                {
                                    var parentSection = (LstrSectionTreeViewItem)draggedItem.parent;
                                    OnMoveItemToSection(parentSection.Section, section2.Section, item.Item);
                                }

                                acceptDrag = true;
                            }
                        }

                        if (args.performDrop && acceptDrag)
                        {
                            Reload();
                            SetSelection(new int[0]);
                        }

                        return acceptDrag
                            ? DragAndDropVisualMode.Move
                            : DragAndDropVisualMode.Rejected;
                    }

                    break;

                case DragAndDropPosition.OutsideItems:
                    break;
            }

            return DragAndDropVisualMode.Rejected;
        }

        private void OnMoveItemWithinSection(SheetSectionTreeView section, SheetItemTreeView item, int position)
        {
            windowData.OnMoveItemWithinSection(section, item, position);
        }

        private void OnMoveItemToSectionAt(SheetSectionTreeView parent, SheetSectionTreeView section, SheetItemTreeView item, int position)
        {
            windowData.OnMoveItemToSectionAt(parent, section, item, position);
        }

        private void OnMoveItemToSection(SheetSectionTreeView parent, SheetSectionTreeView section, SheetItemTreeView item)
        {
            windowData.OnMoveItemToSection(parent, section, item);
        }

        private void OnMoveSection(SheetSectionTreeView section, int position)
        {
            windowData.OnMoveSection(section, position);
        }

        private void HandleCommandEvent(Event uiEvent)
        {
            if (uiEvent.type == EventType.ValidateCommand)
            {
                switch (uiEvent.commandName)
                {
                    case k_DeleteCommand:
                    case k_SoftDeleteCommand:
                        if (HasSelection())
                            uiEvent.Use();
                        break;
                }
            }
            else if (uiEvent.type == EventType.ExecuteCommand)
            {
                switch (uiEvent.commandName)
                {
                    case k_DeleteCommand:
                    case k_SoftDeleteCommand:
                        DeleteSelected();
                        break;
                }
            }
        }

        private void DeleteSelected()
        {
            var toDelete = GetSelection().OrderByDescending(i => i).ToArray();
            if (toDelete.Length <= 0) return;

            if (!EditorUtility.DisplayDialog("Delete Localization Items", $"Delete {toDelete.Length} selected item(s)?", "Delete", "Cancel"))
            {
                return;
            }

            foreach (var index in toDelete)
            {
                var selectedItem = FindItem(index, rootItem);
                if (selectedItem == null) continue;

                if (selectedItem is LstrEntryTreeViewItem item)
                {
                    var parentSection = (LstrSectionTreeViewItem)selectedItem.parent;
                    DeleteLstrEntry(parentSection.Section, item.Item);
                }
                else if (selectedItem is LstrSectionTreeViewItem section)
                {
                    DeleteLstrSection(section.Section);
                }
            }

            SetSelection(new int[0]);
            Reload();
        }


        private void CopyEntryToBiliboard(SheetSectionTreeView section, SheetItemTreeView sheetItem)
        {
            if (!HasFocus())
                return;
            string combineKey = $"{section.Name}.{sheetItem.Key}";
            GUIUtility.systemCopyBuffer = combineKey;
        }

        private void DeleteLstrEntry(SheetSectionTreeView section, SheetItemTreeView sheetItem)
        {
            if (!HasFocus())
                return;

            windowData.RemoveItem(section, sheetItem);
        }

        private void DeleteLstrSection(SheetSectionTreeView section)
        {
            if (!HasFocus())
                return;

            windowData.RemoveSection(section);
        }
    }
}
