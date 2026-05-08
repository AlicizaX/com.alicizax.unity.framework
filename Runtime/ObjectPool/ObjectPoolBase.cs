using System;
namespace AlicizaX.ObjectPool
{

    public abstract class ObjectPoolBase
    {
        private readonly string m_Name;
        private string m_FullName;
        internal bool IsActive;

        public ObjectPoolBase() : this(null) { }

        public ObjectPoolBase(string name)
        {
            m_Name = name ?? string.Empty;
        }

        public string Name => m_Name;

        public string FullName
        {
            get
            {
                if (m_FullName == null)
                {
                    using (var sb = Cysharp.Text.ZString.CreateStringBuilder())
                    {
                        sb.Append(ObjectType.FullName);
                        if (!string.IsNullOrEmpty(m_Name))
                        {
                            sb.Append('.');
                            sb.Append(m_Name);
                        }
                        m_FullName = sb.ToString();
                    }
                }
                return m_FullName;
            }
        }

        public abstract Type ObjectType { get; }
        public abstract int Count { get; }
        public abstract bool AllowMultiSpawn { get; }

        public abstract float AutoReleaseInterval { get; set; }
        public abstract int Capacity { get; set; }
        public abstract float ExpireTime { get; set; }
        public abstract int Priority { get; set; }


        public virtual int ReleasePerFrameBudget
        {
            get => 8;
            set { }
        }

        public abstract void Release();
        public abstract void Release(int toReleaseCount);
        public abstract void ReleaseAllUnused();

        public abstract int GetAllObjectInfos(ObjectInfo[] results);

        internal abstract void Update(float elapseSeconds, float realElapseSeconds);
        internal abstract void Shutdown();

        internal virtual void OnLowMemory()
        {
            ReleaseAllUnused();
        }
    }
}
