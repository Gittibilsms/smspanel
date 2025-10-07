namespace GittBilSmsCore.ViewModels
{
    public class TicketListItemViewModel
    {
        public int Id { get; set; }
        public string Subject { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedByUserName { get; set; }
    }
}
