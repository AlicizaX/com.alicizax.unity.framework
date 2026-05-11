using System.Collections.Generic;
using System.Reflection;
using System;
using UnityEngine;
using UnityEditor;

namespace AlicizaX.Editor
{
    public static class EditorUtils
    {
        public static class Styles
        {
            public static GUIStyle IconButton => GUI.skin.FindStyle("IconButton");
            public static readonly GUIContent PlusIcon = EditorGUIUtility.TrIconContent("Toolbar Plus", "Add Item");
            public static readonly GUIContent MinusIcon = EditorGUIUtility.TrIconContent("Toolbar Minus", "Remove Item");
            public static readonly GUIContent TrashIcon = EditorGUIUtility.TrIconContent("TreeEditor.Trash", "Remove Item");
            public static readonly GUIContent RefreshIcon = EditorGUIUtility.TrIconContent("Refresh", "Refresh");
            public static readonly GUIContent Linked = EditorGUIUtility.TrIconContent("Linked");
            public static readonly GUIContent UnLinked = EditorGUIUtility.TrIconContent("Unlinked");
            public static readonly GUIContent Database = EditorGUIUtility.TrIconContent("Package Manager");
            public static readonly GUIContent GreenLight = EditorGUIUtility.TrIconContent("greenLight");
            public static readonly GUIContent OrangeLight = EditorGUIUtility.TrIconContent("orangeLight");
            public static readonly GUIContent RedLight = EditorGUIUtility.TrIconContent("redLight");

            public static GUIStyle RichLabel => new GUIStyle(EditorStyles.label)
            {
                richText = true
            };
        }

        public class BoxGroupScope : GUI.Scope
        {
            public BoxGroupScope(string icon, string title, float height = 22)
            {
                GUIContent iconTitle = EditorGUIUtility.TrTextContentWithIcon(" " + title, icon);
                EditorGUILayout.BeginVertical(GUI.skin.box);

                Rect headerRect = GUILayoutUtility.GetRect(1, height);
                EditorGUI.DrawRect(headerRect, new Color(0.1f, 0.1f, 0.1f, 0.4f));

                headerRect.x += EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.LabelField(headerRect, iconTitle, EditorStyles.boldLabel);

                EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
            }

            public BoxGroupScope(string title, float height = 22)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);

                Rect headerRect = GUILayoutUtility.GetRect(1, height);
                EditorGUI.DrawRect(headerRect, new Color(0.1f, 0.1f, 0.1f, 0.4f));

                headerRect.x += EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.LabelField(headerRect, title, EditorStyles.boldLabel);

                EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
            }

