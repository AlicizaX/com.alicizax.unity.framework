using System;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    [UnityEngine.Scripting.Preserve]
    public static class GameObjectExtensions
    {
        public static void SafeDestroySelf(
            this Object obj)
        {
            if (obj == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(obj);
            else
                Object.Destroy(obj);
#else
            Object.Destroy(obj);
#endif
        }
        /// <summary>
        /// 获取或增加组件。
        /// </summary>
        /// <typeparam name="T">要获取或增加的组件。</typeparam>
        /// <param name="gameObject">目标对象。</param>
        /// <returns>获取或增加的组件。</returns>
        public static void DestroyComponent<T>(this GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component != null)
            {
                Object.Destroy(component);
            }

            // return component;
        }

        /// <summary>
        /// 获取或增加组件。
        /// </summary>
        /// <typeparam name="T">要获取或增加的组件。</typeparam>
        /// <param name="gameObject">目标对象。</param>
        /// <returns>获取或增加的组件。</returns>
        public static T GetOrAddComponent<T>(this GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component == null)
            {
                component = gameObject.AddComponent<T>();
            }

            return component;
        }

        /// <summary>
        /// 获取或增加组件。
        /// </summary>
        /// <param name="gameObject">目标对象。</param>
        /// <param name="type">要获取或增加的组件类型。</param>
        /// <returns>获取或增加的组件。</returns>
        public static Component GetOrAddComponent(this GameObject gameObject, Type type)
        {
            Component component = gameObject.GetComponent(type);
            if (component == null)
            {
                component = gameObject.AddComponent(type);
            }

            return component;
        }

        /// <summary>
        /// 获取 GameObject 是否在场景中。
        /// </summary>
        /// <param name="gameObject">目标对象。</param>
        /// <returns>GameObject 是否在场景中。</returns>
        /// <remarks>若返回 true，表明此 GameObject 是一个场景中的实例对象；若返回 false，表明此 GameObject 是一个 Prefab。</remarks>
        public static bool InScene(this GameObject gameObject)
        {
            return gameObject.scene.name != null;
        }


        /// <summary>
        /// 设置对象下所有 MeshRenderer 的 rendering layer。
        /// </summary>
        public static void SetRenderingLayer(this GameObject gameObject, uint layer, bool set = true)
        {
            if (layer > 31)
            {
                Debug.LogError("Invalid layer value. Must be between 0 and 31.");
                return;
            }

            uint layerMask = 1u << (int)layer;
            foreach (MeshRenderer renderer in gameObject.GetComponentsInChildren<MeshRenderer>())
            {
                if (set)
                {
                    renderer.renderingLayerMask |= layerMask;
                }
                else
                {
                    renderer.renderingLayerMask &= ~layerMask;
                }
            }
        }

        [UnityEngine.Scripting.Preserve]
        public static void RemoveChildren(this GameObject gameObject)
        {
            for (var i = gameObject.transform.childCount - 1; i >= 0; i--)
            {
                gameObject.transform.GetChild(i).gameObject.DestroyObject();
            }
        }

        [UnityEngine.Scripting.Preserve]
        public static void DestroyObject(this GameObject gameObject)
        {
            gameObject.SafeDestroySelf();
        }

        [UnityEngine.Scripting.Preserve]
        public static void Destroy(this GameObject gameObject)
        {
            gameObject.DestroyObject();
        }

        [UnityEngine.Scripting.Preserve]
        public static GameObject FindChildGamObjectByName(string nodeName, string sceneName = null)
        {
            Scene scene;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                scene = SceneManager.GetActiveScene();
            }
            else
            {
                scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.isLoaded)
                {
                    return null;
                }
            }

            var rootObjects = scene.GetRootGameObjects();
            foreach (var rootObject in rootObjects)
            {
                var result = rootObject.FindChildGamObjectByName(nodeName);
                if (result.IsNotNull())
                {
                    return result;
                }
            }

            return null;
        }

        [UnityEngine.Scripting.Preserve]
        public static GameObject FindChildGamObjectByName(this GameObject gameObject, string name)
        {
            var transform = gameObject.transform.FindChildName(name);
            if (transform.IsNotNull())
            {
                return transform.gameObject;
            }

            return null;
        }

        [UnityEngine.Scripting.Preserve]
        public static GameObject Create(this Transform parent, string name)
        {
            Debug.Assert(!ReferenceEquals(parent, null), nameof(parent) + " == null");
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent);
            return gameObject;
        }

        [UnityEngine.Scripting.Preserve]
        public static GameObject Create(this GameObject parent, string name)
        {
            Debug.Assert(!ReferenceEquals(parent, null), nameof(parent) + " == null");
            return parent.transform.Create(name);
        }

        [UnityEngine.Scripting.Preserve]
        public static void ResetTransform(this GameObject gameObject)
        {
            gameObject.transform.localScale = Vector3.one;
            gameObject.transform.localPosition = Vector3.zero;
            gameObject.transform.localRotation = Quaternion.identity;
        }

        [UnityEngine.Scripting.Preserve]
        public static void SetSortingGroupLayer(this GameObject gameObject, string sortingLayer)
        {
            SortingGroup[] sortingGroups = gameObject.GetComponentsInChildren<SortingGroup>();
            foreach (SortingGroup sortingGroup in sortingGroups)
            {
                sortingGroup.sortingLayerName = sortingLayer;
            }
        }

        [UnityEngine.Scripting.Preserve]
        public static void SetLayer(this GameObject gameObject, int layer, bool children = true)
        {
            if (gameObject.layer != layer)
            {
                gameObject.layer = layer;
            }

            if (children)
            {
                Transform[] transforms = gameObject.GetComponentsInChildren<Transform>();
                foreach (var transform in transforms)
                {
                    transform.gameObject.layer = layer;
                }
            }
        }

    }
}
