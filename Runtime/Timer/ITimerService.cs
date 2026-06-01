using System;

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

namespace AlicizaX.Timer.Runtime
{
    [UnityEngine.Scripting.Preserve]
    public interface ITimerService : IService
    {
        ulong AddTimer(TimerHandlerNoArgs callback, float time, bool isLoop = false, bool isUnscaled = false);

        /// <summary>
        /// 热路径调用方必须使用缓存委托或静态方法组，禁止传入捕获 lambda 或闭包，
        /// 因为闭包对象会产生分配，破坏 0GC 约束。
        /// </summary>
        ulong AddTimer<T>(Action<T> callback, T arg, float time, bool isLoop = false, bool isUnscaled = false) where T : class;

        void Stop(ulong timerHandle);
        void Resume(ulong timerHandle);
        bool IsRunning(ulong timerHandle);
        float GetLeftTime(ulong timerHandle);
        void Restart(ulong timerHandle);
        void RemoveTimer(ulong timerHandle);
    }

    [UnityEngine.Scripting.Preserve]
    public interface ITimerCapacityService
    {
        void Prewarm(int capacity);
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
