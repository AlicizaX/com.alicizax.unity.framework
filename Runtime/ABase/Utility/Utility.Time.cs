using System;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// 时间相关的实用函数。
        /// </summary>
        public static class Time
        {
            private static readonly DateTime EpochTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            private static long s_DifferenceTime;
            private static bool s_IsSecondLevel = true;

            public const long TicksPerMicrosecond = 1;
            public const long TicksPer = 10 * TicksPerMicrosecond;
            public const long TicksMillisecondUnit = TicksPer * 1000;
            public const long TicksSecondUnit = TicksMillisecondUnit * 1000;

            public static void SetDifferenceTime(long timeSpan)
            {
                s_IsSecondLevel = timeSpan <= 1000000000000;
                s_DifferenceTime = s_IsSecondLevel ? timeSpan - ClientNowSeconds() : timeSpan - ClientNowMillisecond();
            }

            public static long ClientNowMillisecond()
            {
                return (DateTime.UtcNow.Ticks - EpochTime.Ticks) / TicksMillisecondUnit;
            }

            public static long ServerToday()
            {
                return s_IsSecondLevel ? s_DifferenceTime + ClientToday() : (s_DifferenceTime + ClientTodayMillisecond()) / 1000;
            }

            public static long ClientTodayMillisecond()
            {
                return (DateTime.Now.Date.ToUniversalTime().Ticks - EpochTime.Ticks) / TicksMillisecondUnit;
            }

            public static long ServerNow()
            {
                return s_IsSecondLevel ? s_DifferenceTime + ClientNowSeconds() : (s_DifferenceTime + ClientNowMillisecond()) / 1000;
            }

            public static TimeSpan FromSeconds(int seconds)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            public static long ClientToday()
            {
                return (DateTime.Now.Date.ToUniversalTime().Ticks - EpochTime.Ticks) / TicksSecondUnit;
            }

            public static long ClientNow()
            {
                return ClientNowMillisecond();
            }

            public static long ClientNowSeconds()
            {
                return (DateTime.UtcNow.Ticks - EpochTime.Ticks) / TicksSecondUnit;
            }

            public static long Now()
            {
                return ClientNow();
            }

            public static long UnixTimeSeconds()
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            public static long UnixTimeMilliseconds()
            {
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            public static bool IsUnixSameDay(long timestamp1, long timestamp2)
            {
                return IsSameDay(UtcToUtcDateTime(timestamp1), UtcToUtcDateTime(timestamp2));
            }

            public static bool IsLocalSameDay(long timestamp1, long timestamp2)
            {
                return IsSameDay(UtcToLocalDateTime(timestamp1), UtcToLocalDateTime(timestamp2));
            }

            public static DateTime UtcToUtcDateTime(long utcTimestamp)
            {
                return DateTimeOffset.FromUnixTimeSeconds(utcTimestamp).UtcDateTime;
            }

            public static DateTime UtcToLocalDateTime(long utcTimestamp)
            {
                return DateTimeOffset.FromUnixTimeSeconds(utcTimestamp).LocalDateTime;
            }

            public static bool IsSameDay(DateTime time1, DateTime time2)
            {
                return time1.Date == time2.Date;
            }

            public static long LocalTimeToUnixTimeSeconds(DateTime time)
            {
                return new DateTimeOffset(time.ToUniversalTime()).ToUnixTimeSeconds();
            }

            public static long LocalTimeToUnixTimeMilliseconds(DateTime time)
            {
                return new DateTimeOffset(time.ToUniversalTime()).ToUnixTimeMilliseconds();
            }
        }
    }
}
