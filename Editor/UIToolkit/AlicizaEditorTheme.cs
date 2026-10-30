using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// Loads the shared AlicizaX UI Toolkit style sheet and applies the editor skin class to a root element.
    /// </summary>
    public static class AlicizaEditorTheme
    {
        private const string StyleSheetRelativePath = "Editor/UIToolkit/Styles/AlicizaEditor.uss";
        private const string FallbackPackagePath = "Packages/com.alicizax.unity.framework";

        private static StyleSheet _styleSheet;
        private static bool _loadAttempted;
        private static bool _missingLogged;

        /// <summary>
        /// Gets the shared editor style sheet. Returns null when the asset has not been imported yet.
        /// </summary>
        public static StyleSheet StyleSheet
        {
            get
            {
                if (!_loadAttempted)
                {
                    _loadAttempted = true;
                    _styleSheet = LoadStyleSheet();
                }

                return _styleSheet;
            }
        }

        /// <summary>
        /// Adds the shared style sheet and the current editor skin class to <paramref name="root"/>.
        /// </summary>
        public static void Apply(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            root.AddToClassList("alicia-inspector");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "alicia-theme--dark" : "alicia-theme--light");

            StyleSheet styleSheet = StyleSheet;
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        private static StyleSheet LoadStyleSheet()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AlicizaEditorTheme).Assembly);
            string packagePath = packageInfo != null && !string.IsNullOrEmpty(packageInfo.assetPath)
                ? packageInfo.assetPath
                : FallbackPackagePath;
            string assetPath = packagePath + "/" + StyleSheetRelativePath;

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(assetPath);
            if (styleSheet == null && !_missingLogged)
            {
                _missingLogged = true;
                Debug.LogWarning(
                    $"[AlicizaX] UI Toolkit style sheet not found at '{assetPath}'. The inspector will use the default editor theme.");
            }

            return styleSheet;
        }
    }
}
