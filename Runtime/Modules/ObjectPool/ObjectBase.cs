namespace AlicizaX.ObjectPool
{
    public abstract class ObjectBase : MemoryObject
    {
        internal ObjectPoolBase Pool;
        private string m_Name;
        private object m_Target;
        private bool m_Locked;
        private float m_LastUseTime;

        public string Name => m_Name;
        public object Target => m_Target;

        public bool Locked
        {
            get => m_Locked;
            set => m_Locked = value;
        }

        public float LastUseTime
        {
            get => m_LastUseTime;
            internal set => m_LastUseTime = value;
        }

        public virtual bool CustomCanReleaseFlag => true;

        protected void Initialize(object target)
        {
            Initialize(string.Empty, target, false);
        }

        protected void Initialize(string name, object target, bool locked = false)
        {
            m_Name = name ?? string.Empty;
            m_Target = target;
            m_Locked = locked;
            m_LastUseTime = 0f;
        }

        protected internal virtual void OnSpawn() { }
        protected internal virtual void OnUnspawn() { }
        protected internal abstract void Release(bool isShutdown);

        public override void Clear()
        {
            Pool = null;
            m_Name = null;
            m_Target = null;
            m_Locked = false;
            m_LastUseTime = 0f;
        }
    }

    public abstract class ObjectBase<TTarget> : ObjectBase where TTarget : class
    {
        public new TTarget Target => (TTarget)base.Target;

        protected void Initialize(TTarget target)
        {
            Initialize(string.Empty, target, false);
        }

        protected void Initialize(string name, TTarget target, bool locked = false)
        {
            base.Initialize(name, target, locked);
        }
    }
}