            protected override void CloseScope()
            {
                EditorGUILayout.EndVertical();
            }
        }

        public struct PopupArray
        {
            public List<string> contents;
            public List<string> selected;

            public PopupArray(string[] contents, string[] selected)
            {
                this.contents = new List<string>(contents);
                this.selected = new List<string>(selected);
            }
        }

        public sealed class PopupElement
        {
            public Action<string[]> onSelect;
            public PopupArray popupArray;
            public string name;

            public PopupElement(PopupArray popupArray, string name, Action<string[]> onSelect)
            {
                this.popupArray = popupArray;
                this.name = name;
                this.onSelect = onSelect;
            }
        }

        public static void DrawOutline(Rect rect, RectOffset border)
        {
            Color color = new Color(0.6f, 0.6f, 0.6f, 1.333f);
            if (EditorGUIUtility.isProSkin)
            {
                color.r = 0.12f;
                color.g = 0.12f;
                color.b = 0.12f;
            }

            if (Event.current.type != EventType.Repaint)
                return;

            Color orgColor = GUI.color;
            GUI.color *= color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, border.top), EditorGUIUtility.whiteTexture); //top
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - border.bottom, rect.width, border.bottom), EditorGUIUtility.whiteTexture); //bottom
            GUI.DrawTexture(new Rect(rect.x, rect.y + 1, border.left, rect.height - 2 * border.left), EditorGUIUtility.whiteTexture); //left
            GUI.DrawTexture(new Rect(rect.xMax - border.right, rect.y + 1, border.right, rect.height - 2 * border.right), EditorGUIUtility.whiteTexture); //right

            GUI.color = orgColor;
        }

        public static void DrawOutline(Rect rect, RectOffset border, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Color orgColor = GUI.color;
            GUI.color *= color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, border.top), EditorGUIUtility.whiteTexture); //top
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - border.bottom, rect.width, border.bottom), EditorGUIUtility.whiteTexture); //bottom
            GUI.DrawTexture(new Rect(rect.x, rect.y + 1, border.left, rect.height - 2 * border.left), EditorGUIUtility.whiteTexture); //left
            GUI.DrawTexture(new Rect(rect.xMax - border.right, rect.y + 1, border.right, rect.height - 2 * border.right), EditorGUIUtility.whiteTexture); //right

            GUI.color = orgColor;
        }

        public static Rect DrawHeader(float height, string title)
        {
            Rect rect = GUILayoutUtility.GetRect(0, height);
            EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 0.4f));

            var labelRect = rect;
            labelRect.x += 3f;

            EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);
            return rect;
        }

        public static void DrawHeader(Rect rect, string title, float labelX, float labelY, GUIStyle labelStyle)
        {
            EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 0.4f));

            var labelRect = rect;
            labelRect.x += labelX;
            labelRect.y += labelY;

            EditorGUI.LabelField(labelRect, title, labelStyle);
        }

        public static Rect DrawHeaderWithBorder(GUIContent title, float height, ref Rect rect, bool rounded)
        {
            GUI.Box(rect, GUIContent.none, new GUIStyle(rounded ? "HelpBox" : "Tooltip"));

            Rect headerRect = rect;
            headerRect.height = height;
            EditorGUI.DrawRect(headerRect, new Color(0.1f, 0.1f, 0.1f, 0.4f));

            Rect labelRect = headerRect;
            labelRect.y += height / 2;
            EditorGUI.LabelField(labelRect, title, EditorStyles.miniBoldLabel);

            rect.x += 1;
            rect.y += 1;
            rect.height -= 1;
            rect.width -= 2;
            rect.y += height;
            rect.height -= height;
            return rect;
        }

        public static Rect DrawHeaderWithBorder(GUIContent title, float height, ref Rect rect, GUIStyle boxStyle)
        {
            GUI.Box(rect, GUIContent.none, boxStyle);
            rect.x += 1;
            rect.y += 1;
            rect.height -= 1;
            rect.width -= 2;

            var headerRect = rect;
            headerRect.height = height + EditorGUIUtility.standardVerticalSpacing;

            rect.y += headerRect.height;
            rect.height -= headerRect.height;

            EditorGUI.DrawRect(headerRect, new Color(0.1f, 0.1f, 0.1f, 0.4f));

            var labelRect = headerRect;
            labelRect.y += EditorGUIUtility.standardVerticalSpacing;
            labelRect.x += 2f;

            EditorGUI.LabelField(labelRect, title, EditorStyles.miniBoldLabel);

            return headerRect;
        }

        public static Rect DrawHeaderWithBorder(GUIContent title, float height, ref Rect rect, RectOffset border)
        {
            DrawOutline(rect, border);
            rect.x += 1;
            rect.y += 1;
            rect.height -= 1;
            rect.width -= 2;

            var headerRect = rect;
            headerRect.height = height + EditorGUIUtility.standardVerticalSpacing;

            rect.y += headerRect.height;
            rect.height -= headerRect.height;

            EditorGUI.DrawRect(headerRect, new Color(0.1f, 0.1f, 0.1f, 0.4f));

            var labelRect = headerRect;
            labelRect.y += EditorGUIUtility.standardVerticalSpacing;
            labelRect.x += 2f;

            EditorGUI.LabelField(labelRect, title, EditorStyles.miniBoldLabel);

            return headerRect;
        }

        public static bool DrawBoxFoldoutHeader(string title, bool state, float height = 22)
        {
            Rect rect = GUILayoutUtility.GetRect(1, height);
            EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 0.4f));

            rect.x += EditorGUIUtility.standardVerticalSpacing;
            Rect foldoutRect = EditorGUI.IndentedRect(rect);
            state = GUI.Toggle(foldoutRect, state, GUIContent.none, EditorStyles.foldout);

            rect.x += EditorGUIUtility.singleLineHeight - EditorGUIUtility.standardVerticalSpacing * 2;
            EditorGUI.LabelField(rect, new GUIContent(title), EditorStyles.boldLabel);

            return state;
        }

        public static bool DrawFoldoutHeader(float height, string title, bool state)
        {
            Rect rect = GUILayoutUtility.GetRect(1, height);

            rect.x += EditorGUIUtility.standardVerticalSpacing;
            Rect foldoutRect = EditorGUI.IndentedRect(rect);
            state = GUI.Toggle(foldoutRect, state, GUIContent.none, EditorStyles.foldout);

            rect.x += EditorGUIUtility.singleLineHeight - EditorGUIUtility.standardVerticalSpacing * 2;
            EditorGUI.LabelField(rect, new GUIContent(title), EditorStyles.boldLabel);

            return state;
        }

        public static void DrawFoldoutToggleHeader(Rect rect, string title, ref bool isExpanded, ref bool isEnabled, bool showFoldout = true, bool toggleDisable = false)
        {
            Color headerColor = new Color(0.1f, 0.1f, 0.1f, 0f);

            var expandRect = rect;
            expandRect.xMin += EditorGUIUtility.singleLineHeight * 2;

            var foldoutRect = rect;
            foldoutRect.width = EditorGUIUtility.singleLineHeight;

            var toggleRect = rect;
            toggleRect.width = EditorGUIUtility.singleLineHeight;
            toggleRect.x += EditorGUIUtility.singleLineHeight;

            var labelRect = rect;
            labelRect.xMin += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2;

            if (showFoldout)
            {
                // events
                var e = Event.current;
                if (expandRect.Contains(e.mousePosition))
                {
                    if (e.type == EventType.MouseDown && e.button == 0)
                    {
                        isExpanded = !isExpanded;
                        e.Use();
                    }
                }

                // foldout
                isExpanded = GUI.Toggle(foldoutRect, isExpanded, GUIContent.none, EditorStyles.foldout);
            }

            // background
            EditorGUI.DrawRect(rect, headerColor);

            // toggle
            using (new EditorGUI.DisabledGroupScope(toggleDisable))
            {
                isEnabled = GUI.Toggle(toggleRect, isEnabled, new GUIContent("", "Extension Enabled State"), EditorStyles.toggle);
            }

            // title
            EditorGUI.LabelField(labelRect, new GUIContent(title), EditorStyles.boldLabel);
        }

        private static void PopupSelect(object data)
        {
            PopupElement element = (PopupElement)data;
            PopupArray array = element.popupArray;
            string name = element.name;

            if (array.selected.Contains(name))
                array.selected.Remove(name);
            else array.selected.Add(name);

            element.onSelect?.Invoke(array.selected.ToArray());
        }

        public static void DrawMultiSelectionPopup(Rect rect, string title, PopupArray popupArray, Action<string[]> onSelect)
        {
            GenericMenu menu = new GenericMenu();

            for (int i = 0; i < popupArray.contents.Count; i++)
            {
                string name = popupArray.contents[i];
                bool on = popupArray.selected.Contains(name);

                PopupElement element = new PopupElement(popupArray, name, onSelect);
                menu.AddItem(new GUIContent(name), on, PopupSelect, element);
            }

            if(GUI.Button(rect, title, EditorStyles.popup))
            {
                menu.ShowAsContext();
            }
        }

        public static void DrawRelativeProperties(SerializedProperty root, float width)
        {
            var childrens = root.GetVisibleChildrens();

            foreach (var childProperty in childrens)
            {
                float height = EditorGUI.GetPropertyHeight(childProperty, true);

                Rect rect = GUILayoutUtility.GetRect(1f, height);
                rect.xMin += width;
                EditorGUI.PropertyField(rect, childProperty, true);
                EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
            }
        }

        public static IEnumerable<SerializedProperty> GetVisibleChildrens(this SerializedProperty serializedProperty)
        {
            SerializedProperty currentProperty = serializedProperty.Copy();
            SerializedProperty nextSiblingProperty = serializedProperty.Copy();
            {
                nextSiblingProperty.NextVisible(false);
            }

            if (currentProperty.NextVisible(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(currentProperty, nextSiblingProperty))
                        break;

                    yield return currentProperty;
                }
                while (currentProperty.NextVisible(false));
            }
        }

        public static void TrHelpIconText(string message, string icon, bool rich = false)
        {
            GUIStyle style = new GUIStyle(EditorStyles.helpBox)
            {
                richText = rich
            };

            EditorGUILayout.LabelField(GUIContent.none, EditorGUIUtility.TrTextContentWithIcon(" " + message, icon), style, new GUILayoutOption[0]);
        }

        public static void TrHelpIconText(Rect rect, string message, string icon, bool rich = false)
        {
            GUIStyle style = new GUIStyle(EditorStyles.helpBox)
            {
                richText = rich
            };

            EditorGUI.LabelField(rect, GUIContent.none, EditorGUIUtility.TrTextContentWithIcon(" " + message, icon), style);
        }

        public static void TrHelpIconText(string message, MessageType messageType, bool rich = false, bool space = true)
        {
            string icon = string.Empty;

            GUIStyle style = new GUIStyle(EditorStyles.helpBox)
            {
                richText = rich
            };

            switch (messageType)
            {
                case MessageType.Info:
                    icon = "console.infoicon.sml";
                    break;
                case MessageType.Warning:
                    icon = "console.warnicon.sml";
                    break;
                case MessageType.Error:
                    icon = "console.erroricon.sml";
                    break;
            }

            if (!string.IsNullOrEmpty(icon))
            {
                string text = space ? " " + message : message;
                EditorGUILayout.LabelField(GUIContent.none, EditorGUIUtility.TrTextContentWithIcon(text, icon), style, new GUILayoutOption[0]);
            }
            else
            {
                EditorGUILayout.LabelField(GUIContent.none, EditorGUIUtility.TrTextContent(message), style, new GUILayoutOption[0]);
            }
        }

        public static void TrHelpIconText(Rect rect, string message, MessageType messageType, bool rich = false, bool space = true)
        {
            string icon = string.Empty;

            GUIStyle style = new GUIStyle(EditorStyles.helpBox)
            {
                richText = rich
            };

            switch (messageType)
            {
                case MessageType.Info:
                    icon = "console.infoicon.sml";
                    break;
                case MessageType.Warning:
                    icon = "console.warnicon.sml";
                    break;
                case MessageType.Error:
                    icon = "console.erroricon.sml";
                    break;
            }

            if (!string.IsNullOrEmpty(icon))
            {
                string text = space ? " " + message : message;
                EditorGUI.LabelField(rect, GUIContent.none, EditorGUIUtility.TrTextContentWithIcon(text, icon), style);
            }
            else
            {
                EditorGUI.LabelField(rect, GUIContent.none, EditorGUIUtility.TrTextContent(message), style);
            }
        }

        public static void TrIconText(string message, string icon, GUIStyle style, bool rich = false, bool space = true)
        {
            style.richText = rich;
            string text = space ? " " + message : message;
            EditorGUILayout.LabelField(GUIContent.none, EditorGUIUtility.TrTextContentWithIcon(text, icon), style, new GUILayoutOption[0]);
        }

        public static void TrIconText(string message, MessageType messageType, GUIStyle style, bool rich = false, bool space = true)
        {
            string icon = string.Empty;
            style.richText = rich;

            switch (messageType)
            {
                case MessageType.Info:
                    icon = "console.infoicon.sml";
                    break;
                case MessageType.Warning:
                    icon = "console.warnicon.sml";
                    break;
                case MessageType.Error:
                    icon = "console.erroricon.sml";
                    break;
            }

            if (!string.IsNullOrEmpty(icon))
            {
                string text = space ? " " + message : message;
                EditorGUILayout.LabelField(GUIContent.none, EditorGUIUtility.TrTextContentWithIcon(text, icon), style, new GUILayoutOption[0]);
            }
            else
            {
                EditorGUILayout.LabelField(GUIContent.none, EditorGUIUtility.TrTextContent(message), style, new GUILayoutOption[0]);
            }
        }

        public static void TrIconText(Rect rect, string message, MessageType messageType, GUIStyle style, bool rich = false, bool space = true)
        {
            string icon = string.Empty;
            style.richText = rich;

            switch (messageType)
            {
                case MessageType.Info:
                    icon = "console.infoicon.sml";
                    break;
                case MessageType.Warning:
                    icon = "console.warnicon.sml";
                    break;
                case MessageType.Error:
                    icon = "console.erroricon.sml";
                    break;
            }

            if (!string.IsNullOrEmpty(icon))
            {
                string text = space ? " " + message : message;
                EditorGUI.LabelField(rect, GUIContent.none, EditorGUIUtility.TrTextContentWithIcon(text, icon), style);
            }
            else
            {
                EditorGUI.LabelField(rect, GUIContent.none, EditorGUIUtility.TrTextContent(message), style);
            }
        }
    }

    public static class AlicizaEditorGUI
    {
        private const float DefaultControlHeight = 20f;
        private const float DropdownArrowWidth = 22f;

        public static class Colors
        {
            public static readonly Color PopupBackground = new Color(0.17f, 0.17f, 0.18f, 1f);
            public static readonly Color PopupHeader = new Color(0.27f, 0.27f, 0.28f, 1f);
            public static readonly Color PopupRow = new Color(0.22f, 0.22f, 0.23f, 1f);
            public static readonly Color PopupRowHover = new Color(0.30f, 0.30f, 0.32f, 1f);
            public static readonly Color Panel = new Color(0.15f, 0.15f, 0.15f, 1f);
            public static readonly Color Toolbar = new Color(0.18f, 0.18f, 0.18f, 1f);
            public static readonly Color Row = new Color(0.25f, 0.25f, 0.24f, 1f);
            public static readonly Color RowExpanded = new Color(0.32f, 0.32f, 0.31f, 1f);
            public static readonly Color RowHover = new Color(0.30f, 0.30f, 0.29f, 1f);
            public static readonly Color Body = new Color(0.18f, 0.18f, 0.17f, 1f);
            public static readonly Color FieldRow = new Color(0.22f, 0.22f, 0.21f, 1f);
            public static readonly Color Border = new Color(0.10f, 0.10f, 0.11f, 1f);
            public static readonly Color FocusBorder = new Color(0.12f, 0.47f, 0.78f, 1f);
            public static readonly Color Dropdown = new Color(0.27f, 0.27f, 0.26f, 1f);
            public static readonly Color DropdownHover = new Color(0.30f, 0.30f, 0.29f, 1f);
            public static readonly Color DropdownArrowArea = new Color(0.24f, 0.24f, 0.23f, 1f);
            public static readonly Color DropdownBorder = new Color(0.15f, 0.15f, 0.14f, 1f);
            public static readonly Color MainText = new Color(0.78f, 0.78f, 0.76f, 1f);
            public static readonly Color MutedText = new Color(0.55f, 0.55f, 0.53f, 1f);
            public static readonly Color WarningText = new Color(1f, 0.66f, 0.24f, 1f);
            public static readonly Color KindText = new Color(0.42f, 0.83f, 0.96f, 1f);
            public static readonly Color Button = new Color(0.24f, 0.24f, 0.24f, 1f);
            public static readonly Color ButtonHover = new Color(0.31f, 0.31f, 0.31f, 1f);
            public static readonly Color ButtonActive = new Color(0.17f, 0.17f, 0.18f, 1f);
            public static readonly Color ButtonText = new Color(0.84f, 0.84f, 0.84f, 1f);
        }

        public static class Styles
        {
            private static GUIStyle _panel;
            private static GUIStyle _entryBody;
            private static GUIStyle _fieldRow;
            private static GUIStyle _toolbarButton;
            private static GUIStyle _inlineButton;
            private static GUIStyle _dropdownInput;
            private static GUIStyle _dropdownLabel;
            private static GUIStyle _searchTextField;
            private static GUIStyle _searchPlaceholder;
            private static GUIStyle _rowLabel;
            private static GUIStyle _mutedLabel;
            private static GUIStyle _fieldLabel;
            private static GUIStyle _mutedMiniLabel;
            private static GUIStyle _warningLabel;
            private static GUIStyle _glyph;
            private static GUIStyle _buttonGlyph;
            private static GUIStyle _kindBadge;
            private static GUIStyle _emptyState;
            private static GUIStyle _pillOn;
            private static GUIStyle _pillOff;

            public static GUIStyle Panel => _panel ??= new GUIStyle(EditorStyles.helpBox)
            {
                normal = { background = CreateTexture(Colors.Panel) },
                padding = new RectOffset(4, 4, 4, 4),
                margin = new RectOffset(0, 0, 2, 2)
            };

            public static GUIStyle EntryBody => _entryBody ??= new GUIStyle(EditorStyles.helpBox)
            {
                normal = { background = CreateTexture(Colors.Body) },
                padding = new RectOffset(8, 8, 6, 8),
                margin = new RectOffset(0, 0, 0, 4)
            };

            public static GUIStyle FieldRow => _fieldRow ??= new GUIStyle(EditorStyles.helpBox)
            {
                normal = { background = CreateTexture(Colors.FieldRow) },
                padding = new RectOffset(5, 5, 3, 3),
                margin = new RectOffset(0, 0, 1, 1)
            };

            public static GUIStyle ToolbarButton => _toolbarButton ??= new GUIStyle(EditorStyles.toolbarButton)
            {
                normal = { background = CreateTexture(new Color(0.18f, 0.18f, 0.19f, 1f)), textColor = Colors.MainText },
                hover = { background = CreateTexture(new Color(0.25f, 0.25f, 0.26f, 1f)), textColor = Color.white },
                active = { background = CreateTexture(new Color(0.13f, 0.13f, 0.14f, 1f)), textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = DefaultControlHeight,
                padding = new RectOffset(1, 1, 1, 1),
                margin = new RectOffset(0, 0, 0, 0)
            };

            public static GUIStyle InlineButton => _inlineButton ??= new GUIStyle(EditorStyles.toolbarButton)
            {
                normal = { background = CreateTexture(Colors.Button), textColor = Colors.MainText },
                hover = { background = CreateTexture(Colors.ButtonHover), textColor = Color.white },
                active = { background = CreateTexture(Colors.ButtonActive), textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = DefaultControlHeight,
                padding = new RectOffset(6, 6, 1, 1),
                margin = new RectOffset(0, 0, 0, 0)
            };

            public static GUIStyle DropdownInput => _dropdownInput ??= new GUIStyle(GUIStyle.none)
            {
                fixedHeight = DefaultControlHeight
            };

            public static GUIStyle DropdownLabel => _dropdownLabel ??= new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = Colors.MainText },
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fixedHeight = DefaultControlHeight,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };

            public static GUIStyle SearchTextField => _searchTextField ??= new GUIStyle(EditorStyles.textField)
            {
                normal = { background = CreateTexture(new Color(0.11f, 0.11f, 0.12f, 1f)), textColor = Colors.MainText },
                focused = { background = CreateTexture(new Color(0.12f, 0.12f, 0.13f, 1f)), textColor = Colors.MainText },
                hover = { background = CreateTexture(new Color(0.12f, 0.12f, 0.13f, 1f)), textColor = Colors.MainText },
                padding = new RectOffset(8, 24, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
                fixedHeight = 22f
            };

            public static GUIStyle SearchPlaceholder => _searchPlaceholder ??= new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = Colors.MutedText },
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            public static GUIStyle RowLabel => _rowLabel ??= new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = Colors.MainText },
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            public static GUIStyle MutedLabel => _mutedLabel ??= new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = Colors.MutedText },
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            public static GUIStyle FieldLabel => _fieldLabel ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Colors.MutedText },
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            public static GUIStyle MutedMiniLabel => _mutedMiniLabel ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Colors.MutedText },
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            public static GUIStyle WarningLabel => _warningLabel ??= new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = Colors.WarningText },
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            public static GUIStyle Glyph => _glyph ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Colors.MutedText },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };

            public static GUIStyle ButtonGlyph => _buttonGlyph ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Colors.ButtonText },
                hover = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };

            public static GUIStyle KindBadge => _kindBadge ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = Colors.KindText },
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };

            public static GUIStyle EmptyState => _emptyState ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                normal = { textColor = Colors.MutedText },
                alignment = TextAnchor.MiddleCenter
            };

            public static GUIStyle PillOn => _pillOn ??= new GUIStyle(EditorStyles.miniButton)
            {
                normal = { background = CreateTexture(new Color(0.20f, 0.36f, 0.53f, 1f)), textColor = Color.white },
                hover = { background = CreateTexture(new Color(0.24f, 0.42f, 0.62f, 1f)), textColor = Color.white },
                fontStyle = FontStyle.Bold,
                fixedHeight = 18f,
                margin = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(2, 2, 1, 1)
            };

            public static GUIStyle PillOff => _pillOff ??= new GUIStyle(EditorStyles.miniButton)
            {
                normal = { background = CreateTexture(new Color(0.30f, 0.30f, 0.31f, 1f)), textColor = Colors.MainText },
                hover = { background = CreateTexture(new Color(0.36f, 0.36f, 0.37f, 1f)), textColor = Color.white },
                fixedHeight = 18f,
                margin = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(2, 2, 1, 1)
            };
        }

        public static Texture2D CreateTexture(Color color)
        {
            var texture = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        public static void DrawOutline(Rect rect)
        {
            DrawOutline(rect, Colors.Border);
        }

        public static void DrawOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        public static void DrawToolbarBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, Colors.Toolbar);
            DrawOutline(rect);
        }

        public static void DrawBodyBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, Colors.Body);
            DrawOutline(rect);
        }

        public static void DrawListItemBackground(Rect rect, bool expanded, bool hovered)
        {
            EditorGUI.DrawRect(rect, expanded ? Colors.RowExpanded : hovered ? Colors.RowHover : Colors.Row);
            DrawOutline(rect);
        }

        public static void DrawFoldoutIcon(Rect rect, bool expanded)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            EditorStyles.foldout.Draw(rect, false, false, expanded, false);
        }

        public static void DrawPopupBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, Colors.PopupBackground);
        }

        public static void DrawPopupRowBackground(Rect rect, bool hovered)
        {
            EditorGUI.DrawRect(rect, hovered ? Colors.PopupRowHover : Colors.PopupRow);
            DrawOutline(rect);
        }

        public static void DrawPopupHeaderRowBackground(Rect rect, bool hovered)
        {
            EditorGUI.DrawRect(rect, hovered ? Colors.PopupRowHover : Colors.PopupHeader);
            DrawOutline(rect);
        }

        public static bool DrawToolbarButton(Rect rect, GUIContent content)
        {
            return GUI.Button(rect, content, Styles.ToolbarButton);
        }

        public static bool DrawInlineButton(string label, float width)
        {
            if (string.IsNullOrEmpty(label))
            {
                return false;
            }

            GUILayout.Space(6f);
            return GUILayout.Button(label, Styles.InlineButton, GUILayout.Width(width));
        }

        public static bool DrawSymbolButton(Rect rect, string symbol)
        {
            Event currentEvent = Event.current;
            bool hovered = rect.Contains(currentEvent.mousePosition);

            if (currentEvent.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, hovered ? Colors.ButtonHover : Colors.Button);
                DrawOutline(rect);
                GUI.Label(rect, symbol, Styles.ButtonGlyph);
            }

            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 || !hovered)
            {
                return false;
            }

            GUI.FocusControl(string.Empty);
            currentEvent.Use();
            return true;
        }

        public static int DrawStyledPopup(Rect rect, int selectedIndex, string[] options)
        {
            string[] safeOptions = options ?? Array.Empty<string>();
            int normalizedIndex = safeOptions.Length == 0 ? -1 : Mathf.Clamp(selectedIndex, 0, safeOptions.Length - 1);
            int nextIndex = safeOptions.Length == 0
                ? normalizedIndex
                : EditorGUI.Popup(rect, normalizedIndex, safeOptions, Styles.DropdownInput);

            bool hovered = rect.Contains(Event.current.mousePosition);
            Rect arrowRect = new Rect(rect.xMax - DropdownArrowWidth, rect.y, DropdownArrowWidth, rect.height);
            EditorGUI.DrawRect(rect, hovered ? Colors.DropdownHover : Colors.Dropdown);
            EditorGUI.DrawRect(arrowRect, Colors.DropdownArrowArea);
            DrawOutline(rect, Colors.DropdownBorder);
            EditorGUI.DrawRect(new Rect(arrowRect.x, arrowRect.y + 1f, 1f, arrowRect.height - 2f), Colors.DropdownBorder);

            string label = string.Empty;
            if (safeOptions.Length > 0 && nextIndex >= 0 && nextIndex < safeOptions.Length)
            {
                label = safeOptions[nextIndex];
            }

            GUI.Label(new Rect(rect.x + 7f, rect.y + 1f, rect.width - 32f, rect.height - 2f), label, Styles.DropdownLabel);
            DrawDropdownArrow(arrowRect);
            return nextIndex;
        }

        public static string DrawSearchField(Rect rect, string value, string placeholder, string controlName, ref bool focus)
        {
            GUI.SetNextControlName(controlName);
            string nextValue = GUI.TextField(rect, value, Styles.SearchTextField);

            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            if (string.IsNullOrEmpty(nextValue) && !focused)
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y + 1f, rect.width - 38f, rect.height), placeholder, Styles.SearchPlaceholder);
            }

            DrawOutline(rect, focused ? Colors.FocusBorder : Colors.Border);

            if (!string.IsNullOrEmpty(nextValue))
            {
                Rect clearRect = new Rect(rect.xMax - 22f, rect.y + 1f, 20f, rect.height - 2f);
                if (GUI.Button(clearRect, "x", Styles.ButtonGlyph))
                {
                    nextValue = string.Empty;
                    EditorGUI.FocusTextInControl(controlName);
                }
            }

            if (focus)
            {
                EditorGUI.FocusTextInControl(controlName);
                focus = false;
            }

            return nextValue;
        }

        public static void DrawDropdownArrow(Rect rect)
        {
            float centerX = rect.x + rect.width * 0.5f;
            float centerY = rect.y + rect.height * 0.5f + 1f;
            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = Colors.MutedText;
            Handles.DrawAAConvexPolygon(
                new Vector3(centerX - 4f, centerY - 2f, 0f),
                new Vector3(centerX + 4f, centerY - 2f, 0f),
                new Vector3(centerX, centerY + 3f, 0f));
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }
}
