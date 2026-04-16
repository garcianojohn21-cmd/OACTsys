// ============================================================
// FILE: Controllers/PaymentAdminController.cs
// ============================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Helpers;
using OACTsys.Models;
using OACTsys.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace OACTsys.Controllers
{
    public class PaymentAdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly PaymentConfirmationEmailService _confirmEmail;

        public PaymentAdminController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            PaymentConfirmationEmailService confirmEmail)
        {
            _context = context;
            _env = env;
            _confirmEmail = confirmEmail;
        }

        // ─────────────────────────────────────────────────────────────
        // ADMIN: Payment Management Page (SuperAdmin only)
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            if (!IsAdminLoggedIn()) return RedirectToAction("Login", "Admin");
            if (!IsSuperAdmin()) return RedirectToAction("AccessDenied", "Admin");
            if (!AdminHelper.HasPermission(HttpContext, "Payments")) return Unauthorized();

            SetLayoutData();

            var gcash = await _context.GCashConfigs.FirstOrDefaultAsync(g => g.IsActive);
            ViewBag.GCashConfig = gcash;

            ViewBag.TotalVerified = await _context.Payments
                .Where(p => p.Status == "Verified" && p.IsActive)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            ViewBag.PendingCount = await _context.Payments
                .Where(p => p.Status == "Pending" && p.IsActive)
                .CountAsync();

            ViewBag.UnpaidBalance = await _context.Payments
                .Where(p => p.Status == "Rejected" && p.IsActive)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            return View("Payments_Index");
        }

        // ─────────────────────────────────────────────────────────────
        // ADMIN: Payment Proof Viewer
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> PaymentProof(int id)
        {
            if (!IsAdminLoggedIn()) return RedirectToAction("Login", "Admin");
            if (!AdminHelper.HasPermission(HttpContext, "Payments")) return Unauthorized();

            SetLayoutData();

            var payment = await _context.Payments
                .Include(p => p.Student)
                    .ThenInclude(s => s.FieldData)
                        .ThenInclude(fd => fd.EnrollmentField)
                .FirstOrDefaultAsync(p => p.PaymentId == id);

            if (payment == null) return NotFound();
            return View("PaymentProof", payment);
        }

        // ─────────────────────────────────────────────────────────────
        // ADMIN: Get Payments JSON  ← FAST VERSION
        //
        // WHY IT WAS SLOW:
        //   The old query did Include(Student).ThenInclude(FieldData)
        //   .ThenInclude(EnrollmentField) for EVERY payment row.
        //   With hundreds of students this causes N+1 queries and loads
        //   thousands of field-data rows just to build a display name.
        //
        // FIX:
        //   1. Project only the columns we need directly from the DB.
        //   2. Resolve the student display name from a separate, tiny
        //      lookup dictionary built in one query — not per-payment.
        //   3. No deep navigation includes at all in the main query.
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetPayments(string status = "all")
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Unauthorized." });
            if (!AdminHelper.HasPermission(HttpContext, "Payments"))
                return Json(new { success = false, message = "No permission." });

            try
            {
                // ── Step 1: Fetch payments with minimal columns ────────
                var paymentsQuery = _context.Payments
                    .Where(p => p.IsActive);

                if (status != "all")
                    paymentsQuery = paymentsQuery.Where(p => p.Status == status);

                var payments = await paymentsQuery
                    .OrderByDescending(p => p.PaymentDate)
                    .Select(p => new
                    {
                        p.PaymentId,
                        p.StudentId,
                        p.PaymentType,
                        p.PaymentMethod,
                        p.Amount,
                        p.ReferenceNumber,
                        p.ProofOfPaymentPath,
                        p.PaymentLocation,
                        p.Status,
                        PaymentDate = p.PaymentDate.ToString("MMM dd, yyyy"),
                        p.VerifiedBy,
                        p.Remarks,
                    })
                    .ToListAsync();

                if (!payments.Any())
                    return Json(new { success = true, data = new List<object>() });

                // ── Step 2: One query to get StudentNumber for all ─────
                var studentIds = payments.Select(p => p.StudentId).Distinct().ToList();

                var studentNumbers = await _context.Students
                    .Where(s => studentIds.Contains(s.StudentId))
                    .Select(s => new { s.StudentId, s.StudentNumber })
                    .ToDictionaryAsync(s => s.StudentId, s => s.StudentNumber ?? "");

                // ── Step 3: One query to get display names ─────────────
                // Pull only last-name / first-name / full-name fields —
                // no entire FieldData table load, just name-related rows.
                var nameKeywords = new[] {
                    "last name", "lastname", "surname", "apellido",
                    "first name", "firstname", "given name", "nombre",
                    "full name", "fullname"
                };

                // EF can't do Contains on arrays in SQL, so load a slim
                // set: only FieldData rows whose EnrollmentField.FieldName
                // contains a name keyword. We filter in memory after.
                var nameFieldIds = await _context.EnrollmentFields
                    .Where(f => f.IsActive)
                    .Select(f => new { f.Id, f.FieldName, f.TemplateKey })
                    .ToListAsync();

                // Identify which field IDs are name-related
                var nameRelatedFieldIds = nameFieldIds
                    .Where(f =>
                        nameKeywords.Any(k =>
                            (f.FieldName ?? "").Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(f.TemplateKey, "full_name", StringComparison.OrdinalIgnoreCase))
                    .Select(f => f.Id)
                    .ToHashSet();

                // Load only those field data rows for our student IDs
                var nameData = await _context.EnrollmentFieldData
                    .Where(fd =>
                        studentIds.Contains(fd.StudentId) &&
                        nameRelatedFieldIds.Contains(fd.EnrollmentFieldId) &&
                        fd.FieldValue != null && fd.FieldValue != "")
                    .Select(fd => new
                    {
                        fd.StudentId,
                        fd.EnrollmentFieldId,
                        fd.FieldValue,
                    })
                    .ToListAsync();

                // Build name lookup: StudentId → display name
                var nameFieldLookup = nameFieldIds.ToDictionary(f => f.Id);

                string ResolveDisplayName(int studentId)
                {
                    var rows = nameData.Where(r => r.StudentId == studentId).ToList();

                    // Priority 1: full_name template key
                    var fullRow = rows.FirstOrDefault(r =>
                        nameFieldLookup.TryGetValue(r.EnrollmentFieldId, out var nf) &&
                        string.Equals(nf.TemplateKey, "full_name", StringComparison.OrdinalIgnoreCase));
                    if (fullRow != null) return fullRow.FieldValue.Trim();

                    // Priority 2: last name + first name
                    string lastName = "", firstName = "";
                    foreach (var row in rows)
                    {
                        if (!nameFieldLookup.TryGetValue(row.EnrollmentFieldId, out var nf)) continue;
                        var fn = (nf.FieldName ?? "").ToLower();
                        if (string.IsNullOrEmpty(lastName) &&
                            new[] { "last name", "lastname", "surname", "apellido" }
                                .Any(k => fn.Contains(k)))
                            lastName = row.FieldValue.Trim();
                        if (string.IsNullOrEmpty(firstName) &&
                            new[] { "first name", "firstname", "given name", "nombre" }
                                .Any(k => fn.Contains(k)))
                            firstName = row.FieldValue.Trim();
                    }

                    if (!string.IsNullOrEmpty(lastName) || !string.IsNullOrEmpty(firstName))
                        return $"{lastName}, {firstName}".Trim(',').Trim();

                    // Fallback: student number
                    return studentNumbers.TryGetValue(studentId, out var sn) ? sn : $"Student #{studentId}";
                }

                // ── Step 4: Assemble result ────────────────────────────
                var result = payments.Select(p => new
                {
                    p.PaymentId,
                    StudentName = ResolveDisplayName(p.StudentId),
                    StudentNumber = studentNumbers.TryGetValue(p.StudentId, out var sn) ? sn : "",
                    p.PaymentType,
                    p.PaymentMethod,
                    p.Amount,
                    p.ReferenceNumber,
                    p.ProofOfPaymentPath,
                    p.PaymentLocation,
                    p.Status,
                    p.PaymentDate,
                    p.VerifiedBy,
                    p.Remarks,
                }).ToList();

                return Json(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ADMIN: Get Student by ID for Manual Payment modal
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetStudentById(int id)
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Unauthorized." });
            if (!AdminHelper.HasPermission(HttpContext, "Payments"))
                return Json(new { success = false, message = "No permission." });

            try
            {
                var student = await _context.Students
                    .Include(s => s.FieldData)
                        .ThenInclude(fd => fd.EnrollmentField)
                    .FirstOrDefaultAsync(s => s.StudentId == id && s.IsActive);

                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                var fields = student.FieldData ?? new List<EnrollmentFieldData>();
                var lastName = fields.FirstOrDefault(f =>
                    f.EnrollmentField != null &&
                    new[] { "last name", "lastname", "surname", "apellido" }
                        .Any(k => f.EnrollmentField.FieldName.ToLower().Contains(k)))
                    ?.FieldValue ?? "";
                var firstName = fields.FirstOrDefault(f =>
                    f.EnrollmentField != null &&
                    new[] { "first name", "firstname", "given name", "nombre" }
                        .Any(k => f.EnrollmentField.FieldName.ToLower().Contains(k)))
                    ?.FieldValue ?? "";

                string fullName;
                if (!string.IsNullOrWhiteSpace(lastName) || !string.IsNullOrWhiteSpace(firstName))
                    fullName = $"{lastName}, {firstName}".Trim(',').Trim();
                else
                    fullName = student.StudentNumber ?? $"Student #{id}";

                return Json(new
                {
                    success = true,
                    studentId = student.StudentId,
                    fullName,
                    studentNumber = student.StudentNumber,
                    program = student.Program ?? "",
                    yearLevel = student.CurrentYearLevel,
                    semester = student.CurrentSemester,
                    paymentStatus = student.PaymentStatus ?? "",
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ADMIN: Verify On-Site Payment (Mark as Paid)
        //
        //   Saves the OR number onto the payment record, marks it
        //   Verified, updates Student.PaymentStatus, then sends the
        //   confirmation email to the student.
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> VerifyOnsitePayment(
            [FromBody] VerifyOnsiteDto dto)
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Unauthorized." });
            if (!AdminHelper.HasPermission(HttpContext, "Payments"))
                return Json(new { success = false, message = "No permission." });

            if (string.IsNullOrWhiteSpace(dto?.OfficialReceiptNumber))
                return Json(new { success = false, message = "OR number is required." });

            try
            {
                var payment = await _context.Payments.FindAsync(dto.PaymentId);
                if (payment == null)
                    return Json(new { success = false, message = "Payment record not found." });

                var adminName = HttpContext.Session.GetString("AdminName");

                // ── Save OR number + verify ────────────────────────────
                payment.ReferenceNumber = dto.OfficialReceiptNumber.Trim();
                payment.Status = "Verified";
                payment.VerifiedDate = DateTime.Now;
                payment.VerifiedBy = adminName;
                payment.Remarks = string.IsNullOrWhiteSpace(dto.Remarks)
                    ? $"On-site payment collected at cashier. OR: {dto.OfficialReceiptNumber.Trim()}"
                    : dto.Remarks.Trim();

                // ── Update student payment status ─────────────────────
                var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.StudentId == payment.StudentId && s.IsActive);
                if (student != null)
                {
                    student.PaymentStatus = "Paid";
                    student.LastModifiedDate = DateTime.Now;
                }

                await _context.SaveChangesAsync();

                // ── Send confirmation email ───────────────────────────
                var emailResult = await _confirmEmail
                    .SendPaymentConfirmationAsync(payment.StudentId, payment.PaymentId);

                return Json(new
                {
                    success = true,
                    emailSent = emailResult.Success,
                    message = emailResult.Success
                        ? $"Payment verified and confirmation e-mail sent to {emailResult.RecipientEmail}."
                        : $"Payment verified. E-mail could not be sent: {emailResult.ErrorMessage}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ADMIN: Approve / Reject / Archive / Restore Payment
        //   Status == "Verified"  → approve GCash; send email
        //   Status == "Rejected"  → decline / archive
        //   Status == "Pending"   → restore from archive
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> UpdatePaymentStatus(
            [FromBody] PaymentStatusDto dto)
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Unauthorized." });
            if (!AdminHelper.HasPermission(HttpContext, "Payments"))
                return Json(new { success = false, message = "No permission." });

            try
            {
                var payment = await _context.Payments.FindAsync(dto.PaymentId);
                if (payment == null)
                    return Json(new { success = false, message = "Payment not found." });

                var adminName = HttpContext.Session.GetString("AdminName");

                payment.Status = dto.Status;
                payment.VerifiedDate = DateTime.Now;
                payment.VerifiedBy = adminName;
                payment.Remarks = dto.Remarks;

                await _context.SaveChangesAsync();

                if (dto.Status == "Verified")
                {
                    var emailResult = await _confirmEmail
                        .SendPaymentConfirmationAsync(payment.StudentId, payment.PaymentId);

                    if (!emailResult.Success)
                        return Json(new
                        {
                            success = true,
                            emailSent = false,
                            emailError = emailResult.ErrorMessage,
                            message = $"Payment verified. E-mail could not be sent: {emailResult.ErrorMessage}"
                        });

                    return Json(new
                    {
                        success = true,
                        emailSent = true,
                        message = $"Payment verified and confirmation e-mail sent to {emailResult.RecipientEmail}."
                    });
                }

                if (dto.Status == "Rejected")
                    return Json(new { success = true, message = "Payment declined and moved to the Archived tab." });

                return Json(new { success = true, message = $"Payment status updated to {dto.Status}." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ADMIN: Record Manual / Office Payment (SuperAdmin only)
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> RecordManualPayment(
            [FromBody] ManualPaymentDto dto)
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Unauthorized." });
            if (!IsSuperAdmin())
                return Json(new { success = false, message = "SuperAdmin access required." });

            try
            {
                var adminName = HttpContext.Session.GetString("AdminName");

                var payment = new Payment
                {
                    StudentId = dto.StudentId,
                    PaymentType = dto.PaymentType,
                    PaymentMethod = "Cash",
                    Amount = dto.Amount,
                    ReferenceNumber = dto.ReceiptNumber,
                    PaymentLocation = dto.PaymentLocation,
                    Status = "Verified",
                    PaymentDate = DateTime.Now,
                    VerifiedDate = DateTime.Now,
                    VerifiedBy = adminName,
                    Remarks = $"Manual office payment. OR: {dto.ReceiptNumber}",
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };

                _context.Payments.Add(payment);

                var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.StudentId == dto.StudentId && s.IsActive);
                if (student != null)
                {
                    student.PaymentStatus = "Paid";
                    student.LastModifiedDate = DateTime.Now;
                }

                await _context.SaveChangesAsync();

                var emailResult = await _confirmEmail
                    .SendPaymentConfirmationAsync(dto.StudentId, payment.PaymentId);

                return Json(new
                {
                    success = true,
                    emailSent = emailResult.Success,
                    message = emailResult.Success
                        ? "Manual payment recorded and confirmation e-mail sent to student."
                        : $"Manual payment recorded. E-mail failed: {emailResult.ErrorMessage}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ADMIN: Save GCash Configuration (SuperAdmin only)
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SaveGCashConfig(
            [FromForm] GCashConfigDto dto)
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Unauthorized." });
            if (!IsSuperAdmin())
                return Json(new { success = false, message = "SuperAdmin access required." });

            try
            {
                var oldConfigs = await _context.GCashConfigs.Where(g => g.IsActive).ToListAsync();
                foreach (var old in oldConfigs) old.IsActive = false;

                string qrPath = null;
                if (dto.QrCodeFile != null && dto.QrCodeFile.Length > 0)
                {
                    var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "gcash");
                    Directory.CreateDirectory(uploadsDir);
                    var fileName = $"gcash_qr_{DateTime.Now:yyyyMMddHHmmss}" +
                                   Path.GetExtension(dto.QrCodeFile.FileName);
                    var filePath = Path.Combine(uploadsDir, fileName);
                    using var stream = new FileStream(filePath, FileMode.Create);
                    await dto.QrCodeFile.CopyToAsync(stream);
                    qrPath = $"/uploads/gcash/{fileName}";
                }
                else
                {
                    qrPath = oldConfigs.FirstOrDefault()?.QrCodePath;
                }

                var config = new GCashConfig
                {
                    AccountName = dto.AccountName,
                    GCashNumber = dto.GCashNumber.Replace("-", "").Replace(" ", ""),
                    QrCodePath = qrPath,
                    PaymentDescription = dto.PaymentDescription ?? "School Fee Payment",
                    IsActive = true,
                    UpdatedAt = DateTime.Now,
                    UpdatedBy = HttpContext.Session.GetString("AdminName")
                };

                _context.GCashConfigs.Add(config);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "GCash configuration saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ADMIN + STUDENT: Get GCash Config (JSON)
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetGCashConfig()
        {
            var config = await _context.GCashConfigs
                .Where(g => g.IsActive)
                .OrderByDescending(g => g.UpdatedAt)
                .FirstOrDefaultAsync();

            if (config == null)
                return Json(new { success = false, message = "No GCash configuration found." });

            return Json(new
            {
                success = true,
                accountName = config.AccountName,
                gcashNumber = config.GCashNumber,
                qrCodePath = config.QrCodePath,
                paymentDescription = config.PaymentDescription
            });
        }

        // ─────────────────────────────────────────────────────────────
        // STUDENT: Initiate GCash Payment
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> InitiateGCashPayment(
            [FromBody] InitiateGCashDto dto)
        {
            try
            {
                var config = await _context.GCashConfigs
                    .Where(g => g.IsActive)
                    .OrderByDescending(g => g.UpdatedAt)
                    .FirstOrDefaultAsync();

                if (config == null)
                    return Json(new { success = false, message = "GCash is not configured yet." });

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
                    Remarks = "Awaiting GCash confirmation"
                };

                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();

                var cleanNumber = config.GCashNumber.Replace("-", "").Replace(" ", "");
                var note = Uri.EscapeDataString($"{config.PaymentDescription} - {dto.PaymentType}");
                var deepLink = $"gcash://payment?amount={dto.Amount}&receiverNumber={cleanNumber}&note={note}";

                return Json(new
                {
                    success = true,
                    paymentId = payment.PaymentId,
                    deepLink,
                    gcashNumber = cleanNumber,
                    accountName = config.AccountName,
                    amount = dto.Amount,
                    qrCodePath = config.QrCodePath
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // STUDENT: Submit GCash Reference Number + Proof
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SubmitGCashReference(
            [FromBody] SubmitGCashReferenceDto dto)
        {
            try
            {
                var payment = await _context.Payments.FindAsync(dto.PaymentId);
                if (payment == null)
                    return Json(new { success = false, message = "Payment record not found." });

                payment.ReferenceNumber = dto.ReferenceNumber;
                payment.ProofOfPaymentPath = dto.ProofPath;
                payment.Status = "Pending";

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Reference number submitted. Awaiting admin verification." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // STUDENT: Get My Payments
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetStudentPayments(int studentId)
        {
            try
            {
                var payments = await _context.Payments
                    .Where(p => p.StudentId == studentId && p.IsActive)
                    .OrderByDescending(p => p.PaymentDate)
                    .Select(p => new
                    {
                        p.PaymentId,
                        p.PaymentType,
                        p.PaymentMethod,
                        p.Amount,
                        p.ReferenceNumber,
                        p.Status,
                        PaymentDate = p.PaymentDate.ToString("MMM dd, yyyy"),
                        p.VerifiedBy,
                        p.Remarks
                    })
                    .ToListAsync();

                return Json(new { success = true, data = payments });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // STUDENT: GCash payment page
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> PayGCash(int studentId, string type, decimal amount)
        {
            var student = await _context.Students.FindAsync(studentId);
            if (student == null) return NotFound();
            ViewBag.TuitionAmount = amount;
            ViewBag.PaymentType = type;
            return View(student);
        }

        // ─────────────────────────────────────────────────────────────
        // STUDENT: GCash method page (redirect target)
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> GcashMethod(
            int studentId, decimal amount, string paymentType)
        {
            var student = await _context.Students.FindAsync(studentId);
            if (student == null) return NotFound();

            var config = await _context.GCashConfigs
                .Where(g => g.IsActive)
                .OrderByDescending(g => g.UpdatedAt)
                .FirstOrDefaultAsync();

            ViewBag.GCashConfig = config;
            ViewBag.Amount = amount;
            ViewBag.PaymentType = paymentType;
            return View(student);
        }

        // ─────────────────────────────────────────────────────────────
        // STUDENT: Upload proof of payment image
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> UploadProof(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "No file provided." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                return Json(new { success = false, message = "Only image files are allowed." });

            if (file.Length > 5 * 1024 * 1024)
                return Json(new { success = false, message = "File must be under 5 MB." });

            var dir = Path.Combine(_env.WebRootPath, "uploads", "proofs");
            Directory.CreateDirectory(dir);
            var fileName = $"proof_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
            var path = Path.Combine(dir, fileName);

            using var stream = new FileStream(path, FileMode.Create);
            await file.CopyToAsync(stream);

            return Json(new { success = true, path = $"/uploads/proofs/{fileName}" });
        }

        // ─────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────
        private bool IsAdminLoggedIn() =>
            !string.IsNullOrEmpty(HttpContext.Session.GetString("AdminRole"));

        private bool IsSuperAdmin() =>
            HttpContext.Session.GetString("AdminRole") == "SuperAdmin";

        private void SetLayoutData()
        {
            ViewBag.AdminName = HttpContext.Session.GetString("AdminName");
            ViewBag.AdminRole = HttpContext.Session.GetString("AdminRole");
            ViewBag.Permissions = AdminHelper.GetSessionPermissions(HttpContext);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────

    public class PaymentStatusDto
    {
        public int PaymentId { get; set; }
        public string Status { get; set; }
        public string Remarks { get; set; }
    }

    /// <summary>
    /// Used by VerifyOnsitePayment — saves the OR number onto the
    /// payment record before marking it Verified.
    /// </summary>
    public class VerifyOnsiteDto
    {
        public int PaymentId { get; set; }
        public string OfficialReceiptNumber { get; set; }
        public string Remarks { get; set; }
    }

    public class ManualPaymentDto
    {
        public int StudentId { get; set; }
        public string PaymentType { get; set; }
        public decimal Amount { get; set; }
        public string ReceiptNumber { get; set; }
        public string PaymentLocation { get; set; }
    }

    public class GCashConfigDto
    {
        public string AccountName { get; set; }
        public string GCashNumber { get; set; }
        public string PaymentDescription { get; set; }
        public IFormFile QrCodeFile { get; set; }
    }

    public class InitiateGCashDto
    {
        public int StudentId { get; set; }
        public string PaymentType { get; set; }
        public decimal Amount { get; set; }
    }

    public class SubmitGCashReferenceDto
    {
        public int PaymentId { get; set; }
        public string ReferenceNumber { get; set; }
        public string ProofPath { get; set; }
    }
}