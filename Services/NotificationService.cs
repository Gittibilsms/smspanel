using System.Security.Claims;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Helpers;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Enums;

namespace GittBilSmsCore.Services
{
    public class NotificationService : INotificationService
    {
        private readonly GittBilSmsDbContext _context;

        public NotificationService(GittBilSmsDbContext context)
        {
            _context = context;
        }

        public async Task AddNotificationAsync(Notifications notification)
        {
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
        }

        //public async Task<List<Notifications>> GetUnreadNotificationsAsync(int? companyId, ClaimsPrincipal user)
        //{
        //    var allowedTypes = new[] {
        //        NotificationType.SupportRequest,
        //        NotificationType.SmsFailed,
        //        NotificationType.UnauthorizedLogin,
        //        NotificationType.SmsAwaitingApproval,
        //        NotificationType.OrderApproved
        //    };
        //    if (RoleHelper.HasGlobalAccess(user))
        //    {
        //        return await _context.Notifications
        //            .Where(n => !n.IsRead && allowedTypes.Contains(n.Type))
        //            .OrderByDescending(n => n.CreatedAt)
        //            .ToListAsync();
        //    }
        //    else if (companyId.HasValue)
        //    {
        //        return await _context.Notifications
        //            .Where(n => !n.IsRead && n.CompanyId == companyId && allowedTypes.Contains(n.Type))
        //            .OrderByDescending(n => n.CreatedAt)
        //            .ToListAsync();
        //    }

        //    return new List<Notifications>();
        //}

        public async Task<List<Notifications>> GetUnreadNotificationsAsync(int? companyId, ClaimsPrincipal user)
        {
            bool isAdmin = RoleHelper.HasGlobalAccess(user);

            // Admins get all SmsAwaitingApproval; non‐admins also get OrderApproved
            NotificationType[] allowedTypes = isAdmin
                ? new[]
                {
            NotificationType.SupportRequest,
            NotificationType.SmsFailed,
            NotificationType.UnauthorizedLogin,
            NotificationType.SmsAwaitingApproval
                }
                : new[]
                {
            NotificationType.SupportRequest,
            NotificationType.SmsFailed,
            NotificationType.UnauthorizedLogin,
            NotificationType.SmsAwaitingApproval,
            NotificationType.OrderApproved
                };

            // Base query: unread + allowed types
            var query = _context.Notifications
                .Where(n => !n.IsRead && allowedTypes.Contains(n.Type));

            if (!isAdmin && companyId.HasValue)
            {
                // Company users only see notifications for their own company
                query = query.Where(n => n.CompanyId == companyId.Value);
            }
            // Admins see all companies, so no CompanyId filter for them

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
        }
        public async Task MarkAllAsReadAsync(int? companyId)
        {
            var notifications = await _context.Notifications
                .Where(n => !n.IsRead && (companyId == null || n.CompanyId == companyId))
                .ToListAsync();

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
            }

            await _context.SaveChangesAsync();
        }
        public async Task MarkAsReadAsync(int notificationId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }
        }
    }
}
