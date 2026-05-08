using UnityEditor;

namespace AlicizaX
{
    [CustomEditor(typeof(PoolConfigScriptableObject))]
    public sealed class PoolConfigScriptableObjectEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
        }
    }
}
