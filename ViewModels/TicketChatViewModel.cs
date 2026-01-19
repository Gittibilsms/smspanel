namespace GittBilSmsCore.ViewModels
{
    public class TicketChatViewModel
    {
        public int TicketId { get; set; }
        public int CurrentUserId { get; set; }
        public List<TicketResponseViewModel> Responses { get; set; }
          = new List<TicketResponseViewModel>();
    }
}
