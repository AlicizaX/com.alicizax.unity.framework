using System.Runtime.InteropServices;

namespace AlicizaX.ObjectPool
{

    [StructLayout(LayoutKind.Auto)]
    public readonly struct ObjectInfo
    {
        public readonly string Name;
        public readonly bool Locked;
        public readonly bool CustomCanReleaseFlag;
        public readonly float LastUseTime;
        public readonly int SpawnCount;

        public ObjectInfo(string name, bool locked, bool customCanReleaseFlag,
            float lastUseTime, int spawnCount)
        {
            Name = name;
            Locked = locked;
            CustomCanReleaseFlag = customCanReleaseFlag;
            LastUseTime = lastUseTime;
            SpawnCount = spawnCount;
        }

        public bool IsInUse => SpawnCount > 0;
    }
}
