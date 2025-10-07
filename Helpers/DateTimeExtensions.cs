namespace GittBilSmsCore.Helpers
{
    public static class DateTimeExtensions
    {
        public static DateTime ToTurkeyTime(this DateTime utcDateTime)
        {
            var turkeyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, turkeyTimeZone);
        }
    }
}
