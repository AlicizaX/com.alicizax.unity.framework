using System;

namespace AlicizaX.UI.Runtime
{
    public sealed class UIRouteEntry
    {
        public Type UIType { get; internal set; }
        public RuntimeTypeHandle TypeHandle { get; internal set; }
        public object[] Args { get; internal set; }
        public bool IsRoot { get; internal set; }
        public int Sequence { get; internal set; }

        internal UIRouteEntry Clone()
        {
            return new UIRouteEntry
            {
                UIType = UIType,
                TypeHandle = TypeHandle,
                Args = CopyArgs(Args),
                IsRoot = IsRoot,
                Sequence = Sequence,
            };
        }

        internal static object[] CopyArgs(object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Array.Empty<object>();
            }

            object[] copy = new object[args.Length];
            Array.Copy(args, copy, args.Length);
            return copy;
        }
    }
}
