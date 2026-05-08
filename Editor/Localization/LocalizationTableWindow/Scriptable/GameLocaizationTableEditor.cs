using AlicizaX.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace AlicizaX.Localization.Editor
{
    [CustomEditor(typeof(GameLocaizationTable))]
    internal class GameLocaizationTableEditor : InspectorEditor<GameLocaizationTable>
    {
        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)

        {
            var obj = EditorUtility.InstanceIDToObject(instanceId);
            var asset = obj as GameLocaizationTable;

            if (asset == null) return false;
            string path = AssetDatabase.GetAssetPath(asset);
            EditorPrefs.SetString("LastSelectedGameLocaizationTable", path);
            LocalizationTableWindow.OpenTableEditor();
            return true;
        }


        public override void OnEnable()
        {
            base.OnEnable();
        }

        public override void OnInspectorGUI()
        {
            EditorDrawing.DrawInspectorHeader(new GUIContent("Game Localization Table"), Target);
            EditorGUILayout.Space();
            serializedObject.Update();
            {
                EditorGUILayout.HelpBox("You can edit this language in the Game Localization Table Editor window.", MessageType.Info);
                EditorGUILayout.Space();

                using (new EditorDrawing.BorderBoxScope(new GUIContent("GenCode"), roundedBox: false))
                {
                    Properties.Draw("GenerateScriptCodeFolderPath", new GUIContent("Folder Path"));
                    Properties.Draw("GenerateScriptCodeClassName", new GUIContent("Class Name"));
                    Properties.Draw("GenerateScriptCodeNamespace", new GUIContent("Namespace"));
                }
            }
            serializedObject.ApplyModifiedProperties();

            using (new EditorDrawing.BorderBoxScope(new GUIContent("Languages"), roundedBox: false))
            {
                if (Target.Languages.Count > 0)
                {
                    using (new EditorGUI.DisabledGroupScope(true))
                    {
                        foreach (var lang in Target.Languages)
                        {
                            string name = lang != null ? lang.LanguageName.Or("Unknown") : "Missing";
                            EditorGUILayout.ObjectField(new GUIContent(name), lang, typeof(LocalizationLanguage), false);
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("There are currently no languages available, enable languages in Localization Settings.", MessageType.Info);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                {
                    if (GUILayout.Button("Open Localization Editor", GUILayout.Width(180f), GUILayout.Height(25)))
                    {
                        string path = AssetDatabase.GetAssetPath(target);
                        EditorPrefs.SetString("LastSelectedGameLocaizationTable", path);
                        LocalizationTableWindow.OpenTableEditor();
                    }

                    if (GUILayout.Button("Gen Code", GUILayout.Width(180f), GUILayout.Height(25)))
                    {
                        LocalizationWindowUtility.GenerateCode(Target);
                    }
                }
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();
        }


    }
}
