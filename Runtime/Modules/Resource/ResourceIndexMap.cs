using System;

namespace AlicizaX.Resource.Runtime
{
    internal sealed class ResourceUlongIntMap
    {
        private const int DefaultCapacity = 16;
        private const int LoadFactorNumerator = 7;
        private const int LoadFactorDenominator = 10;

        private ulong[] _keys;
        private int[] _values;
        private byte[] _states;
        private int _count;
        private int _resizeThreshold;

        public int Count => _count;

        public void Clear()
        {
            if (_states != null)
            {
                Array.Clear(_states, 0, _states.Length);
            }

            _count = 0;
        }

        public void ReserveCapacity(int capacity)
        {
            if (capacity <= 0)
            {
                return;
            }

            int target = NextPowerOfTwo(Math.Max(DefaultCapacity, RequiredCapacity(capacity)));
            if (_keys == null)
            {
                Initialize(target);
                return;
            }

            if (_keys.Length < target)
            {
                Resize(target);
            }
        }

        public bool TryGetValue(ulong key, out int value)
        {
            value = 0;
            if (_keys == null)
            {
                return false;
            }

            int mask = _keys.Length - 1;
            int index = (int)(Mix(key) & (uint)mask);
            while (true)
            {
                byte state = _states[index];
                if (state == 0)
                {
                    return false;
                }

                if (_keys[index] == key)
                {
                    value = _values[index];
                    return true;
                }

                index = (index + 1) & mask;
            }
        }

        public void Set(ulong key, int value)
        {
            if (_keys == null)
            {
                Initialize(DefaultCapacity);
            }

            int mask = _keys.Length - 1;
            int index = (int)(Mix(key) & (uint)mask);
            while (true)
            {
                byte state = _states[index];
                if (state == 0)
                {
                    if (_count == _resizeThreshold)
                    {
                        Resize(_keys.Length << 1);
                        Set(key, value);
                        return;
                    }
                    _keys[index] = key;
                    _values[index] = value;
                    _states[index] = 1;
                    _count++;
                    return;
                }

                if (_keys[index] == key)
                {
                    _values[index] = value;
                    return;
                }

                index = (index + 1) & mask;
            }
        }

        public bool Remove(ulong key)
        {
            if (_keys == null)
            {
                return false;
            }

            int mask = _keys.Length - 1;
            int index = (int)(Mix(key) & (uint)mask);
            while (true)
            {
                byte state = _states[index];
                if (state == 0)
                {
                    return false;
                }

                if (_keys[index] == key)
                {
                    _states[index] = 0;
                    _values[index] = 0;
                    _count--;
                    int hole = index;
                    int next = (index + 1) & mask;
                    while (_states[next] != 0)
                    {
                        int home = (int)(Mix(_keys[next]) & (uint)mask);
                        if (((hole - home) & mask) < ((next - home) & mask))
                        {
                            _keys[hole] = _keys[next];
                            _values[hole] = _values[next];
                            _states[hole] = 1;
                            _states[next] = 0;
                            _values[next] = 0;
                            hole = next;
                        }
                        next = (next + 1) & mask;
                    }
                    return true;
                }

                index = (index + 1) & mask;
            }
        }

        private void Initialize(int capacity)
        {
            int actualCapacity = NextPowerOfTwo(Math.Max(DefaultCapacity, capacity));
            _keys = new ulong[actualCapacity];
            _values = new int[actualCapacity];
            _states = new byte[actualCapacity];
            _count = 0;
            _resizeThreshold = Math.Max(1, actualCapacity * LoadFactorNumerator / LoadFactorDenominator);
        }

        private void Resize(int capacity)
        {
            ulong[] oldKeys = _keys;
            int[] oldValues = _values;
            byte[] oldStates = _states;
            Initialize(capacity);
            if (oldKeys == null)
            {
                return;
            }

            for (int i = 0; i < oldKeys.Length; i++)
            {
                if (oldStates[i] == 1)
                {
                    Set(oldKeys[i], oldValues[i]);
                }
            }
        }

        private static int RequiredCapacity(int capacity)
        {
            return (capacity * LoadFactorDenominator + LoadFactorNumerator - 1) / LoadFactorNumerator;
        }

        private static uint Mix(ulong key)
        {
            unchecked
            {
                key ^= key >> 33;
                key *= 0xff51afd7ed558ccdUL;
                key ^= key >> 33;
                key *= 0xc4ceb9fe1a85ec53UL;
                key ^= key >> 33;
                return (uint)(key ^ (key >> 32));
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }
    }
}
