using Microsoft.AspNetCore.Mvc;
using GittBilSmsCore.Data;

namespace GittBilSmsCore.Controllers
{
    public class PaymentsController : BaseController
    {
        public PaymentsController(GittBilSmsDbContext context)
            : base(context) // ✅ Pass context to BaseController
        {
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult SubmitReceipt([FromBody] ReceiptInputModel model)
        {
            if (string.IsNullOrWhiteSpace(model.ReceiptNumber))
                return BadRequest(new { messageKey = "Receiptnumberrequired." });

            // TODO: Save to DB
            // Example:
            // _context.Payments.Add(new Payment { ReceiptNumber = model.ReceiptNumber });
            // _context.SaveChanges();

            return Ok(new { messageKey = "Receiptreceived" });
        }

        public class ReceiptInputModel
        {
            public string ReceiptNumber { get; set; }
        }
    }
}