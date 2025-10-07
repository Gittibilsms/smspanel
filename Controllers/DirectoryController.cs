using System;
using System.Text;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;


namespace GittBilSmsCore.Controllers
{
    using ModelsDirectory = GittBilSmsCore.Models.PhoneDirectory;
    public class DirectoryController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly UserManager<User> _userManager;
        private readonly IWebHostEnvironment _env;

        public DirectoryController(GittBilSmsDbContext context, IWebHostEnvironment env) : base(context)
        {
            _context = context;
            _env = env;
        }
        public IActionResult Index()
        {
            if (!HasAccessRoles("Directory", "Read"))
            {
                return Forbid();
            }

            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var userType = HttpContext.Session.GetString("UserType") ?? "";
            var userId = HttpContext.Session.GetInt32("UserId");
            var isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;

            var directoriesQuery = _context.Directories
                .Include(d => d.DirectoryNumbers)
                .Where(d => d.CompanyId == companyId);

            // If sub-user, restrict to their own directories
            if (userType == "CompanyUser" && !isMainUser)
            {
                directoriesQuery = directoriesQuery.Where(d => d.CreatedByUserId == userId);
            }

            var directories = directoriesQuery
                .OrderByDescending(d => d.CreatedAt)
                .ToList();

            return View(directories);
        }
        [HttpPost]
        public async Task<IActionResult> Create(DirectoryViewModel model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            if (companyId == null) return Json(new { success = false, message = "Unauthorized" });

            bool hasText = !string.IsNullOrWhiteSpace(model.Numbers);
            bool hasFile = model.UploadedFile != null && model.UploadedFile.Length > 0;

            if (!hasText && !hasFile)
                return Json(new { success = false, message = "Please upload a file or paste numbers." });

            // 🔒 Check if a directory with the same name already exists for this company
            bool exists = await _context.Set<ModelsDirectory>()
                .AnyAsync(d => d.CompanyId == companyId && d.DirectoryName == model.DirectoryName);

            if (exists)
                return Json(new { success = false, message = "A directory with this name already exists." });

            var directory = new ModelsDirectory
            {
                DirectoryName = model.DirectoryName,
                CompanyId = companyId.Value,
                CreatedByUserId = userId,
                CreatedAt = DateTime.Now,
                UploadDate = DateTime.Now
            };

            // ✅ Save uploaded file if provided
            if (hasFile)
            {
                var fileName = Path.GetFileNameWithoutExtension(model.UploadedFile.FileName);
                var ext = Path.GetExtension(model.UploadedFile.FileName);
                var savedFileName = $"{fileName}_{Guid.NewGuid()}{ext}";
                var path = Path.Combine(_env.WebRootPath, "uploads", "directories");
                System.IO.Directory.CreateDirectory(path);
                var filePath = Path.Combine(path, savedFileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await model.UploadedFile.CopyToAsync(stream);

                directory.FileName = model.UploadedFile.FileName;
                directory.FilePath = $"/uploads/directories/{savedFileName}";
                directory.FileSizeBytes = model.UploadedFile.Length;
            }

            _context.Set<ModelsDirectory>().Add(directory);
            await _context.SaveChangesAsync();

            var numberList = new List<string>();
            if (hasText)
            {
                numberList.AddRange(model.Numbers
                    .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Trim()));
            }

            if (hasFile)
            {
                using var reader = new StreamReader(model.UploadedFile.OpenReadStream());
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                        numberList.Add(line.Trim());
                }
            }

            foreach (var number in numberList.Distinct())
            {
                _context.DirectoryNumbers.Add(new DirectoryNumber
                {
                    DirectoryId = directory.DirectoryId,
                    PhoneNumber = number,
                    SourceMethod = hasFile ? "Upload" : "Manual",
                    CreatedAt = DateTime.Now,
                    CreatedByUserId = userId
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
        [HttpGet]
        public async Task<IActionResult> Download(int id)
        {
            var directory = await _context.Directories
         .Include(d => d.DirectoryNumbers)
         .FirstOrDefaultAsync(d => d.DirectoryId == id);

            if (directory == null) return NotFound();

            var sb = new StringBuilder();

            foreach (var number in directory.DirectoryNumbers)
            {
                sb.AppendLine(number.PhoneNumber);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"{directory.DirectoryName}_numbers.csv");
        }
        [HttpGet]
        public async Task<IActionResult> Get(int id)
        {
            var directory = await _context.Directories
                .Include(d => d.DirectoryNumbers)
                .FirstOrDefaultAsync(d => d.DirectoryId == id);

            if (directory == null)
                return Json(new { success = false, message = "Directory not found." });

            var data = new
            {
                directoryId = directory.DirectoryId,
                directoryName = directory.DirectoryName,
                count = directory.DirectoryNumbers?.Count() ?? 0
            };

            return Json(new { success = true, data });
        }
        [HttpPost]
        public async Task<IActionResult> Edit(DirectoryViewModel model)
        {
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var userId = HttpContext.Session.GetInt32("UserId");

            var directory = await _context.Directories
                .Include(d => d.DirectoryNumbers)
                .FirstOrDefaultAsync(d => d.DirectoryId == model.DirectoryId && d.CompanyId == companyId);

            if (directory == null)
                return Json(new { success = false, message = "Directory not found." });

            directory.DirectoryName = model.DirectoryName;

            // Clear existing numbers
            _context.DirectoryNumbers.RemoveRange(directory.DirectoryNumbers);

            var numberList = new List<string>();
            if (!string.IsNullOrWhiteSpace(model.Numbers))
            {
                numberList.AddRange(model.Numbers
                    .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Trim()));
            }

            if (model.UploadedFile != null && model.UploadedFile.Length > 0)
            {
                var fileName = Path.GetFileNameWithoutExtension(model.UploadedFile.FileName);
                var ext = Path.GetExtension(model.UploadedFile.FileName);
                var savedFileName = $"{fileName}_{Guid.NewGuid()}{ext}";
                var path = Path.Combine(_env.WebRootPath, "uploads", "directories");
                System.IO.Directory.CreateDirectory(path);
                var filePath = Path.Combine(path, savedFileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await model.UploadedFile.CopyToAsync(stream);

                using var reader = new StreamReader(model.UploadedFile.OpenReadStream());
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                        numberList.Add(line.Trim());
                }

                directory.FileName = model.UploadedFile.FileName;
                directory.FilePath = $"/uploads/directories/{savedFileName}";
                directory.FileSizeBytes = model.UploadedFile.Length;
            }

            // Re-add numbers
            foreach (var number in numberList.Distinct())
            {
                _context.DirectoryNumbers.Add(new DirectoryNumber
                {
                    DirectoryId = directory.DirectoryId,
                    PhoneNumber = number,
                    SourceMethod = model.UploadedFile != null ? "Upload" : "Manual",
                    CreatedAt = DateTime.Now,
                    CreatedByUserId = userId
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var directory = await _context.Directories
                .FirstOrDefaultAsync(d => d.DirectoryId == id && d.CompanyId == companyId);

            if (directory == null)
                return Json(new { success = false, message = "Directory not found." });

            _context.Directories.Remove(directory);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = _sharedLocalizer["directorydeltesuccess"] });
        }

    }
}
