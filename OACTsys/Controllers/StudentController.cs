using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Helpers;
using OACTsys.Models;
using OACTsys.Models.ViewModels;
using OACTsys.Services;
using System.IO;

namespace OACTsys.Controllers
{
    public class StudentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly GradesService _gradesService;

        public StudentController(ApplicationDbContext context, GradesService gradesService)
        {
            _context = context;
            _gradesService = gradesService;
        }

        public IActionResult Index()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                HttpContext.Session.SetString("UserEmail", "dev-test@orson.edu.ph");
                HttpContext.Session.SetString("UserRole", "Student");
            }
            return View();
        }

        public IActionResult ProfileManagement()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                HttpContext.Session.SetString("UserEmail", "dev-test@orson.edu.ph");
                HttpContext.Session.SetString("UserRole", "Student");
            }
            return View();
        }

        // GET: /Student/SemesterEnrollment
        public IActionResult SemesterEnrollment()
        {
            return View();
        }

        [HttpPost]
        public IActionResult VerifyEnrollmentAccess(string StudentID, string Birthdate)
        {
            if (StudentID == "2024-0001" && Birthdate == "2000-01-01")
            {
                return RedirectToAction("EnrollmentForm");
            }

            TempData["EnrollError"] = "Invalid Student ID or Birthdate. Please try again.";
            return RedirectToAction("SemesterEnrollment");
        }

        public IActionResult EnrollmentForm()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile(IFormFile ProfilePicture, string FullName, string Birthday, string Mobile, string Street, string Barangay, string ZipCode)
        {
            if (ProfilePicture != null && ProfilePicture.Length > 0)
            {
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profiles");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string fileName = Guid.NewGuid().ToString() + "_" + ProfilePicture.FileName;
                string filePath = Path.Combine(folder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create)) { await ProfilePicture.CopyToAsync(stream); }
            }
            TempData["ProfileSuccess"] = "Profile updated successfully!";
            return RedirectToAction("ProfileManagement");
        }

        // GET: /Student/DocumentRequest
        public IActionResult DocumentRequest()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                HttpContext.Session.SetString("UserEmail", "dev-test@orson.edu.ph");
                HttpContext.Session.SetString("UserRole", "Student");
            }
            return View();
        }

        // POST: /Student/SubmitDocumentRequest
        [HttpPost]
        public IActionResult SubmitDocumentRequest(string DocumentType, string Reason)
        {
            TempData["RequestSuccess"] = "Your request for " + DocumentType + " has been submitted!";
            return RedirectToAction("DocumentRequest");
        }

        // ─────────────────────────────────────────────────────────────
        // GET /Student/Grades
        // Shows the student's grades on-screen + download options
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Grades()
        {
            var email = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(email))
                return RedirectToAction("Login", "Account");

            var student = await _context.Students
                .Include(s => s.FieldData)
                    .ThenInclude(fd => fd.EnrollmentField)
                .FirstOrDefaultAsync(s => s.Email == email && s.IsActive);

            if (student == null)
                return RedirectToAction("Login", "Account");

            // Load SubjectEnrollments with Subjects
            var enrollments = await _context.SubjectEnrollments
                .Include(se => se.Subject)
                .Where(se => se.StudentId == student.StudentId
                          && se.Subject != null
                          && se.Subject.IsActive)
                .OrderBy(se => se.YearLevel)
                .ThenBy(se => se.Semester)
                .ThenBy(se => se.Subject!.CourseCode)
                .ToListAsync();

            // Resolve display name from FieldData
            var fd = GradeFieldHelper.BuildFieldDict(student.FieldData);

            string Resolve(params string[] keys)
            {
                foreach (var k in keys)
                    if (fd.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                        return v.Trim();
                return "";
            }

            var fullName = Resolve("full_name", "fullname", "name", "complete_name");
            if (string.IsNullOrEmpty(fullName)) fullName = student.StudentNumber ?? "";

            var vm = new StudentGradesViewModel
            {
                StudentId = student.StudentId,
                StudentNumber = student.StudentNumber ?? "",
                FullName = fullName,
                Course = student.Program ?? "",
                YearLevel = student.CurrentYearLevel switch
                {
                    1 => "1st Year",
                    2 => "2nd Year",
                    3 => "3rd Year",
                    4 => "4th Year",
                    _ => $"{student.CurrentYearLevel}th Year"
                },
                Semester = student.CurrentSemester switch
                {
                    1 => "1st Semester",
                    2 => "2nd Semester",
                    3 => "Summer",
                    _ => $"Semester {student.CurrentSemester}"
                },
                AcademicYear = $"{(DateTime.Now.Month >= 6 ? DateTime.Now.Year : DateTime.Now.Year - 1)}-{(DateTime.Now.Month >= 6 ? DateTime.Now.Year + 1 : DateTime.Now.Year)}",
                Enrollments = enrollments
            };

            return View(vm);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /Student/DownloadGradesDocx
        // Downloads the filled grades_template.docx
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> DownloadGradesDocx()
        {
            var email = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(email))
                return RedirectToAction("Login", "Account");

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Email == email && s.IsActive);

            if (student == null) return NotFound();

            try
            {
                var (bytes, fileName) = await _gradesService.GenerateGradesDocxAsync(student.StudentId);
                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    fileName);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Could not generate document: {ex.Message}";
                return RedirectToAction("Grades");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // GET /Student/DownloadGradesPdf
        // Downloads the filled grades as PDF
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> DownloadGradesPdf()
        {
            var email = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(email))
                return RedirectToAction("Login", "Account");

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Email == email && s.IsActive);

            if (student == null) return NotFound();

            try
            {
                var (bytes, fileName) = await _gradesService.GenerateGradesPdfAsync(student.StudentId);
                return File(bytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Could not generate PDF: {ex.Message}";
                return RedirectToAction("Grades");
            }
        }

        // GET: /Student/ViewCOR
        public IActionResult ViewCOR()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                HttpContext.Session.SetString("UserEmail", "dev-test@orson.edu.ph");
                HttpContext.Session.SetString("UserRole", "Student");
            }

            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "documents", "student_cor.pdf");

            if (!System.IO.File.Exists(filePath))
            {
                return Content("Certificate of Registration (COR) is not yet available for your account.");
            }

            var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            return new FileStreamResult(fileStream, "application/pdf");
        }
    }
}