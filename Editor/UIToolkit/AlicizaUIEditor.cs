using UnityEditor;
using UnityEngine.UIElements;

namespace AlicizaX.Editor.UIToolkit
{
    /// <summary>
    /// Base class for UI Toolkit inspectors built with the AlicizaX toolkit.
    /// Subclasses only implement <see cref="BuildInspector"/>; theming and the Editor lifecycle are handled here.
    /// </summary>
    public abstract class AlicizaUIEditor : UnityEditor.Editor
    {
        /// <inheritdoc />
        public sealed override VisualElement CreateInspectorGUI()
        {
            VisualElement root = AlicizaEditorUI.CreateInspectorRoot();
            serializedObject.Update();
            BuildInspector(root);
            return root;
        }

        /// <summary>
        /// Builds the inspector content. The returned root is bound to <see cref="Editor.serializedObject"/> after this call.
        /// </summary>
        protected abstract void BuildInspector(VisualElement root);

        /// <summary>
        /// Finds a serialized property on the inspected object.
        /// </summary>
        protected SerializedProperty Find(string propertyPath)
        {
            return serializedObject.FindProperty(propertyPath);
        }
    }
}
