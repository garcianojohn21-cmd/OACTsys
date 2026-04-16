// ============================================================
// FILE: Services/PaymentConfirmationEmailService.cs
// ============================================================
// Called by PaymentAdminController.UpdatePaymentStatus()
// when admin sets a payment to "Verified".
//
// What this service does:
//   1. Generates a random 6-digit PIN
//   2. Saves PIN → Student.TokenUsed  (StringLength 100 ✓)
//   3. Sets Student.PaymentStatus = "Verified"
//   4. Sets Student.IsActive = true
//   5. Sends a styled HTML e-mail with the PIN and payment summary
//
// The student uses the PIN on your account-creation page:
//   StudentNumber + TokenUsed (PIN) → creates username/password
//   After account created → clear TokenUsed, set HasAccount = true
//
// ── Register in Program.cs ──────────────────────────────────
//   builder.Services.AddScoped<PaymentConfirmationEmailService>();
// ============================================================

using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OACTsys.Data;
using OACTsys.Models;

namespace OACTsys.Services
{
    public class PaymentConfirmationEmailService
    {
        private readonly ApplicationDbContext _context;
        private readonly SmtpSettings _smtp;
        private readonly ILogger<PaymentConfirmationEmailService> _logger;

        public PaymentConfirmationEmailService(
            ApplicationDbContext context,
            IOptions<SmtpSettings> smtp,
            ILogger<PaymentConfirmationEmailService> logger)
        {
            _context = context;
            _smtp = smtp.Value;
            _logger = logger;
        }

        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Call this right after admin verifies a payment.
        /// Generates PIN → saves to Student.TokenUsed → sends email.
        /// </summary>
        public async Task<EmailResult> SendPaymentConfirmationAsync(int studentId, int paymentId)
        {
            try
            {
                // ── Load student with field data ───────────────────────────
                var student = await _context.Students
                    .Include(s => s.FieldData)
                        .ThenInclude(fd => fd.EnrollmentField)
                    .FirstOrDefaultAsync(s => s.StudentId == studentId);

                if (student == null)
                    return Fail("", $"Student ID {studentId} not found.");

                // ── Load payment ───────────────────────────────────────────
                var payment = await _context.Payments.FindAsync(paymentId);
                if (payment == null)
                    return Fail("", $"Payment ID {paymentId} not found.");

                // ── Resolve recipient email ────────────────────────────────
                string? recipientEmail = ResolveEmail(student);
                if (string.IsNullOrWhiteSpace(recipientEmail))
                    return Fail("", "No e-mail address found for this student.");

                // ── Generate 6-digit PIN ───────────────────────────────────
                string pin = GeneratePin();

                // ── Save PIN to Student.TokenUsed ──────────────────────────
                // Student.TokenUsed  [StringLength(100)] — stores the PIN
                // Student.PaymentStatus                  — set to "Verified"
                // Student.IsActive                       — activated
                student.TokenUsed = pin;
                student.PaymentStatus = "Verified";
                student.EnrollmentStatus = "Enrolled";
                student.IsActive = true;
                student.LastModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "PIN generated and saved to Student.TokenUsed — StudentId={Id}, PaymentId={Pid}",
                    studentId, paymentId);

                // ── Build and send email ───────────────────────────────────
                string studentName = ResolveStudentName(student);
                string subject = $"Payment Confirmed - Your Portal Access PIN | {studentName}";
                string body = BuildHtmlBody(student, payment, pin, studentName);

                await SendEmailAsync(recipientEmail, subject, body);

                _logger.LogInformation(
                    "Payment confirmation email sent → {Email}  StudentId={Id}  PaymentId={Pid}",
                    recipientEmail, studentId, paymentId);

                return new EmailResult { Success = true, RecipientEmail = recipientEmail };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send payment confirmation for StudentId={Id}", studentId);
                return Fail("", ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Generate a 6-digit numeric PIN (100000–999999)
        // ══════════════════════════════════════════════════════════════════
        private static string GeneratePin()
            => Random.Shared.Next(100000, 999999).ToString("D6");

        // ══════════════════════════════════════════════════════════════════
        // Resolve student email — same priority as EnrollmentAcknowledgement
        // ══════════════════════════════════════════════════════════════════
        private static string? ResolveEmail(Student student)
        {
            if (student.FieldData != null)
            {
                // Priority 1: FieldType == "email"
                var byType = student.FieldData.FirstOrDefault(fd =>
                    fd.EnrollmentField != null &&
                    string.Equals(fd.EnrollmentField.FieldType, "email",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(fd.FieldValue));
                if (byType != null) return byType.FieldValue;

                // Priority 2: TemplateKey == "email_address"
                var byKey = student.FieldData.FirstOrDefault(fd =>
                    fd.EnrollmentField != null &&
                    string.Equals(fd.EnrollmentField.TemplateKey, "email_address",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(fd.FieldValue));
                if (byKey != null) return byKey.FieldValue;
            }

            // Priority 3: Student.Email column
            return string.IsNullOrWhiteSpace(student.Email) ? null : student.Email;
        }

        // ══════════════════════════════════════════════════════════════════
        // Resolve display name from FieldData
        // ══════════════════════════════════════════════════════════════════
        private static string ResolveStudentName(Student student)
        {
            if (student.FieldData != null)
            {
                // Try TemplateKey "full_name"
                var fullName = student.FieldData.FirstOrDefault(fd =>
                    fd.EnrollmentField != null &&
                    string.Equals(fd.EnrollmentField.TemplateKey, "full_name",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(fd.FieldValue));
                if (fullName != null) return fullName.FieldValue!;

                // Fallback: last + first name fields
                string GetField(params string[] names)
                {
                    foreach (var n in names)
                    {
                        var v = student.FieldData.FirstOrDefault(f =>
                            f.EnrollmentField != null &&
                            f.EnrollmentField.FieldName.Contains(n,
                                StringComparison.OrdinalIgnoreCase))?.FieldValue;
                        if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                    }
                    return "";
                }
                var ln = GetField("last name", "lastname", "surname");
                var fn = GetField("first name", "firstname", "given name");
                if (!string.IsNullOrEmpty(ln) || !string.IsNullOrEmpty(fn))
                    return $"{ln}, {fn}".Trim(',').Trim();
            }
            return student.StudentNumber ?? "Student";
        }

        // ══════════════════════════════════════════════════════════════════
        // HTML email — white/navy, table-based, Gmail-compatible (no <style>)
        // ══════════════════════════════════════════════════════════════════
        private static string BuildHtmlBody(Student student, Payment payment,
            string pin, string studentName)
        {
            static string E(string? v) => WebUtility.HtmlEncode(v ?? "");

            int year = DateTime.Now.Year;
            string stuNo = E(student.StudentNumber ?? "—");
            string program = E(student.Program ?? "—");
            string yearLvl = student.CurrentYearLevel switch
            {
                1 => "1st Year",
                2 => "2nd Year",
                3 => "3rd Year",
                4 => "4th Year",
                _ => $"{student.CurrentYearLevel}th Year"
            };
            string semester = student.CurrentSemester switch
            {
                1 => "1st Semester",
                2 => "2nd Semester",
                _ => $"{student.CurrentSemester}"
            };
            string amount = payment.Amount.ToString("N2");
            string payType = E(payment.PaymentType ?? "Payment");
            string payMethod = E(payment.PaymentMethod ?? "GCash");
            string refNo = E(payment.ReferenceNumber ?? "—");
            string verAt = (payment.VerifiedDate ?? DateTime.Now).ToString("MMMM d, yyyy hh:mm tt");
            string verBy = E(payment.VerifiedBy ?? "Admin");

            // Individual PIN digit boxes
            string pinBoxes = string.Concat(pin.Select(c =>
                $@"<td align=""center"" valign=""middle""
                      style=""width:46px;height:58px;background:#0d2f6e;
                             border-radius:8px;border:2px solid #1a4fa0;
                             font-family:'Courier New',monospace;font-size:28px;
                             font-weight:900;color:#ffffff;padding:0;"">
                     {c}
                   </td>
                   <td width=""6"" style=""font-size:0;"">&nbsp;</td>"));

            // Reusable detail row
            static string Row(string label, string value) =>
                $@"<tr>
                     <td width=""44%"" style=""padding:10px 14px;font-family:Arial,sans-serif;
                         font-size:13px;color:#1e3a5f;font-weight:600;background:#f0f6ff;
                         border-top:1px solid #dce8f5;"">{label}</td>
                     <td style=""padding:10px 14px;font-family:Arial,sans-serif;
                         font-size:13px;color:#0a2240;font-weight:700;
                         border-top:1px solid #dce8f5;"">{value}</td>
                   </tr>";

            // Reusable step row
            static string Step(string num, string text) =>
                $@"<table cellpadding=""0"" cellspacing=""0"" border=""0""
                         style=""margin-bottom:13px;width:100%;"">
                     <tr>
                       <td width=""36"" valign=""top"">
                         <table cellpadding=""0"" cellspacing=""0"" border=""0"">
                           <tr>
                             <td width=""28"" height=""28"" align=""center"" valign=""middle""
                                 style=""background:#0d2f6e;border-radius:50%;
                                        font-family:Arial,sans-serif;font-size:13px;
                                        font-weight:800;color:#ffffff;line-height:28px;"">
                               {num}
                             </td>
                           </tr>
                         </table>
                       </td>
                       <td valign=""middle"" style=""padding-left:10px;font-family:Arial,sans-serif;
                           font-size:13px;color:#1e3a5f;line-height:1.7;"">
                         {text}
                       </td>
                     </tr>
                   </table>";

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8""/>
  <meta name=""viewport"" content=""width=device-width,initial-scale=1""/>
  <title>Payment Confirmed</title>
</head>
<body style=""margin:0;padding:0;background:#e8eef7;"">

<table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
       style=""background:#e8eef7;padding:32px 0;"">
  <tr><td align=""center"">

    <!-- ═══ CARD ═══ -->
    <table width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0""
           style=""background:#ffffff;border-radius:12px;border:1px solid #c9d9f0;"">

      <!-- HEADER -->
      <tr>
        <td align=""center""
            style=""background:#0d2f6e;padding:36px 40px 28px;
                   border-radius:12px 12px 0 0;"">
          <div style=""font-size:38px;line-height:1;margin-bottom:14px;"">&#9992;&#65039;</div>
          <div style=""font-family:Arial,sans-serif;font-size:10px;font-weight:700;
                       letter-spacing:3px;text-transform:uppercase;color:#90b8e8;
                       margin-bottom:8px;"">Payment Confirmation</div>
          <div style=""font-family:Arial,sans-serif;font-size:20px;font-weight:800;
                       color:#ffffff;line-height:1.4;margin-bottom:14px;"">
            Orson Aerospace College<br/>and Technology, Inc.
          </div>
          <table cellpadding=""0"" cellspacing=""0"" border=""0"">
            <tr>
              <td style=""background:#16a34a;border-radius:20px;padding:6px 22px;"">
                <span style=""font-family:Arial,sans-serif;font-size:12px;font-weight:800;
                              letter-spacing:1px;text-transform:uppercase;color:#ffffff;"">
                  &#10003;&nbsp; Payment Verified
                </span>
              </td>
            </tr>
          </table>
        </td>
      </tr>

      <!-- GREETING -->
      <tr>
        <td style=""padding:32px 40px 8px;"">
          <p style=""font-family:Arial,sans-serif;font-size:15px;font-weight:700;
                     color:#0d2f6e;margin:0 0 10px;"">
            Congratulations, {E(studentName)}!
          </p>
          <p style=""font-family:Arial,sans-serif;font-size:13px;line-height:1.8;
                     color:#3a5f8a;margin:0;"">
            Your payment has been <strong style=""color:#16a34a;"">successfully verified</strong>
            by the Registrar&apos;s Office. You may now create your
            <strong style=""color:#0d2f6e;"">Student Portal Account</strong> using
            the PIN code below.
          </p>
        </td>
      </tr>

      <!-- PIN SECTION -->
      <tr>
        <td style=""padding:28px 40px;"">
          <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
                 style=""background:#f0f6ff;border:2px solid #0d2f6e;border-radius:12px;
                         overflow:hidden;"">
            <!-- PIN heading -->
            <tr>
              <td align=""center""
                  style=""background:#0d2f6e;padding:14px 20px;"">
                <span style=""font-family:Arial,sans-serif;font-size:11px;font-weight:800;
                              letter-spacing:2px;text-transform:uppercase;color:#b8d4f5;"">
                  &#128272;&nbsp; Your Portal Access PIN Code
                </span>
              </td>
            </tr>
            <!-- PIN digits -->
            <tr>
              <td align=""center"" style=""padding:28px 20px 16px;"">
                <table cellpadding=""0"" cellspacing=""0"" border=""0"">
                  <tr>{pinBoxes}</tr>
                </table>
              </td>
            </tr>
            <!-- PIN sub-label -->
            <tr>
              <td align=""center"" style=""padding:0 24px 18px;"">
                <p style=""font-family:Arial,sans-serif;font-size:12px;line-height:1.7;
                           color:#1e3a5f;margin:0;text-align:center;"">
                  This <strong>6-digit PIN</strong> is linked to your student account.<br/>
                  Use it together with your <strong>Student Number ({stuNo})</strong>
                  to register on the portal.
                </p>
              </td>
            </tr>
            <!-- Warning bar -->
            <tr>
              <td style=""background:#fff8e1;border-top:1px solid #fde68a;padding:11px 20px;"">
                <p style=""font-family:Arial,sans-serif;font-size:11px;line-height:1.6;
                           color:#92400e;margin:0;text-align:center;"">
                  &#9888;&#65039; <strong>Keep this PIN private.</strong>
                  It is invalidated once you successfully create your account.
                </p>
              </td>
            </tr>
          </table>
        </td>
      </tr>

      <!-- HOW TO USE -->
      <tr>
        <td style=""padding:0 40px 28px;"">
          <p style=""font-family:Arial,sans-serif;font-size:11px;font-weight:800;
                     letter-spacing:2px;text-transform:uppercase;color:#0d2f6e;
                     margin:0 0 16px;"">
            &#128203;&nbsp; How to Create Your Account
          </p>
          {Step("1", "Go to the <strong>OACT Student Portal</strong> registration/sign-up page.")}
          {Step("2", $"Enter your <strong>Student Number ({stuNo})</strong> and the <strong>6-digit PIN</strong> above.")}
          {Step("3", "Choose a <strong>username and password</strong> to complete your account setup.")}
          {Step("4", "Log in to access your <strong>enrollment dashboard, grades, and class schedules</strong>.")}
        </td>
      </tr>

      <!-- PAYMENT SUMMARY -->
      <tr>
        <td style=""padding:0 40px 28px;"">
          <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
                 style=""border:1px solid #c9d9f0;border-radius:8px;overflow:hidden;"">
            <tr>
              <td colspan=""2""
                  style=""background:#0d2f6e;padding:12px 14px;
                          font-family:Arial,sans-serif;font-size:11px;font-weight:800;
                          letter-spacing:2px;text-transform:uppercase;color:#b8d4f5;"">
                &#128196;&nbsp; Payment Summary
              </td>
            </tr>
            <!-- First row has no top border -->
            <tr>
              <td width=""44%"" style=""padding:10px 14px;font-family:Arial,sans-serif;
                  font-size:13px;color:#1e3a5f;font-weight:600;background:#f0f6ff;"">
                Student Number
              </td>
              <td style=""padding:10px 14px;font-family:Arial,sans-serif;
                  font-size:13px;color:#0a2240;font-weight:700;"">
                {stuNo}
              </td>
            </tr>
            {Row("Full Name", E(studentName))}
            {Row("Program", program)}
            {Row("Year Level", E(yearLvl))}
            {Row("Semester", E(semester))}
            {Row("Payment Purpose", payType)}
            {Row("Payment Method", payMethod)}
            {Row("Reference No.", refNo)}
            <!-- Amount — green highlight -->
            <tr>
              <td width=""44%"" style=""padding:10px 14px;font-family:Arial,sans-serif;
                  font-size:13px;color:#1e3a5f;font-weight:600;background:#f0f6ff;
                  border-top:1px solid #dce8f5;"">Amount Paid</td>
              <td style=""padding:10px 14px;font-family:Arial,sans-serif;
                  font-size:14px;color:#16a34a;font-weight:800;
                  border-top:1px solid #dce8f5;"">
                &#8369;{E(amount)}
              </td>
            </tr>
            {Row("Verified By", verBy)}
            {Row("Date Verified", E(verAt))}
            <!-- Status pill -->
            <tr>
              <td width=""44%"" style=""padding:10px 14px;font-family:Arial,sans-serif;
                  font-size:13px;color:#1e3a5f;font-weight:600;background:#f0f6ff;
                  border-top:1px solid #dce8f5;"">Status</td>
              <td style=""padding:10px 14px;border-top:1px solid #dce8f5;"">
                <span style=""display:inline-block;background:#dcfce7;color:#166534;
                              border:1px solid #86efac;border-radius:20px;
                              font-family:Arial,sans-serif;font-size:10px;font-weight:800;
                              letter-spacing:0.5px;text-transform:uppercase;
                              padding:3px 12px;"">
                  &#10003; Verified
                </span>
              </td>
            </tr>
          </table>
        </td>
      </tr>

      <!-- DIVIDER -->
      <tr>
        <td style=""padding:0 40px;"">
          <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
            <tr>
              <td style=""border-top:1px solid #dce8f5;font-size:0;line-height:0;"">&nbsp;</td>
            </tr>
          </table>
        </td>
      </tr>

      <!-- SIGN-OFF -->
      <tr>
        <td style=""padding:24px 40px 32px;"">
          <p style=""font-family:Arial,sans-serif;font-size:13px;line-height:1.8;
                     color:#3a5f8a;margin:0 0 18px;"">
            For questions, please contact the Registrar&apos;s Office:<br/>
            &#128231;&nbsp;
            <a href=""mailto:oactech1953@gmail.com""
               style=""color:#0d2f6e;font-weight:700;text-decoration:none;"">
              oactech1953@gmail.com
            </a>
            &nbsp;&#124;&nbsp; &#9990;&nbsp;(32) 326-4409
            &nbsp;&#124;&nbsp; &#128241;&nbsp;(63) 977 381 0089<br/>
            RLW Building, MV Patalinghug Avenue, Barangay Basak,
            Lapu-Lapu City, Cebu 6015
          </p>
          <p style=""font-family:Arial,sans-serif;font-size:13px;line-height:1.8;
                     color:#3a5f8a;margin:0;"">
            Warm regards,<br/>
            <strong style=""color:#0d2f6e;font-size:14px;"">Enrollment Office</strong><br/>
            <span style=""font-size:12px;color:#7a9fc4;"">
              Orson Aerospace College and Technology, Inc.
            </span>
          </p>
        </td>
      </tr>

      <!-- FOOTER -->
      <tr>
        <td align=""center""
            style=""background:#0d2f6e;padding:16px 40px;
                   border-radius:0 0 12px 12px;"">
          <p style=""font-family:Arial,sans-serif;font-size:11px;color:#7aa3cc;
                     margin:0;line-height:1.7;"">
            This is an automated message &mdash; please do not reply directly to this e-mail.<br/>
            &copy; {year} Orson Aerospace College and Technology, Inc. All rights reserved.
          </p>
        </td>
      </tr>

    </table>
    <!-- /CARD -->

  </td></tr>
</table>

</body>
</html>";
        }

        // ══════════════════════════════════════════════════════════════════
        // Send — HTML only, no AlternateViews (prevents Gmail plain-text bug)
        // ══════════════════════════════════════════════════════════════════
        private async Task SendEmailAsync(string to, string subject, string htmlBody)
        {
            var from = string.IsNullOrWhiteSpace(_smtp.FromAddress)
                ? _smtp.User
                : _smtp.FromAddress;

            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                EnableSsl = _smtp.EnableSSL,
                Credentials = new NetworkCredential(_smtp.User, _smtp.Password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };

            using var msg = new MailMessage
            {
                From = new MailAddress(from, _smtp.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
            };
            msg.To.Add(to);

            await client.SendMailAsync(msg);
        }

        private static EmailResult Fail(string email, string msg) =>
            new() { Success = false, RecipientEmail = email, ErrorMessage = msg };
    }
}