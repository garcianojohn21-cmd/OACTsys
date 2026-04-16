// ============================================================
// FILE: Services/AutoRejectHostedService.cs
//
// Runs in the background every hour.
// Any student whose EnrollmentStatus == "Pending" and whose
// EnrollmentDate is older than 24 hours is automatically set
// to "Rejected" and sent a rejection notification email.
//
// Register in Program.cs:
//   builder.Services.AddHostedService<AutoRejectHostedService>();
// ============================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OACTsys.Data;
using OACTsys.Models;
using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace OACTsys.Services
{
    public class AutoRejectHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AutoRejectHostedService> _logger;
        private readonly SmtpSettings _smtp;

        // How often the job runs
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        // How long a student can stay in Pending before being rejected
        public static readonly TimeSpan PendingGracePeriod = TimeSpan.FromHours(24);

        public AutoRejectHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<AutoRejectHostedService> logger,
            IOptions<SmtpSettings> smtp)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
            _smtp         = smtp.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AutoRejectHostedService started.");

            // Run once immediately on startup, then every CheckInterval
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunAutoRejectAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoRejectHostedService encountered an error.");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        // ── Core logic ────────────────────────────────────────────────────
        private async Task RunAutoRejectAsync(CancellationToken ct)
        {
            using var scope   = _scopeFactory.CreateScope();
            var context       = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cutoff        = DateTime.Now - PendingGracePeriod;

            // Find all active students that are still Pending and have been
            // waiting longer than the grace period
            var expired = await context.Students
                .Include(s => s.FieldData)
                    .ThenInclude(fd => fd.EnrollmentField)
                .Where(s =>
                    s.IsActive &&
                    s.EnrollmentStatus == "Pending" &&
                    s.EnrollmentDate != null &&
                    s.EnrollmentDate < cutoff)
                .ToListAsync(ct);

            if (!expired.Any())
            {
                _logger.LogInformation(
                    "AutoReject: no expired pending students found at {Time}.", DateTime.Now);
                return;
            }

            _logger.LogInformation(
                "AutoReject: found {Count} expired pending student(s). Processing…", expired.Count);

            int rejected = 0, emailsFailed = 0;

            foreach (var student in expired)
            {
                // 1. Update status
                student.EnrollmentStatus = "Rejected";
                student.LastModifiedDate = DateTime.Now;

                // 2. Send rejection email (best-effort — don't fail the whole batch)
                var email = ResolveEmail(student);
                if (!string.IsNullOrWhiteSpace(email))
                {
                    try
                    {
                        var name    = ResolveFullName(student);
                        var subject = "Enrollment Application — Status Update";
                        var body    = BuildRejectionEmail(student, name);
                        await SendEmailAsync(email, subject, body);
                        _logger.LogInformation(
                            "AutoReject: rejection email sent to {Email} (StudentId={Id}).",
                            email, student.StudentId);
                    }
                    catch (Exception ex)
                    {
                        emailsFailed++;
                        _logger.LogWarning(ex,
                            "AutoReject: failed to send email to StudentId={Id}.", student.StudentId);
                    }
                }

                rejected++;
            }

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "AutoReject: {Rejected} student(s) rejected. {EmailsFailed} email(s) failed.",
                rejected, emailsFailed);
        }

        // ── Email helpers ─────────────────────────────────────────────────
        private static string? ResolveEmail(Student student)
        {
            if (student.FieldData != null)
            {
                var byType = student.FieldData.FirstOrDefault(fd =>
                    fd.EnrollmentField != null &&
                    string.Equals(fd.EnrollmentField.FieldType, "email",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(fd.FieldValue));
                if (byType != null) return byType.FieldValue;

                var byKey = student.FieldData.FirstOrDefault(fd =>
                    fd.EnrollmentField != null &&
                    string.Equals(fd.EnrollmentField.TemplateKey, "email_address",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(fd.FieldValue));
                if (byKey != null) return byKey.FieldValue;
            }
            return string.IsNullOrWhiteSpace(student.Email) ? null : student.Email;
        }

        private static string ResolveFullName(Student student)
        {
            if (student.FieldData != null)
            {
                var full = student.FieldData.FirstOrDefault(fd =>
                    fd.EnrollmentField != null &&
                    string.Equals(fd.EnrollmentField.TemplateKey, "full_name",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(fd.FieldValue));
                if (full != null) return full.FieldValue!;
            }
            return student.Email ?? $"Student #{student.StudentId}";
        }

        private static string BuildRejectionEmail(Student student, string name)
        {
            static string E(string? v) => WebUtility.HtmlEncode(v ?? "");
            int year = DateTime.Now.Year;

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head><meta charset=""UTF-8""/><title>Enrollment Status Update</title></head>
<body style=""margin:0;padding:0;background:#e8eef7;"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
       style=""background:#e8eef7;padding:32px 0;"">
  <tr><td align=""center"">
    <table width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0""
           style=""background:#fff;border-radius:12px;border:1px solid #c9d9f0;"">

      <!-- Header -->
      <tr>
        <td align=""center""
            style=""background:#0d2f6e;padding:32px 40px 24px;border-radius:12px 12px 0 0;"">
          <div style=""font-size:36px;margin-bottom:12px;"">&#9992;&#65039;</div>
          <div style=""font-family:Arial,sans-serif;font-size:18px;font-weight:800;
                       color:#fff;margin-bottom:10px;"">
            Orson Aerospace College and Technology, Inc.
          </div>
          <table cellpadding=""0"" cellspacing=""0"" border=""0"">
            <tr>
              <td style=""background:#dc3545;border-radius:20px;padding:5px 20px;"">
                <span style=""font-family:Arial,sans-serif;font-size:11px;font-weight:800;
                              text-transform:uppercase;color:#fff;letter-spacing:1px;"">
                  &#10007;&nbsp; Enrollment Not Confirmed
                </span>
              </td>
            </tr>
          </table>
        </td>
      </tr>

      <!-- Body -->
      <tr>
        <td style=""padding:32px 40px;"">
          <p style=""font-family:Arial,sans-serif;font-size:15px;font-weight:700;
                     color:#0d2f6e;margin:0 0 12px;"">
            Dear {E(name)},
          </p>
          <p style=""font-family:Arial,sans-serif;font-size:13px;line-height:1.8;
                     color:#3a5f8a;margin:0 0 16px;"">
            We regret to inform you that your enrollment application for
            <strong>{E(student.Program ?? "—")}</strong> was not confirmed within the
            required <strong>24-hour window</strong> and has been automatically closed.
          </p>
          <p style=""font-family:Arial,sans-serif;font-size:13px;line-height:1.8;
                     color:#3a5f8a;margin:0 0 16px;"">
            If you wish to continue with your enrollment, please submit a new application
            or contact the Registrar's Office as soon as possible.
          </p>

          <!-- Info box -->
          <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
                 style=""background:#fff8e1;border:1px solid #fde68a;
                         border-left:4px solid #f59e0b;border-radius:8px;margin:20px 0;"">
            <tr>
              <td style=""padding:14px 18px;font-family:Arial,sans-serif;
                          font-size:13px;line-height:1.7;color:#92400e;"">
                &#9888;&#65039; <strong>Next steps:</strong><br/>
                &bull; Visit the Registrar&apos;s Office during office hours<br/>
                &bull; Re-submit your enrollment form online<br/>
                &bull; Contact us at
                <a href=""mailto:oactech1953@gmail.com""
                   style=""color:#0d2f6e;font-weight:700;"">oactech1953@gmail.com</a>
              </td>
            </tr>
          </table>

          <p style=""font-family:Arial,sans-serif;font-size:13px;line-height:1.8;
                     color:#3a5f8a;margin:0;"">
            We apologize for any inconvenience and hope to assist you in completing
            your enrollment soon.<br/><br/>
            Warm regards,<br/>
            <strong style=""color:#0d2f6e;"">Enrollment Office</strong><br/>
            <span style=""font-size:12px;color:#7a9fc4;"">
              Orson Aerospace College and Technology, Inc.
            </span>
          </p>
        </td>
      </tr>

      <!-- Footer -->
      <tr>
        <td align=""center""
            style=""background:#0d2f6e;padding:16px 40px;border-radius:0 0 12px 12px;"">
          <p style=""font-family:Arial,sans-serif;font-size:11px;color:#7aa3cc;
                     margin:0;line-height:1.7;"">
            This is an automated message — please do not reply directly to this e-mail.<br/>
            &copy; {year} Orson Aerospace College and Technology, Inc. All rights reserved.
          </p>
        </td>
      </tr>

    </table>
  </td></tr>
</table>
</body>
</html>";
        }

        private async Task SendEmailAsync(string to, string subject, string htmlBody)
        {
            var from = string.IsNullOrWhiteSpace(_smtp.FromAddress) ? _smtp.User : _smtp.FromAddress;

            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                EnableSsl       = _smtp.EnableSSL,
                Credentials     = new NetworkCredential(_smtp.User, _smtp.Password),
                DeliveryMethod  = SmtpDeliveryMethod.Network,
            };

            using var msg = new MailMessage
            {
                From            = new MailAddress(from, _smtp.FromName),
                Subject         = subject,
                Body            = htmlBody,
                IsBodyHtml      = true,
                BodyEncoding    = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
            };
            msg.To.Add(to);

            await client.SendMailAsync(msg);
        }
    }
}
