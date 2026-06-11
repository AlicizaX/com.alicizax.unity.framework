using System;

namespace AlicizaX.UI.Runtime
{
    internal static class UITypeIndexArray
    {
        public static int[] Create(int capacity)
        {
            int[] values = new int[capacity];
            Fill(values, 0, capacity);
            return values;
        }

        public static void EnsureCapacity(ref int[] values, int typeId)
        {
            if ((uint)typeId < (uint)values.Length)
            {
                return;
            }

            int oldLength = values.Length;
            int newLength = oldLength;
            while (newLength <= typeId)
            {
                newLength <<= 1;
            }

            Array.Resize(ref values, newLength);
            Fill(values, oldLength, newLength);
        }

        private static void Fill(int[] values, int startInclusive, int endExclusive)
        {
            for (int i = startInclusive; i < endExclusive; i++)
            {
                values[i] = -1;
            }
        }
    }
}
