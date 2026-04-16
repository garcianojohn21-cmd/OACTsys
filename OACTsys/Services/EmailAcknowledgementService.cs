// ============================================================
// FILE: Services/EmailAcknowledgementService.cs
// ============================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OACTsys.Data;
using OACTsys.Models;

namespace OACTsys.Services
{
    public sealed class SmtpSettings
    {
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public bool EnableSSL { get; set; } = true;
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromName { get; set; } = "OACT Enrollment Office";
        public string FromAddress { get; set; } = "";
    }

    public sealed class EmailResult
    {
        public bool Success { get; init; }
        public string RecipientEmail { get; init; } = "";
        public string? ErrorMessage { get; init; }
    }

    public class EmailAcknowledgementService
    {
        private readonly ApplicationDbContext _context;
        private readonly SmtpSettings _smtp;
        private readonly ILogger<EmailAcknowledgementService> _logger;

        public EmailAcknowledgementService(
            ApplicationDbContext context,
            IOptions<SmtpSettings> smtp,
            ILogger<EmailAcknowledgementService> logger)
        {
            _context = context;
            _smtp = smtp.Value;
            _logger = logger;
        }

        public async Task<EmailResult> SendAcknowledgementAsync(int studentId)
        {
            var student = await _context.Students
                .Include(s => s.FieldData)
                    .ThenInclude(fd => fd.EnrollmentField)
                .FirstOrDefaultAsync(s => s.StudentId == studentId);

            if (student == null)
                return Fail("", $"Student ID {studentId} not found.");

            return await SendAcknowledgementAsync(student);
        }

