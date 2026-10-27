using AlicizaX.Resource.Runtime;
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
    public static class ResourceModeToolbarDropdown
    {
        private const string ElementPath = "AlicizaX/ResourceMode";
        private const string Tooltip = "配置 App 运行时资源模式";
        private static readonly string[] ModeNames = { "None", "Editor", "Offline", "Host", "Webgl" };
        private static readonly Texture2D SettingsIcon = EditorGUIUtility.IconContent("Settings").image as Texture2D;
#if UNITY_6000_3_OR_NEWER
        private static int _lastModeIndex;
#endif

        static ResourceModeToolbarDropdown()
        {
#if UNITY_6000_3_OR_NEWER
            _lastModeIndex = GetSelectedModeIndex();
            EditorApplication.update += RefreshWhenModeChanges;
#else
            LegacyToolbarExtender.RegisterRight(ElementPath, 0, CreateLegacyDropdown);
#endif
        }

#if UNITY_6000_3_OR_NEWER
        [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 101)]
        public static MainToolbarElement CreateElement() => new MainToolbarDropdown(
            new MainToolbarContent(GetLabel(), SettingsIcon, Tooltip), rect => CreateMenu().DropDown(rect));

        private static void RefreshWhenModeChanges()
        {
            int selected = GetSelectedModeIndex();
            if (selected == _lastModeIndex) return;
            _lastModeIndex = selected;
            MainToolbar.Refresh(ElementPath);
        }
#else
        private static VisualElement CreateLegacyDropdown() => new IMGUIContainer(() =>
        {
            GUILayout.BeginHorizontal();
            if (EditorGUILayout.DropdownButton(new GUIContent(GetLabel(), SettingsIcon, Tooltip),
                    FocusType.Passive, EditorStyles.toolbarPopup, GUILayout.MaxWidth(100)))
                CreateMenu().ShowAsContext();
            GUILayout.Space(5);
            GUILayout.EndHorizontal();
        });
#endif

        private static GenericMenu CreateMenu()
        {
            var menu = new GenericMenu();
            int selected = GetSelectedModeIndex();
            for (int i = 0; i < ModeNames.Length; i++)
            {
                int index = i;
                menu.AddItem(new GUIContent(ModeNames[index]), selected == index, () => SetMode(index));
            }
            return menu;
        }

        private static void SetMode(int index)
        {
            EditorPrefs.SetInt(ResourceComponent.PrefsKey, index);
#if UNITY_6000_3_OR_NEWER
            _lastModeIndex = index;
            MainToolbar.Refresh(ElementPath);
#endif
        }

        private static int GetSelectedModeIndex() =>
            Mathf.Clamp(EditorPrefs.GetInt(ResourceComponent.PrefsKey, 0), 0, ModeNames.Length - 1);

        private static string GetLabel() => $"Res:{ModeNames[GetSelectedModeIndex()]}";
    }
}
