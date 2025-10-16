using ClosedXML.Excel;
using CsvHelper;
using GittBilSmsCore.Data;
using GittBilSmsCore.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Text;


namespace GittBilSmsCore.Controllers
{
    [Route("Downloads")]
    public class DownloadsController : BaseController
    {
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly GittBilSmsDbContext _context;
        private readonly TelegramMessageService _svc;
        public IActionResult Index()
        {
            return View();
        }
        private readonly IWebHostEnvironment _env;
        public DownloadsController(GittBilSmsDbContext context, IStringLocalizerFactory factory, IWebHostEnvironment env, TelegramMessageService svc)
          : base(context) // ✅ Pass context to BaseController
        {
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _context = context;
            _env = env;
            _svc = svc;
        }

        [HttpGet("Export")]
        // public IActionResult Export(int orderId, string type, string format)
        public async Task<IActionResult> Export(int orderId, string type, string format)
        {
            //var folderPath = Path.Combine("D:\\home\\data", "orders", orderId.ToString());
            var home = Environment.GetEnvironmentVariable("HOME")
                             ?? _env.ContentRootPath;
            var ordersRoot = Path.Combine(home, "data", "orders");
            var folderPath = Path.Combine(ordersRoot, orderId.ToString());
            var txtFile = type == "recipients"
                ? Path.Combine(folderPath, "Recipients.txt")
                : Path.Combine(folderPath, $"{type.ToLower()}.txt");

            if (!System.IO.File.Exists(txtFile))
                return NotFound("Kaynak metin dosyası bulunamadı.");

            var numbers = System.IO.File.ReadAllLines(txtFile).ToList();
            var fileName = $"Order_No_{orderId}-{type}.{format}";


            int performedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
            var filerewriteName = $"{type}.{format}";

            var userName = _context.Users.Find(performedByUserId)?.UserName ?? "UnknownUser";

            int? companyId = HttpContext.Session.GetInt32("CompanyId") ?? 0;           
            var textMsg = string.Format(
                                        _sharedLocalizer["Filedownloadmessage"],
                                        filerewriteName,
                                        orderId,
                                        userName,
                                        TimeHelper.NowInTurkey()
                                    );

            string dataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                Message = "File downloaded from orders",
                UserName = userName,
                OrderId = orderId,
                FileName = fileName,
                Time = TimeHelper.NowInTurkey(),
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString()
            });


            var validFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "txt", "csv", "xlsx"
            };

            if (validFormats.Contains(format))
            {
                 var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _svc.SendToUsersAsync(companyId.Value, performedByUserId, textMsg, dataJson,"",1);
            }
            if (format == "txt")
            {
                return File(Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, numbers)), "text/plain", fileName);
            }
            else if (format == "csv")
            {
                var csv = "PhoneNumber\n" + string.Join("\n", numbers);
                return File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
            }
            else if (format == "xlsx")
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Numbers");

                worksheet.Cell(1, 1).Value = "PhoneNumber";
                for (int i = 0; i < numbers.Count; i++)
                {
                    worksheet.Cell(i + 2, 1).Value = numbers[i];
                }

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Seek(0, SeekOrigin.Begin);

                return File(stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }

            return BadRequest("Unsupported format.");
        }
    }
}
