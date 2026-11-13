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
#if UNITY_6000_5_OR_NEWER
                        RefreshOwnerOf(structure.entityId);
#else
                        RefreshOwnerOf(structure.instanceId);
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out ChangeGameObjectStructureHierarchyEventArgs hierarchy);
#if UNITY_6000_5_OR_NEWER
                        RefreshOwnerOf(hierarchy.entityId);
#else
                        RefreshOwnerOf(hierarchy.instanceId);
#endif
                        break;
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        stream.GetCreateGameObjectHierarchyEvent(i, out CreateGameObjectHierarchyEventArgs created);
#if UNITY_6000_5_OR_NEWER
                        RefreshOwnerOf(created.entityId);
#else
                        RefreshOwnerOf(created.instanceId);
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out ChangeGameObjectParentEventArgs moved);
#if UNITY_6000_5_OR_NEWER
                        RefreshOwnerOf(moved.newParentEntityId);
                        RefreshOwnerOf(moved.previousParentEntityId);
#else
                        RefreshOwnerOf(moved.newParentInstanceId);
                        RefreshOwnerOf(moved.previousParentInstanceId);
#endif
                        break;
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        stream.GetDestroyGameObjectHierarchyEvent(i, out DestroyGameObjectHierarchyEventArgs destroyed);
#if UNITY_6000_5_OR_NEWER
                        RefreshOwnerOf(destroyed.parentEntityId);
#else
                        RefreshOwnerOf(destroyed.parentInstanceId);
#endif
                        break;
                    case ObjectChangeKind.ChangeChildrenOrder:
                        stream.GetChangeChildrenOrderEvent(i, out ChangeChildrenOrderEventArgs reordered);
#if UNITY_6000_5_OR_NEWER
                        RefreshOwnerOf(reordered.entityId);
#else
                        RefreshOwnerOf(reordered.instanceId);
#endif
                        break;
                }
            }
        }

        /// <summary>
        /// 受影响的是节点所属的 Holder（自身或最近的祖先）；节点自身新增了 Holder 时，
        /// 外层 Holder 的查找会在这里截断，也要重新解析。
        /// </summary>
#if UNITY_6000_5_OR_NEWER
        private static void RefreshOwnerOf(EntityId entityId)
        {
            RefreshOwner(EditorUtility.EntityIdToObject(entityId) as GameObject);
        }
#else
        private static void RefreshOwnerOf(int instanceId)
        {
            RefreshOwner(EditorUtility.InstanceIDToObject(instanceId) as GameObject);
        }
#endif

        private static void RefreshOwner(GameObject node)
        {
            if (node == null) return;
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
