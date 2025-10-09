namespace GittBilSmsCore.Models
{
    public class SendSmsApiRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public List<string> To { get; set; }
        public string Message { get; set; }
    }
}
