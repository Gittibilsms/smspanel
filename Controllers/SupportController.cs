using GittBilSmsCore.Data;
using GittBilSmsCore.Helpers;
using GittBilSmsCore.Models;
using GittBilSmsCore.ViewModels;
using GittBilSmsCore.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GittBilSmsCore.Controllers
{
    public class SupportController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IHubContext<ChatHub> _hubContext;

        public SupportController(GittBilSmsDbContext context, UserManager<User> userManager, IHubContext<ChatHub> hubContext) : base(context)
        {
            _context = context;
            _userManager = userManager;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                //var currentUser = await _userManager.GetUserAsync(User);
                var companyId = HttpContext.Session.GetInt32("CompanyId");
                var userId = HttpContext.Session.GetInt32("UserId");
                //if (currentUser == null)
                //{
                //    return Content($"<h3 style='color:red'>Error:</h3><h4>Stack Trace:</h4><pre>Top error</pre>", "text/html");

                //    //  _logger.LogWarning("User not found in Index. Redirecting to login.");
                //    //return RedirectToAction("Login", "Account");
                //}

                var userType = HttpContext.Session.GetString("UserType") ?? string.Empty;
                var isCompanyUser = userType == "CompanyUser";
                var isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;
                var canCreateTicket = HttpContext.Session.GetInt32("CanSendSupportRequest") == 1;

                var permissionJson = HttpContext.Session.GetString("UserPermissions");
                var permissions = string.IsNullOrEmpty(permissionJson)
                    ? new List<RolePermission>()
                    : JsonSerializer.Deserialize<List<RolePermission>>(permissionJson);

                var hasSupportRead = permissions.Any(p =>
                    p.Module == "Request_for_support" && (p.CanRead || p.CanEdit));

                var ticketQuery =
                    from t in _context.Tickets
                    join u in _context.Users on t.CreatedByUserId equals u.Id
                    select new { Ticket = t, User = u };

                if (!isCompanyUser && hasSupportRead)
                {
                    // System/Admin: show *all* tickets
                }
                else if (isCompanyUser && isMainUser)
                {
                    ticketQuery = ticketQuery
                        .Where(x => x.User.CompanyId == companyId);
                }
                else if (isCompanyUser && canCreateTicket)
                {
                    ticketQuery = ticketQuery
                        .Where(x => x.Ticket.CreatedByUserId == userId);
                }
                else
                {
                    return RedirectToAction("AccessDenied", "Home");
                }

                var list = await ticketQuery
                    .OrderByDescending(x => x.Ticket.CreatedAt)
                    .Select(x => new TicketListItemViewModel
                    {
                        Id = x.Ticket.Id,
                        Subject = x.Ticket.Subject,
                        Status = x.Ticket.Status.ToString(),
                        CreatedAt = x.Ticket.CreatedAt,
                        CreatedByUserName = x.User.UserName
                    })
                    .ToListAsync();

                ViewBag.CurrentCompanyId = companyId;
                return View(list);
            }
            catch (Exception ex)
            {
                // Log to console / file / Azure log stream
                //_log.LogError(ex, "Error in SupportController.Index");
                // Option 1: Show custom error page
                //return RedirectToAction("Error", "Home");
                return Content($"<h3 style='color:red'>Error:</h3><pre>{ex.Message}</pre><h4>Stack Trace:</h4><pre>{ex.StackTrace}</pre>", "text/html");

                // Option 2: For debugging, you could temporarily use:
                // return Content($"Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Chat(int id)
        {
            // 1) Load the ticket + all its responses
            var ticket = await _context.Tickets
                .Include(t => t.TicketResponses)
                    .ThenInclude(r => r.Responder)
                .Include(t => t.CreatedByUser)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (ticket == null)
                return NotFound();

            // 2) Build your VM, first adding the ticket itself...
            var vm = new TicketChatViewModel
            {
                TicketId = ticket.Id,
                Responses = new List<TicketResponseViewModel>()
            };

            // Initial message comes from the ticket.Message
            vm.Responses.Add(new TicketResponseViewModel
            {
                ResponderName = ticket.CreatedByUser?.UserName ?? "System",
                Message = ticket.Message,
                CreatedDate = ticket.CreatedAt,
                IsAdmin = ticket.CreatedByUser?.UserType == "Admin"
            });

            // 3) Then add any follow‑up TicketResponses
            vm.Responses.AddRange(
                ticket.TicketResponses
                      .OrderBy(r => r.CreatedDate)
                      .Select(r => new TicketResponseViewModel
                      {
                          ResponderName = r.Responder?.UserName,
                          Message = r.ResponseText,
                          CreatedDate = r.CreatedDate,
                          IsAdmin = r.Responder?.UserType == "Admin"
                      })
            );

            return PartialView("_ChatPartial", vm);
        }
        [HttpPost]
        public async Task<IActionResult> CreateTicket([FromBody] TicketCreateModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Subject) ||
                string.IsNullOrWhiteSpace(model.Message))
                return BadRequest(new { success = false });

            var user = await _userManager.GetUserAsync(User);
            var defaultAdmin = await _userManager.Users
                .FirstOrDefaultAsync(u => u.UserType == "Admin");

            var ticket = new Ticket
            {
                Subject = model.Subject,
                Message = model.Message,
                Status = TicketStatus.Open,
                CreatedAt = TimeHelper.NowInTurkey(),
                UpdatedDate = TimeHelper.NowInTurkey(),
                CreatedByUserId = user.Id,
                AssignedTo = defaultAdmin?.Id
            };
            _context.Tickets.Add(ticket);
            await _context.SaveChangesAsync();

            var payload = new
            {
                id = ticket.Id,
                subject = ticket.Subject,
                status = ticket.Status.ToString(),
                createdByUserName = user.UserName,
                isoDate = ticket.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                displayDate = ticket.CreatedAt.ToString("dd/MM/yyyy HH:mm")
            };

            await _hubContext.Clients.All
              .SendAsync("ReceiveNewTicket", payload);

            return Json(new { success = true });
        }
        [HttpPost]
        public async Task<IActionResult> RespondToTicket([FromBody] TicketResponse model)
        {
            if (string.IsNullOrWhiteSpace(model.ResponseText))
                return BadRequest("Message cannot be empty.");
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var userId = HttpContext.Session.GetInt32("UserId");
            var user = await _userManager.Users
              .Include(u => u.UserRoles)
                  .ThenInclude(ur => ur.Role)
                      .ThenInclude(r => r.RolePermissions)
              .FirstOrDefaultAsync(u => u.Id == userId);
            var userType = HttpContext.Session.GetString("UserType") ?? string.Empty;
            var isCompanyUser = userType == "CompanyUser";
            var isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;
            var ticket = await _context.Tickets.FindAsync(model.TicketId);
            if (ticket == null) return NotFound();

            // 1) Add the response
            var response = new TicketResponse
            {
                TicketId = model.TicketId,
                Message = model.ResponseText,
                ResponseText = model.ResponseText,
                CreatedDate = TimeHelper.NowInTurkey(),
                ResponderId = userId,
                RespondedByUserId = userId
            };
            _context.TicketResponses.Add(response);
            var roleId = HttpContext.Session.GetInt32("RoleId");
            var isAdmin = roleId == 1;
            if (isAdmin)
            {
                ticket.Status = TicketStatus.Answered;
                ticket.UpdatedDate = TimeHelper.NowInTurkey();
            }


            var isAdminUser = (await _userManager.GetRolesAsync(user))
                                  .Contains("Admin");
            await _context.SaveChangesAsync();

            // 3) Broadcast the new chat message as before
            var payload = new
            {
                name = user.UserName,
                text = response.ResponseText,
                time = response.CreatedDate.ToString("dd/MM/yyyy HH:mm"),
                isAdmin = isAdminUser
            };
            await _hubContext.Clients.Group($"ticket-{ticket.Id}")
                             .SendAsync("ReceiveMessage", payload);

            // 4) And _also_ notify everyone (or just admins) that this ticket’s status changed
            await _hubContext.Clients.All
                             .SendAsync("TicketStatusChanged", new
                             {
                                 ticketId = ticket.Id,
                                 status = ticket.Status.ToString()
                             });

            return Ok();
        }
    }
}
