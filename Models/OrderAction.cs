using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using GittBilSmsCore.Helpers;
namespace GittBilSmsCore.Models
{
    public class OrderAction
    {

        [Key]
        [Column("Id")]  // <-- match DB column name
        public int Id { get; set; }

        [ForeignKey("Order")]
        public int OrderId { get; set; }

        public string ActionName { get; set; }

        public string? Message { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

        public virtual Order Order { get; set; }
    }
}
