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
            // 内容加在覆盖层之后，这里把覆盖层移回最上层，首帧就盖在内容上面。
            AlicizaEditorText.Attach(root);
            return root;
        }

        protected abstract void BuildInspector(VisualElement root);

        protected SerializedProperty Find(string propertyPath)
        {
            return serializedObject.FindProperty(propertyPath);
        }
    }
}
