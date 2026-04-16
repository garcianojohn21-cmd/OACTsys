using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OACTsys.Data;
using OACTsys.Helpers;
using OACTsys.Models;
using OACTsys.Models.ViewModels;
using OACTsys.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace OACTsys.Controllers
{
    public class AdminController : BaseAdminController
    {
        private readonly string _adminToken;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly SubjectService _subjectService;

        public AdminController(
            ApplicationDbContext context,
            IConfiguration config,
            IWebHostEnvironment environment,
            SubjectService subjectService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _adminToken = config["AdminToken"];
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _subjectService = subjectService ?? throw new ArgumentNullException(nameof(subjectService));
        }

        // =====================================================================
        // LOGIN
        // =====================================================================

        [SkipAdminAuth]
        [HttpGet]
        public IActionResult Login(string token)
        {
            if (!string.IsNullOrEmpty(HttpContext.Session.GetString("AdminRole")))
                return RedirectToAction("Dashboard");

            if (string.IsNullOrEmpty(token)) return Unauthorized("Portal token is required.");
            if (token != _adminToken) return Unauthorized("Invalid portal token.");
            ViewBag.Token = token;
            return View();
        }

        [SkipAdminAuth]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, string token)
        {
            if (token != _adminToken) return Unauthorized("Invalid portal token.");

            if (AdminHelper.ValidateSuperAdmin(username, password))
            {
                HttpContext.Session.SetString("AdminName", AdminHelper.SuperFullName);
                HttpContext.Session.SetString("AdminRole", "SuperAdmin");
                HttpContext.Session.SetString("UserRole", "SuperAdmin");
                AdminHelper.SetPermissionsSession(HttpContext, AdminHelper.AllPermissions);
                return RedirectToAction("Dashboard");
            }

            var admin = await _context.Admins
                .Include(a => a.AdminPermissions)
                .FirstOrDefaultAsync(a => a.Username == username && a.IsActive);

            if (admin != null && AdminHelper.VerifyPassword(password, admin.PasswordHash))
            {
                admin.LastLogin = DateTime.Now;
                await _context.SaveChangesAsync();

                HttpContext.Session.SetInt32("AdminId", admin.Id);
                HttpContext.Session.SetString("AdminName", admin.FullName);
                HttpContext.Session.SetString("AdminRole", admin.RoleName);
                HttpContext.Session.SetString("UserRole", admin.RoleName);

                var permissions = admin.AdminPermissions.Select(p => p.PermissionName).ToList();
                AdminHelper.SetPermissionsSession(HttpContext, permissions);
                return RedirectToAction("Dashboard");
            }

            ViewBag.Error = "Invalid username or password";
            ViewBag.Token = token;
            return View();
        }

        // =====================================================================
        // LOGOUT
        // =====================================================================

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", new { token = _adminToken });
        }

        // =====================================================================
        // DASHBOARD
        // =====================================================================

        public IActionResult Dashboard()
        {
            if (!HasPermission("Dashboard")) return Unauthorized();
            SetLayoutData();
            return View();
        }

        // =====================================================================
        // ENROLLMENT MANAGEMENT
        // =====================================================================

        public IActionResult Enrollment()
        {
            if (!HasPermission("Enrollment")) return Unauthorized();

            ViewBag.IsSuperAdmin = IsSuperAdmin();
            SetLayoutData();

            ViewBag.ExistingFields = _context.EnrollmentFields
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .ToList();

            ViewBag.TemplateKeyOptions = EnrollmentFieldMappingHelper.GetOptions();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SaveEnrollmentFields([FromBody] List<EnrollmentFieldDto> fields)
        {
            if (!IsSuperAdmin())
                return Json(new { success = false, message = "Access Denied: Only Super Administrators can modify enrollment fields." });

            try
            {
                if (fields == null || fields.Count == 0)
                    return Json(new { success = false, message = "No fields received." });

                var validationErrors = ValidateEnrollmentFields(fields);
                if (validationErrors.Any())
                    return Json(new { success = false, message = "Validation failed: " + string.Join("; ", validationErrors) });

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var oldFields = await _context.EnrollmentFields.Where(f => f.IsActive).ToListAsync();
                    foreach (var field in oldFields) field.IsActive = false;

                    int displayOrder = 1;
                    foreach (var fieldDto in fields)
                        _context.EnrollmentFields.Add(MapToEnrollmentField(fieldDto, displayOrder++));

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return Json(new { success = true, message = "Enrollment fields saved successfully." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = $"Transaction failed: {GetFullError(ex)}" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {GetFullError(ex)}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateFieldTemplateKey(int id, string templateKey)
        {
            if (!IsSuperAdmin())
                return Json(new { success = false, message = "Access Denied." });
            try
            {
                var field = await _context.EnrollmentFields.FindAsync(id);
                if (field == null)
                    return Json(new { success = false, message = "Field not found." });

                field.TemplateKey = string.IsNullOrWhiteSpace(templateKey) ? null : templateKey.Trim();
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Template key updated." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateEnrollmentField(int id, [FromBody] EnrollmentFieldDto dto)
        {
            if (!IsSuperAdmin())
                return Json(new { success = false, message = "Access Denied." });
            try
            {
                var field = await _context.EnrollmentFields.FindAsync(id);
                if (field == null)
                    return Json(new { success = false, message = "Field not found." });

                field.FieldName = dto.FieldName?.Trim();
                field.Category = dto.Category?.Trim() ?? "";
                field.FieldType = dto.FieldType?.Trim();
                field.IsRequired = dto.IsRequired;
                field.HelperText = dto.HelperText?.Trim() ?? "";
                field.TemplateKey = string.IsNullOrWhiteSpace(dto.TemplateKey) ? null : dto.TemplateKey.Trim();
                field.MinLimit = dto.MinLimit;
                field.MaxLimit = dto.MaxLimit;

                if (dto.Options != null && dto.Options.Any())
                    field.Options = JsonConvert.SerializeObject(dto.Options);

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Field updated." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

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
                        f.Id,
                        f.FieldName,
                        f.Category,
                        f.FieldType,
                        f.IsRequired,
                        Options = !string.IsNullOrEmpty(f.Options)
                            ? JsonConvert.DeserializeObject<List<string>>(f.Options)
                            : new List<string>(),
                        f.AcceptedFileTypes,
                        f.MaxFileSize,
                        f.HelperText,
                        f.DisplayOrder,
                        f.TemplateKey,
                        f.MinLimit,
                        f.MaxLimit
                    })
                    .ToListAsync();

                return Json(new { success = true, fields, isSuperAdmin = IsSuperAdmin() });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteEnrollmentField(int id)
        {
            if (!IsSuperAdmin())
                return Json(new { success = false, message = "Access Denied: Only Super Administrators can delete enrollment fields." });
            try
            {
                var field = await _context.EnrollmentFields.FindAsync(id);
                if (field == null)
                    return Json(new { success = false, message = "Field not found." });

                field.IsActive = false;
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Field deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // =====================================================================
        // SUBJECTS
        // =====================================================================

        [HttpPost]
        public async Task<IActionResult> SaveSubjects([FromBody] List<SubjectDto> subjects)
        {
            if (!IsSuperAdmin())
                return Json(new { success = false, message = "Access Denied: Only Super Administrators can modify subjects." });
            try
            {
                if (subjects == null || subjects.Count == 0)
                    return Json(new { success = false, message = "No subjects received." });

                var validationErrors = ValidateSubjects(subjects);
                if (validationErrors.Any())
                    return Json(new { success = false, message = "Validation failed: " + string.Join("; ", validationErrors) });

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    foreach (var subjectDto in subjects)
                    {
                        if (subjectDto.SubjectId.HasValue && subjectDto.SubjectId.Value > 0)
                        {
                            var existing = await _context.Subjects.FindAsync(subjectDto.SubjectId.Value);
                            if (existing != null)
                            {
                                existing.Program = subjectDto.Program;
                                existing.CourseCode = subjectDto.CourseCode;
                                existing.DescriptiveTitle = subjectDto.DescriptiveTitle;
                                existing.LectureHours = subjectDto.LectureHours;
                                existing.LaboratoryHours = subjectDto.LabHours;
                                existing.Units = subjectDto.Units;
                                existing.YearLevel = subjectDto.YearLevel;
                                existing.Semester = subjectDto.Semester;
                                existing.TotalHours = subjectDto.TotalHours;
                                existing.IsActive = true;
                                _context.Subjects.Update(existing);
                            }
                        }
                        else
                        {
                            _context.Subjects.Add(MapToSubject(subjectDto));
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return Json(new { success = true, message = "Subjects saved successfully." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = $"Transaction failed: {GetFullError(ex)}" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSubjects(string program = null, int? yearLevel = null, int? semester = null)
        {
            try
            {
                var query = _context.Subjects.Where(s => s.IsActive);
                if (!string.IsNullOrEmpty(program))
                    query = query.Where(s => s.Program == program);
                if (yearLevel.HasValue)
                    query = query.Where(s => s.YearLevel == yearLevel.Value);
                if (semester.HasValue)
                    query = query.Where(s => s.Semester == semester.Value);

                var subjects = await query
                    .OrderBy(s => s.Program).ThenBy(s => s.YearLevel)
                    .ThenBy(s => s.Semester).ThenBy(s => s.CourseCode)
                    .Select(s => new
                    {
                        id = s.SubjectId,
                        program = s.Program,
                        courseCode = s.CourseCode,
                        descriptiveTitle = s.DescriptiveTitle,
                        lectureHours = s.LectureHours,
                        laboratoryHours = s.LaboratoryHours,
                        units = s.Units,
                        yearLevel = s.YearLevel,
                        semester = s.Semester,
                        totalHours = s.TotalHours
                    }).ToListAsync();

                return Json(new { success = true, subjects, isSuperAdmin = IsSuperAdmin() });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSubjectsForTerm(string program = null, int yearLevel = 1, int semester = 1)
        {
            try
            {
                var allSubjects = await _context.Subjects.ToListAsync();

                var needsFix = allSubjects.Where(s => !s.IsActive).ToList();
                if (needsFix.Any())
                {
                    foreach (var s in needsFix) s.IsActive = true;
                    await _context.SaveChangesAsync();
                }

                var progNorm = (program ?? "").Trim().ToUpper();

                var subjects = allSubjects
                    .Where(s =>
                        (string.IsNullOrWhiteSpace(progNorm) ||
                         (s.Program ?? "").Trim().ToUpper() == progNorm)
                        && s.YearLevel == yearLevel
                        && s.Semester == semester)
                    .OrderBy(s => s.CourseCode)
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
                    .ToList();

                return Json(new
                {
                    success = true,
                    subjects,
                    debug_totalSubjectsInDb = allSubjects.Count,
                    debug_autoFixedIsActive = needsFix.Count,
                    debug_requestedFilter = new { program = progNorm, yearLevel, semester },
                    debug_matchedCount = subjects.Count,
                    debug_programsInDb = allSubjects.Select(s => (s.Program ?? "").Trim().ToUpper()).Distinct().OrderBy(p => p).ToList(),
                    debug_yearLevelsInDb = allSubjects.Select(s => s.YearLevel).Distinct().OrderBy(y => y).ToList(),
                    debug_semestersInDb = allSubjects.Select(s => s.Semester).Distinct().OrderBy(s => s).ToList(),
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, subjects = new List<object>() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAllSubjectsDebug()
        {
            var all = await _context.Subjects.ToListAsync();
            return Json(new
            {
                totalRows = all.Count,
                subjects = all.Select(s => new
                {
                    s.SubjectId,
                    s.Program,
                    s.CourseCode,
                    s.DescriptiveTitle,
                    s.YearLevel,
                    s.Semester,
                    s.Units,
                    s.IsActive
                })
            });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteSubject(int id)
        {
            if (!IsSuperAdmin())
                return Json(new { success = false, message = "Access Denied." });
            try
            {
                var subject = await _context.Subjects.FindAsync(id);
                if (subject == null)
                    return Json(new { success = false, message = "Subject not found." });

                subject.IsActive = false;
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Subject deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPut]
        public async Task<IActionResult> UpdateSubject(int id, [FromBody] SubjectDto dto)
        {
            if (!IsSuperAdmin())
                return Json(new { success = false, message = "Access Denied." });
            try
            {
                var subject = await _context.Subjects.FindAsync(id);
                if (subject == null)
                    return Json(new { success = false, message = "Subject not found." });

                subject.Program = dto.Program?.Trim();
                subject.CourseCode = dto.CourseCode?.Trim();
                subject.DescriptiveTitle = dto.DescriptiveTitle?.Trim();
                subject.LectureHours = dto.LectureHours;
                subject.LaboratoryHours = dto.LabHours;
                subject.Units = dto.Units;
                subject.YearLevel = dto.YearLevel;
                subject.Semester = dto.Semester;
                subject.TotalHours = dto.TotalHours;

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Subject updated successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // SUBJECT DOCX UPLOAD — PreviewSubjectUpload
        // ──────────────────────────────────────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> PreviewSubjectUpload(IFormFile file)
        {
            try
            {
                if (!IsSuperAdmin())
                    return Json(new { success = false, message = "Only Super Administrators can upload subjects." });

                if (file == null || file.Length == 0)
                    return Json(new { success = false, message = "No file was uploaded." });

                if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                    return Json(new { success = false, message = "Please upload a .docx file." });

                using var stream = file.OpenReadStream();
                var result = _subjectService.PreviewDocx(stream);

                if (!result.Success)
                    return Json(new { success = false, message = result.Message });

                var existingSubjects = await _context.Subjects
                    .Where(s => s.IsActive)
                    .Select(s => new { s.Program, s.CourseCode, s.YearLevel, s.Semester })
                    .ToListAsync();

                var existingCodesMap = existingSubjects
                    .ToDictionary(
                        s => $"{(s.Program ?? "").Trim().ToUpper()}_{(s.CourseCode ?? "").Trim().ToUpper()}_{s.YearLevel}_{s.Semester}",
                        s => true,
                        StringComparer.OrdinalIgnoreCase
                    );

                var subjects = result.ParsedSubjects.Select(s => new
                {
                    program = s.Program,
                    courseCode = s.CourseCode,
                    descriptiveTitle = s.DescriptiveTitle,
                    lectureHours = s.LectureHours,
                    laboratoryHours = s.LaboratoryHours,
                    totalHours = s.TotalHours,
                    units = s.Units,
                    yearLevel = s.YearLevel,
                    semester = s.Semester
                }).ToList();

                return Json(new
                {
                    success = true,
                    message = result.Message,
                    totalParsed = result.TotalParsed,
                    subjects,
                    existingCodesMap
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error reading file: " + ex.Message });
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // SUBJECT DOCX UPLOAD — UploadSubjectsFromDocx
        // ──────────────────────────────────────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> UploadSubjectsFromDocx(IFormFile file, string replaceExisting = "false")
        {
            try
            {
                if (!IsSuperAdmin())
                    return Json(new { success = false, message = "Only Super Administrators can upload subjects." });

                if (file == null || file.Length == 0)
                    return Json(new { success = false, message = "No file was uploaded." });

                if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                    return Json(new { success = false, message = "Please upload a .docx file." });

                bool doReplace = string.Equals(replaceExisting, "true", StringComparison.OrdinalIgnoreCase);

                using var stream = file.OpenReadStream();
                var result = await _subjectService.UploadSubjectsFromDocxAsync(stream, doReplace);

                return Json(new
                {
                    success = result.Success,
                    message = result.Message,
                    totalParsed = result.TotalParsed,
                    inserted = result.Inserted,
                    updated = result.Updated,
                    skipped = result.Skipped
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error importing subjects: " + ex.Message });
            }
        }

        // =====================================================================
        // STUDENT MANAGEMENT
        // =====================================================================

        public IActionResult StudentRecords()
        {
            if (!HasPermission("StudentRecords")) return Unauthorized();
            SetLayoutData();
            return View();
        }

        public IActionResult DoneStudents()
        {
            if (!HasPermission("StudentRecords")) return Unauthorized();
            SetLayoutData();
            return View();
        }

        public IActionResult PendingStudents()
        {
            if (!HasPermission("StudentRecords")) return Unauthorized();
            SetLayoutData();
            return View();
        }

        public IActionResult ArchivedStudents()
        {
            if (!HasPermission("StudentRecords")) return Unauthorized();
            SetLayoutData();
            return View();
        }

        public async Task<IActionResult> ViewStudentData(int id)
        {
            if (!HasPermission("StudentRecords")) return Unauthorized();

            var student = await _context.Students
                .Include(s => s.FieldData).ThenInclude(fd => fd.EnrollmentField)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.StudentId == id);

            if (student == null) return NotFound();
            SetLayoutData();
            return View(student);
        }

        [HttpGet]
        public async Task<IActionResult> GetStudentByNumber(string studentNumber)
        {
            if (string.IsNullOrWhiteSpace(studentNumber))
                return Json(new { success = false, message = "Student number is required." });

            try
            {
                var student = await _context.Students
                    .Include(s => s.FieldData).ThenInclude(fd => fd.EnrollmentField)
                    .FirstOrDefaultAsync(s => s.StudentNumber == studentNumber.Trim() && s.IsActive);

                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                var fields = student.FieldData ?? new List<EnrollmentFieldData>();

                var lastName = fields.FirstOrDefault(f =>
                    f.EnrollmentField != null &&
                    new[] { "last name", "lastname", "surname", "apellido" }
                        .Any(k => f.EnrollmentField.FieldName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    ?.FieldValue?.Trim() ?? "";

                var firstName = fields.FirstOrDefault(f =>
                    f.EnrollmentField != null &&
                    new[] { "first name", "firstname", "given name", "nombre" }
                        .Any(k => f.EnrollmentField.FieldName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    ?.FieldValue?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(firstName))
                {
                    var fullNameField = fields.FirstOrDefault(f =>
                        f.EnrollmentField != null &&
                        string.Equals(f.EnrollmentField.TemplateKey, "full_name", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(f.FieldValue));

                    if (fullNameField != null)
                        return Json(new
                        {
                            success = true,
                            studentId = student.StudentId,
                            fullName = fullNameField.FieldValue.Trim(),
                            studentNumber = student.StudentNumber,
                            program = student.Program ?? "",
                            yearLevel = student.CurrentYearLevel,
                            semester = student.CurrentSemester,
                            paymentStatus = student.PaymentStatus ?? "",
                        });
                }

                string displayName;
                if (!string.IsNullOrWhiteSpace(lastName) || !string.IsNullOrWhiteSpace(firstName))
                    displayName = $"{lastName}, {firstName}".Trim(',').Trim();
                else
                    displayName = student.StudentNumber;

                return Json(new
                {
                    success = true,
                    studentId = student.StudentId,
                    fullName = displayName,
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

        [HttpGet]
        public async Task<IActionResult> CreateStudentRecord()
        {
            if (!HasPermission("StudentRecords")) return Unauthorized();

            SetLayoutData();

            var fields = await _context.EnrollmentFields
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .ToListAsync();

            var programs = await _context.Subjects
                .Where(s => s.IsActive && s.Program != null && s.Program != "")
                .Select(s => s.Program.Trim().ToUpper())
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync();

            if (!programs.Any())
            {
                programs = await _context.Students
                    .Where(s => s.IsActive && s.Program != null && s.Program != "")
                    .Select(s => s.Program.Trim().ToUpper())
                    .Distinct()
                    .OrderBy(p => p)
                    .ToListAsync();
            }

            ViewBag.Programs = programs;
            return View("CreateStudent", fields);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStudentRecord(IFormCollection form)
        {
            if (!HasPermission("StudentRecords")) return Unauthorized();

            try
            {
                var chosenProgram = form["ChosenProgram"].ToString().Trim().ToUpper();
                var chosenStudentType = form["ChosenStudentType"].ToString().Trim();
                var enrollmentStatus = form["EnrollmentStatus"].ToString().Trim();
                var paymentStatus = form["PaymentStatus"].ToString().Trim();
                var adminNotes = form["AdminNotes"].ToString().Trim();

                if (!int.TryParse(form["ChosenYearLevel"].ToString(), out int yearLevel) || yearLevel < 1)
                    yearLevel = 1;
                if (!int.TryParse(form["ChosenSemester"].ToString(), out int semester) || semester < 1)
                    semester = 1;

                if (string.IsNullOrEmpty(chosenProgram))
                {
                    TempData["CreateStudentError"] = "Please select a program.";
                    return RedirectToAction("CreateStudentRecord");
                }

                var enrollmentFields = await _context.EnrollmentFields
                    .Where(f => f.IsActive)
                    .OrderBy(f => f.DisplayOrder)
                    .ToListAsync();

                string studentEmail = string.Empty;
                foreach (var field in enrollmentFields)
                {
                    bool isEmail =
                        string.Equals(field.FieldType, "email", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(field.TemplateKey, "email_address", StringComparison.OrdinalIgnoreCase) ||
                        field.FieldName.Contains("email", StringComparison.OrdinalIgnoreCase);

                    if (isEmail)
                    {
                        var val = form[$"Field_{field.Id}"].ToString();
                        if (!string.IsNullOrWhiteSpace(val)) { studentEmail = val; break; }
                    }
                }

                var student = new Student
                {
                    StudentNumber = await GenerateTempStudentNumberAsync(),
                    StudentType = string.IsNullOrEmpty(chosenStudentType) ? "Old Student" : chosenStudentType,
                    Program = chosenProgram,
                    CurrentYearLevel = yearLevel,
                    CurrentSemester = semester,
                    EnrollmentStatus = string.IsNullOrEmpty(enrollmentStatus) ? "Pending" : enrollmentStatus,
                    PaymentStatus = string.IsNullOrEmpty(paymentStatus) ? "Unpaid" : paymentStatus,
                    Email = studentEmail,
                    IsActive = true,
                    HasAccount = false,
                    PasswordHash = string.Empty,
                    EnrollmentDate = DateTime.Now,
                    CreatedDate = DateTime.Now,
                    LastModifiedDate = DateTime.Now,
                };

                _context.Students.Add(student);
                await _context.SaveChangesAsync();

                var uploadPath = Path.Combine(_environment.WebRootPath, "uploads", "enrollments");
                Directory.CreateDirectory(uploadPath);

                var uploadedFiles = new Dictionary<int, string>();
                foreach (var file in Request.Form.Files)
                {
                    if (file.Length > 0)
                    {
                        var fieldIdStr = file.Name.Replace("File_", "");
                        if (int.TryParse(fieldIdStr, out int fieldId))
                        {
                            var safeFileName = Path.GetFileName(file.FileName);
                            var fileName = $"student_{student.StudentId}_{Guid.NewGuid()}_{safeFileName}";
                            var filePath = Path.Combine(uploadPath, fileName);
                            using var fileStream = new FileStream(filePath, FileMode.Create);
                            await file.CopyToAsync(fileStream);
                            uploadedFiles[fieldId] = $"/uploads/enrollments/{fileName}";
                        }
                    }
                }

                foreach (var field in enrollmentFields)
                {
                    var fieldData = new EnrollmentFieldData
                    {
                        StudentId = student.StudentId,
                        EnrollmentFieldId = field.Id,
                        SubmittedDate = DateTime.Now,
                        FieldValue = string.Empty,
                        FilePath = string.Empty,
                    };

                    if (field.FieldType == "file" || field.FieldType == "image")
                    {
                        if (uploadedFiles.TryGetValue(field.Id, out var fp))
                        {
                            fieldData.FilePath = fp;
                            fieldData.FieldValue = Path.GetFileName(fp);
                        }
                    }
                    else
                    {
                        fieldData.FieldValue = form[$"Field_{field.Id}"].ToString();
                    }

                    if (!string.IsNullOrEmpty(fieldData.FieldValue) || !string.IsNullOrEmpty(fieldData.FilePath))
                        _context.EnrollmentFieldData.Add(fieldData);
                }

                var selectedSubjects = form["SelectedSubjects"]
                    .ToList()
                    .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => int.Parse(v))
                    .Distinct()
                    .ToList();

                if (selectedSubjects.Any())
                {
                    int academicYear = DateTime.Now.Month >= 6 ? DateTime.Now.Year : DateTime.Now.Year - 1;

                    var subjectLookup = await _context.Subjects
                        .Where(s => selectedSubjects.Contains(s.SubjectId))
                        .ToDictionaryAsync(s => s.SubjectId);

                    foreach (var subjectId in selectedSubjects)
                    {
                        subjectLookup.TryGetValue(subjectId, out var subj);
                        _context.SubjectEnrollments.Add(new SubjectEnrollment
                        {
                            StudentId = student.StudentId,
                            SubjectId = subjectId,
                            YearLevel = subj?.YearLevel ?? yearLevel,
                            Semester = subj?.Semester ?? semester,
                            AcademicYear = academicYear,
                            Status = "Pending",
                            EnrolledDate = DateTime.Now,
                            FinalGrade = string.Empty,
                        });
                    }
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Student record created successfully. You can now assign a student number below.";
                return RedirectToAction("ViewStudentData", new { id = student.StudentId });
            }
            catch (Exception ex)
            {
                TempData["CreateStudentError"] = $"Error creating student: {GetFullError(ex)}";
                return RedirectToAction("CreateStudentRecord");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPrograms()
        {
            try
            {
                var programs = await _context.Students
                    .Where(s => s.IsActive && s.Program != null && s.Program != "")
                    .Select(s => s.Program.Trim().ToUpper())
                    .Distinct()
                    .OrderBy(p => p)
                    .ToListAsync();

                return Json(new { success = true, programs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AssignStudentNumber([FromBody] AssignStudentNumberDto dto)
        {
            if (!HasPermission("StudentRecords"))
                return Json(new { success = false, message = "Unauthorized." });

            if (dto == null || string.IsNullOrWhiteSpace(dto.StudentNumber))
                return Json(new { success = false, message = "Student number cannot be blank." });

            var number = dto.StudentNumber.Trim();
            if (number.Length > 50)
                return Json(new { success = false, message = "Student number cannot exceed 50 characters." });

            try
            {
                var duplicate = await _context.Students
                    .AnyAsync(s => s.StudentNumber == number && s.StudentId != dto.StudentId && s.IsActive);

                if (duplicate)
                    return Json(new { success = false, message = $"'{number}' is already assigned to another student." });

                var student = await _context.Students.FindAsync(dto.StudentId);
                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                var oldNumber = student.StudentNumber;
                student.StudentNumber = number;
                student.LastModifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    studentNumber = number,
                    message = $"Student number updated from '{oldNumber}' to '{number}'."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetStudents(string status = "Enrolled", string program = null, string search = null)
        {
            if (!HasPermission("StudentRecords"))
                return Json(new { success = false, message = "Unauthorized." });

            try
            {
                var query = _context.Students
                    .Include(s => s.FieldData).ThenInclude(fd => fd.EnrollmentField)
                    .Include(s => s.Payments)
                    .Where(s => s.IsActive)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(s => s.EnrollmentStatus == status);

                if (!string.IsNullOrWhiteSpace(program))
                    query = query.Where(s => s.Program.ToUpper() == program.ToUpper());

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim().ToLower();
                    query = query.Where(s =>
                        s.StudentNumber.ToLower().Contains(term) ||
                        (s.Email != null && s.Email.ToLower().Contains(term)) ||
                        s.FieldData.Any(fd =>
                            fd.EnrollmentField != null &&
                            fd.EnrollmentField.TemplateKey == "full_name" &&
                            fd.FieldValue != null &&
                            fd.FieldValue.ToLower().Contains(term)));
                }

                var rawList = await query.OrderByDescending(s => s.EnrollmentDate ?? s.CreatedDate).ToListAsync();

                var students = rawList.Select(s =>
                {
                    string ByKey(string key) =>
                        s.FieldData?
                         .FirstOrDefault(fd =>
                             fd.EnrollmentField?.TemplateKey == key &&
                             !string.IsNullOrWhiteSpace(fd.FieldValue))
                         ?.FieldValue?.Trim() ?? "";

                    string FileByKey(string key) =>
                        s.FieldData?
                         .FirstOrDefault(fd =>
                             fd.EnrollmentField?.TemplateKey == key &&
                             !string.IsNullOrWhiteSpace(fd.FilePath))
                         ?.FilePath;

                    var fullName = ByKey("full_name");
                    if (string.IsNullOrWhiteSpace(fullName))
                    {
                        var first = ByKey("first_name");
                        var last = ByKey("last_name");
                        fullName = $"{first} {last}".Trim();
                    }
                    if (string.IsNullOrWhiteSpace(fullName))
                        fullName = s.Email ?? $"Student #{s.StudentId}";

                    var lastVerifiedPayment = s.Payments?
                        .Where(p => p.IsActive && p.Status == "Verified")
                        .OrderByDescending(p => p.VerifiedDate ?? p.PaymentDate)
                        .FirstOrDefault();

                    var totalPaid = s.Payments?
                        .Where(p => p.IsActive && p.Status == "Verified")
                        .Sum(p => p.Amount) ?? 0m;

                    return new
                    {
                        studentId = s.StudentId,
                        studentNumber = s.StudentNumber,
                        program = s.Program,
                        studentType = s.StudentType,
                        currentYearLevel = s.CurrentYearLevel,
                        currentSemester = s.CurrentSemester,
                        enrollmentStatus = s.EnrollmentStatus,
                        paymentStatus = s.PaymentStatus,
                        enrollmentDate = s.EnrollmentDate,
                        createdDate = s.CreatedDate,
                        email = s.Email,
                        hasAccount = s.HasAccount,
                        fullName,
                        photoUrl = FileByKey("photo_2x2"),
                        totalPaid,
                        lastPaymentAmount = lastVerifiedPayment?.Amount,
                        lastPaymentDate = lastVerifiedPayment?.PaymentDate.ToString("MMM dd, yyyy"),
                        lastPaymentMethod = lastVerifiedPayment?.PaymentMethod,
                    };
                }).ToList();

                return Json(new { success = true, students, total = students.Count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = GetFullError(ex) });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetStudentStats()
        {
            try
            {
                var grouped = await _context.Students
                    .Where(s => s.IsActive)
                    .GroupBy(s => s.EnrollmentStatus)
                    .Select(g => new { status = g.Key, count = g.Count() })
                    .ToListAsync();

                int Count(string st) => grouped.FirstOrDefault(g => g.status == st)?.count ?? 0;

                var withAccount = await _context.Students.CountAsync(s => s.IsActive && s.HasAccount);

                return Json(new
                {
                    success = true,
                    total = grouped.Sum(g => g.count),
                    enrolled = Count("Enrolled"),
                    pending = Count("Pending"),
                    approved = Count("Approved"),
                    rejected = Count("Rejected"),
                    withAccount
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveStudent(int id)
        {
            if (!HasPermission("StudentRecords"))
                return Json(new { success = false, message = "Unauthorized." });
            try
            {
                var student = await _context.Students.FindAsync(id);
                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                student.IsActive = false;
                student.LastModifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"Student {student.StudentNumber} archived." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateEnrollmentStatus([FromBody] UpdateEnrollmentStatusDto dto)
        {
            if (!HasPermission("StudentRecords"))
                return Json(new { success = false, message = "Unauthorized." });

            var allowed = new[] { "Pending", "Approved", "Enrolled", "Rejected" };
            if (!allowed.Contains(dto.Status))
                return Json(new { success = false, message = "Invalid status value." });

            try
            {
                var student = await _context.Students.FindAsync(dto.StudentId);
                if (student == null)
                    return Json(new { success = false, message = "Student not found." });

                student.EnrollmentStatus = dto.Status;
                student.LastModifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"Status updated to '{dto.Status}'." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> BulkUpdateStudents([FromBody] BulkUpdateDto dto)
        {
            if (!HasPermission("StudentRecords"))
                return Json(new { success = false, message = "Unauthorized." });

            if (dto == null || dto.StudentIds == null || !dto.StudentIds.Any())
                return Json(new { success = false, message = "No students selected." });

            var validActions = new[] { "Approve", "Reject", "Enroll", "Archive" };
            if (!validActions.Contains(dto.Action))
                return Json(new { success = false, message = "Invalid action." });

            try
            {
                var students = await _context.Students
                    .Where(s => dto.StudentIds.Contains(s.StudentId) && s.IsActive)
                    .ToListAsync();

                if (!students.Any())
                    return Json(new { success = false, message = "No matching students found." });

                foreach (var s in students)
                {
                    s.LastModifiedDate = DateTime.Now;
                    switch (dto.Action)
                    {
                        case "Approve": s.EnrollmentStatus = "Approved"; break;
                        case "Reject": s.EnrollmentStatus = "Rejected"; break;
                        case "Enroll": s.EnrollmentStatus = "Enrolled"; break;
                        case "Archive": s.IsActive = false; break;
                    }
                }

                await _context.SaveChangesAsync();

                var verb = dto.Action switch
                {
                    "Approve" => "approved",
                    "Reject" => "rejected",
                    "Enroll" => "enrolled",
                    "Archive" => "archived",
                    _ => dto.Action.ToLower()
                };

                return Json(new
                {
                    success = true,
                    affected = students.Count,
                    message = $"{students.Count} student(s) {verb} successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // =====================================================================
        // CREATE / MANAGE ADMINS
        // =====================================================================

        [HttpGet]
        public IActionResult CreateAdmin()
        {
            if (!HasPermission("CreateAdmin")) return Unauthorized();
            SetLayoutData();
            ViewBag.AllPermissions = AdminHelper.AllPermissions;
            ViewBag.GeneratedPassword = AdminHelper.GenerateRandomPassword();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAdmin(
            string FullName, string Username, string Email,
            string Password, string RoleName, List<string> Permissions)
        {
            if (!HasPermission("CreateAdmin")) return Unauthorized();

            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(FullName)) errors.Add("Full name is required");
            if (string.IsNullOrWhiteSpace(Username)) errors.Add("Username is required");
            else if (Username.Length > 50) errors.Add("Username cannot exceed 50 characters");
            if (string.IsNullOrWhiteSpace(Email)) errors.Add("Email is required");
            else if (!IsValidEmail(Email)) errors.Add("Invalid email format");
            if (string.IsNullOrWhiteSpace(Password)) errors.Add("Password is required");
            else if (Password.Length < 8) errors.Add("Password must be at least 8 characters");
            if (string.IsNullOrWhiteSpace(RoleName)) errors.Add("Role name is required");

            if (await _context.Admins.AnyAsync(a => a.Username == Username))
                errors.Add("Username already exists");
            if (await _context.Admins.AnyAsync(a => a.Email == Email))
                errors.Add("Email already exists");

            if (errors.Any())
            {
                TempData["ErrorMessage"] = string.Join("<br>", errors);
                SetLayoutData();
                ViewBag.AllPermissions = AdminHelper.AllPermissions;
                ViewBag.GeneratedPassword = Password;
                ViewBag.FullName = FullName;
                ViewBag.Username = Username;
                ViewBag.Email = Email;
                ViewBag.RoleName = RoleName;
                ViewBag.SelectedPermissions = Permissions ?? new List<string>();
                return View();
            }

            try
            {
                var admin = new Admin
                {
                    FullName = FullName.Trim(),
                    Username = Username.Trim(),
                    Email = Email.Trim(),
                    PasswordHash = AdminHelper.HashPassword(Password),
                    RoleName = RoleName.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                _context.Admins.Add(admin);
                await _context.SaveChangesAsync();

                if (Permissions != null && Permissions.Any())
                {
                    foreach (var p in Permissions)
                        _context.AdminPermissions.Add(new AdminPermission { AdminId = admin.Id, PermissionName = p });
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = $"Admin '{admin.FullName}' created successfully!";
                TempData["TempPassword"] = Password;
                TempData["NewUsername"] = Username;
                return RedirectToAction("AdminList");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error creating admin: {ex.Message}";
                SetLayoutData();
                ViewBag.AllPermissions = AdminHelper.AllPermissions;
                return View();
            }
        }

        public async Task<IActionResult> AdminList()
        {
            if (!HasPermission("UserManagement")) return Unauthorized();
            SetLayoutData();

            var admins = await _context.Admins
                .Include(a => a.AdminPermissions)
                .Where(a => a.IsActive)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return View(admins);
        }

        [HttpGet]
        public async Task<IActionResult> EditAdmin(int id)
        {
            if (!HasPermission("UserManagement")) return Unauthorized();

            var admin = await _context.Admins
                .Include(a => a.AdminPermissions)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (admin == null) return NotFound();

            SetLayoutData();
            ViewBag.AllPermissions = AdminHelper.AllPermissions;
            ViewBag.Admin = admin;
            ViewBag.SelectedPermissions = admin.AdminPermissions.Select(p => p.PermissionName).ToList();
            return View(admin);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAdmin(
            int id, string FullName, string Email,
            string RoleName, List<string> Permissions, string NewPassword)
        {
            if (!HasPermission("UserManagement")) return Unauthorized();

            var admin = await _context.Admins
                .Include(a => a.AdminPermissions)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (admin == null) return NotFound();

            try
            {
                admin.FullName = FullName.Trim();
                admin.Email = Email.Trim();
                admin.RoleName = RoleName.Trim();

                if (!string.IsNullOrWhiteSpace(NewPassword))
                {
                    if (NewPassword.Length < 8)
                    {
                        TempData["ErrorMessage"] = "Password must be at least 8 characters";
                        return RedirectToAction("EditAdmin", new { id });
                    }
                    admin.PasswordHash = AdminHelper.HashPassword(NewPassword);
                }

                _context.AdminPermissions.RemoveRange(admin.AdminPermissions);

                if (Permissions != null && Permissions.Any())
                    foreach (var p in Permissions)
                        _context.AdminPermissions.Add(new AdminPermission { AdminId = admin.Id, PermissionName = p });

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Admin '{admin.FullName}' updated successfully!";
                return RedirectToAction("AdminList");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error updating admin: {ex.Message}";
                return RedirectToAction("EditAdmin", new { id });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeactivateAdmin([FromBody] DeactivateAdminRequest request)
        {
            if (!HasPermission("UserManagement"))
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var admin = await _context.Admins.FindAsync(request.Id);
                if (admin == null)
                    return Json(new { success = false, message = "Admin not found" });

                var currentAdminId = HttpContext.Session.GetInt32("AdminId");
                if (currentAdminId.HasValue && currentAdminId.Value == admin.Id)
                    return Json(new { success = false, message = "You cannot deactivate your own account" });

                admin.IsActive = false;
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Admin '{admin.FullName}' deactivated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        public class DeactivateAdminRequest { public int Id { get; set; } }

        // =====================================================================
        // GRADES — list view
        // =====================================================================

        public async Task<IActionResult> Grades()
        {
            if (!HasPermission("Grades")) return Unauthorized();
            SetLayoutData();

            var students = await _context.Students
                .Include(s => s.FieldData).ThenInclude(fd => fd.EnrollmentField)
                .Where(s => s.IsActive && s.EnrollmentStatus == "Enrolled")
                .OrderBy(s => s.StudentNumber)
                .ToListAsync();

            var studentIds = students.Select(s => s.StudentId).ToList();

            var subjectEnrollments = await _context.SubjectEnrollments
                .Include(se => se.Subject)
                .Where(se => studentIds.Contains(se.StudentId))
                .ToListAsync();

            var enrollmentsByStudent = subjectEnrollments
                .GroupBy(se => se.StudentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var model = new GradesViewModel();

            foreach (var s in students)
            {
                string ByKey(string key) =>
                    s.FieldData?
                     .FirstOrDefault(fd =>
                         string.Equals(fd.EnrollmentField?.TemplateKey, key, StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(fd.FieldValue))
                     ?.FieldValue?.Trim() ?? "";

                string ByName(params string[] keywords) =>
                    s.FieldData?
                     .FirstOrDefault(fd =>
                         fd.EnrollmentField != null &&
                         keywords.Any(k => fd.EnrollmentField.FieldName.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                         !string.IsNullOrWhiteSpace(fd.FieldValue))
                     ?.FieldValue?.Trim() ?? "";

                string FileByKey(string key) =>
                    s.FieldData?
                     .FirstOrDefault(fd =>
                         string.Equals(fd.EnrollmentField?.TemplateKey, key, StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(fd.FilePath))
                     ?.FilePath ?? "";

                var fullName = ByKey("full_name");
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    var first = ByKey("first_name");
                    var last = ByKey("last_name");
                    fullName = $"{first} {last}".Trim();
                }
                if (string.IsNullOrWhiteSpace(fullName))
                    fullName = s.Email ?? s.StudentNumber;

                var gender = ByKey("gender");
                if (string.IsNullOrWhiteSpace(gender)) gender = ByName("gender", "sex");

                var phone = ByKey("phone_number");
                if (string.IsNullOrWhiteSpace(phone)) phone = ByKey("contact_number");
                if (string.IsNullOrWhiteSpace(phone)) phone = ByName("phone", "mobile", "contact", "cellular");

                var email = s.Email;
                if (string.IsNullOrWhiteSpace(email)) email = ByKey("email_address");
                if (string.IsNullOrWhiteSpace(email)) email = ByName("email");

                enrollmentsByStudent.TryGetValue(s.StudentId, out var ses);

                model.Students.Add(new GradeStudentRow
                {
                    StudentId = s.StudentId,
                    StudentNumber = s.StudentNumber ?? "",
                    FullName = fullName,
                    Gender = gender,
                    PhoneNumber = phone,
                    Email = email ?? "",
                    Course = s.Program ?? "",
                    YearLevel = ToYearLabel(s.CurrentYearLevel),
                    Semester = ToSemLabel(s.CurrentSemester),
                    PhotoUrl = FileByKey("photo_2x2"),
                    Subjects = (ses ?? new List<SubjectEnrollment>())
                        .OrderBy(se => se.Semester)
                        .ThenBy(se => se.Subject?.CourseCode)
                        .Select(se => new GradeSubjectRow
                        {
                            SubjectEnrollmentId = se.SubjectEnrollmentId,
                            SubjectId = se.SubjectId,
                            Code = se.Subject?.CourseCode ?? "",
                            Title = se.Subject?.DescriptiveTitle ?? "",
                            Units = se.Subject?.Units ?? 0,
                            Semester = ToSemLabel(se.Semester),
                            PrelimGrade = se.PrelimGrade ?? "",
                            MidtermGrade = se.MidtermGrade ?? "",
                            FinalGrade = se.FinalGrade ?? "",
                            Status = se.Status ?? ""
                        })
                        .ToList()
                });
            }

            return View(model);
        }

        // =====================================================================
        // GRADES — save (manual / per-student modal)
        // =====================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveGrades([FromBody] SaveGradesRequest dto)
        {
            if (!HasPermission("Grades"))
                return Json(new { success = false, message = "Unauthorized." });

            if (dto?.Grades == null || !dto.Grades.Any())
                return Json(new { success = false, message = "No grades provided." });

            try
            {
                foreach (var entry in dto.Grades)
                {
                    if (!ValidateGradeValue(entry.PrelimGrade, out var err1)) return Json(new { success = false, message = err1 });
                    if (!ValidateGradeValue(entry.MidtermGrade, out var err2)) return Json(new { success = false, message = err2 });
                    if (!ValidateGradeValue(entry.FinalGrade, out var err3)) return Json(new { success = false, message = err3 });
                }

                var ids = dto.Grades.Select(g => g.SubjectEnrollmentId).Distinct().ToList();
                var enrollments = await _context.SubjectEnrollments
                    .Where(se => ids.Contains(se.SubjectEnrollmentId) && se.StudentId == dto.StudentId)
                    .ToListAsync();

                if (!enrollments.Any())
                    return Json(new { success = false, message = "No matching enrollments found for this student." });

                int saved = 0;
                foreach (var entry in dto.Grades)
                {
                    var enrollment = enrollments.FirstOrDefault(e => e.SubjectEnrollmentId == entry.SubjectEnrollmentId);
                    if (enrollment == null) continue;

                    if (!string.IsNullOrWhiteSpace(entry.PrelimGrade))
                        enrollment.PrelimGrade = entry.PrelimGrade.Trim();

                    if (!string.IsNullOrWhiteSpace(entry.MidtermGrade))
                        enrollment.MidtermGrade = entry.MidtermGrade.Trim();

                    if (!string.IsNullOrWhiteSpace(entry.FinalGrade))
                    {
                        enrollment.FinalGrade = entry.FinalGrade.Trim();
                        decimal g = decimal.Parse(entry.FinalGrade, CultureInfo.InvariantCulture);
                        enrollment.Status = g switch
                        {
                            5.0m => "Failed",
                            4.0m => "Incomplete",
                            >= 1.0m and <= 3.0m => "Passed",
                            _ => "Conditional"
                        };
                    }

                    saved++;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"{saved} subject(s) updated successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {GetFullError(ex)}" });
            }
        }

        // =====================================================================
        // GRADES — bulk Excel upload  ★ FIXED ★
        // Accepts the raw .xlsx file directly (IFormFile).
        // Parses server-side using ClosedXML — no JS column-mapping needed.
        // Supports both template formats:
        //   • Old: Student Number | Student Name | Gender | Subject Code | Prelim | Midterm | Final
        //   • New: Student Number | Student Name | Gender | Subject Code | Final Grade | Semester | School Year
        // Header row is found by scanning for "Student Number" (rows 1-20).
        // =====================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkUploadGrades(IFormFile file)
        {
            if (!HasPermission("Grades"))
                return Json(new { success = false, message = "Unauthorized." });

            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "No file uploaded." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xls")
                return Json(new { success = false, message = "Please upload an Excel file (.xlsx)." });

            try
            {
                var rows = new List<BulkGradeEntryDto>();
                var parseWarnings = new List<string>();

                using var stream = file.OpenReadStream();
                using var wb = new XLWorkbook(stream);
                var ws = wb.Worksheets.First();

                // ── Find header row by scanning for "Student Number" in any column ──
                int headerRow = -1;
                int lastScanRow = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 20, 20);

                for (int r = 1; r <= lastScanRow; r++)
                {
                    int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 10;
                    for (int c = 1; c <= lastCol; c++)
                    {
                        if (ws.Cell(r, c).GetString().Trim()
                            .Equals("Student Number", StringComparison.OrdinalIgnoreCase))
                        {
                            headerRow = r;
                            break;
                        }
                    }
                    if (headerRow > 0) break;
                }

                if (headerRow < 0)
                    return Json(new
                    {
                        success = false,
                        message = "Could not find the 'Student Number' header row. " +
                                  "Make sure you are using the correct GradesTemplate.xlsx."
                    });

                // ── Map column names → column indices ────────────────────────────────
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int totalCols = ws.LastColumnUsed()?.ColumnNumber() ?? 10;
                for (int c = 1; c <= totalCols; c++)
                {
                    var h = ws.Cell(headerRow, c).GetString().Trim();
                    if (!string.IsNullOrEmpty(h) && !headers.ContainsKey(h))
                        headers[h] = c;
                }

                // Required columns
                if (!headers.ContainsKey("Student Number"))
                    return Json(new { success = false, message = "Column 'Student Number' not found." });
                if (!headers.ContainsKey("Subject Code"))
                    return Json(new { success = false, message = "Column 'Subject Code' not found." });

                // Must have at least one grade column
                bool hasFinalGrade = headers.ContainsKey("Final Grade");
                bool hasFinal = headers.ContainsKey("Final");
                bool hasPrelim = headers.ContainsKey("Prelim");
                bool hasMidterm = headers.ContainsKey("Midterm");

                if (!hasFinalGrade && !hasFinal && !hasPrelim && !hasMidterm)
                    return Json(new
                    {
                        success = false,
                        message = "No grade columns found. Expected 'Final Grade', 'Prelim', 'Midterm', or 'Final'."
                    });

                int colStudentNum = headers["Student Number"];
                int colSubjectCode = headers["Subject Code"];
                int colFinalGrade = hasFinalGrade ? headers["Final Grade"] : (hasFinal ? headers["Final"] : 0);
                int colPrelim = hasPrelim ? headers["Prelim"] : 0;
                int colMidterm = hasMidterm ? headers["Midterm"] : 0;

                // ── Read data rows ───────────────────────────────────────────────────
                int lastDataRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;

                for (int r = headerRow + 1; r <= lastDataRow; r++)
                {
                    var studentNo = ws.Cell(r, colStudentNum).GetString().Trim();
                    var subjectCode = ws.Cell(r, colSubjectCode).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(studentNo)) continue;  // blank row = end of data

                    if (string.IsNullOrWhiteSpace(subjectCode))
                    {
                        parseWarnings.Add($"Row {r}: Student '{studentNo}' has no Subject Code — skipped.");
                        continue;
                    }

                    decimal? ParseGrade(int col)
                    {
                        if (col == 0) return null;
                        var raw = ws.Cell(r, col).GetString().Trim();
                        if (string.IsNullOrWhiteSpace(raw)) return null;
                        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                            return v;
                        parseWarnings.Add($"Row {r}: Invalid grade value '{raw}' for '{studentNo}' — ignored.");
                        return null;
                    }

                    rows.Add(new BulkGradeEntryDto
                    {
                        StudentNumber = studentNo,
                        SubjectCode = subjectCode,
                        PrelimGrade = ParseGrade(colPrelim),
                        MidtermGrade = ParseGrade(colMidterm),
                        FinalGrade = ParseGrade(colFinalGrade),
                    });
                }

                if (!rows.Any())
                    return Json(new { success = false, message = "No data rows found in the uploaded file." });

                // ── Lookup students & subjects from DB ───────────────────────────────
                var studentNumbers = rows.Select(r => r.StudentNumber!).Distinct().ToList();
                var subjectCodes = rows.Select(r => r.SubjectCode!).Distinct().ToList();

                var students = await _context.Students
                    .Where(s => studentNumbers.Contains(s.StudentNumber) && s.IsActive)
                    .ToDictionaryAsync(s => s.StudentNumber!);

                var subjects = await _context.Subjects
                    .Where(s => subjectCodes.Contains(s.CourseCode) && s.IsActive)
                    .ToDictionaryAsync(s => s.CourseCode!);

                if (!students.Any())
                    return Json(new
                    {
                        success = false,
                        message = "None of the student numbers were found in the database. " +
                                  "Check that students are active and their numbers match exactly."
                    });

                var studentIds = students.Values.Select(s => s.StudentId).ToList();
                var subjectIds = subjects.Values.Select(s => s.SubjectId).ToList();

                var enrollments = await _context.SubjectEnrollments
                    .Where(se => studentIds.Contains(se.StudentId) && subjectIds.Contains(se.SubjectId))
                    .ToListAsync();

                // ── Apply grades ─────────────────────────────────────────────────────
                int updated = 0, skipped = 0;

                foreach (var row in rows)
                {
                    if (!students.TryGetValue(row.StudentNumber!, out var student)) { skipped++; continue; }
                    if (!subjects.TryGetValue(row.SubjectCode!, out var subject)) { skipped++; continue; }

                    var enrollment = enrollments.FirstOrDefault(e =>
                        e.StudentId == student.StudentId && e.SubjectId == subject.SubjectId);

                    if (enrollment == null) { skipped++; continue; }

                    bool anyGrade = false;

                    if (row.PrelimGrade.HasValue)
                    {
                        if (row.PrelimGrade < 1m || row.PrelimGrade > 5m) { skipped++; continue; }
                        enrollment.PrelimGrade = row.PrelimGrade.Value.ToString("0.00", CultureInfo.InvariantCulture);
                        anyGrade = true;
                    }

                    if (row.MidtermGrade.HasValue)
                    {
                        if (row.MidtermGrade < 1m || row.MidtermGrade > 5m) { skipped++; continue; }
                        enrollment.MidtermGrade = row.MidtermGrade.Value.ToString("0.00", CultureInfo.InvariantCulture);
                        anyGrade = true;
                    }

                    if (row.FinalGrade.HasValue)
                    {
                        if (row.FinalGrade < 1m || row.FinalGrade > 5m) { skipped++; continue; }
                        enrollment.FinalGrade = row.FinalGrade.Value.ToString("0.00", CultureInfo.InvariantCulture);
                        enrollment.Status = row.FinalGrade.Value switch
                        {
                            5.0m => "Failed",
                            4.0m => "Incomplete",
                            >= 1.0m and <= 3.0m => "Passed",
                            _ => "Conditional"
                        };
                        anyGrade = true;
                    }

                    if (anyGrade) updated++;
                    else skipped++;
                }

                await _context.SaveChangesAsync();

                var message = $"{updated} grade record(s) updated successfully.";
                if (skipped > 0)
                    message += $" {skipped} row(s) skipped (student/subject not found, not enrolled, or out-of-range grade).";
                if (parseWarnings.Any())
                    message += $" Warnings: {string.Join(" | ", parseWarnings)}";

                return Json(new { success = true, message, updated, skipped });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error processing file: {GetFullError(ex)}" });
            }
        }

        // =====================================================================
        // GRADES — download template
        // =====================================================================

        [HttpGet]
        public IActionResult DownloadGradesTemplate()
        {
            if (!HasPermission("Grades")) return Unauthorized();

            var filePath = Path.Combine(_environment.WebRootPath, "templates", "GradesTemplate.xlsx");

            if (!System.IO.File.Exists(filePath))
                return NotFound(
                    "Template file not found. " +
                    "Please place GradesTemplate.xlsx in wwwroot/templates/ and redeploy.");

            return PhysicalFile(
                filePath,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "GradesTemplate.xlsx");
        }

        // ── Helper: validate Philippine grade scale ───────────────────────────
        private static bool ValidateGradeValue(string raw, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(raw)) return true;
            if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v) ||
                v < 1m || v > 5m)
            {
                error = $"Invalid grade '{raw}'. Accepted range: 1.00 – 5.00.";
                return false;
            }
            return true;
        }

        // =====================================================================
        // OTHER SIMPLE PAGE ACTIONS
        // =====================================================================

        public IActionResult Licensure() { if (!HasPermission("Licensure")) return Unauthorized(); SetLayoutData(); return View(); }
        public IActionResult Payments() { if (!HasPermission("Payments")) return Unauthorized(); SetLayoutData(); return View(); }
        public IActionResult Reports() { if (!HasPermission("Reports")) return Unauthorized(); SetLayoutData(); return View(); }
        public IActionResult Settings() { if (!HasPermission("SystemSettings")) return Unauthorized(); SetLayoutData(); return View(); }
        public IActionResult Messages() { if (!HasPermission("Messages")) return Unauthorized(); SetLayoutData(); return View(); }
        public IActionResult Logs() { if (!HasPermission("AuditLogs")) return Unauthorized(); SetLayoutData(); return View(); }
        public IActionResult AccessDenied() { SetLayoutData(); return View(); }

        // =====================================================================
        // VALIDATION HELPERS
        // =====================================================================

        private List<string> ValidateEnrollmentFields(List<EnrollmentFieldDto> fields)
        {
            var errors = new List<string>();
            var validTypes = new[] { "text", "number", "date", "select", "file", "image", "email", "phone" };

            for (int i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (string.IsNullOrWhiteSpace(f.FieldName)) errors.Add($"Field #{i + 1}: Field name is required");
                if (f.FieldName?.Length > 200) errors.Add($"Field #{i + 1}: Field name max 200 chars");
                if (!string.IsNullOrEmpty(f.Category) && f.Category.Length > 100) errors.Add($"Field #{i + 1}: Category max 100 chars");
                if (string.IsNullOrWhiteSpace(f.FieldType)) errors.Add($"Field #{i + 1}: Field type is required");
                if (!validTypes.Contains(f.FieldType?.ToLower())) errors.Add($"Field #{i + 1}: Invalid field type '{f.FieldType}'");
                if (f.FieldType?.ToLower() == "select" && (f.Options == null || !f.Options.Any()))
                    errors.Add($"Field #{i + 1}: Dropdown fields must have at least one option");
                if (f.MaxFileSize < 1 || f.MaxFileSize > 50) errors.Add($"Field #{i + 1}: Max file size must be 1–50 MB");
                if (f.MinLimit.HasValue && f.MaxLimit.HasValue && f.MinLimit > f.MaxLimit)
                    errors.Add($"Field #{i + 1}: Min limit cannot be greater than max limit");
            }
            return errors;
        }

        private List<string> ValidateSubjects(List<SubjectDto> subjects)
        {
            var errors = new List<string>();
            for (int i = 0; i < subjects.Count; i++)
            {
                var s = subjects[i];
                if (string.IsNullOrWhiteSpace(s.Program)) errors.Add($"Subject #{i + 1}: Program required");
                if (s.Program?.Length > 50) errors.Add($"Subject #{i + 1}: Program max 50 chars");
                if (string.IsNullOrWhiteSpace(s.CourseCode)) errors.Add($"Subject #{i + 1}: Course code required");
                if (s.CourseCode?.Length > 20) errors.Add($"Subject #{i + 1}: Course code max 20 chars");
                if (string.IsNullOrWhiteSpace(s.DescriptiveTitle)) errors.Add($"Subject #{i + 1}: Title required");
                if (s.DescriptiveTitle?.Length > 200) errors.Add($"Subject #{i + 1}: Title max 200 chars");
                if (s.YearLevel < 1 || s.YearLevel > 4) errors.Add($"Subject #{i + 1}: Year level must be 1–4");
                if (s.Semester < 1 || s.Semester > 3) errors.Add($"Subject #{i + 1}: Semester must be 1–3");
                if (s.Units < 0 || s.Units > 10) errors.Add($"Subject #{i + 1}: Units must be 0–10");
            }
            return errors;
        }

        // =====================================================================
        // MAPPING
        // =====================================================================

        private EnrollmentField MapToEnrollmentField(EnrollmentFieldDto dto, int displayOrder)
        {
            string optionsJson = "[]";
            if (dto.Options != null && dto.Options.Any())
                optionsJson = JsonConvert.SerializeObject(dto.Options);

            return new EnrollmentField
            {
                FieldName = dto.FieldName?.Trim().Substring(0, Math.Min(dto.FieldName.Trim().Length, 200)),
                Category = !string.IsNullOrWhiteSpace(dto.Category)
                                       ? dto.Category.Trim().Substring(0, Math.Min(dto.Category.Trim().Length, 100))
                                       : string.Empty,
                FieldType = dto.FieldType?.Trim().Substring(0, Math.Min(dto.FieldType.Trim().Length, 50)),
                IsRequired = dto.IsRequired,
                Options = optionsJson,
                AcceptedFileTypes = dto.AcceptedFileTypes?.Trim().Substring(0, Math.Min(dto.AcceptedFileTypes?.Trim().Length ?? 0, 200)) ?? "",
                MaxFileSize = dto.MaxFileSize > 0 ? dto.MaxFileSize : 5,
                HelperText = dto.HelperText?.Trim().Substring(0, Math.Min(dto.HelperText?.Trim().Length ?? 0, 500)) ?? "",
                DisplayOrder = displayOrder,
                IsActive = true,
                CreatedDate = DateTime.Now,
                TemplateKey = string.IsNullOrWhiteSpace(dto.TemplateKey) ? null : dto.TemplateKey.Trim(),
                MinLimit = dto.MinLimit,
                MaxLimit = dto.MaxLimit
            };
        }

        private Subject MapToSubject(SubjectDto dto) => new Subject
        {
            Program = dto.Program?.Trim(),
            CourseCode = dto.CourseCode?.Trim(),
            DescriptiveTitle = dto.DescriptiveTitle?.Trim(),
            LectureHours = dto.LectureHours,
            LaboratoryHours = dto.LabHours,
            Units = dto.Units,
            YearLevel = dto.YearLevel,
            Semester = dto.Semester,
            TotalHours = dto.TotalHours,
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        // =====================================================================
        // PRIVATE HELPERS
        // =====================================================================

        private static string ToYearLabel(int y) => y switch
        {
            1 => "1st Year",
            2 => "2nd Year",
            3 => "3rd Year",
            4 => "4th Year",
            _ => $"Year {y}"
        };

        private static string ToSemLabel(int s) => s switch
        {
            1 => "1st Sem",
            2 => "2nd Sem",
            3 => "Summer",
            _ => $"Sem {s}"
        };

        private string GetFullError(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            var cur = ex;
            while (cur != null)
            {
                sb.AppendLine(cur.GetType().Name + ": " + cur.Message);
                if (cur is Microsoft.Data.SqlClient.SqlException sqlEx)
                {
                    sb.AppendLine($"SQL Error: {sqlEx.Number} / State: {sqlEx.State}");
                    foreach (Microsoft.Data.SqlClient.SqlError err in sqlEx.Errors)
                        sb.AppendLine($"- {err.Message}");
                }
                cur = cur.InnerException;
            }
            return sb.ToString();
        }

        private bool IsValidEmail(string email)
        {
            try { var a = new System.Net.Mail.MailAddress(email); return a.Address == email; }
            catch { return false; }
        }

        private async Task<string> GenerateTempStudentNumberAsync()
        {
            var existingTemps = await _context.Students
                .Where(s => s.StudentNumber != null && s.StudentNumber.StartsWith("TEMP-"))
                .Select(s => s.StudentNumber)
                .ToListAsync();

            int maxSeq = 0;
            foreach (var num in existingTemps)
            {
                var suffix = num.Substring(5);
                if (int.TryParse(suffix, out int seq) && seq > maxSeq)
                    maxSeq = seq;
            }

            return $"TEMP-{maxSeq + 1:D4}";
        }
    }

    // =========================================================================
    // DTOs
    // =========================================================================

    public class EnrollmentFieldDto
    {
        public string FieldName { get; set; }
        public string Category { get; set; }
        public string FieldType { get; set; }
        public bool IsRequired { get; set; }
        public List<string> Options { get; set; }
        public string AcceptedFileTypes { get; set; }
        public int MaxFileSize { get; set; }
        public string HelperText { get; set; }
        public string TemplateKey { get; set; }
        public int? MinLimit { get; set; }
        public int? MaxLimit { get; set; }
    }

    public class SubjectDto
    {
        public int? SubjectId { get; set; }
        public string Program { get; set; }
        public string CourseCode { get; set; }
        public string DescriptiveTitle { get; set; }
        public int LectureHours { get; set; }
        public int LabHours { get; set; }
        public decimal Units { get; set; }
        public int YearLevel { get; set; }
        public int Semester { get; set; }
        public int TotalHours { get; set; }
    }

    public class UpdateEnrollmentStatusDto
    {
        public int StudentId { get; set; }
        public string Status { get; set; }
    }

    public class AssignStudentNumberDto
    {
        public int StudentId { get; set; }
        public string StudentNumber { get; set; }
    }

    public class BulkUpdateDto
    {
        public List<int> StudentIds { get; set; }
        public string Action { get; set; }
    }

    /// <summary>
    /// Parsed from the Excel template by BulkUploadGrades (server-side).
    /// PrelimGrade and MidtermGrade are nullable — safely ignored when their
    /// column is absent (e.g., "Final Grade only" template variant).
    /// </summary>
    public class BulkGradeEntryDto
    {
        public string? StudentNumber { get; set; }
        public string? SubjectCode { get; set; }
        public decimal? PrelimGrade { get; set; }
        public decimal? MidtermGrade { get; set; }
        public decimal? FinalGrade { get; set; }
    }

    // =========================================================================
    // SESSION EXTENSIONS
    // =========================================================================

    public static class SessionExtensions
    {
        public static void SetObjectAsJson(this ISession session, string key, object value) =>
            session.SetString(key, System.Text.Json.JsonSerializer.Serialize(value));

        public static T GetObjectFromJson<T>(this ISession session, string key)
        {
            var value = session.GetString(key);
            return value == null
                ? default(T)
                : System.Text.Json.JsonSerializer.Deserialize<T>(value);
        }
    }
}