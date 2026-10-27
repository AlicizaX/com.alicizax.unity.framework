#if !UNITY_6000_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.Toolbar
{
    [InitializeOnLoad]
    public static class LegacyToolbarExtender
    {
        private const string ToolbarRootFieldName = "m_Root";
        private const string CenterContainerName = "ToolbarZonePlayMode";
        private const string LeftContainerName = "ToolbarZoneLeftAlign";
        private const string RightContainerName = "ToolbarZoneRightAlign";
        private const string LeftExtensionName = "OldToolbarMinimalExtenderLeft";
        private const string RightExtensionName = "OldToolbarMinimalExtenderRight";

        private static readonly Type ToolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
        private static readonly SortedDictionary<(int Order, string Name), Func<VisualElement>> LeftExtensions = new();
        private static readonly SortedDictionary<(int Order, string Name), Func<VisualElement>> RightExtensions = new();
        private static ScriptableObject toolbarObject;
        private static VisualElement toolbarRoot;

        static LegacyToolbarExtender()
        {
            EditorApplication.update += TryInstall;
        }

        public static void RegisterLeft(string name, int order, Func<VisualElement> create)
        {
            LeftExtensions[(order, name)] = create;
            if (toolbarRoot != null) Install(toolbarRoot);
        }

        public static void RegisterRight(string name, int order, Func<VisualElement> create)
        {
            RightExtensions[(order, name)] = create;
            if (toolbarRoot != null) Install(toolbarRoot);
        }

        private static void TryInstall()
        {
            if (ToolbarType == null)
            {
                EditorApplication.update -= TryInstall;
                return;
            }

            var currentToolbar = Resources.FindObjectsOfTypeAll(ToolbarType);
            if (currentToolbar.Length == 0) return;
            var currentObject = (ScriptableObject)currentToolbar[0];
            var rootField = ToolbarType.GetField(ToolbarRootFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            var currentRoot = rootField?.GetValue(currentObject) as VisualElement;
            if (currentRoot == null) return;

            if (toolbarObject == currentObject && toolbarRoot == currentRoot &&
                currentRoot.Q(LeftExtensionName) != null && currentRoot.Q(RightExtensionName) != null)
            {
                EditorApplication.update -= TryInstall;
                return;
            }

            toolbarObject = currentObject;
            toolbarRoot = currentRoot;
            Install(currentRoot);
        }

        private static void Install(VisualElement root)
        {
            root.Q(LeftExtensionName)?.RemoveFromHierarchy();
            root.Q(RightExtensionName)?.RemoveFromHierarchy();

            var centerContainer = root.Q(CenterContainerName);
            if (centerContainer == null) return;
            ConfigureToolbarContainers(root, centerContainer);

            var leftExtension = CreateExtensionContainer(LeftExtensionName, FlexDirection.RowReverse);
            var rightExtension = CreateExtensionContainer(RightExtensionName, FlexDirection.Row);
            foreach (var create in LeftExtensions.Values) leftExtension.Add(create());
            foreach (var create in RightExtensions.Values) rightExtension.Add(create());
            centerContainer.Insert(0, leftExtension);
            centerContainer.Add(rightExtension);
        }

        private static VisualElement CreateExtensionContainer(string name, FlexDirection flexDirection)
        {
            var container = new VisualElement { name = name };
            container.style.flexDirection = flexDirection;
            container.style.flexGrow = 1;
            container.style.width = 0;
            container.style.alignItems = Align.Center;
            container.style.paddingLeft = 5;
            container.style.paddingRight = 5;
            return container;
        }

        private static void ConfigureToolbarContainers(VisualElement root, VisualElement centerContainer)
        {
            var leftContainer = root.Q(LeftContainerName);
            if (leftContainer != null)
            {
                leftContainer.style.flexGrow = 0;
                leftContainer.style.width = Length.Auto();
            }

            var rightContainer = root.Q(RightContainerName);
            if (rightContainer != null)
            {
                rightContainer.style.flexGrow = 0;
                rightContainer.style.width = Length.Auto();
            }

            centerContainer.style.flexGrow = 1;
            if (centerContainer.parent == null) return;
            centerContainer.parent.style.paddingTop = 0;
            centerContainer.parent.style.paddingBottom = 0;
        }
    }
}
#endif
