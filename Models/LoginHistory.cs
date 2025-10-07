namespace GittBilSmsCore.Models
{
    public class LoginHistory
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string Username { get; set; }
        public string IPAddress { get; set; }
        public string UserAgent { get; set; }
        public string Device { get; set; }
        public string Location { get; set; }  
        public DateTime LoggedInAt { get; set; }
    }
}
