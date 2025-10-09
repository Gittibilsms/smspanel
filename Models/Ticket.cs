using GittBilSmsCore.Models;

public class Ticket
{
    public int Id { get; set; }
    public int? CreatedByUserId { get; set; }
    public string Subject { get; set; }
    public string Message { get; set; }
    public TicketStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedDate { get; set; }

    public int? AssignedTo { get; set; }

    public User CreatedByUser { get; set; }
 
    public User AssignedAdmin { get; set; }

    public virtual ICollection<TicketResponse> TicketResponses { get; set; } = new List<TicketResponse>();

}