using System;
using System.Collections.Generic;
using System.Linq;

namespace AlicizaX
{
    [UnityEngine.Scripting.Preserve]
    public static class EnumerableExtensions
    {
        /// <summary>
        /// 根据指定键进行去重。
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            var identifiedKeys = new HashSet<TKey>();
            foreach (var item in source)
            {
                if (identifiedKeys.Add(keySelector(item)))
                {
                    yield return item;
                }
            }
        }

        /// <summary>
        /// 判断集合是否包含另一个集合中的所有元素。
        /// </summary>
        public static bool ContainsAll<T>(this IEnumerable<T> source, IEnumerable<T> values)
        {
            return !source.Except(values).Any();
        }
    }
}
