// ============================================================
// FILE: Controllers/GradesController.cs
//
// IMPORTANT — place this file ONLY in:
//   OACTsys/Controllers/GradesController.cs
//
// If Visual Studio shows "ambiguous" errors, the most common
// cause is that this file was accidentally saved inside the
// Models folder, creating a duplicate class. Delete any copy
// of this file that is NOT in the Controllers folder.
// ============================================================

using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Helpers;
using OACTsys.Models;
using OACTsys.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OACTsys.Controllers
{
    public class GradesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public GradesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ─────────────────────────────────────────────────────────────
        // Auth helpers
        // ─────────────────────────────────────────────────────────────
        private bool IsAdminLoggedIn()
            => !string.IsNullOrEmpty(HttpContext.Session.GetString("AdminRole"));

        private void SetLayoutData()
        {
            ViewBag.AdminName = HttpContext.Session.GetString("AdminName");
            ViewBag.AdminRole = HttpContext.Session.GetString("AdminRole");
            ViewBag.Permissions = AdminHelper.GetSessionPermissions(HttpContext);
        }

        // ─────────────────────────────────────────────────────────────
        // GET  /Grades
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            if (!IsAdminLoggedIn())
                return RedirectToAction("Login", "Admin");

            if (!AdminHelper.HasPermission(HttpContext, "Grades"))
                return Unauthorized();

            SetLayoutData();

            // 1. Students + FieldData
            var students = await _context.Students
                .Where(s => s.IsActive)
                .Include(s => s.FieldData)
                    .ThenInclude(fd => fd.EnrollmentField)
                .OrderBy(s => s.Program)
                .ThenBy(s => s.CurrentYearLevel)
                .ThenBy(s => s.StudentNumber)
                .ToListAsync();

            // 2. SubjectEnrollments + Subjects
            var allSE = await _context.SubjectEnrollments
                .Include(se => se.Subject)
                .Where(se => se.Subject != null && se.Subject.IsActive)
                .ToListAsync();

            var seByStudent = allSE
                .GroupBy(se => se.StudentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var vm = new GradesViewModel();

            foreach (var s in students)
            {
                // Build FieldData lookup keyed by normalised TemplateKey + FieldName
                var fd = GradeFieldHelper.BuildFieldDict(s.FieldData);

                // Try multiple key variants per column
                string Resolve(params string[] keys)
                {
                    foreach (var k in keys)
                        if (fd.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                            return v.Trim();
                    return "";
                }

                var fullName = Resolve(
                    "full_name", "fullname", "full_name_",
                    "name", "student_name", "studentname",
                    "complete_name", "completename",
                    "full name", "student name");

                var gender = Resolve(
                    "gender", "sex", "gender_sex", "gendersex");

                var phone = Resolve(
                    "phone_number", "phonenumber", "phone",
                    "mobile", "mobile_number", "mobilenumber",
                    "contact", "contact_number", "contactnumber",
                    "cellphone", "cellphone_number",
                    "phone number", "mobile number", "contact number");

                var email = Resolve(
                    "email_address", "emailaddress", "email",
                    "email_add", "emailadd", "email address");

                if (string.IsNullOrEmpty(email)) email = s.Email ?? "";
                if (string.IsNullOrEmpty(fullName)) fullName = s.StudentNumber ?? "";

                // Subject rows
                var seList = seByStudent.TryGetValue(s.StudentId, out var seItems)
                    ? seItems
                    : new List<SubjectEnrollment>();

                var subjects = seList
                    .Where(se => se.Subject != null && se.Subject.IsActive)
                    .Select(se => new GradeSubjectRow
                    {
                        SubjectEnrollmentId = se.SubjectEnrollmentId,
                        SubjectId = se.SubjectId,
                        Code = se.Subject!.CourseCode ?? "",
                        Title = se.Subject!.DescriptiveTitle ?? "",
                        Units = se.Subject!.Units,
                        Semester = se.Semester switch
                        {
                            1 => "1st Sem",
                            2 => "2nd Sem",
                            3 => "Summer",
                            _ => $"Sem {se.Semester}"
                        },
                        FinalGrade = se.FinalGrade ?? "",
                        Status = se.Status ?? ""
                    })
                    .OrderBy(r => r.Semester)
                    .ThenBy(r => r.Code)
                    .ToList();

                vm.Students.Add(new GradeStudentRow
                {
                    StudentId = s.StudentId,
                    StudentNumber = s.StudentNumber ?? "",
                    FullName = fullName,
                    Gender = gender,
                    PhoneNumber = phone,
                    Email = email,
                    Course = s.Program ?? "",
                    YearLevel = s.CurrentYearLevel switch
                    {
                        1 => "1st Year",
                        2 => "2nd Year",
                        3 => "3rd Year",
                        4 => "4th Year",
                        _ => $"{s.CurrentYearLevel}th Year"
                    },
                    Semester = s.CurrentSemester switch
                    {
                        1 => "1st Sem",
                        2 => "2nd Sem",
                        3 => "Summer",
                        _ => $"Sem {s.CurrentSemester}"
                    },
                    Subjects = subjects
                });
            }

            return View(vm);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /Grades/DebugFields?studentId=5
        // Shows every TemplateKey + FieldValue for one student.
        // Use this to find the exact key names the admin created.
        // REMOVE or restrict this in production.
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> DebugFields(int studentId)
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Not authenticated." });

            var student = await _context.Students
                .Include(s => s.FieldData)
                    .ThenInclude(fd => fd.EnrollmentField)
                .FirstOrDefaultAsync(s => s.StudentId == studentId);

            if (student == null)
                return Json(new { success = false, message = $"Student {studentId} not found." });

            var fields = (student.FieldData ?? new List<EnrollmentFieldData>())
                .Select(fd => new
                {
                    templateKey = fd.EnrollmentField?.TemplateKey ?? "",
                    templateKeyNorm = GradeFieldHelper.NormaliseKey(fd.EnrollmentField?.TemplateKey ?? ""),
                    fieldName = fd.EnrollmentField?.FieldName ?? "",
                    fieldNameNorm = GradeFieldHelper.NormaliseKey(fd.EnrollmentField?.FieldName ?? ""),
                    value = fd.FieldValue ?? ""
                })
                .OrderBy(x => x.fieldName)
                .ToList();

            return Json(new
            {
                success = true,
                studentId = student.StudentId,
                studentNumber = student.StudentNumber,
                studentEmail = student.Email,
                fieldCount = fields.Count,
                fields
            });
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Grades/SaveGrades
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveGrades([FromBody] SaveGradesRequest request)
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Not authenticated." });

            if (!AdminHelper.HasPermission(HttpContext, "Grades"))
                return Json(new { success = false, message = "Access denied." });

            if (request?.Grades == null || !request.Grades.Any())
                return Json(new { success = false, message = "No grades submitted." });

            foreach (var entry in request.Grades)
            {
                if (string.IsNullOrWhiteSpace(entry.FinalGrade)) continue;
                if (!decimal.TryParse(entry.FinalGrade, out var v) || v < 1.0m || v > 5.0m)
                    return Json(new { success = false, message = $"Invalid grade \"{entry.FinalGrade}\". Must be 1.0–5.0." });
            }

            try
            {
                var ids = request.Grades.Select(g => g.SubjectEnrollmentId).ToList();

                var rows = await _context.SubjectEnrollments
                    .Where(se => ids.Contains(se.SubjectEnrollmentId)
                              && se.StudentId == request.StudentId)
                    .ToListAsync();

                int saved = 0;
                foreach (var entry in request.Grades)
                {
                    if (string.IsNullOrWhiteSpace(entry.FinalGrade)) continue;
                    var row = rows.FirstOrDefault(se => se.SubjectEnrollmentId == entry.SubjectEnrollmentId);
                    if (row == null) continue;

                    row.FinalGrade = entry.FinalGrade.Trim();
                    if (decimal.TryParse(entry.FinalGrade, out var gv))
                        row.Status = GradeFieldHelper.ComputeStatus(gv);

                    saved++;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, savedCount = saved, message = $"{saved} grade(s) saved." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Grades/BulkUpload
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkUpload(IFormFile file)
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Not authenticated." });

            if (!AdminHelper.HasPermission(HttpContext, "Grades"))
                return Json(new { success = false, message = "Access denied." });

            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "No file uploaded." });

            var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xls" && ext != ".csv")
                return Json(new { success = false, message = "Only .xlsx, .xls, and .csv are supported." });

            if (file.Length > 5 * 1024 * 1024)
                return Json(new { success = false, message = "File must be under 5 MB." });

            try
            {
                var parseErrors = new List<string>();
                var rows = new List<BulkRow>();

                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                stream.Position = 0;

                using var wb = new XLWorkbook(stream);
                var ws = wb.Worksheets.First();

                var headers = ws.Row(1).CellsUsed()
                    .ToDictionary(
                        c => GradeFieldHelper.NormaliseKey(c.Value.ToString()),
                        c => c.Address.ColumnNumber,
                        StringComparer.OrdinalIgnoreCase);

                int Col(params string[] names)
                {
                    foreach (var n in names)
                        if (headers.TryGetValue(GradeFieldHelper.NormaliseKey(n), out var c)) return c;
                    return -1;
                }

                int cStu = Col("studentnumber", "studentno", "student");
                int cSub = Col("subjectcode", "subcode", "coursecode", "subject");
                int cGrade = Col("finalgrade", "final", "grade");
                int cSem = Col("semester", "sem");

                if (cStu < 0 || cSub < 0)
                    return Json(new { success = false, message = "Missing columns: StudentNumber, SubjectCode." });
                if (cGrade < 0)
                    return Json(new { success = false, message = "Missing column: FinalGrade." });

                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

                for (int r = 2; r <= lastRow; r++)
                {
                    var stuNo = ws.Cell(r, cStu).GetValue<string>()?.Trim() ?? "";
                    var subCd = ws.Cell(r, cSub).GetValue<string>()?.Trim() ?? "";
                    var grade = ws.Cell(r, cGrade).GetValue<string>()?.Trim() ?? "";
                    var semStr = cSem > 0 ? (ws.Cell(r, cSem).GetValue<string>()?.Trim() ?? "") : "";

                    if (string.IsNullOrEmpty(stuNo) && string.IsNullOrEmpty(subCd)) continue;

                    if (string.IsNullOrEmpty(stuNo)) { parseErrors.Add($"Row {r}: Missing StudentNumber."); continue; }
                    if (string.IsNullOrEmpty(subCd)) { parseErrors.Add($"Row {r}: Missing SubjectCode."); continue; }
                    if (string.IsNullOrEmpty(grade)) { parseErrors.Add($"Row {r}: Missing FinalGrade."); continue; }
                    if (!decimal.TryParse(grade, out var gv) || gv < 1.0m || gv > 5.0m)
                    { parseErrors.Add($"Row {r}: Invalid grade \"{grade}\" (must be 1.0–5.0)."); continue; }

                    rows.Add(new BulkRow { RowNum = r, StudentNumber = stuNo, SubjectCode = subCd, FinalGrade = grade, Semester = semStr });
                }

                if (!rows.Any())
                    return Json(new { success = false, message = "No valid rows found.", errors = parseErrors });

                var studentNos = rows.Select(r => r.StudentNumber).Distinct().ToList();
                var subCodes = rows.Select(r => r.SubjectCode).Distinct().ToList();

                var seRecords = await _context.SubjectEnrollments
                    .Include(se => se.Student)
                    .Include(se => se.Subject)
                    .Where(se => se.Student != null && se.Subject != null
                              && studentNos.Contains(se.Student!.StudentNumber)
                              && subCodes.Contains(se.Subject!.CourseCode))
                    .ToListAsync();

                var seMap = seRecords
                    .Where(se => se.Student != null && se.Subject != null)
                    .ToDictionary(
                        se => $"{se.Student!.StudentNumber}|{se.Subject!.CourseCode}",
                        se => se);

                int saved = 0, skipped = 0;
                var warnings = new List<string>();

                foreach (var row in rows)
                {
                    var key = $"{row.StudentNumber}|{row.SubjectCode}";
                    if (!seMap.TryGetValue(key, out var seRow))
                    {
                        warnings.Add($"Row {row.RowNum}: No enrollment for \"{row.StudentNumber}\" / \"{row.SubjectCode}\".");
                        skipped++; continue;
                    }

                    seRow.FinalGrade = row.FinalGrade;
                    if (decimal.TryParse(row.FinalGrade, out var gv))
                        seRow.Status = GradeFieldHelper.ComputeStatus(gv);
                    saved++;
                }

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    savedCount = saved,
                    skippedCount = skipped,
                    parseErrors,
                    warnings,
                    message = $"{saved} grade(s) saved. {skipped} skipped."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Upload failed: {ex.Message}" });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // GET /Grades/GetStudentGrades?studentId=5
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetStudentGrades(int studentId)
        {
            if (!IsAdminLoggedIn())
                return Json(new { success = false, message = "Not authenticated." });

            try
            {
                var grades = await _context.SubjectEnrollments
                    .Where(se => se.StudentId == studentId)
                    .Include(se => se.Subject)
                    .Select(se => new
                    {
                        subjectEnrollmentId = se.SubjectEnrollmentId,
                        subjectCode = se.Subject != null ? se.Subject.CourseCode : "",
                        subjectTitle = se.Subject != null ? se.Subject.DescriptiveTitle : "",
                        units = se.Subject != null ? se.Subject.Units : 0,
                        semester = se.Semester,
                        finalGrade = se.FinalGrade,
                        status = se.Status
                    })
                    .ToListAsync();

                return Json(new { success = true, grades });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ── Internal DTO ──────────────────────────────────────────────
        private sealed class BulkRow
        {
            public int RowNum { get; init; }
            public string StudentNumber { get; init; } = "";
            public string SubjectCode { get; init; } = "";
            public string FinalGrade { get; init; } = "";
            public string Semester { get; init; } = "";
        }
    }
}