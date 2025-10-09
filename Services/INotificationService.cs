using GittBilSmsCore.Models;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace GittBilSmsCore.Services
{
    public interface INotificationService
    {
        Task AddNotificationAsync(Notifications notification);
        Task<List<Notifications>> GetUnreadNotificationsAsync(int? companyId, ClaimsPrincipal user);

        Task MarkAsReadAsync(int notificationId);
        Task MarkAllAsReadAsync(int? companyId);
    }
}
