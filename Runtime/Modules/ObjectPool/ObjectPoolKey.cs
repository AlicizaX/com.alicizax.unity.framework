using System;
using System.Runtime.CompilerServices;

namespace AlicizaX.ObjectPool
{
    internal readonly struct ObjectPoolKey : IEquatable<ObjectPoolKey>
    {
        private readonly Type m_Type;
        private readonly string m_Name;
        private readonly int m_HashCode;

        public ObjectPoolKey(Type type) : this(type, string.Empty)
        {
        }

        public ObjectPoolKey(Type type, string name)
        {
            m_Type = type ?? throw new ArgumentNullException(nameof(type));
            m_Name = name ?? string.Empty;
            unchecked
            {
                m_HashCode = (m_Type.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(m_Name);
            }
        }

        public Type Type => m_Type;
        public string Name => m_Name;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ObjectPoolKey other)
        {
            return m_Type == other.m_Type && string.Equals(m_Name, other.m_Name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ObjectPoolKey other && Equals(other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return m_HashCode;
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(m_Name))
                return m_Type.FullName ?? m_Type.Name;

            using (var builder = Cysharp.Text.ZString.CreateStringBuilder())
            {
                builder.Append(m_Type.FullName ?? m_Type.Name);
                builder.Append('.');
                builder.Append(m_Name);
                return builder.ToString();
            }
        }
    }
}
