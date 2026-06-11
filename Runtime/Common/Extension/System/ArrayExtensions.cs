using UnityEngine;

[UnityEngine.Scripting.Preserve]
public static class ArrayExtensions
{
    /// <summary>
    /// 从数组中随机取一个元素。
    /// </summary>
    public static T Random<T>(this T[] items)
    {
        System.Random rnd = new System.Random();
        if (items.Length > 0)
        {
            return items[rnd.Next(0, items.Length)];
        }

        return default;
    }

    /// <summary>
    /// 获取与目标值最接近的元素索引。
    /// </summary>
    public static int ClosestIndex(this int[] array, int value)
    {
        int closestIndex = 0;
        int minDifference = Mathf.Abs(array[0] - value);
        for (int i = 1; i < array.Length; i++)
        {
            int difference = Mathf.Abs(array[i] - value);
            if (difference < minDifference)
            {
                minDifference = difference;
                closestIndex = i;
            }
        }

        return closestIndex;
    }
}

namespace AlicizaX
{
    [UnityEngine.Scripting.Preserve]
    public static class MinMaxExtensions
    {
        public static float Random(this MinMax minMax)
        {
            return UnityEngine.Random.Range(minMax.RealMin, minMax.RealMax);
        }

        public static int Random(this MinMaxInt minMax)
        {
            return UnityEngine.Random.Range(minMax.RealMin, minMax.RealMax);
        }
    }
}
