using System;

namespace AlicizaX.ObjectPool
{
    public interface IObjectPool<T> where T : ObjectBase
    {
        string Name { get; }
        string FullName { get; }
        Type ObjectType { get; }
        int Count { get; }
        bool AllowMultiSpawn { get; }
        float AutoReleaseInterval { get; set; }
        int Capacity { get; set; }
        float ExpireTime { get; set; }
        int Priority { get; set; }

        void Register(T obj, bool spawned);
        bool CanSpawn();
        bool CanSpawn(string name);
        T Spawn();
        T Spawn(string name);
        void Unspawn(T obj);
        void UnspawnTarget(object target);
        void Release();
        void Release(int toReleaseCount);
        void ReleaseAllUnused();
    }
}
