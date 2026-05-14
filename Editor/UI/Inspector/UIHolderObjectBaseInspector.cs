using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Reflection;
using AlicizaX.UI.Runtime;

[CustomEditor(typeof(UIHolderObjectBase), true)]
public class UIHolderObjectBaseEditor : Editor
{
    private const string TransitionPlayerComponentPropertyName = "_transitionPlayerComponent";

    private SerializedProperty[] serializedProperties;
    private Dictionary<string, ReorderableList> reorderableDic = new Dictionary<string, ReorderableList>();
    private SerializedProperty transitionPlayerComponentProperty;

    private void OnEnable()
    {
        transitionPlayerComponentProperty = serializedObject.FindProperty(TransitionPlayerComponentPropertyName);

        var fields = target.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        serializedProperties = new SerializedProperty[fields.Length];

        for (int i = 0; i < fields.Length; i++)
        {
            SerializedProperty prop = serializedObject.FindProperty(fields[i].Name);
            if (prop != null)
            {
                if (prop.propertyPath == TransitionPlayerComponentPropertyName)
                {
                    continue;
                }

                serializedProperties[i] = prop;


                if (prop.isArray)
                {
                    var fieldType = fields[i].FieldType;
                    var arrayElementType = fieldType.IsArray ? fieldType.GetElementType() : null;
                    string arrayElementTypeName = arrayElementType?.Name ?? "Element";

                    ReorderableList reorderableList = new ReorderableList(serializedObject, prop, true, true, true, true);
                    reorderableList.drawElementCallback = (rect, index, isActive, isFocused) => DrawElementCallback(rect, index, prop, isActive, isFocused);
                    reorderableList.drawHeaderCallback = (rect) => DrawHeaderCallback(rect, arrayElementTypeName);

                    reorderableDic.Add(prop.propertyPath, reorderableList);
                }
            }
        }

        serializedObject.Update();
        RefreshTransitionPlayerCache(true);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        RefreshTransitionPlayerCache(false);

        if (transitionPlayerComponentProperty != null)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(transitionPlayerComponentProperty, new GUIContent("Transition Player"));
            EditorGUI.EndDisabledGroup();
        }

        EditorGUI.BeginDisabledGroup(true);

        for (int i = 0; i < serializedProperties.Length; i++)
        {
            var property = serializedProperties[i];
            if (property != null)
            {
                if (property.isArray && reorderableDic.TryGetValue(property.propertyPath, out var reorderableList))
                {
                    reorderableList.DoLayoutList();
                }
                else
                {
                    EditorGUILayout.PropertyField(property, GUIContent.none, true);
                }
            }
        }

        EditorGUI.EndDisabledGroup();
        serializedObject.ApplyModifiedProperties();
    }

    private void RefreshTransitionPlayerCache(bool applyImmediately)
    {
        UIHolderObjectBase holder = target as UIHolderObjectBase;
        if (Application.isPlaying || transitionPlayerComponentProperty == null || holder == null)
        {
            return;
        }

        Component transitionPlayer = holder.FindTransitionPlayerInEditor();
        if (transitionPlayerComponentProperty.objectReferenceValue == transitionPlayer)
        {
            return;
        }

        transitionPlayerComponentProperty.objectReferenceValue = transitionPlayer;

        if (applyImmediately)
        {
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private void DrawElementCallback(Rect rect, int index, SerializedProperty arrayProperty, bool isActive, bool isFocused)
    {
        var element = arrayProperty.GetArrayElementAtIndex(index);
        EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), element, GUIContent.none);
    }


    private void DrawHeaderCallback(Rect rect, string arrayElementTypeName)
    {
        string arrayTypeName = $"{arrayElementTypeName}[]";
        EditorGUI.LabelField(rect, arrayTypeName);
    }
}
