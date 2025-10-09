namespace GittBilSmsCore.Models
{
    public class Pricing
    {

            public int PricingId { get; set; }
            public string Currency { get; set; }
            public decimal Low { get; set; }
            public decimal Middle { get; set; }
            public decimal High { get; set; }
            public bool IsActive { get; set; }
            public DateTime CreatedAt { get; set; }
        
    }
}
