using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using System.Text;
using GittBilSmsCore.Data;


namespace GittBilSmsCore.Controllers
{
    [Route("Downloads")]
    public class DownloadsController : BaseController
    {
        public IActionResult Index()
        {
            return View();
        }
        private readonly IWebHostEnvironment _env;
        public DownloadsController(GittBilSmsDbContext context, IWebHostEnvironment env)
          : base(context) // ✅ Pass context to BaseController
        {
            _env = env;
        }

        [HttpGet("Export")]
        public IActionResult Export(int orderId, string type, string format)
        {
            var folderPath = Path.Combine("D:\\home\\data", "orders", orderId.ToString());
            var txtFile = type == "recipients"
                ? Path.Combine(folderPath, "Recipients.txt")
                : Path.Combine(folderPath, $"{type.ToLower()}.txt");

            if (!System.IO.File.Exists(txtFile))
                return NotFound("Kaynak metin dosyası bulunamadı.");

            var numbers = System.IO.File.ReadAllLines(txtFile).ToList();
            var fileName = $"Order_No_{orderId}-{type}.{format}";

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
