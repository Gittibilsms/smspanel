namespace GittBilSmsCore.ViewModels
{
    public class BlacklistViewModel
    {
        public string PhoneNumbersInput { get; set; }
        public string SearchPhoneNumber { get; set; }
        public bool? SearchResultFound { get; set; }
        public bool SearchPerformed { get; set; } = false;
    }
}
