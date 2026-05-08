using System;
using System.Threading;
using Cysharp.Text;

public static class ToStringNonAllocExtensions
{
    // 使用 ThreadLocal 确保线程安全
    private static readonly ThreadLocal<Utf16ValueStringBuilder> ThreadLocalStringBuilder =
        new ThreadLocal<Utf16ValueStringBuilder>(() => ZString.CreateStringBuilder(), true);

    public static string ToStringNonAlloc(this int value)
    {
        var sb = ThreadLocalStringBuilder.Value;
        sb.Clear();
        sb.Append(value);
        return sb.ToString();
    }

    public static string ToStringNonAlloc(this float value)
    {
        var sb = ThreadLocalStringBuilder.Value;
        sb.Clear();
        sb.Append(value);
        return sb.ToString();
    }

    public static string ToStringNonAlloc(this double value)
    {
        var sb = ThreadLocalStringBuilder.Value;
        sb.Clear();
        sb.Append(value);
        return sb.ToString();
    }

    public static string ToStringNonAlloc(this bool value)
    {
        var sb = ThreadLocalStringBuilder.Value;
        sb.Clear();
        sb.Append(value);
        return sb.ToString();
    }

    public static string ToStringNonAlloc(this string value)
    {
        var sb = ThreadLocalStringBuilder.Value;
        sb.Clear();
        sb.Append(value);
        return sb.ToString();
    }

    public static string ToStringNonAlloc<T>(this T value) where T : IFormattable
    {
        var sb = ThreadLocalStringBuilder.Value;
        sb.Clear();
        sb.AppendFormat(null, "{0}", value);
        return sb.ToString();
    }

    // 释放所有 ThreadLocal 的实例
    public static void Dispose()
    {
        if (ThreadLocalStringBuilder.IsValueCreated)
        {
            ThreadLocalStringBuilder.Value.Dispose();
        }

        ThreadLocalStringBuilder.Dispose();
    }
}