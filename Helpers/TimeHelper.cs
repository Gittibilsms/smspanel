namespace GittBilSmsCore.Helpers
{
    public class TimeHelper
    {
        private static readonly TimeZoneInfo TurkeyTimeZone =
    TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");

        public static DateTime NowInTurkey()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TurkeyTimeZone);
        }
        public static DateTime ToTurkeyTime(DateTime utcTime)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, TurkeyTimeZone);
        }
        public static DateTime ConvertToTurkeyTime(DateTime utcDateTime)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, TurkeyTimeZone);
        }

        public static DateTime ConvertToUtcFromTurkey(DateTime turkeyTime)
        {
            return TimeZoneInfo.ConvertTimeToUtc(turkeyTime, TurkeyTimeZone);
        }
    }
}
