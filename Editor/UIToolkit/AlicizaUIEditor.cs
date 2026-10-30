using UnityEditor;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{

    public abstract class AlicizaUIEditor : UnityEditor.Editor
    {

        public sealed override VisualElement CreateInspectorGUI()
        {
            VisualElement root = AlicizaEditorUI.CreateInspectorRoot();
            serializedObject.Update();
            BuildInspector(root);
            return root;
        }

        protected abstract void BuildInspector(VisualElement root);

        protected SerializedProperty Find(string propertyPath)
        {
            return serializedObject.FindProperty(propertyPath);
        }
    }
}
