using UnityEditor;
using UnityEngine;

namespace AlicizaX
{
    [CustomEditor(typeof(PoolConfigScriptableObject))]
    public sealed class PoolConfigScriptableObjectEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("在编辑器窗口中打开", GUILayout.Height(28f)))
            {
                PoolConfigEditorWindow.OpenForAsset((PoolConfigScriptableObject)target);
            }

            EditorGUILayout.Space(4f);
            DrawDefaultInspector();
        }
    }
}
