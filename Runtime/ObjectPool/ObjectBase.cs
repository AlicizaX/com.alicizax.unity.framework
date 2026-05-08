namespace AlicizaX.ObjectPool
{
    public abstract class ObjectBase : IMemory
    {
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

        protected void Initialize(string name, object target)
        {
            Initialize(name, target, false);
        }

        protected void Initialize(string name, object target, bool locked)
        {
            m_Name = name ?? string.Empty;
            m_Target = target;
            m_Locked = locked;
            m_LastUseTime = 0f;
        }

        protected internal virtual void OnSpawn() { }
        protected internal virtual void OnUnspawn() { }
        protected internal abstract void Release(bool isShutdown);

        public virtual void Clear()
        {
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
            base.Initialize(string.Empty, target, false);
        }

        protected void Initialize(string name, TTarget target)
        {
            base.Initialize(name, target, false);
        }

        protected void Initialize(string name, TTarget target, bool locked)
        {
            base.Initialize(name, target, locked);
        }
    }
}
