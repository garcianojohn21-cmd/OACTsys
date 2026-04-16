using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace OACTsys.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly bool _enableSSL;
        private readonly string _smtpUser;
        private readonly string _smtpPassword;

        public EmailService(IConfiguration config)
        {
            _config = config;

            _smtpHost = config["SmtpSettings:Host"];
            _smtpUser = config["SmtpSettings:User"];
            _smtpPassword = config["SmtpSettings:Password"];

            // Safe parsing
            _smtpPort = int.TryParse(config["SmtpSettings:Port"], out int port)
                ? port
                : 587;

            _enableSSL = bool.TryParse(config["SmtpSettings:EnableSSL"], out bool ssl)
                && ssl;
        }

        // ===============================
        // BASE EMAIL SENDER
        // ===============================
        private async Task SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                using (var message = new MailMessage())
                {
                    message.From = new MailAddress(
                        _smtpUser,
                        "OACT Enrollment System"
                    );

                    message.To.Add(to);
                    message.Subject = subject;
                    message.Body = body;
                    message.IsBodyHtml = true;

                    using (var smtpClient = new SmtpClient(_smtpHost, _smtpPort))
                    {
                        smtpClient.Credentials = new NetworkCredential(
                            _smtpUser,
                            _smtpPassword
                        );

                        smtpClient.EnableSsl = _enableSSL;

                        await smtpClient.SendMailAsync(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Email sending failed: " + ex.Message);
                throw;
            }
        }

        // ===============================
        // ENROLLMENT CONFIRMATION
        // ===============================
        public async Task SendEnrollmentConfirmationEmail(
            string toEmail,
            string studentName,
            string studentNumber)
        {
            string subject = "Enrollment Application Received - OACT";

            string body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
<div style='max-width:600px;margin:auto;padding:20px;border:2px solid #051094;'>

<h2 style='color:#051094;'>Enrollment Application Received</h2>

<p>Dear <strong>{studentName}</strong>,</p>

<p>Thank you for submitting your enrollment application.</p>

<div style='background:#f8f9fa;padding:15px;margin:20px 0;'>
<p><strong>Student Number:</strong> {studentNumber}</p>
<p><strong>Status:</strong> Pending Review</p>
</div>

<h3>Next Steps</h3>

<ol>
<li>Pay enrollment fees</li>
<li>Wait for verification</li>
<li>Receive account token</li>
<li>Create account</li>
</ol>

<p style='color:red;'>
Important: Processing starts after payment verification.
</p>

<hr>

<p style='font-size:12px;color:gray;'>
Automated message. Do not reply.
</p>

</div>
</body>
</html>";

            await SendEmailAsync(toEmail, subject, body);
        }

        // ===============================
        // ACCOUNT CREATION TOKEN
        // ===============================
        public async Task SendAccountCreationTokenEmail(
            string toEmail,
            string studentName,
            string accountToken,
            string studentNumber)
        {
            string subject = "Payment Verified - Create Account - OACT";

            string body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
<div style='max-width:600px;margin:auto;padding:20px;border:2px solid #28a745;'>

<h2 style='color:#28a745;'>Payment Verified</h2>

<p>Dear <strong>{studentName}</strong>,</p>

<p>Your payment has been approved.</p>

<div style='background:#d4edda;padding:20px;text-align:center;margin:20px 0;'>
<p>Your Account Token</p>

<h1 style='letter-spacing:4px;font-family:monospace;'>
{accountToken}
</h1>
</div>

<div style='background:#fff3cd;padding:15px;margin:20px 0;'>
<p><strong>Student Number:</strong> {studentNumber}</p>
<p><strong>Status:</strong> Enrolled</p>
</div>

<h3>How to Register</h3>

<ol>
<li>Open Student Portal</li>
<li>Click Create Account</li>
<li>Enter: {toEmail}</li>
<li>Use Token: {accountToken}</li>
<li>Set password</li>
</ol>

<p style='color:red;'>
Keep this token private. Valid for 30 days.
</p>

<hr>

<p style='font-size:12px;color:gray;'>
Automated message. Do not reply.
</p>

</div>
</body>
</html>";

            await SendEmailAsync(toEmail, subject, body);
        }

        // ===============================
        // PAYMENT PENDING
        // ===============================
        public async Task SendPaymentPendingEmail(
            string toEmail,
            string studentName,
            string referenceNumber,
            decimal amount)
        {
            string subject = "Payment Received - Pending - OACT";

            string body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
<div style='max-width:600px;margin:auto;padding:20px;border:2px solid #ffc107;'>

<h2 style='color:#ffc107;'>Payment Under Review</h2>

<p>Dear <strong>{studentName}</strong>,</p>

<p>Your payment was received.</p>

<div style='background:#fff3cd;padding:15px;margin:20px 0;'>
<p><strong>Reference:</strong> {referenceNumber}</p>
<p><strong>Amount:</strong> ₱{amount:N2}</p>
<p><strong>Status:</strong> Pending</p>
</div>

<p>
Verification takes 1–2 business days.
</p>

<hr>

<p style='font-size:12px;color:gray;'>
Automated message. Do not reply.
</p>

</div>
</body>
</html>";

            await SendEmailAsync(toEmail, subject, body);
        }
    }
}
