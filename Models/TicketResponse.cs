using System.ComponentModel.DataAnnotations.Schema;
using GittBilSmsCore.Models;

public class TicketResponse
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public int? ResponderId { get; set; }         // FK to User
    [Column(TypeName = "int")]
    public int? RespondedByUserId { get; set; }   // (optional if same as ResponderId)

    public string Message { get; set; }
    public string ResponseText { get; set; }     // (if used by frontend)
    public DateTime RespondedAt { get; set; }
    public DateTime CreatedDate { get; set; }

    public Ticket Ticket { get; set; }
    public User Responder { get; set; }
    public User RespondedByUser { get; set; } // optional (remove if unused)
}

public class TicketResponseRequest
{
    public int TicketId { get; set; }
    public string ResponseText { get; set; }
    public string? ConnectionId { get; set; }  // Not stored in DB
}