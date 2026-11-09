using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Reflection;
using AlicizaX.UI.Editor;
using AlicizaX.UI.Runtime;

[CustomEditor(typeof(UIHolderObjectBase), true)]
public class UIHolderObjectBaseEditor : Editor
{
    private const string TransitionPropertyName = "_defaultTransition";

    private SerializedProperty[] serializedProperties;
    private readonly Dictionary<string, ReorderableList> reorderableDic = new Dictionary<string, ReorderableList>();
    private SerializedProperty transitionProperty;

    private void OnEnable()
    {
        // 结构变化时由 UIHolderTransitionBinder 维护；打开 Inspector 时补一次，修正在别处改动后过期的引用
        foreach (Object holder in targets) UIHolderTransitionBinder.Refresh((UIHolderObjectBase)holder);

        serializedObject.Update();
        transitionProperty = serializedObject.FindProperty(TransitionPropertyName);

        var fields = target.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        serializedProperties = new SerializedProperty[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            SerializedProperty prop = serializedObject.FindProperty(fields[i].Name);
            if (prop == null) continue;
            serializedProperties[i] = prop;
            if (!prop.isArray) continue;

            var fieldType = fields[i].FieldType;
            var arrayElementType = fieldType.IsArray ? fieldType.GetElementType() : null;
            string arrayElementTypeName = arrayElementType?.Name ?? "Element";
            var reorderableList = new ReorderableList(serializedObject, prop, true, true, true, true);
            reorderableList.drawElementCallback = (rect, index, isActive, isFocused) => DrawElementCallback(rect, index, prop);
            reorderableList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, $"{arrayElementTypeName}[]");
            reorderableDic.Add(prop.propertyPath, reorderableList);
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUI.BeginDisabledGroup(true);
        if (transitionProperty != null)
            EditorGUILayout.PropertyField(transitionProperty, new GUIContent("Transition"));

        for (int i = 0; i < serializedProperties.Length; i++)
        {
            var property = serializedProperties[i];
            if (property == null) continue;
            if (property.isArray && reorderableDic.TryGetValue(property.propertyPath, out var reorderableList))
                reorderableList.DoLayoutList();
            else
                EditorGUILayout.PropertyField(property, GUIContent.none, true);
        }
        EditorGUI.EndDisabledGroup();
        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawElementCallback(Rect rect, int index, SerializedProperty arrayProperty)
    {
        var element = arrayProperty.GetArrayElementAtIndex(index);
        EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), element, GUIContent.none);
    }
}
