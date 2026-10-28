using System;

namespace AlicizaX.Timer.Runtime
{
    public static class TimerDebugFlags
    {
        public const byte Running = 1 << 0;
        public const byte Loop = 1 << 1;
        public const byte Unscaled = 1 << 2;
    }

    public struct TimerDebugInfo
    {
        public ulong TimerHandle;
        public float LeftTime;
        public float Duration;
        public float Age;
        public byte Flags;
    }

    /// <summary>
    /// 无状态计时器回调。结构体通过缓存的静态委托调用，因此
    /// <see cref="ITimerService.AddTimer{TCallback,TArg}"/> 在调用点零分配
    /// （不同于由方法组或 lambda 构造的 <see cref="Action{T}"/>）。
    /// </summary>
    public interface ITimerCallback<in TArg>
    {
        void Invoke(TArg arg);
    }

    [UnityEngine.Scripting.Preserve]
    public interface ITimerService : IService
    {
        ulong AddTimer(TimerHandlerNoArgs callback, float time, bool isLoop = false, bool isUnscaled = false);

        /// <summary>
        /// 热路径调用方必须使用缓存委托或静态方法组，禁止传入捕获 lambda 或闭包，
        /// 因为闭包对象会产生分配，破坏 0GC 约束。
        /// </summary>
        ulong AddTimer<T>(Action<T> callback, T arg, float time, bool isLoop = false, bool isUnscaled = false) where T : class;

        /// <summary>
        /// 热路径零分配回调重载。回调类型通过按 (TCallback, TArg) 缓存的委托调用，
        /// 调用点不会创建委托。
        /// </summary>
        ulong AddTimer<TCallback, TArg>(TArg arg, float time, bool isLoop = false, bool isUnscaled = false)
            where TCallback : struct, ITimerCallback<TArg>
            where TArg : class;

        void Stop(ulong timerHandle);
        void Resume(ulong timerHandle);
        bool IsRunning(ulong timerHandle);
        float GetLeftTime(ulong timerHandle);
        void Restart(ulong timerHandle);
        void RemoveTimer(ulong timerHandle);

        /// <summary>
        /// 释放尾部不含已租用计时器槽位的页。应在大量计时器被移除后调用；
        /// 否则服务会一直保留峰值页容量直到销毁。
        /// </summary>
        void TrimExcess();
    }

    [UnityEngine.Scripting.Preserve]
    public interface ITimerDebugService
    {
        int GetAllTimers(TimerDebugInfo[] results);

        void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount);
    }

#if UNITY_EDITOR
    [UnityEngine.Scripting.Preserve]
    public interface ITimerEditorDebugService
    {
        int GetStaleOneShotTimers(TimerDebugInfo[] results);
    }
#endif
}
