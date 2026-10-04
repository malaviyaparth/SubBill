using System;

namespace SubBill
{
    public static class DateTimeExtensions
    {
        private static readonly TimeZoneInfo IstTimeZone = GetIstTimeZone();

        private static TimeZoneInfo GetIstTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                }
                catch
                {
                    return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "India Standard Time", "IST");
                }
            }
        }

        public static DateTime ToIst(this DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Unspecified)
            {
                dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            }
            return TimeZoneInfo.ConvertTimeFromUtc(dateTime.ToUniversalTime(), IstTimeZone);
        }

        public static DateTime? ToIst(this DateTime? dateTime)
        {
            return dateTime.HasValue ? ToIst(dateTime.Value) : null;
        }

        public static DateTime NowIst()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IstTimeZone);
        }

        public static string ToIstFormatted(this DateTime dateTime, string format = "dd MMM yyyy, hh:mm tt")
        {
            return $"{dateTime.ToIst().ToString(format)} IST";
        }
    }
}
