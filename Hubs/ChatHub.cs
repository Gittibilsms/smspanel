using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.Threading.Tasks;

namespace GittBilSmsCore.Hubs
{
    public class ChatHub : Hub
    {
        // 🔹 Send message to all users in the ticket group
        public async Task SendMessage(int ticketId, string user, string message)
        {
            var data = new
            {
                name = user,
                text = message,
                time = DateTime.Now.ToString("dd MMM yyyy HH:mm")
            };

            await Clients.Group($"ticket-{ticketId}")
                .SendAsync("ReceiveMessage", data); // ✅ sending as single object
        }
        // 🔹 Used if you want to notify all clients about a new ticket response
        public async Task SendTicketResponse(int ticketId)
        {
            await Clients.All.SendAsync("ReceiveTicketResponse", ticketId);
        }

        // 🔹 Group join logic — user joins "ticket-{id}" group
        public Task JoinGroup(int ticketId)
       => Groups.AddToGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");
        public Task JoinCompanyGroup(int companyId)
             => Groups.AddToGroupAsync(Context.ConnectionId, $"company_{companyId}");
        public override async Task OnConnectedAsync()
        {
            // 1) Company group
            var cid = Context.User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(cid, out var companyId))
                await Groups.AddToGroupAsync(Context.ConnectionId, $"company_{companyId}");

            // 2) Admin group, based on your custom UserType claim
            //    (or change to Context.User.IsInRole("Admin") if you use Identity roles)
            var userType = Context.User.FindFirst("UserType")?.Value;
            if (userType == "Admin")
                await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");

            var uid = Context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(uid))
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{uid}");

            await base.OnConnectedAsync();
        }
        public Task JoinAdminGroup()
    => Groups.AddToGroupAsync(Context.ConnectionId, "Admins");

        public Task JoinPanelGroup() =>
    Groups.AddToGroupAsync(Context.ConnectionId, "PanelUsers");

        public override Task OnDisconnectedAsync(System.Exception exception)
        {
            return base.OnDisconnectedAsync(exception);
        }
    }
}
