using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Helpers;
using OACTsys.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OACTsys.Controllers
{
    public class EnrollmentAdminController : BaseAdminController
    {
        private readonly ApplicationDbContext _context;

        public EnrollmentAdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ══════════════════════════════════════════
        // MAIN ENROLLMENT PAGE
        // ══════════════════════════════════════════
        public IActionResult Enrollment()
        {
            if (!IsAdminLoggedIn()) return RedirectToAction("Login", "Admin");
            SetLayoutData();
            return View();
        }

        // ══════════════════════════════════════════
        // REVIEW LIST PAGES
        // ══════════════════════════════════════════
        public IActionResult EnrollmentFormReviewList()
        {
            if (!IsAdminLoggedIn()) return RedirectToAction("Login", "Admin");
            SetLayoutData();
            return View();
        }

        public IActionResult SubjectsReviewList()
        {
            if (!IsAdminLoggedIn()) return RedirectToAction("Login", "Admin");
            SetLayoutData();
            return View();
        }

        // ══════════════════════════════════════════
        // TUITION FEE SETUP  (SuperAdmin only)
        // ══════════════════════════════════════════
        public async Task<IActionResult> TuitionFeeSetup()
        {
            if (!IsAdminLoggedIn()) return RedirectToAction("Login", "Admin");
            SetLayoutData();

            // Fetch raw programs to memory first — EF Core cannot translate
            // custom C# methods to SQL, so we pull strings then normalize in-memory.
            // NormalizeProgram strips "BS" prefix: "BSAMT" -> "AMT", "BSAVT" -> "AVT"
            var programs = (await _context.Subjects
                .Where(s => s.IsActive)
                .Select(s => s.Program)       // pull raw string to memory
                .ToListAsync())
                .Select(NormalizeProgram)      // normalize in C# (not SQL)
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            ViewBag.Programs = programs;
            return View();
        }

        // ══════════════════════════════════════════
        // GET TUITION FEE DETAIL
        // ══════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetTuitionFeeDetail(
            string program, string studentType, int yearLevel, int semester)
        {
            try
            {
                // Normalize so "amt " == "AMT"
                var prog = NormalizeProgram(program);
                var type = (studentType ?? "").Trim();

                var fee = await _context.TuitionFees
                    .FirstOrDefaultAsync(t =>
                        t.Program.Trim().ToUpper() == prog &&
                        t.StudentType.Trim() == type &&
                        t.YearLevel == yearLevel &&
                        t.Semester == semester &&
                        t.IsActive);

                if (fee == null)
                    return Json(new { id = 0 });   // signals "no record yet"

                return Json(new
                {
                    id = fee.Id,
                    program = fee.Program,
                    studentType = fee.StudentType,
                    yearLevel = fee.YearLevel,
                    semester = fee.Semester,
                    tuitionFees = fee.TuitionFees,
                    miscellaneous = fee.Miscellaneous,
                    laboratory = fee.Laboratory,
                    total = fee.Total,
                    hasDiscount = fee.HasDiscount,
                    discountPercent = fee.DiscountPercent,
                    discountAmount = fee.DiscountAmount,
                    finalTotal = fee.FinalTotal,
                    downPayment = fee.DownPayment,
                    otherFees = fee.OtherFees,
                    otherFeesDescription = fee.OtherFeesDescription,
                    totalPaymentUponEnrollment = fee.TotalPaymentUponEnrollment,
                    numberOfMonths = fee.NumberOfMonths,
                    prelimPayment = fee.PrelimPayment,
                    midtermPayment = fee.MidtermPayment,
                    semiFinalPayment = fee.SemiFinalPayment,
                    finalPayment = fee.FinalPayment,
                    requirements = fee.Requirements
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // SAVE TUITION FEES  (SuperAdmin only)
        // ══════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTuitionFees([FromBody] TuitionFeeRequest req)
        {
            try
            {
                if (!IsAdminLoggedIn()) return Unauthorized();
                if (!IsSuperAdmin())
                    return Json(new { success = false, message = "Only SuperAdmin can save tuition fees." });
                if (string.IsNullOrWhiteSpace(req.Program))
                    return Json(new { success = false, message = "Program is required." });
                if (string.IsNullOrWhiteSpace(req.StudentType))
                    return Json(new { success = false, message = "Student Type is required." });

                // Normalize before saving so DB is always clean
                var prog = NormalizeProgram(req.Program);
                var type = req.StudentType.Trim();

                var existing = await _context.TuitionFees
                    .FirstOrDefaultAsync(t =>
                        t.Program.Trim().ToUpper() == prog &&
                        t.StudentType.Trim() == type &&
                        t.YearLevel == req.YearLevel &&
                        t.Semester == req.Semester);

                if (existing != null)
                {
                    // UPDATE — also normalize stored values
                    existing.Program = prog;
                    existing.StudentType = type;
                    existing.TuitionFees = req.TuitionFee;
                    existing.Miscellaneous = req.Miscellaneous;
                    existing.Laboratory = req.Laboratory;
                    existing.Total = req.Total;
                    existing.HasDiscount = req.HasDiscount;
                    existing.DiscountPercent = req.DiscountPercent;
                    existing.DiscountAmount = req.DiscountAmount;
                    existing.FinalTotal = req.FinalTotal;
                    existing.DownPayment = req.DownPayment;
                    existing.OtherFees = req.OtherFees;
                    existing.OtherFeesDescription = req.OtherFeesDescription ?? string.Empty;
                    existing.TotalPaymentUponEnrollment = req.TotalPaymentUponEnrollment;
                    existing.NumberOfMonths = req.NumberOfMonths;
                    existing.PrelimPayment = req.PrelimPayment;
                    existing.MidtermPayment = req.MidtermPayment;
                    existing.SemiFinalPayment = req.SemiFinalPayment;
                    existing.FinalPayment = req.FinalPayment;
                    existing.Requirements = req.Requirements ?? string.Empty;
                    existing.IsActive = true;
                    existing.UpdatedDate = DateTime.Now;
                }
                else
                {
                    // INSERT — store normalized values
                    var entity = new TuitionFee
                    {
                        Program = prog,
                        StudentType = type,
                        YearLevel = req.YearLevel,
                        Semester = req.Semester,
                        TuitionFees = req.TuitionFee,
                        Miscellaneous = req.Miscellaneous,
                        Laboratory = req.Laboratory,
                        Total = req.Total,
                        HasDiscount = req.HasDiscount,
                        DiscountPercent = req.DiscountPercent,
                        DiscountAmount = req.DiscountAmount,
                        FinalTotal = req.FinalTotal,
                        DownPayment = req.DownPayment,
                        OtherFees = req.OtherFees,
                        OtherFeesDescription = req.OtherFeesDescription ?? string.Empty,
                        TotalPaymentUponEnrollment = req.TotalPaymentUponEnrollment,
                        NumberOfMonths = req.NumberOfMonths,
                        PrelimPayment = req.PrelimPayment,
                        MidtermPayment = req.MidtermPayment,
                        SemiFinalPayment = req.SemiFinalPayment,
                        FinalPayment = req.FinalPayment,
                        Requirements = req.Requirements ?? string.Empty,
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    };
                    await _context.TuitionFees.AddAsync(entity);
                }

                await _context.SaveChangesAsync();
                return Json(new
                {
                    success = true,
                    message = $"Saved — {prog} | {type} | Year {req.YearLevel} Sem {req.Semester}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // DONE STUDENTS
        // ══════════════════════════════════════════
        public async Task<IActionResult> DoneStudents()
        {
            if (!IsAdminLoggedIn()) return RedirectToAction("Login", "Admin");
            SetLayoutData();

            var students = await _context.Students
                .Where(s => s.EnrollmentStatus == "Enrolled" && s.PaymentStatus == "Verified")
                .Include(s => s.EnrollmentForm)
                .OrderByDescending(s => s.EnrollmentDate)
                .ToListAsync();

            return View(students);
        }

        // ══════════════════════════════════════════
        // PENDING STUDENTS
        // ══════════════════════════════════════════
        public async Task<IActionResult> PendingStudents()
        {
            if (!IsAdminLoggedIn()) return RedirectToAction("Login", "Admin");
            SetLayoutData();

            var students = await _context.Students
                .Where(s => s.EnrollmentStatus == "Pending" || s.PaymentStatus == "Pending")
                .Include(s => s.EnrollmentForm)
                .Include(s => s.Payments)
                .OrderByDescending(s => s.EnrollmentDate)
                .ToListAsync();

            return View(students);
        }

        // ══════════════════════════════════════════
        // ARCHIVED STUDENTS
        // ══════════════════════════════════════════
        public async Task<IActionResult> ArchivedStudents()
        {
            if (!IsAdminLoggedIn()) return RedirectToAction("Login", "Admin");
            SetLayoutData();

            var students = await _context.Students
                .Where(s => s.EnrollmentStatus == "Archived")
                .Include(s => s.EnrollmentForm)
                .OrderByDescending(s => s.EnrollmentDate)
                .ToListAsync();

            return View(students);
        }

        // ══════════════════════════════════════════
        // GET ENROLLMENT FIELDS
        // ══════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetEnrollmentFields()
        {
            try
            {
                var fields = await _context.EnrollmentFields
                    .Where(f => f.IsActive)
                    .OrderBy(f => f.DisplayOrder)
                    .Select(f => new
                    {
                        id = f.Id,
                        fieldName = f.FieldName,
                        fieldType = f.FieldType,
                        isRequired = f.IsRequired,
                        options = f.Options,
                        acceptedFileTypes = f.AcceptedFileTypes,
                        maxFileSize = f.MaxFileSize,
                        helperText = f.HelperText,
                        displayOrder = f.DisplayOrder
                    })
                    .ToListAsync();

                return Json(new { success = true, fields });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // GET SUBJECTS
        // ══════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetSubjects()
        {
            try
            {
                var subjects = await _context.Subjects
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.Program)
                    .ThenBy(s => s.YearLevel)
                    .ThenBy(s => s.Semester)
                    .ThenBy(s => s.CourseCode)
                    .Select(s => new
                    {
                        subjectId = s.SubjectId,
                        program = s.Program,
                        courseCode = s.CourseCode,
                        descriptiveTitle = s.DescriptiveTitle,
                        lectureHours = s.LectureHours,
                        laboratoryHours = s.LaboratoryHours,
                        units = s.Units,
                        yearLevel = s.YearLevel,
                        semester = s.Semester,
                        totalHours = s.TotalHours
                    })
                    .ToListAsync();

                return Json(new { success = true, subjects });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // SAVE ENROLLMENT FIELDS
        // ══════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> SaveEnrollmentFields([FromBody] List<EnrollmentFieldRequest> fields)
        {
            try
            {
                if (!IsAdminLoggedIn()) return Unauthorized();

                var oldFields = await _context.EnrollmentFields.ToListAsync();
                _context.EnrollmentFields.RemoveRange(oldFields);

                int displayOrder = 1;
                var entities = fields.Select(f => new EnrollmentField
                {
                    FieldName = f.FieldName ?? string.Empty,
                    FieldType = f.FieldType ?? "text",
                    IsRequired = f.IsRequired,
                    Options = f.Options,
                    AcceptedFileTypes = f.AcceptedFileTypes,
                    MaxFileSize = f.MaxFileSizeMB,
                    HelperText = f.HelperText,
                    DisplayOrder = displayOrder++,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                }).ToList();

                await _context.EnrollmentFields.AddRangeAsync(entities);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Enrollment fields saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // SAVE SUBJECTS
        // ══════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> SaveSubjects([FromBody] List<SubjectRequest> subjects)
        {
            try
            {
                if (!IsAdminLoggedIn()) return Unauthorized();

                var oldSubjects = await _context.Subjects.ToListAsync();
                _context.Subjects.RemoveRange(oldSubjects);

                var entities = subjects.Select(s => new Subject
                {
                    Program = NormalizeProgram(s.Program),  // always "AMT"/"AVT" — strips BS prefix
                    CourseCode = s.CourseCode ?? string.Empty,
                    DescriptiveTitle = s.DescriptiveTitle ?? string.Empty,
                    LectureHours = s.Lecture,
                    LaboratoryHours = s.Laboratory,
                    Units = s.Units,
                    YearLevel = ParseYearLevel(s.YearLevel),
                    Semester = ParseSemester(s.Semester),
                    TotalHours = s.TotalHours > 0 ? s.TotalHours : (s.Lecture + s.Laboratory),
                    IsActive = true,
                    CreatedDate = DateTime.Now
                }).ToList();

                await _context.Subjects.AddRangeAsync(entities);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Subjects saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // DELETE ENROLLMENT FIELD
        // ══════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> DeleteEnrollmentField(int id)
        {
            try
            {
                if (!IsAdminLoggedIn()) return Unauthorized();
                var field = await _context.EnrollmentFields.FindAsync(id);
                if (field == null) return Json(new { success = false, message = "Field not found." });
                _context.EnrollmentFields.Remove(field);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Field deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // DELETE SUBJECT
        // ══════════════════════════════════════════
        [HttpPost]
        public async Task<IActionResult> DeleteSubject(int id)
        {
            try
            {
                if (!IsAdminLoggedIn()) return Unauthorized();
                var subject = await _context.Subjects.FindAsync(id);
                if (subject == null) return Json(new { success = false, message = "Subject not found." });
                _context.Subjects.Remove(subject);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Subject deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // GET TUITION FEES (legacy simple endpoint)
        // ══════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetTuitionFees(string program, int yearLevel)
        {
            try
            {
                var prog = NormalizeProgram(program);

                var fees = await _context.TuitionFees
                    .Where(t =>
                        t.Program.Trim().ToUpper() == prog &&
                        t.YearLevel == yearLevel &&
                        t.IsActive)
                    .FirstOrDefaultAsync();

                if (fees == null)
                    return Json(new { tuitionFee = 0, miscellaneous = 0, laboratory = 0, total = 0 });

                return Json(new
                {
                    tuitionFee = fees.TuitionFees,
                    miscellaneous = fees.Miscellaneous,
                    laboratory = fees.Laboratory,
                    total = fees.Total
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // HELPER METHODS
        // ══════════════════════════════════════════
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

        // Strips "BS" prefix: "BSAMT" -> "AMT", "BSAVT" -> "AVT", "CS" -> "CS"

        private string NormalizeProgram(string program)
        {
            if (string.IsNullOrWhiteSpace(program)) return string.Empty;
            var p = program.Trim().ToUpper();
            if (p.StartsWith("BS")) p = p.Substring(2);
            return p;
        }

        private int ParseYearLevel(string yearLevel)
        {
            if (string.IsNullOrEmpty(yearLevel)) return 1;
            if (yearLevel.Contains("1")) return 1;
            if (yearLevel.Contains("2")) return 2;
            if (yearLevel.Contains("3")) return 3;
            if (yearLevel.Contains("4")) return 4;
            return 1;
        }

        private int ParseSemester(string semester)
        {
            if (string.IsNullOrEmpty(semester)) return 1;
            if (semester.ToLower().Contains("summer")) return 3;
            if (semester.Contains("1")) return 1;
            if (semester.Contains("2")) return 2;
            return 1;
        }
    }

    // ══════════════════════════════════════════
    // REQUEST MODELS
    // ══════════════════════════════════════════
    public class EnrollmentFieldRequest
    {
        public string FieldName { get; set; }
        public string FieldType { get; set; }
        public bool IsRequired { get; set; }
        public string Options { get; set; }
        public string AcceptedFileTypes { get; set; }
        public int MaxFileSizeMB { get; set; }
        public string HelperText { get; set; }
    }

    public class SubjectRequest
    {
        public string Program { get; set; }
        public string CourseCode { get; set; }
        public string DescriptiveTitle { get; set; }
        public int Lecture { get; set; }
        public int Laboratory { get; set; }
        public decimal Units { get; set; }
        public string YearLevel { get; set; }
        public string Semester { get; set; }
        public int TotalHours { get; set; }
    }

    public class TuitionFeeRequest
    {
        public string Program { get; set; }
        public string StudentType { get; set; }
        public int YearLevel { get; set; }
        public int Semester { get; set; }
        public decimal TuitionFee { get; set; }
        public decimal Miscellaneous { get; set; }
        public decimal Laboratory { get; set; }
        public decimal Total { get; set; }
        public bool HasDiscount { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalTotal { get; set; }
        public decimal DownPayment { get; set; }
        public decimal OtherFees { get; set; }
        public string OtherFeesDescription { get; set; }
        public decimal TotalPaymentUponEnrollment { get; set; }
        public int NumberOfMonths { get; set; }
        public decimal PrelimPayment { get; set; }
        public decimal MidtermPayment { get; set; }
        public decimal SemiFinalPayment { get; set; }
        public decimal FinalPayment { get; set; }
        public string Requirements { get; set; }
    }
}