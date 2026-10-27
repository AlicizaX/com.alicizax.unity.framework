using System;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#else
using UnityEngine.UIElements;
#endif

namespace AlicizaX.Editor.Toolbar
{
    [InitializeOnLoad]
    public static class EditorQuickToolbarDropdown
    {
        private const string ElementPath = "AlicizaX/EditorQuickTools";
        private const string Tooltip = "编辑器快捷工具";
        private static readonly Texture2D ToolIcon = EditorGUIUtility.IconContent("CustomTool").image as Texture2D
                                                     ?? EditorGUIUtility.IconContent("Settings").image as Texture2D;

        static EditorQuickToolbarDropdown()
        {
#if !UNITY_6000_3_OR_NEWER
            LegacyToolbarExtender.RegisterRight(ElementPath, 2, CreateLegacyDropdown);
#endif
        }

#if UNITY_6000_3_OR_NEWER
        [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Right, defaultDockIndex = 1)]
        public static MainToolbarElement CreateElement() => new MainToolbarDropdown(
            new MainToolbarContent("Tools", ToolIcon, Tooltip), rect => CreateMenu().DropDown(rect));
#else
        private static VisualElement CreateLegacyDropdown() => new IMGUIContainer(() =>
        {
            GUILayout.BeginHorizontal();
            if (EditorGUILayout.DropdownButton(new GUIContent("Tools", ToolIcon, Tooltip),
                    FocusType.Passive, EditorStyles.toolbarPopup, GUILayout.MaxWidth(90)))
                CreateMenu().ShowAsContext();
            GUILayout.Space(5);
            GUILayout.EndHorizontal();
        });
#endif

        private static GenericMenu CreateMenu()
        {
            var menu = new GenericMenu();
            if (EditorToolFunctionAttributeCollector.Attributes.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No tools found"));
                return menu;
            }

            foreach (var tool in EditorToolFunctionAttributeCollector.Attributes)
            {
                var selected = tool;
                menu.AddItem(new GUIContent(selected.ToolMenuPath), false, () => InvokeTool(selected));
            }
            return menu;
        }

        private static void InvokeTool(EditorToolFunctionAttribute tool)
        {
            if (tool.MethodInfo == null || !tool.MethodInfo.IsStatic ||
                tool.MethodInfo.GetParameters().Length != 0)
            {
                Debug.LogError("Editor tool method must be static and parameterless.");
                return;
            }

            try
            {
                tool.MethodInfo.Invoke(null, null);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
    }
}
