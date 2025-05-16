using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;

namespace GittBilSmsCore.Controllers
{
    [Microsoft.AspNetCore.Components.Route("[controller]")]
    public class CompanyController : Controller
    {
        private readonly GittBilSmsDbContext _context;

        public CompanyController(GittBilSmsDbContext context)
        {
            _context = context;
        }

        [HttpPost("Add")]
        public async Task<IActionResult> AddCompany([FromBody] AddCompanyViewModel model)
        {
            var company = new Company
            {
                CompanyName = model.CompanyName,
                IsTrustedSender = model.IsTrustedSender,
                IsRefundable = model.IsRefundable,
                CanSendSupportRequest = model.CanSendSupportRequest,
                Apid = model.Apid,
                CurrencyCode = model.CurrencyCode,
                LowPrice = model.LowPrice,
                MediumPrice = model.MediumPrice,
                HighPrice = model.HighPrice,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _context.Companies.Add(company);
            await _context.SaveChangesAsync();

            var user = new CompanyUser
            {
                CompanyId = company.CompanyId,
                FullName = model.FullName,
                UserName = model.UserName,
                Email = model.Email,
                Phone = model.Phone,
                Password = model.Password, // Make sure to hash in production
                IsMainUser = true,
                UserType = "Admin",
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
    }
}
