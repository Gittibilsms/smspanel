namespace GittBilSmsCore.ViewModels
{
    public class DirectoryViewModel
    {
        public int? DirectoryId { get; set; }
        public string DirectoryName { get; set; }
        public string Numbers { get; set; }
        public IFormFile UploadedFile { get; set; }
    }
}
