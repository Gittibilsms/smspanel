using GittBilSmsCore.ViewModels;

namespace GittBilSmsCore.Services
{
    public interface IShortUrlService
    {
        Task<ShortUrlViewModel> CreateShortUrlAsync(CreateShortUrlViewModel model, int companyId, int userId);
        Task<ShortUrlViewModel> GetByIdAsync(long id, int companyId);
        Task<ShortUrlViewModel> GetByShortCodeAsync(string shortCode);
        Task<List<ShortUrlViewModel>> GetCompanyShortUrlsAsync(int companyId, int pageNumber = 1, int pageSize = 20);
        Task<bool> UpdateShortUrlAsync(long id, CreateShortUrlViewModel model, int companyId, int userId);
        Task<bool> DeleteShortUrlAsync(long id, int companyId);
        Task<bool> IsShortCodeAvailableAsync(string shortCode);
        Task<int> GetTotalCountAsync(int companyId);
    }
}
