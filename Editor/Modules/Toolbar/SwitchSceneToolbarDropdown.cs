using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Scene = UnityEngine.SceneManagement.Scene;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#else
using UnityEngine.UIElements;
#endif

namespace AlicizaX.Editor.Toolbar
{
    [InitializeOnLoad]
    public static class SwitchSceneToolbarDropdown
    {
        private const string ElementPath = "AlicizaX/SwitchScene";
        private const string Tooltip = "切换当前场景";
        private static readonly string[] SearchRoots = { "Assets/Bundles/", "Assets/Scenes/" };
        private static readonly Texture2D SceneIcon = EditorGUIUtility.IconContent("UnityLogo").image as Texture2D;

        private readonly struct SceneEntry
        {
            public readonly string Path;
            public readonly string Label;

            public SceneEntry(string path, string label)
            {
                Path = path;
                Label = label;
            }
        }

        static SwitchSceneToolbarDropdown()
        {
#if UNITY_6000_3_OR_NEWER
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
#else
            LegacyToolbarExtender.RegisterLeft(ElementPath, 0, CreateLegacyDropdown);
#endif
        }

#if UNITY_6000_3_OR_NEWER
        [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Left, defaultDockIndex = 1)]
        public static MainToolbarElement CreateElement() => new MainToolbarDropdown(
            new MainToolbarContent(GetLabel(), SceneIcon, Tooltip), rect => CreateMenu().DropDown(rect));

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => MainToolbar.Refresh(ElementPath);
        private static void OnActiveSceneChanged(Scene previous, Scene next) => MainToolbar.Refresh(ElementPath);
#else
        private static VisualElement CreateLegacyDropdown() => new IMGUIContainer(() =>
        {
            GUILayout.BeginHorizontal();
            if (EditorGUILayout.DropdownButton(new GUIContent(GetLabel(), SceneIcon, Tooltip),
                    FocusType.Passive, EditorStyles.toolbarPopup, GUILayout.MaxWidth(150)))
                CreateMenu().ShowAsContext();
            GUILayout.EndHorizontal();
        });
#endif

        private static GenericMenu CreateMenu()
        {
            var menu = new GenericMenu { allowDuplicateNames = true };
            var scenes = CollectScenes();
            if (scenes.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No scenes found"));
                return menu;
            }

            string activePath = EditorSceneManager.GetActiveScene().path;
            foreach (var scene in scenes)
            {
                var selected = scene;
                menu.AddItem(new GUIContent(selected.Label),
                    string.Equals(selected.Path, activePath, StringComparison.OrdinalIgnoreCase),
                    () => SwitchScene(selected.Path));
            }
            return menu;
        }

        private static List<SceneEntry> CollectScenes()
        {
            var scenes = new List<SceneEntry>();
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", SearchRoots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                scenes.Add(new SceneEntry(path, BuildLabel(path)));
            }
            scenes.Sort((left, right) => string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase));
            return scenes;
        }

        private static string BuildLabel(string path)
        {
            string sceneName = Path.GetFileNameWithoutExtension(path);
            foreach (string root in SearchRoots)
            {
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                string directory = Path.GetDirectoryName(path.Substring(root.Length))?.Replace('\\', '/');
                return string.IsNullOrEmpty(directory) ? sceneName : $"{directory}/{sceneName}";
            }
            return sceneName;
        }

        private static void SwitchScene(string path)
        {
            var current = EditorSceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty)
            {
                int choice = EditorUtility.DisplayDialogComplex("Warning",
                    $"Current scene '{current.name}' has unsaved changes. Save before switching?",
                    "Save", "Cancel", "Don't Save");
                if (choice == 1) return;
                if (choice == 0 && !EditorSceneManager.SaveOpenScenes()) return;
            }
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static string GetLabel()
        {
            string name = EditorSceneManager.GetActiveScene().name;
            return string.IsNullOrEmpty(name) ? "Switch Scene" : name;
        }
    }
}
