namespace GittBilSmsCore.Models
{
    public class ShortUrlClick
    {
        public long Id { get; set; }
        public long ShortUrlId { get; set; }
        public string ShortCode { get; set; }
        public string? PhoneNumber { get; set; }
        public string? DeviceType { get; set; }
        public string? OperatingSystem { get; set; }
        public string? Browser { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime ClickedAt { get; set; }
    }
}