namespace GittBilSmsCore.Models
{    

   public sealed class TelegramOptions
    {
        public string BotToken { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty; // string for safety (usernames / big ints / negatives)
    }

}
