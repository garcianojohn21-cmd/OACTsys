using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Models;
using System.Text;
using System.Text.Json;

namespace OACTsys.Controllers
{
    public class PayMongoController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public PayMongoController(
            ApplicationDbContext context,
            IConfiguration config,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST: /PayMongo/CreateQRPh
        // QR Ph uses Payment Intent → Payment Method → Attach workflow
        // Returns a base64 QR image to display directly on the page
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> CreateQRPh([FromBody] GCashSourceDto dto)
        {
            try
            {
                var secretKey = _config["PayMongo:SecretKey"]
                    ?? throw new Exception("PayMongo SecretKey not configured.");

                // PayMongo minimum is PHP 100
                decimal chargeAmount = dto.Amount < 100 ? 100m : dto.Amount;
                decimal creditBack = dto.Amount < 100 ? 100m - dto.Amount : 0m;
                var amountCentavos = (int)(chargeAmount * 100);

                var authHeader = "Basic " + Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(secretKey + ":"));

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization", authHeader);

                // ── STEP 1: Create Payment Intent ─────────────────────────
                var intentPayload = JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        attributes = new
                        {
                            amount = amountCentavos,
                            currency = "PHP",
                            payment_method_allowed = new[] { "qrph" },
                            description = $"OACT {dto.PaymentType} - Student #{dto.StudentId}",
                            statement_descriptor = "OACT School"
                        }
                    }
                });

                var intentRes = await client.PostAsync(
                    "https://api.paymongo.com/v1/payment_intents",
                    new StringContent(intentPayload, Encoding.UTF8, "application/json"));

                var intentBody = await intentRes.Content.ReadAsStringAsync();
                if (!intentRes.IsSuccessStatusCode)
                    return Json(new { success = false, message = "PayMongo error: " + intentBody });

                using var intentDoc = JsonDocument.Parse(intentBody);
                var intentId = intentDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
                var clientKey = intentDoc.RootElement
                    .GetProperty("data").GetProperty("attributes")
                    .GetProperty("client_key").GetString();

                // ── STEP 2: Create Payment Method (qrph) ─────────────────
                var methodPayload = JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        attributes = new
                        {
                            type = "qrph",
                            billing = new
                            {
                                name = dto.StudentName ?? "Student",
                                email = dto.Email ?? "student@oact.edu.ph",
                                phone = dto.SenderNumber != null ? "+63" + dto.SenderNumber : null
                            }
                        }
                    }
                });

                var methodRes = await client.PostAsync(
                    "https://api.paymongo.com/v1/payment_methods",
                    new StringContent(methodPayload, Encoding.UTF8, "application/json"));

                var methodBody = await methodRes.Content.ReadAsStringAsync();
                if (!methodRes.IsSuccessStatusCode)
                    return Json(new { success = false, message = "PayMongo error: " + methodBody });

                using var methodDoc = JsonDocument.Parse(methodBody);
                var methodId = methodDoc.RootElement.GetProperty("data").GetProperty("id").GetString();

                // ── STEP 3: Attach Payment Method to Intent ───────────────
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var successUrl = $"{baseUrl}/PayMongo/Success?studentId={dto.StudentId}&paymentType={Uri.EscapeDataString(dto.PaymentType)}&amount={dto.Amount}";
                var failedUrl = $"{baseUrl}/PayMongo/Failed?studentId={dto.StudentId}&paymentType={Uri.EscapeDataString(dto.PaymentType)}";

                var attachPayload = JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        attributes = new
                        {
                            payment_method = methodId,
                            client_key = clientKey,
                            return_url = successUrl
                        }
                    }
                });

                var attachRes = await client.PostAsync(
                    $"https://api.paymongo.com/v1/payment_intents/{intentId}/attach",
                    new StringContent(attachPayload, Encoding.UTF8, "application/json"));

                var attachBody = await attachRes.Content.ReadAsStringAsync();
                if (!attachRes.IsSuccessStatusCode)
                    return Json(new { success = false, message = "PayMongo error: " + attachBody });

                using var attachDoc = JsonDocument.Parse(attachBody);
                var attachAttrs = attachDoc.RootElement
                    .GetProperty("data").GetProperty("attributes");

                // ── STEP 4: Extract QR image from next_action ─────────────
                // next_action.type == "consume_qr"
                // next_action.code.image_url == "data:image/png;base64,..."
                string qrImageUrl = null;
                if (attachAttrs.TryGetProperty("next_action", out var nextAction))
                {
                    if (nextAction.TryGetProperty("code", out var code))
                    {
                        qrImageUrl = code.GetProperty("image_url").GetString();
                    }
                }

                // ── Save pending payment record ───────────────────────────
                var remarks = $"pi:{intentId}|+63{dto.SenderNumber}";
                if (remarks.Length > 490) remarks = remarks[..490];
                if (creditBack > 0) remarks += $"|credit:PHP{creditBack:N2}";

                try
                {
                    var studentExists = await _context.Students
                        .AnyAsync(s => s.StudentId == dto.StudentId && s.IsActive);

                    if (studentExists)
                    {
                        var existing = await _context.Payments.FirstOrDefaultAsync(p =>
                            p.StudentId == dto.StudentId &&
                            p.PaymentType == dto.PaymentType &&
                            p.PaymentMethod == "GCash" &&
                            p.Status == "Pending" &&
                            p.IsActive);

                        if (existing == null)
                        {
                            _context.Payments.Add(new Payment
                            {
                                StudentId = dto.StudentId,
                                PaymentType = dto.PaymentType,
                                PaymentMethod = "GCash",
                                Amount = dto.Amount,
                                Status = "Pending",
                                PaymentDate = DateTime.Now,
                                IsActive = true,
                                CreatedDate = DateTime.Now,
                                Remarks = remarks
                            });
                        }
                        else
                        {
                            existing.Remarks = remarks;
                        }

                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception dbEx)
                {
                    Console.WriteLine($"[PayMongo] DB save error (non-fatal): {dbEx.InnerException?.Message ?? dbEx.Message}");
                }

                return Json(new
                {
                    success = true,
                    qrImageUrl,          // base64 QR image — display directly on page
                    intentId,
                    actualAmount = dto.Amount,
                    chargedAmount = chargeAmount,
                    creditBack
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET: /PayMongo/Success
        // ─────────────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Success(int studentId, string paymentType, decimal amount)
        {
            try
            {
                var payment = await _context.Payments.FirstOrDefaultAsync(p =>
                    p.StudentId == studentId &&
                    p.PaymentType == paymentType &&
                    p.PaymentMethod == "GCash" &&
                    p.IsActive);

                if (payment != null && payment.Status == "Pending")
                {
                    payment.Status = "Verified";
                    payment.VerifiedDate = DateTime.Now;
                    payment.VerifiedBy = "PayMongo Auto-Verified";
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PayMongo] Success DB error: {ex.InnerException?.Message ?? ex.Message}");
            }

            ViewBag.StudentId = studentId;
            ViewBag.PaymentType = paymentType;
            ViewBag.Amount = amount;
            return View("~/Views/PayMongo/GcashSuccess.cshtml");
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET: /PayMongo/Failed
        // ─────────────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Failed(int studentId, string paymentType)
        {
            ViewBag.StudentId = studentId;
            ViewBag.PaymentType = paymentType;
            return View("~/Views/PayMongo/GcashFailed.cshtml");
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST: /PayMongo/Webhook
        // Subscribe to: payment.paid event in PayMongo dashboard
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Webhook()
        {
            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                using var doc = JsonDocument.Parse(body);

                var eventType = doc.RootElement
                    .GetProperty("data").GetProperty("attributes")
                    .GetProperty("type").GetString();

                // payment.paid fires when QR Ph is scanned and confirmed
                if (eventType == "payment.paid")
                {
                    var paymentData = doc.RootElement
                        .GetProperty("data").GetProperty("attributes")
                        .GetProperty("data");

                    var intentId = paymentData
                        .GetProperty("attributes")
                        .GetProperty("payment_intent_id").GetString();

                    // Find by payment intent ID stored in Remarks
                    var payment = await _context.Payments.FirstOrDefaultAsync(p =>
                        p.Remarks != null &&
                        p.Remarks.Contains(intentId!) &&
                        p.Status == "Pending");

                    if (payment != null)
                    {
                        payment.Status = "Verified";
                        payment.VerifiedDate = DateTime.Now;
                        payment.VerifiedBy = "PayMongo QR Ph Auto";
                        await _context.SaveChangesAsync();
                    }
                }

                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PayMongo] Webhook error: {ex.InnerException?.Message ?? ex.Message}");
                return StatusCode(500, ex.Message);
            }
        }
    }

    public class GCashSourceDto
    {
        public int StudentId { get; set; }
        public string PaymentType { get; set; }
        public decimal Amount { get; set; }
        public string? SenderNumber { get; set; }
        public string? StudentName { get; set; }
        public string? Email { get; set; }
    }
}