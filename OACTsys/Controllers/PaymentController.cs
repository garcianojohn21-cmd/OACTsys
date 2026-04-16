using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Models;

namespace OACTsys.Controllers
{
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public PaymentController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ─────────────────────────────────────────────
        // GET /Payment/GcashMethod
        // ─────────────────────────────────────────────
        public IActionResult GcashMethod(int studentId, decimal amount,
            string paymentType = "Down Payment",
            string studentEmail = "", string studentName = "")
        {
            ViewBag.StudentId = studentId;
            ViewBag.Amount = amount;
            ViewBag.PaymentType = paymentType;
            ViewBag.StudentEmail = studentEmail;
            ViewBag.StudentName = studentName;
            return View("~/Views/Payment/GcashMethod.cshtml");
        }

        // ─────────────────────────────────────────────
        // GET /Payment/GCashPayView
        // ─────────────────────────────────────────────
        public async Task<IActionResult> GCashPayView(int paymentId, decimal amount,
            string paymentType = "Payment")
        {
            var payment = await _context.Payments.FindAsync(paymentId);
            ViewBag.PaymentId = paymentId;
            ViewBag.Amount = payment?.Amount ?? amount;
            ViewBag.PaymentType = payment?.PaymentType ?? paymentType;
            return View("~/Views/Payment/GCashPayView.cshtml");
        }

        // ─────────────────────────────────────────────
        // GET /Payment/PaymentConfirmation
        // ─────────────────────────────────────────────
        public IActionResult PaymentConfirmation(int paymentId)
        {
            ViewBag.PaymentId = paymentId;
            return View("~/Views/Payment/PaymentConfirmation.cshtml");
        }

        // ─────────────────────────────────────────────
        // GET /Payment/GcashSuccess
        // Uses: ReferenceNumber, VerifiedBy, VerifiedDate
        // ─────────────────────────────────────────────
        public async Task<IActionResult> GcashSuccess(int paymentId)
        {
            var payment = await _context.Payments.FindAsync(paymentId);
            ViewBag.PaymentId = paymentId;
            ViewBag.Amount = payment?.Amount ?? 0m;
            ViewBag.PaymentType = payment?.PaymentType ?? "Payment";
            ViewBag.ReferenceNo = payment?.ReferenceNumber ?? "—";
            ViewBag.ApprovedBy = payment?.VerifiedBy ?? "Admin";
            ViewBag.ApprovedAt = payment?.VerifiedDate?.ToString("MMM dd, yyyy hh:mm tt")
                                  ?? DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
            return View("~/Views/Payment/GcashSuccess.cshtml");
        }

        // ─────────────────────────────────────────────
        // GET /Payment/GetGcashConfig
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetGcashConfig()
        {
            try
            {
                var config = await _context.GCashConfigs
                    .Where(c => c.IsActive)
                    .OrderByDescending(c => c.UpdatedAt)
                    .FirstOrDefaultAsync();

                if (config == null)
                    return Json(new { success = false, message = "GCash configuration not found." });

                return Json(new
                {
                    success = true,
                    gcashNumber = config.GCashNumber,
                    accountName = config.AccountName,
                    qrImageUrl = config.QrCodePath ?? "",
                    description = config.PaymentDescription
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────
        // POST /Payment/CreatePendingPayment
        // ─────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> CreatePendingPayment([FromBody] PendingPaymentDto dto)
        {
            try
            {
                // Reuse existing pending record to avoid duplicates
                var existing = await _context.Payments
                    .FirstOrDefaultAsync(p =>
                        p.StudentId == dto.StudentId &&
                        p.PaymentType == dto.PaymentType &&
                        p.PaymentMethod == "GCash" &&
                        p.Status == "Pending" &&
                        p.IsActive);

                if (existing != null)
                    return Json(new { success = true, paymentId = existing.PaymentId });

                var payment = new Payment
                {
                    StudentId = dto.StudentId,
                    PaymentType = dto.PaymentType,
                    PaymentMethod = "GCash",
                    Amount = dto.Amount,
                    Status = "Pending",
                    PaymentDate = DateTime.Now,
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    Remarks = $"GCash initiated — sender: +63{dto.SenderNumber}"
                };

                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();

                return Json(new { success = true, paymentId = payment.PaymentId });
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                return Json(new { success = false, message = inner });
            }
        }

        // ─────────────────────────────────────────────
        // POST /Payment/UploadProof
        // Uses: ReferenceNumber, ProofOfPaymentPath
        // ─────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> UploadProof(IFormFile proofImage, int paymentId,
            string referenceNo)
        {
            try
            {
                if (proofImage == null || proofImage.Length == 0)
                    return Json(new { success = false, message = "No file received." });

                var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
                var ext = Path.GetExtension(proofImage.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                    return Json(new { success = false, message = "Only image files are allowed." });

                if (proofImage.Length > 5 * 1024 * 1024)
                    return Json(new { success = false, message = "File must be under 5MB." });

                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "payment-proofs");
                Directory.CreateDirectory(uploadsFolder);

                var fileName = $"proof_{paymentId}_{DateTime.Now:yyyyMMddHHmmss}{ext}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                    await proofImage.CopyToAsync(stream);

                var payment = await _context.Payments.FindAsync(paymentId);
                if (payment == null)
                    return Json(new { success = false, message = "Payment record not found." });

                // ← Correct model field names
                payment.ReferenceNumber = referenceNo;
                payment.ProofOfPaymentPath = $"/uploads/payment-proofs/{fileName}";
                payment.Remarks = (payment.Remarks ?? "")
                    + $" | Ref: {referenceNo} | Proof uploaded: {DateTime.Now:g}";

                await _context.SaveChangesAsync();

                return Json(new { success = true, paymentId });
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                return Json(new { success = false, message = inner });
            }
        }

        // ─────────────────────────────────────────────
        // GET /Payment/CheckPaymentStatus?paymentId=X
        // NOTE: your admin sets Status = "Verified" (not "Approved")
        //       so PaymentConfirmation.cshtml polls for "Verified"
        // Uses: ReferenceNumber, VerifiedBy, VerifiedDate
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> CheckPaymentStatus(int paymentId)
        {
            try
            {
                var payment = await _context.Payments.FindAsync(paymentId);
                if (payment == null)
                    return Json(new { success = false, message = "Not found." });

                return Json(new
                {
                    success = true,
                    status = payment.Status,           // "Pending" | "Verified" | "Rejected"
                    referenceNo = payment.ReferenceNumber,  // ← ReferenceNumber
                    amount = payment.Amount,
                    remarks = payment.Remarks,
                    approvedBy = payment.VerifiedBy,       // ← VerifiedBy
                    approvedAt = payment.VerifiedDate?.ToString("MMM dd, yyyy hh:mm tt") // ← VerifiedDate
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    // ── DTOs ─────────────────────────────────────────
    public class PendingPaymentDto
    {
        public int StudentId { get; set; }
        public string PaymentType { get; set; }
        public decimal Amount { get; set; }
        public string SenderNumber { get; set; }
    }
}