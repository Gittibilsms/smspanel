namespace GittBilSmsCore.ViewModels
{
    public class TicketResponseViewModel
    {
        public string ResponderName { get; set; }
        public int Id { get; set; }


        public string Message { get; set; }

        public DateTime CreatedDate { get; set; }
        public bool IsAdmin { get; set; }
    }
}
