using System.Text.Json.Serialization;

namespace GittBilSmsCore.Models
{
    public class PayloadModel
    {
        public string Username { get; set; }
        public string Password { get; set; }

        [JsonPropertyName("MessageId")]
        public object MessageId { get; set; }
    }
}