        public async Task<EmailResult> SendAcknowledgementAsync(Student student)
        {
            try
            {
                string? recipientEmail = ResolveEmail(student);
                if (string.IsNullOrWhiteSpace(recipientEmail))
                    return Fail("", "No e-mail address found for this student.");

                var tokens = BuildTokens(student);
                string name = GetToken(tokens, "{{full_name}}", student.StudentNumber ?? "Student");
                string subj = $"Enrollment Acknowledgment - {name}";
                string body = BuildHtmlBody(tokens, student);

                await SendEmailAsync(recipientEmail, subj, body);

                _logger.LogInformation(
                    "Enrollment acknowledgment sent to {Email} for StudentId={Id}",
                    recipientEmail, student.StudentId);

                return new EmailResult { Success = true, RecipientEmail = recipientEmail };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send acknowledgment for StudentId={Id}", student.StudentId);
                return Fail("", ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        private static string? ResolveEmail(Student student)
        {
            if (student.FieldData != null)
            {
                var byType = student.FieldData.FirstOrDefault(fd =>
                    fd.EnrollmentField != null &&
                    string.Equals(fd.EnrollmentField.FieldType, "email", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(fd.FieldValue));
                if (byType != null) return byType.FieldValue;

                var byKey = student.FieldData.FirstOrDefault(fd =>
                    fd.EnrollmentField != null &&
                    string.Equals(fd.EnrollmentField.TemplateKey, "email_address", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(fd.FieldValue));
                if (byKey != null) return byKey.FieldValue;
            }
            return string.IsNullOrWhiteSpace(student.Email) ? null : student.Email;
        }

        // ══════════════════════════════════════════════════════════════════
        private static Dictionary<string, string> BuildTokens(Student student)
        {
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

            if (student.FieldData != null)
            {
                foreach (var fd in student.FieldData)
                {
                    if (fd.EnrollmentField == null) continue;
                    var key = fd.EnrollmentField.TemplateKey?.Trim();
                    if (string.IsNullOrEmpty(key)) continue;

                    var ftype = fd.EnrollmentField.FieldType ?? "";
                    if (ftype == "file" || ftype == "image") continue;

                    var value = fd.FieldValue ?? "";
                    if (ftype == "date" && !string.IsNullOrEmpty(value))
                        value = DateTime.TryParse(value, out var d) ? d.ToString("MMMM d, yyyy") : value;

                    tokens[$"{{{{{key}}}}}"] = value;
                }
            }

            var now = DateTime.Now;
            int ayStart = now.Month >= 6 ? now.Year : now.Year - 1;

            Set(tokens, "{{academic_year}}", $"{ayStart}-{ayStart + 1}");
            Set(tokens, "{{semester}}", student.CurrentSemester switch
            {
                1 => "1st Semester",
                2 => "2nd Semester",
                _ => $"{student.CurrentSemester}"
            });
            Set(tokens, "{{course_year}}", student.Program ?? "");
            Set(tokens, "{{applicant_type}}", student.StudentType ?? "");
            Set(tokens, "{{year_level}}", student.CurrentYearLevel switch
            {
                1 => "1st Year",
                2 => "2nd Year",
                3 => "3rd Year",
                4 => "4th Year",
                _ => $"{student.CurrentYearLevel}th Year"
            });
            Set(tokens, "{{signature_date}}",
                (student.EnrollmentDate ?? now).ToString("MMMM d, yyyy"));
            Set(tokens, "{{email_address}}", student.Email ?? "");

            if (tokens.TryGetValue("{{full_name}}", out var fn))
                Set(tokens, "{{signature_over_printed_name}}", fn);

            return tokens;
        }

        private static void Set(Dictionary<string, string> d, string k, string v)
        { if (!d.ContainsKey(k)) d[k] = v; }

        private static string GetToken(Dictionary<string, string> t, string k, string fallback) =>
            t.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        // ══════════════════════════════════════════════════════════════════
        // HTML body — white background, navy blue design, table-based layout
        // No <style> block — all inline styles for full Gmail compatibility
        // ══════════════════════════════════════════════════════════════════
        private static string BuildHtmlBody(Dictionary<string, string> tokens, Student student)
        {
            string Get(string key, string fb = "-") => GetToken(tokens, key, fb);

            string studentName = Get("{{full_name}}");
            string studentNo = student.StudentNumber ?? "-";
            string program = Get("{{course_year}}");
            string yearLevel = Get("{{year_level}}");
            string semester = Get("{{semester}}");
            string academicYear = Get("{{academic_year}}");
            string applicantType = Get("{{applicant_type}}");
            string enrollDate = Get("{{signature_date}}");
            int year = DateTime.Now.Year;

            static string E(string? v) => WebUtility.HtmlEncode(v ?? "");

            static string Row(string label, string value, bool first = false) =>
                $@"<tr>
                  <td width=""42%"" style=""padding:10px 14px;font-family:Arial,sans-serif;font-size:13px;
                      color:#1e3a5f;font-weight:600;background:#f0f6ff;
                      border-top:{(first ? "none" : "1px solid #dce8f5")};"">
                    {label}
                  </td>
                  <td style=""padding:10px 14px;font-family:Arial,sans-serif;font-size:13px;
                      color:#0a2240;font-weight:700;
                      border-top:{(first ? "none" : "1px solid #dce8f5")};"">
                    {value}
                  </td>
                </tr>";

            static string Step(string num, string text) =>
                $@"<table cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin-bottom:14px;width:100%;"">
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
  <title>Enrollment Acknowledgment</title>
</head>
<body style=""margin:0;padding:0;background:#e8eef7;"">

<table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
       style=""background:#e8eef7;padding:32px 0;"">
  <tr>
    <td align=""center"">

      <!-- ═══ CARD ═══ -->
      <table width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0""
             style=""background:#ffffff;border-radius:12px;
                    border:1px solid #c9d9f0;"">

        <!-- HEADER -->
        <tr>
          <td align=""center"" style=""background:#0d2f6e;padding:36px 40px 30px;
                                      border-radius:12px 12px 0 0;"">
            <div style=""font-size:38px;line-height:1;margin-bottom:14px;"">&#9992;&#65039;</div>
            <div style=""font-family:Arial,sans-serif;font-size:10px;font-weight:700;
                         letter-spacing:3px;text-transform:uppercase;color:#90b8e8;
                         margin-bottom:10px;"">
              Official Communication
            </div>
            <div style=""font-family:Arial,sans-serif;font-size:20px;font-weight:800;
                         color:#ffffff;line-height:1.4;margin-bottom:14px;"">
              Orson Aerospace College<br/>and Technology, Inc.
            </div>
            <table cellpadding=""0"" cellspacing=""0"" border=""0"">
              <tr>
                <td style=""background:#1a4fa0;border:1px solid #3a6fc4;
                            border-radius:20px;padding:6px 20px;"">
                  <span style=""font-family:Arial,sans-serif;font-size:11px;font-weight:700;
                                letter-spacing:1.5px;text-transform:uppercase;color:#b8d4f5;"">
                    Enrollment Acknowledgment
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
              Dear {E(studentName)},
            </p>
            <p style=""font-family:Arial,sans-serif;font-size:13px;line-height:1.8;
                       color:#3a5f8a;margin:0;"">
              Your enrollment application has been
              <strong style=""color:#0d2f6e;"">successfully received</strong>
              and is currently being reviewed by the Registrar&apos;s Office.
              Please find a summary of your enrollment details below.
            </p>
          </td>
        </tr>

        <!-- DETAIL BOX -->
        <tr>
          <td style=""padding:24px 40px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
                   style=""border:1px solid #c9d9f0;border-radius:8px;overflow:hidden;"">
              <!-- Box heading -->
              <tr>
                <td colspan=""2""
                    style=""background:#0d2f6e;padding:12px 14px;
                            font-family:Arial,sans-serif;font-size:11px;font-weight:800;
                            letter-spacing:2px;text-transform:uppercase;color:#b8d4f5;"">
                  &#128196;&nbsp; Enrollment Details
                </td>
              </tr>
              {Row("Student Number", E(studentNo), first: true)}
              {Row("Full Name", E(studentName))}
              {Row("Program / Course", E(program))}
              {Row("Year Level", E(yearLevel))}
              {Row("Semester", E(semester))}
              {Row("Academic Year", E(academicYear))}
              {Row("Applicant Type", E(applicantType))}
              {Row("Enrollment Date", E(enrollDate))}
              <!-- Status row -->
              <tr>
                <td width=""42%"" style=""padding:10px 14px;font-family:Arial,sans-serif;
                    font-size:13px;color:#1e3a5f;font-weight:600;background:#f0f6ff;
                    border-top:1px solid #dce8f5;"">Status</td>
                <td style=""padding:10px 14px;border-top:1px solid #dce8f5;"">
                  <span style=""font-family:Arial,sans-serif;font-size:13px;
                                font-weight:700;color:#0d2f6e;"">Pending&nbsp;</span>
                  <span style=""display:inline-block;background:#fff8e1;color:#7a4f00;
                                border:1px solid #f0c040;border-radius:20px;
                                font-family:Arial,sans-serif;font-size:10px;font-weight:800;
                                letter-spacing:0.5px;text-transform:uppercase;
                                padding:3px 11px;"">
                    Under Review
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
              <tr><td style=""border-top:1px solid #dce8f5;font-size:0;line-height:0;"">&nbsp;</td></tr>
            </table>
          </td>
        </tr>

        <!-- NEXT STEPS -->
        <tr>
          <td style=""padding:24px 40px 8px;"">
            <p style=""font-family:Arial,sans-serif;font-size:11px;font-weight:800;
                       letter-spacing:2px;text-transform:uppercase;color:#0d2f6e;
                       margin:0 0 16px;"">
              &#10003;&nbsp; What Happens Next?
            </p>
            {Step("1", "Proceed to the <strong>Payment Portal</strong> to pay your assessment fees and secure your slot for the semester.")}
            {Step("2", "The Registrar&apos;s Office will verify your submitted documents within <strong>3&ndash;5 business days</strong>.")}
            {Step("3", "You will receive a follow-up e-mail once your enrollment is <strong>officially confirmed</strong>.")}
            {Step("4", "Keep a copy of this e-mail for your records.")}
          </td>
        </tr>

        <!-- IMPORTANT NOTICE -->
        <tr>
          <td style=""padding:8px 40px 28px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
                   style=""background:#f0f6ff;border:1px solid #c9d9f0;
                           border-left:4px solid #0d2f6e;border-radius:6px;"">
              <tr>
                <td style=""padding:14px 16px;font-family:Arial,sans-serif;
                            font-size:12px;line-height:1.7;color:#1e3a5f;"">
                  &#9888;&#65039; <strong>Important:</strong>
                  This is an <em>acknowledgment only</em> and does
                  <strong>not</strong> constitute final enrollment approval.
                  Your slot is confirmed only after payment is received and
                  documents are verified.
                </td>
              </tr>
            </table>
          </td>
        </tr>

        <!-- DIVIDER -->
        <tr>
          <td style=""padding:0 40px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
              <tr><td style=""border-top:1px solid #dce8f5;font-size:0;line-height:0;"">&nbsp;</td></tr>
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

    </td>
  </tr>
</table>

</body>
</html>";
        }

        // ══════════════════════════════════════════════════════════════════
        // Send — HTML only body, no AlternateViews.
        // Adding a text/plain AlternateView causes Gmail to show plain text
        // instead of the HTML version. IsBodyHtml = true is sufficient.
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