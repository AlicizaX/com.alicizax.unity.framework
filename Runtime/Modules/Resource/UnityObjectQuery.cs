using System;
using Object = UnityEngine.Object;

namespace UnityEngine
{
    /// <summary>
    /// Unity 版本之间 <see cref="Object"/> 查找 API 的兼容层，调用点无需再写 #if。
    ///
    /// 版本事实（官方文档 / 反射校验）：
    /// - Unity 2022.2 起提供 <c>FindAnyObjectByType</c> / <c>FindObjectsByType</c>；
    ///   其中 <c>FindObjectsByType</c> 必须传入 <see cref="FindObjectsSortMode"/>。
    /// - Unity 6000.5 起 <c>FindObjectsByType(SortMode)</c> 重载被标记过时
    ///   （InstanceID 排序不再维护），改为提供不带排序参数的重载。
    /// - 更老的版本只有 <c>FindObjectOfType</c> / <c>FindObjectsOfType</c>。
    ///
    /// 语义差异：旧 API 按 InstanceID 排序返回，新 API 不保证顺序；这里的 FindAll 明确不承诺顺序，
    /// 需要稳定顺序的调用方应自行排序（与 Unity 的推荐一致）。
    /// </summary>
    public static class UnityObjectQuery
    {
        /// <summary>取任意一个匹配对象（旧版本语义是按 InstanceID 排序后的第一个）。</summary>
        public static T FindFirst<T>() where T : Object
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindAnyObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        /// <summary>取全部匹配对象（不含未激活对象），不保证顺序。</summary>
        public static T[] FindAll<T>() where T : Object
        {
#if UNITY_6000_5_OR_NEWER
            return Object.FindObjectsByType<T>();
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>();
#endif
        }

        /// <summary>取全部匹配对象（包含未激活对象），不保证顺序。</summary>
        public static T[] FindAllIncludingInactive<T>() where T : Object
        {
#if UNITY_6000_5_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>(true);
#endif
        }

        /// <summary>取任意一个匹配对象（旧版本语义是按 InstanceID 排序后的第一个）。</summary>
        public static Object FindFirst(Type type)
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindAnyObjectByType(type);
#else
            return Object.FindObjectOfType(type);
#endif
        }

        /// <summary>取全部匹配对象（不含未激活对象），不保证顺序。</summary>
        public static Object[] FindAll(Type type)
        {
#if UNITY_6000_5_OR_NEWER
            return Object.FindObjectsByType(type);
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType(type, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType(type);
#endif
        }

        /// <summary>取全部匹配对象（包含未激活对象），不保证顺序。</summary>
        public static Object[] FindAllIncludingInactive(Type type)
        {
#if UNITY_6000_5_OR_NEWER
            return Object.FindObjectsByType(type, FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType(type, true);
#endif
        }
    }
}
