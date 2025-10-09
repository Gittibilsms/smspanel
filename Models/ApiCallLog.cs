namespace GittBilSmsCore.Models
{
    public class ApiCallLog
    {
        public int Id { get; set; }
        public int? CompanyId { get; set; }
        public int? UserId { get; set; }

        public int? OrderId { get; set; }
        public Order Order { get; set; }
        public string ApiUrl { get; set; }
        public string RequestBody { get; set; }
        public string ResponseContent { get; set; }
        public long ResponseTimeMs { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
