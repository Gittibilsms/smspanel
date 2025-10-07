namespace GittBilSmsCore.Models
{
    public class OrderRecipient
    {
        public int OrderRecipientId { get; set; }
        public int OrderId { get; set; }
        public string RecipientName { get; set; }
        public string RecipientNumber { get; set; }

        public Order Order { get; set; }
    }
}
