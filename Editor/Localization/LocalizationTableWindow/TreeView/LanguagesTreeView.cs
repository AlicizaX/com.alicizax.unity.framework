using System;
using System.Linq;
using System.Collections.Generic;
using AlicizaX.Editor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEditor;

namespace AlicizaX.Localization.Editor
{
    public class LanguagesTreeView : TreeView
    {
        private const string k_DeleteCommand = "Delete";
        private const string k_SoftDeleteCommand = "SoftDelete";

        public Action<LocalizationTableWindow.WindowSelection> OnLanguageSelect;

        private readonly LocalizationWindowData windowData;

        internal class LanguageTreeViewItem : TreeViewItem
        {
            public TempLanguageData language;

            public LanguageTreeViewItem(int id, int depth, TempLanguageData language) : base(id, depth, language.Entry.LanguageName)
            {
                this.language = language;
            }
        }

        public LanguagesTreeView(TreeViewState state, LocalizationWindowData data, GameLocaizationTable table) : base(state)
        {
            windowData = data;
            rowHeight = 20f;
            Reload();
        }


        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Languages" };
            int id = 1;

            foreach (var lang in windowData.Languages)
            {
                root.AddChild(new LanguageTreeViewItem(id++, 1, lang));
            }

            if (root.children == null)
                root.children = new List<TreeViewItem>();

            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item;
            var rect = args.rowRect;

            GUIContent labelIcon = EditorGUIUtility.TrTextContentWithIcon(" " + item.displayName, "BuildSettings.Web.Small");
            Rect labelRect = new(rect.x + 2f, rect.y, rect.width - 2f, rect.height);
            EditorGUI.LabelField(labelRect, labelIcon);
        }

        public override void OnGUI(Rect rect)
        {
            EditorDrawing.DrawHeaderWithBorder(ref rect, new GUIContent("LANGUAGES"), 20f, false);

            HandleCommandEvent(Event.current);
            base.OnGUI(rect);
        }

        protected override bool CanRename(TreeViewItem item) => false;
        protected override bool CanMultiSelect(TreeViewItem item) => true;

        protected override void RenameEnded(RenameEndedArgs args) { }

        protected override void ContextClickedItem(int id) { }

        protected override void SingleClickedItem(int id)
        {
            var selectedItem = FindItem(id, rootItem);
            if (selectedItem != null)
            {
                if (selectedItem is LanguageTreeViewItem item)
                {
                    OnLanguageSelect?.Invoke(new LocalizationTableWindow.LanguageSelect()
                    {
                        Language = item.language,
                        TreeViewItem = item
                    });
                }
            }
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectedIds.Count > 1)
                OnLanguageSelect?.Invoke(null);
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
            DragAndDrop.SetGenericData("Type", "Languages");
            DragAndDrop.StartDrag("Languages");
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
                        uiEvent.Use();
                        break;
                }
            }
        }
    }
}
