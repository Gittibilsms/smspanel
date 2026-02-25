using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
//using GittiBillSmsCore.Data;
//using GittiBillSmsCore.Models;
//using GittiBillSmsCore.ViewModels.ShortUrl;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Services;
using GittBilSmsCore.ViewModels;
using Microsoft.Extensions.Options;

namespace GittiBillSmsCore.Services
{
    public class ShortUrlService : IShortUrlService
    {
        private readonly GittBilSmsDbContext _context;
        private readonly string _baseUrl;
        private const string CHARSET = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private readonly Random _random = new Random();

        public ShortUrlService(GittBilSmsDbContext context, IOptions<ShortUrlSettings> shortUrlSettings)
        {
            _context = context;
            _baseUrl = shortUrlSettings.Value.BaseUrl.TrimEnd('/');
        }

        public async Task<ShortUrlViewModel> CreateShortUrlAsync(CreateShortUrlViewModel model, int companyId, int userId)
        {
            try
            {
                // Generate or validate short code
                string shortCode;
                if (!string.IsNullOrWhiteSpace(model.CustomBackHalf))
                {
                    // Validate custom back-half
                    if (!await IsShortCodeAvailableAsync(model.CustomBackHalf))
                    {
                        throw new InvalidOperationException("This custom short code is already in use. Please choose another.");
                    }
                    shortCode = model.CustomBackHalf;
                }
                else
                {
                    // Generate random short code
                    shortCode = await GenerateUniqueShortCodeAsync();
                }

                // Ensure URL has protocol
                string originalUrl = model.DestinationUrl;
                if (!originalUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !originalUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    originalUrl = "https://" + originalUrl;
                }

                // Create short URL
                var shortUrl = new ShortUrl
                {
                    ShortCode = shortCode,
                    OriginalUrl = originalUrl,
                    Title = model.Title,
                    CompanyId = companyId,
                    CreatedBy = userId,
                    ExpiryDate = model.ExpiryDate,
                    MaxClicks = model.MaxClicks,
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    TotalClicks = 0
                };

                _context.ShortUrls.Add(shortUrl);
                await _context.SaveChangesAsync();


                return MapToViewModel(shortUrl);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error creating short URL: {ex.Message}", ex);
            }
        }

        public async Task<ShortUrlViewModel> GetByIdAsync(long id, int companyId)
        {
            var shortUrl = await _context.ShortUrls
                .Include(s => s.Company)
                .Include(s => s.CreatedByUser)
                .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);

            if (shortUrl == null)
                return null;

            return MapToViewModel(shortUrl);
        }

        public async Task<ShortUrlViewModel> GetByShortCodeAsync(string shortCode)
        {
            var shortUrl = await _context.ShortUrls
                .Include(s => s.Company)
                .FirstOrDefaultAsync(s => s.ShortCode == shortCode && s.IsActive);

            if (shortUrl == null)
                return null;

            return MapToViewModel(shortUrl);
        }

        public async Task<List<ShortUrlViewModel>> GetCompanyShortUrlsAsync(int companyId, int pageNumber = 1, int pageSize = 20)
        {
            var shortUrls = await _context.ShortUrls
                .Include(s => s.CreatedByUser)
                .Where(s => s.CompanyId == companyId)
                .OrderByDescending(s => s.CreatedDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return shortUrls.Select(s => MapToViewModel(s)).ToList();
        }

        public async Task<int> GetTotalCountAsync(int companyId)
        {
            return await _context.ShortUrls
                .Where(s => s.CompanyId == companyId)
                .CountAsync();
        }

        public async Task<bool> UpdateShortUrlAsync(long id, CreateShortUrlViewModel model, int companyId, int userId)
        {
            try
            {
                var shortUrl = await _context.ShortUrls
                    .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);

                if (shortUrl == null)
                    return false;

                // Update fields
                shortUrl.Title = model.Title;
                shortUrl.ExpiryDate = model.ExpiryDate;
                shortUrl.MaxClicks = model.MaxClicks;

                // If destination URL changed, log it
                if (shortUrl.OriginalUrl != model.DestinationUrl)
                {
                    string newUrl = model.DestinationUrl;
                    if (!newUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !newUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        newUrl = "https://" + newUrl;
                    }

                    shortUrl.OriginalUrl = newUrl;
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error updating short URL: {ex.Message}", ex);
            }
        }

        public async Task<bool> DeleteShortUrlAsync(long id, int companyId)
        {
            var shortUrl = await _context.ShortUrls
                .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);

            if (shortUrl == null)
                return false;

            shortUrl.IsActive = false;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> IsShortCodeAvailableAsync(string shortCode)
        {
            return !await _context.ShortUrls.AnyAsync(s => s.ShortCode == shortCode);
        }

        private async Task<string> GenerateUniqueShortCodeAsync(int length = 7)
        {
            int maxAttempts = 10;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                string code = GenerateRandomCode(length);
                if (await IsShortCodeAvailableAsync(code))
                {
                    return code;
                }
            }

            // If we couldn't find a unique code, increase length and try again
            return await GenerateUniqueShortCodeAsync(length + 1);
        }

        private string GenerateRandomCode(int length)
        {
            var code = new char[length];
            for (int i = 0; i < length; i++)
            {
                code[i] = CHARSET[_random.Next(CHARSET.Length)];
            }
            return new string(code);
        }

        private ShortUrlViewModel MapToViewModel(ShortUrl shortUrl)
        {
            return new ShortUrlViewModel
            {
                Id = shortUrl.Id,
                ShortCode = shortUrl.ShortCode,
                ShortUrl = $"{_baseUrl}/{shortUrl.ShortCode}",
                OriginalUrl = shortUrl.OriginalUrl,
                Title = shortUrl.Title,
                CreatedDate = shortUrl.CreatedDate,
                ExpiryDate = shortUrl.ExpiryDate,
                TotalClicks = shortUrl.TotalClicks,
                MaxClicks = shortUrl.MaxClicks,
                IsActive = shortUrl.IsActive,
                CompanyName = shortUrl.Company?.CompanyName,
                CreatedByName = shortUrl.CreatedByUser?.FullName
            };
        }
    }
}