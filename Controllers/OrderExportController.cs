using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using GittBilSmsCore.Data;

namespace GittBilSmsCore.Controllers
{
    [Route("Order")]
    public class OrderExportController : BaseController
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<OrderExportController> _logger;

        public OrderExportController(
            GittBilSmsDbContext context, // ✅ Inject DbContext
            IWebHostEnvironment env,
            ILogger<OrderExportController> logger
        ) : base(context) // ✅ Pass to base controller
        {
            _env = env;
            _logger = logger;
        }
        [HttpGet("Export")]
        public IActionResult Export(int orderId, string type, string format)
        {
            try
            {
                // Map type → file name
                var typeFile = type.ToLowerInvariant() switch
                {
                    "all" => "original.txt",          // ALL uploaded
                    "processed" => "recipients.txt",  // Valid sent
                    "filtered" => "recipients.txt",   // SAME as processed
                    "invalid" => "invalid.txt",
                    "blacklisted" => "blacklisted.txt",
                    "repeated" => "repeated.txt",
                    "banned" => "banned.txt",
                    "expired" => "expired.txt", 
                    _ => null
                };

                if (typeFile == null)
                    return BadRequest("Invalid export type.");

                var filePath = Path.Combine("D:\\home\\data", "orders", orderId.ToString(), typeFile);

                if (!System.IO.File.Exists(filePath))
                    return NotFound("File not found.");

                var numbers = System.IO.File.ReadAllLines(filePath).ToList();

                if (numbers.Count == 0)
                    return BadRequest("File is empty.");

                if (format.ToLower() == "txt")
                {
                    // Return TXT
                    var fileBytes = System.IO.File.ReadAllBytes(filePath);
                    return File(fileBytes, "text/plain", $"Order_{orderId}_{type}.txt");
                }
                else if (format.ToLower() == "csv")
                {
                    var headerName = type.ToLowerInvariant() switch
                    {
                        "all" => "Yüklenen Numaralar",
                        "processed" => "Gönderilen Numaralar",
                        "filtered" => "Filtrelenen Numaralar",
                        "invalid" => "Geçersiz Numaralar",
                        "blacklisted" => "Kara Liste Numaralar",
                        "repeated" => "Tekrarlayan Numaralar",
                        "banned" => "Yasaklı Numaralar",
                        "expired" => "Zaman Aşımı Numaralar",
                        _ => "Numaralar"
                    };

                    var csv = new StringBuilder();
                    csv.AppendLine(headerName); // ✅ use dynamic header
                    foreach (var n in numbers)
                        csv.AppendLine(n);

                    var csvBytes = Encoding.UTF8.GetBytes(csv.ToString());
                    return File(csvBytes, "text/csv", $"Order_{orderId}_{type}.csv");
                }
                else if (format.ToLower() == "xlsx")
                {
                    var headerName = type.ToLowerInvariant() switch
                    {
                        "all" => "Yüklenen Numaralar",
                        "processed" => "Gönderilen Numaralar",
                        "filtered" => "Filtrelenen Numaralar",
                        "invalid" => "Geçersiz Numaralar",
                        "blacklisted" => "Kara Liste Numaralar",
                        "repeated" => "Tekrarlayan Numaralar",
                        "banned" => "Yasaklı Numaralar",
                        "expired" => "Zaman Aşımı Numaralar",
                        _ => "Numaralar"
                    };

                    using var workbook = new XLWorkbook();
                    var worksheet = workbook.Worksheets.Add("Numbers");

                    // ✅ Use headerName instead of "GSM"
                    worksheet.Cell(1, 1).Value = headerName;

                    int row = 2;
                    foreach (var number in numbers)
                    {
                        worksheet.Cell(row++, 1).Value = number;
                    }

                    using var stream = new MemoryStream();
                    workbook.SaveAs(stream);
                    stream.Position = 0;

                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Order_{orderId}_{type}.xlsx");
                }
                else
                {
                    return BadRequest("Invalid export format.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Export");
                return StatusCode(500, "Error during export.");
            }
        }
    }
}
