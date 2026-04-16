using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;

namespace OACTsys.Controllers
{
    public class LicensureController : Controller
    {
        private void EnsureMockSession()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                HttpContext.Session.SetString("UserEmail", "examinee-dev@orson.edu.ph");
                HttpContext.Session.SetString("UserRole", "Examinee");
            }

            if (string.IsNullOrEmpty(HttpContext.Session.GetString("ReservedDate")))
            {
                HttpContext.Session.SetString("ReservedDate", DateTime.Now.AddDays(7).ToString("yyyy-MM-dd"));
                HttpContext.Session.SetString("ReservedTime", "08:00 AM - 12:00 PM");
            }

            // Default Verification Status
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("VerificationStatus")))
            {
                HttpContext.Session.SetString("VerificationStatus", "Not Submitted");
            }
        }

        public IActionResult Index() { EnsureMockSession(); return View(); }
        public IActionResult Dashboard() { EnsureMockSession(); return View(); }

        public IActionResult ReservationManagement()
        {
            EnsureMockSession();
            ViewBag.CurrentDate = HttpContext.Session.GetString("ReservedDate");
            ViewBag.CurrentTime = HttpContext.Session.GetString("ReservedTime");
            return View();
        }

        [HttpPost]
        public IActionResult UpdateReservation(DateTime selectedDate, string slotTime)
        {
            HttpContext.Session.SetString("ReservedDate", selectedDate.ToString("yyyy-MM-dd"));
            HttpContext.Session.SetString("ReservedTime", slotTime);
            TempData["SuccessMessage"] = $"Reservation successfully updated to {selectedDate.ToShortDateString()} at {slotTime}.";
            return RedirectToAction("ReservationManagement");
        }

        // --- FUNCTIONAL VERIFICATION LOGIC ---

        public IActionResult ExamineeVerification()
        {
            EnsureMockSession();
            // Pass the status from session to the view
            ViewBag.Status = HttpContext.Session.GetString("VerificationStatus");
            return View();
        }

        [HttpPost]
        public IActionResult SubmitVerification()
        {
            // Simulate document processing
            HttpContext.Session.SetString("VerificationStatus", "Pending");
            TempData["SuccessMessage"] = "Your verification documents have been submitted successfully!";
            return RedirectToAction("ExamineeVerification");
        }

        // --- REMAINING ACTIONS ---
        public IActionResult ExamSchedule()
        {
            EnsureMockSession();

            // 1. Get user's specific reservation from Session
            ViewBag.UserDate = HttpContext.Session.GetString("ReservedDate");
            ViewBag.UserTime = HttpContext.Session.GetString("ReservedTime");
            ViewBag.UserStatus = HttpContext.Session.GetString("VerificationStatus") ?? "Not Submitted";

            // 2. Mock data for the institutional calendar
            // In a real app, this would come from a database table 'LicensurePrograms'
            return View();
        }
        public IActionResult AttendanceStatus()
        {
            EnsureMockSession();

            // Mock data for the view
            ViewBag.IsCheckedIn = false; // Simulated: Has the proctor scanned them in?
            ViewBag.ExamResult = "Pending"; // Options: Pending, Passed, Failed
            ViewBag.AttendanceLog = new List<string> { "07:30 AM - Gate Entry", "07:45 AM - Room 402 Entry" };

            return View();
        }
        public IActionResult Reports()
        {
            EnsureMockSession();

            // Determine eligibility for specific reports
            ViewBag.IsVerified = HttpContext.Session.GetString("VerificationStatus") == "Approved";
            ViewBag.HasResult = false; // Set to true once board results are released

            return View();
        }

        // Action to simulate PDF generation/download
        public IActionResult DownloadReport(string reportType)
        {
            // In a real app, this would use a library like Rotativa or SelectPdf
            // For now, we simulate a file download or redirect
            TempData["SuccessMessage"] = $"{reportType} is being prepared for download.";
            return RedirectToAction("Reports");
        }

        [HttpPost]
        public IActionResult ProcessPayment(string paymentMethod)
        {
            // Save payment status to Session
            HttpContext.Session.SetString("PaymentStatus", "Paid");

            TempData["SuccessMessage"] = $"Payment of ₱1,500.00 via {paymentMethod} was successful!";
            return RedirectToAction("ReservationManagement");
        }

        public IActionResult Theoretical()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                TempData["AuthMessage"] = "You must sign in to apply for the Licensure Exam.";
                return RedirectToAction("SignIn", "Account");
            }
            ViewBag.ExamType = "Theoretical Examination";
            ViewBag.IsTheoretical = true;
            ViewBag.Icon = "bi-journal-check";
            return View("ExamForm");
        }

        public IActionResult Practical()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                TempData["AuthMessage"] = "You must sign in to apply for the Practical Exam.";
                return RedirectToAction("SignIn", "Account");
            }
            ViewBag.ExamType = "Practical Assessment";
            ViewBag.IsTheoretical = false;
            ViewBag.Icon = "bi-gear-wide-connected";
            return View("ExamForm");
        }



        [HttpPost]
        public IActionResult SubmitApplication(IFormCollection form) => RedirectToAction("Dashboard");

        public IActionResult Success() => View();
    }
}