using System;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// 随机相关的实用函数。
        /// </summary>
        public static class Random
        {
            private static System.Random s_Random = new System.Random((int)DateTime.UtcNow.Ticks);

            /// <summary>
            /// 设置随机数种子。
            /// </summary>
            /// <param name="seed">随机数种子。</param>
            public static void SetSeed(int seed)
            {
                s_Random = new System.Random(seed);
            }

            /// <summary>
            /// 返回非负随机数。
            /// </summary>
            /// <returns>大于等于零且小于 System.Int32.MaxValue 的 32 位带符号整数。</returns>
            public static int GetRandom()
            {
                return s_Random.Next();
            }

            /// <summary>
            /// 返回一个小于所指定最大值的非负随机数。
            /// </summary>
            /// <param name="maxValue">要生成的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于零。</param>
            /// <returns>大于等于零且小于 maxValue 的 32 位带符号整数，即：返回值的范围通常包括零但不包括 maxValue。不过，如果 maxValue 等于零，则返回 maxValue。</returns>
            public static int GetRandom(int maxValue)
            {
                return s_Random.Next(maxValue);
            }

            /// <summary>
            /// 返回一个指定范围内的随机数。
            /// </summary>
            /// <param name="minValue">返回的随机数的下界（随机数可取该下界值）。</param>
            /// <param name="maxValue">返回的随机数的上界（随机数不能取该上界值）。maxValue 必须大于等于 minValue。</param>
            /// <returns>一个大于等于 minValue 且小于 maxValue 的 32 位带符号整数，即：返回的值范围包括 minValue 但不包括 maxValue。如果 minValue 等于 maxValue，则返回 minValue。</returns>
            public static int GetRandom(int minValue, int maxValue)
            {
                return s_Random.Next(minValue, maxValue);
            }

            /// <summary>
            /// 返回一个介于 0.0 和 1.0 之间的随机数。
            /// </summary>
            /// <returns>大于等于 0.0 并且小于 1.0 的双精度浮点数。</returns>
            public static double GetRandomDouble()
            {
                return s_Random.NextDouble();
            }

            /// <summary>
            /// 返回一个介于 0.0 和 1.0 之间的随机数。
            /// </summary>
            /// <returns>大于等于 0.0 并且小于 1.0 的单精度浮点数。</returns>
            public static float GetRandomSingle()
            {
                return (float)s_Random.NextDouble();
            }

            /// <summary>
            /// 返回一个随机的 Int64。
            /// </summary>
            public static long GetRandomInt64()
            {
                byte[] bytes = new byte[8];
                s_Random.NextBytes(bytes);
                return BitConverter.ToInt64(bytes, 0);
            }

            /// <summary>
            /// 返回一个随机的 UInt64。
            /// </summary>
            public static ulong GetRandomUInt64()
            {
                byte[] bytes = new byte[8];
                s_Random.NextBytes(bytes);
                return BitConverter.ToUInt64(bytes, 0);
            }

            /// <summary>
            /// 用随机数填充指定字节数组的元素。
            /// </summary>
            /// <param name="buffer">包含随机数的字节数组。</param>
            public static void GetRandomBytes(byte[] buffer)
            {
                s_Random.NextBytes(buffer);
            }

            /// <summary>
            /// 获取一个与上次值不同的随机数。
            /// </summary>
            public static int RandomUnique(int min, int max, int last)
            {
                if (min + 1 >= max)
                {
                    return min;
                }

                int result = last;
                while (result == last)
                {
                    result = UnityEngine.Random.Range(min, max);
                }

                return result;
            }

            /// <summary>
            /// 获取一个排除指定值集合的随机数。
            /// </summary>
            public static int RandomExclude(int min, int max, int[] excludedValues, int maxIterations = 1000)
            {
                int result;
                int iterations = 0;
                do
                {
                    if (iterations > maxIterations)
                    {
                        return -1;
                    }

                    result = UnityEngine.Random.Range(min, max);
                    iterations++;
                }
                while (Contains(excludedValues, result));

                return result;
            }

            /// <summary>
            /// 获取一个排除现有集合和已选集合的随机数。
            /// </summary>
            public static int RandomExcludeUnique(int min, int max, int[] excludedValues, int[] currentValues, int maxIterations = 1000)
            {
                int result;
                int iterations = 0;
                do
                {
                    if (iterations > maxIterations)
                    {
                        return -1;
                    }

                    result = UnityEngine.Random.Range(min, max);
                    iterations++;
                }
                while (Contains(excludedValues, result) || Contains(currentValues, result));

                return result;
            }

            private static bool Contains(int[] values, int target)
            {
                if (values == null)
                {
                    return false;
                }

                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] == target)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
