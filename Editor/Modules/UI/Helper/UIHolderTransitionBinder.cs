using AlicizaX.UI.Runtime;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.UI.Editor
{
    /// <summary>
    /// 编辑期维护 Holder 序列化的默认转场引用：节点结构变化（增删组件、增删、移动或重排子节点）时，
    /// 重新解析受影响的 Holder，经 SerializedObject 写入以正确记录脏标记与预制体覆盖。
    /// 结构变化事件只覆盖可撤销的编辑；脚本里添加 Holder 的工具需自行调用 <see cref="Refresh"/>。
    /// </summary>
    [InitializeOnLoad]
    internal static class UIHolderTransitionBinder
    {
        private const string TransitionPropertyName = "_defaultTransition";

        static UIHolderTransitionBinder() => ObjectChangeEvents.changesPublished += OnChangesPublished;

        /// <summary>重新解析 Holder 的转场：自身优先，然后深度优先查子节点，遇到嵌套 Holder 截断。</summary>
        internal static void Refresh(UIHolderObjectBase holder)
        {
            if (holder == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            using var serializedHolder = new SerializedObject(holder);
            SerializedProperty property = serializedHolder.FindProperty(TransitionPropertyName);
            Component found = FindTransition(holder.transform);
            if (property.objectReferenceValue == found) return;
            property.objectReferenceValue = found;
            serializedHolder.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out ChangeGameObjectStructureEventArgs structure);
                        RefreshOwnerOf(structure.instanceId);
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out ChangeGameObjectStructureHierarchyEventArgs hierarchy);
                        RefreshOwnerOf(hierarchy.instanceId);
                        break;
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        stream.GetCreateGameObjectHierarchyEvent(i, out CreateGameObjectHierarchyEventArgs created);
                        RefreshOwnerOf(created.instanceId);
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out ChangeGameObjectParentEventArgs moved);
                        RefreshOwnerOf(moved.newParentInstanceId);
                        RefreshOwnerOf(moved.previousParentInstanceId);
                        break;
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        stream.GetDestroyGameObjectHierarchyEvent(i, out DestroyGameObjectHierarchyEventArgs destroyed);
                        RefreshOwnerOf(destroyed.parentInstanceId);
                        break;
                    case ObjectChangeKind.ChangeChildrenOrder:
                        stream.GetChangeChildrenOrderEvent(i, out ChangeChildrenOrderEventArgs reordered);
                        RefreshOwnerOf(reordered.instanceId);
                        break;
                }
            }
        }

        /// <summary>
        /// 受影响的是节点所属的 Holder（自身或最近的祖先）；节点自身新增了 Holder 时，
        /// 外层 Holder 的查找会在这里截断，也要重新解析。
        /// </summary>
        private static void RefreshOwnerOf(int instanceId)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is not GameObject node) return;
            Refresh(node.GetComponentInParent<UIHolderObjectBase>(true));
            Transform parent = node.transform.parent;
            if (parent != null) Refresh(parent.GetComponentInParent<UIHolderObjectBase>(true));
        }

        private static Component FindTransition(Transform node)
        {
            if (node.TryGetComponent(out IUITransition transition)) return (Component)transition;
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                if (child.TryGetComponent<UIHolderObjectBase>(out _)) continue;
                Component found = FindTransition(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
