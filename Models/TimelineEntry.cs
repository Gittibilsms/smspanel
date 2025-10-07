namespace GittBilSmsCore.Models
{
    public class TimelineEntry
    {
        public int Index { get; set; }
        public DateTime Timestamp { get; set; }
        public string StatusText { get; set; } 
        public string Description { get; set; }
        public bool IsCompleted { get; set; }
    }
}
