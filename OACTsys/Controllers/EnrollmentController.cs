// ============================================================
// FILE: Controllers/EnrollmentController.cs
// ============================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Models;
using OACTsys.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OACTsys.Controllers
{
    public class EnrollmentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EnrollmentService _enrollmentService;
        private readonly IWebHostEnvironment _environment;
        private readonly AdmissionFormService _admissionFormService;
        private readonly EmailAcknowledgementService _emailService;

        public EnrollmentController(
            ApplicationDbContext context,
            EnrollmentService enrollmentService,
            IWebHostEnvironment environment,
            AdmissionFormService admissionFormService,
            EmailAcknowledgementService emailService)
        {
            _context = context;
            _enrollmentService = enrollmentService;
            _environment = environment;
            _admissionFormService = admissionFormService;
            _emailService = emailService;
        }

        public IActionResult Index() => View();

        // ══════════════════════════════════════════
        // GET: Freshmen Form
        // ══════════════════════════════════════════
        public async Task<IActionResult> FreshmenForm()
        {
            var fields = await _context.EnrollmentFields
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .ToListAsync();

            var subjects = await _context.Subjects
                .Where(s => s.IsActive && s.YearLevel == 1 && s.Semester == 1)
                .OrderBy(s => s.Program)
                .ThenBy(s => s.CourseCode)
                .ToListAsync();

            ViewBag.Subjects = subjects;
            return View(fields);
        }

        // ══════════════════════════════════════════
        // POST: Submit Freshmen Registration
        // ══════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitFreshmenRegistration(IFormCollection form)
        {
            try
            {
                // ── 1. Validate program ───────────────────────────────────
                var chosenProgram = form["ChosenProgram"].ToString().ToUpper().Trim();
                if (string.IsNullOrEmpty(chosenProgram))
                {
                    TempData["Error"] = "Please select a program.";
                    return RedirectToAction("FreshmenForm");
                }

                // ── 2. Validate subjects ──────────────────────────────────
                var selectedSubjects = form["SelectedSubjects"]
                    .ToList()
                    .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(int.Parse)
                    .Distinct()
                    .ToList();

                if (!selectedSubjects.Any())
                {
                    TempData["Error"] = "Please select at least one subject.";
                    return RedirectToAction("FreshmenForm");
                }

                var validSubjectIds = await _context.Subjects
                    .Where(s => selectedSubjects.Contains(s.SubjectId))
                    .Select(s => s.SubjectId)
                    .ToListAsync();

                if (validSubjectIds.Count != selectedSubjects.Count)
                {
                    TempData["Error"] = "One or more selected subjects are invalid.";
                    return RedirectToAction("FreshmenForm");
                }

                // ── 3. Load active enrollment fields ──────────────────────
                var enrollmentFields = await _context.EnrollmentFields
                    .Where(f => f.IsActive)
                    .OrderBy(f => f.DisplayOrder)
                    .ToListAsync();

                // ── 4. Resolve student email ───────────────────────────────
                string studentEmail = string.Empty;
                foreach (var field in enrollmentFields)
                {
                    bool isEmailField =
                        string.Equals(field.FieldType, "email", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(field.TemplateKey, "email_address", StringComparison.OrdinalIgnoreCase) ||
                        field.FieldName.Contains("email", StringComparison.OrdinalIgnoreCase);

                    if (isEmailField)
                    {
                        var val = form[$"Field_{field.Id}"].ToString();
                        if (!string.IsNullOrWhiteSpace(val)) { studentEmail = val; break; }
                    }
                }

                // ── 5. Create and save Student ────────────────────────────
                // Temporary number format: TEMP-0001, TEMP-0002, …
                // Admin replaces it with the real number from Student Records.
                var student = new Student
                {
                    StudentNumber = await GenerateTempStudentNumberAsync(),
                    StudentType = "Freshmen",
                    Program = chosenProgram,
                    CurrentYearLevel = 1,
                    CurrentSemester = 1,
                    EnrollmentStatus = "Pending",
                    PaymentStatus = "Unpaid",
                    Email = studentEmail,
                    IsActive = true,
                    HasAccount = false,
                    PasswordHash = string.Empty,
                    EnrollmentDate = DateTime.Now,
                    CreatedDate = DateTime.Now,
                };

                _context.Students.Add(student);
                await _context.SaveChangesAsync();

                // ── 6. Handle file uploads ────────────────────────────────
                var uploadPath = Path.Combine(
                    _environment.WebRootPath, "uploads", "enrollments");
                Directory.CreateDirectory(uploadPath);

                var uploadedFiles = new Dictionary<int, string>();
                foreach (var file in Request.Form.Files)
                {
                    if (file.Length > 0)
                    {
                        var fieldIdStr = file.Name.Replace("File_", "");
                        if (int.TryParse(fieldIdStr, out int fieldId))
                        {
                            var fileName = $"student_{student.StudentId}_{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                            var filePath = Path.Combine(uploadPath, fileName);
                            using var stream = new FileStream(filePath, FileMode.Create);
                            await file.CopyToAsync(stream);
                            uploadedFiles[fieldId] = $"/uploads/enrollments/{fileName}";
                        }
                    }
                }

                // ── 7. Save field data ────────────────────────────────────
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

                    if (!string.IsNullOrEmpty(fieldData.FieldValue) ||
                        !string.IsNullOrEmpty(fieldData.FilePath))
                        _context.EnrollmentFieldData.Add(fieldData);
                }

                // ── 8. Save subject enrollments ───────────────────────────
                int academicYear = GetCurrentAcademicYearInt();
                foreach (var subjectId in validSubjectIds)
                {
                    _context.SubjectEnrollments.Add(new SubjectEnrollment
                    {
                        StudentId = student.StudentId,
                        SubjectId = subjectId,
                        YearLevel = 1,
                        Semester = 1,
                        AcademicYear = academicYear,
                        Status = "Pending",
                        EnrolledDate = DateTime.Now,
                        FinalGrade = string.Empty,
                    });
                }

                await _context.SaveChangesAsync();

                // ── 9. Send acknowledgment email ──────────────────────────
                try
                {
                    var emailResult = await _emailService
                        .SendAcknowledgementAsync(student.StudentId);

                    TempData["EmailSent"] = emailResult.Success ? "true" : "false";
                    TempData["EmailAddress"] = emailResult.RecipientEmail ?? "";
                    TempData["EmailError"] = emailResult.ErrorMessage ?? "";
                }
                catch (Exception emailEx)
                {
                    TempData["EmailSent"] = "false";
                    TempData["EmailError"] = emailEx.Message;
                }

                TempData["Success"] = "Registration submitted successfully!";
                TempData["StudentNumber"] = student.StudentNumber;

                return RedirectToAction("RegistrationSuccess",
                    new { studentId = student.StudentId });
            }
            catch (Exception ex)
            {
                var msgs = new List<string>();
                var cur = (Exception?)ex;
                while (cur != null) { msgs.Add(cur.Message); cur = cur.InnerException; }
                TempData["Error"] = "Error: " + string.Join(" → ", msgs);
                return RedirectToAction("FreshmenForm");
            }
        }

        // ══════════════════════════════════════════
        // GET: Transferee Form
        // ══════════════════════════════════════════
        public async Task<IActionResult> TransfereeForm()
        {
            var fields = await _context.EnrollmentFields
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .ToListAsync();

            var subjects = await _context.Subjects
                .Where(s => s.IsActive)
                .OrderBy(s => s.Program)
                .ThenBy(s => s.YearLevel)
                .ThenBy(s => s.Semester)
                .ThenBy(s => s.CourseCode)
                .ToListAsync();

            ViewBag.Subjects = subjects;
            return View(fields);
        }

        // ══════════════════════════════════════════
        // POST: Submit Transferee Registration
        // ══════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitTransfereeRegistration(IFormCollection form)
        {
            try
            {
                // ── 1. Validate program ───────────────────────────────────
                var chosenProgram = form["ChosenProgram"].ToString().ToUpper().Trim();
                if (string.IsNullOrEmpty(chosenProgram))
                {
                    TempData["Error"] = "Please select a program.";
                    return RedirectToAction("TransfereeForm");
                }

                // ── 2. Validate subjects ──────────────────────────────────
                var selectedSubjects = form["SelectedSubjects"]
                    .ToList()
                    .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(int.Parse)
                    .Distinct()
                    .ToList();

                if (!selectedSubjects.Any())
                {
                    TempData["Error"] = "Please select at least one subject.";
                    return RedirectToAction("TransfereeForm");
                }

                var validSubjectIds = await _context.Subjects
                    .Where(s => selectedSubjects.Contains(s.SubjectId))
                    .Select(s => s.SubjectId)
                    .ToListAsync();

                if (validSubjectIds.Count != selectedSubjects.Count)
                {
                    TempData["Error"] = "One or more selected subjects are invalid.";
                    return RedirectToAction("TransfereeForm");
                }

                // ── 3. Load active enrollment fields ──────────────────────
                var enrollmentFields = await _context.EnrollmentFields
                    .Where(f => f.IsActive)
                    .OrderBy(f => f.DisplayOrder)
                    .ToListAsync();

                // ── 4. Resolve student email ───────────────────────────────
                string studentEmail = string.Empty;
                foreach (var field in enrollmentFields)
                {
                    bool isEmailField =
                        string.Equals(field.FieldType, "email", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(field.TemplateKey, "email_address", StringComparison.OrdinalIgnoreCase) ||
                        field.FieldName.Contains("email", StringComparison.OrdinalIgnoreCase);

                    if (isEmailField)
                    {
                        var val = form[$"Field_{field.Id}"].ToString();
                        if (!string.IsNullOrWhiteSpace(val)) { studentEmail = val; break; }
                    }
                }

                // ── 5. Determine year level ───────────────────────────────
                int yearLevel = 1;
                var yearField = enrollmentFields.FirstOrDefault(f =>
                    f.FieldName.Contains("year level", StringComparison.OrdinalIgnoreCase) ||
                    f.FieldName.Contains("yearlevel", StringComparison.OrdinalIgnoreCase));

                if (yearField != null)
                {
                    var raw = form[$"Field_{yearField.Id}"].ToString();
                    if (raw.StartsWith("2") || raw.Contains("second", StringComparison.OrdinalIgnoreCase)) yearLevel = 2;
                    else if (raw.StartsWith("3") || raw.Contains("third", StringComparison.OrdinalIgnoreCase)) yearLevel = 3;
                    else yearLevel = 1;
                }

                // ── 6. Create and save Student ────────────────────────────
                var student = new Student
                {
                    StudentNumber = await GenerateTempStudentNumberAsync(),
                    StudentType = "Transferee",
                    Program = chosenProgram,
                    CurrentYearLevel = yearLevel,
                    CurrentSemester = 1,
                    EnrollmentStatus = "Pending",
                    PaymentStatus = "Unpaid",
                    Email = studentEmail,
                    IsActive = true,
                    HasAccount = false,
                    PasswordHash = string.Empty,
                    EnrollmentDate = DateTime.Now,
                    CreatedDate = DateTime.Now,
                };

                _context.Students.Add(student);
                await _context.SaveChangesAsync();

                // ── 7. Handle file uploads ────────────────────────────────
                var uploadPath = Path.Combine(
                    _environment.WebRootPath, "uploads", "enrollments");
                Directory.CreateDirectory(uploadPath);

                var uploadedFiles = new Dictionary<int, string>();
                foreach (var file in Request.Form.Files)
                {
                    if (file.Length > 0)
                    {
                        var fieldIdStr = file.Name.Replace("File_", "");
                        if (int.TryParse(fieldIdStr, out int fieldId))
                        {
                            var fileName = $"student_{student.StudentId}_{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                            var filePath = Path.Combine(uploadPath, fileName);
                            using var stream = new FileStream(filePath, FileMode.Create);
                            await file.CopyToAsync(stream);
                            uploadedFiles[fieldId] = $"/uploads/enrollments/{fileName}";
                        }
                    }
                }

                // ── 8. Save enrollment field data ─────────────────────────
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

                    if (!string.IsNullOrEmpty(fieldData.FieldValue) ||
                        !string.IsNullOrEmpty(fieldData.FilePath))
                        _context.EnrollmentFieldData.Add(fieldData);
                }

                // ── 9. Save subject accreditation data ────────────────────
                var accredCodes = form["AccredCode[]"].ToList();
                var accredTitles = form["AccredTitle[]"].ToList();
                var accredUnits = form["AccredUnits[]"].ToList();
                var accredGrades = form["AccredGrade[]"].ToList();

                var accredEntries = new List<object>();
                for (int i = 0; i < accredCodes.Count; i++)
                {
                    var code = (accredCodes.ElementAtOrDefault(i) ?? "").Trim();
                    var titl = (accredTitles.ElementAtOrDefault(i) ?? "").Trim();
                    if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(titl)) continue;

                    accredEntries.Add(new
                    {
                        Code = code,
                        Title = titl,
                        Units = (accredUnits.ElementAtOrDefault(i) ?? "").Trim(),
                        Grade = (accredGrades.ElementAtOrDefault(i) ?? "").Trim(),
                    });
                }

                if (accredEntries.Any())
                {
                    var accredField = enrollmentFields.FirstOrDefault(f =>
                        string.Equals(f.TemplateKey, "accreditation_data",
                            StringComparison.OrdinalIgnoreCase));

                    if (accredField != null)
                    {
                        _context.EnrollmentFieldData.Add(new EnrollmentFieldData
                        {
                            StudentId = student.StudentId,
                            EnrollmentFieldId = accredField.Id,
                            SubmittedDate = DateTime.Now,
                            FieldValue = Newtonsoft.Json.JsonConvert.SerializeObject(accredEntries),
                            FilePath = string.Empty,
                        });
                    }
                }

                // ── 10. Save subject enrollments ──────────────────────────
                int academicYear = GetCurrentAcademicYearInt();

                var subjectLookup = await _context.Subjects
                    .Where(s => validSubjectIds.Contains(s.SubjectId))
                    .ToDictionaryAsync(s => s.SubjectId);

                foreach (var subjectId in validSubjectIds)
                {
                    subjectLookup.TryGetValue(subjectId, out var subj);

                    _context.SubjectEnrollments.Add(new SubjectEnrollment
                    {
                        StudentId = student.StudentId,
                        SubjectId = subjectId,
                        YearLevel = subj?.YearLevel ?? yearLevel,
                        Semester = subj?.Semester ?? 1,
                        AcademicYear = academicYear,
                        Status = "Pending",
                        EnrolledDate = DateTime.Now,
                        FinalGrade = string.Empty,
                    });
                }

                await _context.SaveChangesAsync();

                // ── 11. Send acknowledgment email ─────────────────────────
                try
                {
                    var emailResult = await _emailService
                        .SendAcknowledgementAsync(student.StudentId);

                    TempData["EmailSent"] = emailResult.Success ? "true" : "false";
                    TempData["EmailAddress"] = emailResult.RecipientEmail ?? "";
                    TempData["EmailError"] = emailResult.ErrorMessage ?? "";
                }
                catch (Exception emailEx)
                {
                    TempData["EmailSent"] = "false";
                    TempData["EmailError"] = emailEx.Message;
                }

                TempData["Success"] = "Transferee registration submitted successfully!";
                TempData["StudentNumber"] = student.StudentNumber;

                return RedirectToAction("RegistrationSuccess",
                    new { studentId = student.StudentId });
            }
            catch (Exception ex)
            {
                var msgs = new List<string>();
                var cur = (Exception?)ex;
                while (cur != null) { msgs.Add(cur.Message); cur = cur.InnerException; }
                TempData["Error"] = "Error: " + string.Join(" → ", msgs);
                return RedirectToAction("TransfereeForm");
            }
        }

        // ══════════════════════════════════════════
        // GET: Old Student Form
        // ══════════════════════════════════════════
        public IActionResult OldStudentForm() => View();

        // ══════════════════════════════════════════
        // POST: Verify Old Student Identity (AJAX)
        // ══════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOldStudent(
            [FromBody] VerifyStudentRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req?.StudentNumber))
                    return Json(new { success = false, message = "Please enter your Student ID." });

                if (!DateTime.TryParse(req.BirthDate, out DateTime birthDate))
                    return Json(new { success = false, message = "Invalid date of birth format." });

                var student = await _context.Students
                    .Include(s => s.FieldData)
                        .ThenInclude(fd => fd.EnrollmentField)
                    .FirstOrDefaultAsync(s =>
                        s.StudentNumber == req.StudentNumber.Trim() &&
                        s.IsActive);

                if (student == null)
                    return Json(new
                    {
                        success = false,
                        message = "Student ID not found. Please check and try again."
                    });

                bool birthMatch = false;
                if (student.FieldData != null)
                {
                    foreach (var fd in student.FieldData)
                    {
                        if (fd.EnrollmentField == null) continue;

                        bool isDateField =
                            string.Equals(fd.EnrollmentField.FieldType, "date",
                                StringComparison.OrdinalIgnoreCase) &&
                            (fd.EnrollmentField.FieldName.Contains("birth",
                                StringComparison.OrdinalIgnoreCase) ||
                             (fd.EnrollmentField.TemplateKey ?? "").Contains("birth",
                                StringComparison.OrdinalIgnoreCase));

                        if (!isDateField) continue;

                        if (DateTime.TryParse(fd.FieldValue, out DateTime storedDate) &&
                            storedDate.Date == birthDate.Date)
                        {
                            birthMatch = true;
                            break;
                        }
                    }
                }

                if (!birthMatch)
                    return Json(new
                    {
                        success = false,
                        message = "Date of birth does not match our records. Please try again."
                    });

                string fullName = ResolveFullName(student);

                return Json(new
                {
                    success = true,
                    student = new
                    {
                        studentId = student.StudentId,
                        studentNumber = student.StudentNumber,
                        fullName,
                        program = student.Program ?? "",
                        currentYearLevel = student.CurrentYearLevel,
                        currentSemester = student.CurrentSemester,
                        enrollmentStatus = student.EnrollmentStatus ?? "",
                        email = student.Email ?? "",
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "An error occurred: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // GET: Subjects for program / year level / semester (AJAX)
        // ══════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetSubjectsForTerm(
            string program, int yearLevel, int semester)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(program) || yearLevel <= 0 || semester <= 0)
                    return Json(new { subjects = new List<object>() });

                var subjects = await _context.Subjects
                    .Where(s => s.IsActive
                             && s.Program.Trim().ToUpper() == program.Trim().ToUpper()
                             && s.YearLevel == yearLevel
                             && s.Semester == semester)
                    .OrderBy(s => s.CourseCode)
                    .Select(s => new
                    {
                        subjectId = s.SubjectId,
                        courseCode = s.CourseCode,
                        descriptiveTitle = s.DescriptiveTitle,
                        lectureHours = s.LectureHours,
                        laboratoryHours = s.LaboratoryHours,
                        units = s.Units,
                        program = s.Program,
                        yearLevel = s.YearLevel,
                        semester = s.Semester,
                    })
                    .ToListAsync();

                return Json(new { subjects });
            }
            catch (Exception ex)
            {
                return Json(new { subjects = new List<object>(), error = ex.Message });
            }
        }

        // ══════════════════════════════════════════
        // POST: Submit Old Student Re-enrollment
        // ══════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitOldStudentEnrollment(IFormCollection form)
        {
            try
            {
                // ── 1. Resolve existing student ───────────────────────────
                if (!int.TryParse(form["StudentDbId"].ToString(), out int studentDbId) || studentDbId <= 0)
                {
                    TempData["Error"] = "Invalid student session. Please verify your identity again.";
                    return RedirectToAction("OldStudentForm");
                }

                var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.StudentId == studentDbId && s.IsActive);

                if (student == null)
                {
                    TempData["Error"] = "Student record not found.";
                    return RedirectToAction("OldStudentForm");
                }

                // ── 2. Validate year / semester ───────────────────────────
                if (!int.TryParse(form["SelectedYearLevel"].ToString(), out int yearLevel) || yearLevel <= 0)
                {
                    TempData["Error"] = "Invalid year level selected.";
                    return RedirectToAction("OldStudentForm");
                }
                if (!int.TryParse(form["SelectedSemester"].ToString(), out int semester) || semester <= 0)
                {
                    TempData["Error"] = "Invalid semester selected.";
                    return RedirectToAction("OldStudentForm");
                }

                // ── 3. Validate subjects ──────────────────────────────────
                var selectedSubjects = form["SelectedSubjects"]
                    .ToList()
                    .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(int.Parse)
                    .Distinct()
                    .ToList();

                if (!selectedSubjects.Any())
                {
                    TempData["Error"] = "Please select at least one subject.";
                    return RedirectToAction("OldStudentForm");
                }

                var validSubjectIds = await _context.Subjects
                    .Where(s => selectedSubjects.Contains(s.SubjectId))
                    .Select(s => s.SubjectId)
                    .ToListAsync();

                if (validSubjectIds.Count != selectedSubjects.Count)
                {
                    TempData["Error"] = "One or more selected subjects are invalid.";
                    return RedirectToAction("OldStudentForm");
                }

                // ── 4. Check for duplicate enrollment in same term ────────
                int academicYear = GetCurrentAcademicYearInt();
                bool alreadyEnrolled = await _context.SubjectEnrollments
                    .AnyAsync(se =>
                        se.StudentId == studentDbId &&
                        se.YearLevel == yearLevel &&
                        se.Semester == semester &&
                        se.AcademicYear == academicYear);

                if (alreadyEnrolled)
                {
                    TempData["Error"] =
                        $"You are already enrolled for {OrdinalLabel(yearLevel)} Year, " +
                        $"{OrdinalLabel(semester)} Semester of A.Y. {academicYear}-{academicYear + 1}.";
                    return RedirectToAction("OldStudentForm");
                }

                // ── 5. Update student record ──────────────────────────────
                // Old students already have a real StudentNumber — do NOT overwrite.
                student.StudentType = "Old Student";
                student.CurrentYearLevel = yearLevel;
                student.CurrentSemester = semester;
                student.EnrollmentStatus = "Pending";
                student.PaymentStatus = "Unpaid";
                student.EnrollmentDate = DateTime.Now;
                student.LastModifiedDate = DateTime.Now;

                // ── 6. Save new SubjectEnrollments ────────────────────────
                var subjectLookup = await _context.Subjects
                    .Where(s => validSubjectIds.Contains(s.SubjectId))
                    .ToDictionaryAsync(s => s.SubjectId);

                foreach (var subjectId in validSubjectIds)
                {
                    subjectLookup.TryGetValue(subjectId, out var subj);

                    _context.SubjectEnrollments.Add(new SubjectEnrollment
                    {
                        StudentId = studentDbId,
                        SubjectId = subjectId,
                        YearLevel = subj?.YearLevel ?? yearLevel,
                        Semester = subj?.Semester ?? semester,
                        AcademicYear = academicYear,
                        Status = "Pending",
                        EnrolledDate = DateTime.Now,
                        FinalGrade = string.Empty,
                    });
                }

                await _context.SaveChangesAsync();

                // ── 7. Send acknowledgment email ──────────────────────────
                try
                {
                    var emailResult = await _emailService
                        .SendAcknowledgementAsync(studentDbId);

                    TempData["EmailSent"] = emailResult.Success ? "true" : "false";
                    TempData["EmailAddress"] = emailResult.RecipientEmail ?? "";
                    TempData["EmailError"] = emailResult.ErrorMessage ?? "";
                }
                catch (Exception emailEx)
                {
                    TempData["EmailSent"] = "false";
                    TempData["EmailError"] = emailEx.Message;
                }

                TempData["Success"] = "Re-enrollment submitted successfully!";
                TempData["StudentNumber"] = student.StudentNumber;

                return RedirectToAction("RegistrationSuccess",
                    new { studentId = studentDbId });
            }
            catch (Exception ex)
            {
                var msgs = new List<string>();
                var cur = (Exception?)ex;
                while (cur != null) { msgs.Add(cur.Message); cur = cur.InnerException; }
                TempData["Error"] = "Error: " + string.Join(" → ", msgs);
                return RedirectToAction("OldStudentForm");
            }
        }

        // ══════════════════════════════════════════
        // GET: Registration Success
        // ══════════════════════════════════════════
        public async Task<IActionResult> RegistrationSuccess(int studentId)
        {
            var student = await _context.Students
                .Include(s => s.FieldData)
                    .ThenInclude(fd => fd.EnrollmentField)
                .FirstOrDefaultAsync(s => s.StudentId == studentId);

            if (student == null) return NotFound("Student not found");

            var subjectEnrollments = await _context.SubjectEnrollments
                .Include(se => se.Subject)
                .Where(se => se.StudentId == studentId)
                .ToListAsync();

            ViewBag.Subjects = subjectEnrollments
                .Where(se => se.Subject != null)
                .Select(se => new
                {
                    Code = se.Subject.CourseCode,
                    Name = se.Subject.DescriptiveTitle,
                    Lec = se.Subject.LectureHours,
                    Lab = se.Subject.LaboratoryHours,
                    Hours = se.Subject.LectureHours + se.Subject.LaboratoryHours,
                    Units = se.Subject.Units
                }).ToList();

            var dbProgram = (student.Program ?? "").Trim().ToUpper();
            var dbStudentType = (student.StudentType ?? "Freshmen").Trim();
            var dbYearLevel = student.CurrentYearLevel;
            var dbSemester = student.CurrentSemester;

            var tuitionFee = await _context.TuitionFees
                .FirstOrDefaultAsync(tf =>
                    tf.Program.Trim().ToUpper() == dbProgram &&
                    tf.StudentType.Trim() == dbStudentType &&
                    tf.YearLevel == dbYearLevel &&
                    tf.Semester == dbSemester &&
                    tf.IsActive);

            if (tuitionFee != null)
            {
                ViewBag.TuitionFee = tuitionFee.TuitionFees;
                ViewBag.MiscFee = tuitionFee.Miscellaneous;
                ViewBag.LabFee = tuitionFee.Laboratory;
                ViewBag.OtherFee = tuitionFee.OtherFees;
                ViewBag.TotalAssessment = tuitionFee.HasDiscount ? tuitionFee.FinalTotal : tuitionFee.Total;
                ViewBag.DownPayment = tuitionFee.DownPayment;
                ViewBag.PrelimPayment = tuitionFee.PrelimPayment;
                ViewBag.MidtermPayment = tuitionFee.MidtermPayment;
                ViewBag.SemiFinalPayment = tuitionFee.SemiFinalPayment;
                ViewBag.FinalPayment = tuitionFee.FinalPayment;
                ViewBag.HasDiscount = tuitionFee.HasDiscount;
                ViewBag.DiscountAmount = tuitionFee.DiscountAmount;
                ViewBag.DiscountPercent = tuitionFee.DiscountPercent;
                ViewBag.FeeFound = true;
            }
            else
            {
                ViewBag.TuitionFee = ViewBag.MiscFee = ViewBag.LabFee =
                ViewBag.OtherFee = ViewBag.TotalAssessment = ViewBag.DownPayment =
                ViewBag.PrelimPayment = ViewBag.MidtermPayment =
                ViewBag.SemiFinalPayment = ViewBag.FinalPayment =
                ViewBag.DiscountAmount = ViewBag.DiscountPercent = 0m;
                ViewBag.HasDiscount = false;
                ViewBag.FeeFound = false;
                ViewBag.DebugProgram = dbProgram;
                ViewBag.DebugStudentType = dbStudentType;
                ViewBag.DebugYearLevel = dbYearLevel;
                ViewBag.DebugSemester = dbSemester;
            }

            // Show the TEMP number as-is so the student can see it;
            // the "Pending" message only shows once the admin replaces it.
            ViewBag.StudentNumber =
                string.IsNullOrWhiteSpace(student.StudentNumber)
                    ? "Pending — Admin will assign your Student Number"
                    : student.StudentNumber;   // e.g. "TEMP-0001" until admin replaces

            ViewBag.Student = student;

            return View();
        }

        // ══════════════════════════════════════════════════════════════════
        // POST: Generate Admission Form (.docx)
        // ══════════════════════════════════════════════════════════════════
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> GenerateAdmissionForm(
            [FromBody] GenerateFormRequest req)
        {
            try
            {
                if (req?.StudentId <= 0)
                    return Json(new { success = false, error = "Invalid student ID." });

                var filePath = await _admissionFormService
                                   .GenerateAdmissionFormAsync(req.StudentId);

                HttpContext.Session.SetString(
                    $"AdmissionForm_{req.StudentId}", filePath);

                return Json(new { success = true, filePath });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // POST: Generate Admission Form PDF
        // ══════════════════════════════════════════════════════════════════
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> GenerateAdmissionFormPdf(
            [FromBody] GenerateFormRequest req)
        {
            try
            {
                if (req?.StudentId <= 0)
                    return Json(new { success = false, error = "Invalid student ID." });

                var pdfPath = await _admissionFormService
                                  .GenerateAdmissionFormPdfAsync(req.StudentId);

                HttpContext.Session.SetString(
                    $"AdmissionFormPdf_{req.StudentId}", pdfPath);

                return Json(new { success = true, url = pdfPath });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // POST: Record student's on-site payment intent
        //       Creates a Payment row; updates Student.PaymentStatus
        // ══════════════════════════════════════════════════════════════════
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SelectOnsitePayment(
            [FromBody] GenerateFormRequest req)
        {
            try
            {
                if (req?.StudentId <= 0)
                    return Json(new { success = false, error = "Invalid student ID." });

                var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.StudentId == req.StudentId && s.IsActive);

                if (student == null)
                    return Json(new { success = false, error = "Student record not found." });

                // ── Prevent duplicate pending on-site records ─────────────
                bool alreadyExists = await _context.Payments.AnyAsync(p =>
                    p.StudentId == req.StudentId &&
                    p.PaymentMethod == "On-Site" &&
                    p.Status == "Pending" &&
                    p.IsActive);

                if (alreadyExists)
                {
                    var existing = await _context.Payments
                        .FirstAsync(p =>
                            p.StudentId == req.StudentId &&
                            p.PaymentMethod == "On-Site" &&
                            p.Status == "Pending" &&
                            p.IsActive);

                    return Json(new
                    {
                        success = true,
                        alreadyExists = true,
                        referenceNumber = existing.ReferenceNumber,
                        amount = existing.Amount,
                        paymentId = existing.PaymentId,
                    });
                }

                // ── Resolve down payment amount from TuitionFees ──────────
                var tuitionFee = await _context.TuitionFees
                    .FirstOrDefaultAsync(tf =>
                        tf.Program.Trim().ToUpper() == student.Program.Trim().ToUpper() &&
                        tf.StudentType.Trim() == student.StudentType.Trim() &&
                        tf.YearLevel == student.CurrentYearLevel &&
                        tf.Semester == student.CurrentSemester &&
                        tf.IsActive);

                decimal downPayment = tuitionFee?.DownPayment ?? 0m;

                // Reference uses StudentId (not StudentNumber) so TEMP- numbers are safe
                string referenceNumber = $"REG-{req.StudentId:D6}-{DateTime.Now:yyyyMMddHHmm}";

                var payment = new Payment
                {
                    StudentId = req.StudentId,
                    PaymentType = "Down Payment",
                    PaymentMethod = "On-Site",
                    Amount = downPayment,
                    ReferenceNumber = referenceNumber,
                    PaymentLocation = "Main Building, Second Floor — Cashier's Office",
                    Status = "Pending",
                    PaymentDate = DateTime.Now,
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    Remarks = "Student selected on-site payment at registration.",
                };

                _context.Payments.Add(payment);

                student.PaymentStatus = "Pending On-Site";
                student.LastModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    alreadyExists = false,
                    referenceNumber,
                    amount = downPayment,
                    paymentId = payment.PaymentId,
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // GET: Download Admission Form (.docx)
        // ══════════════════════════════════════════════════════════════════
        public IActionResult ViewAdmissionForm(int studentId)
        {
            var relativePath = HttpContext.Session
                                   .GetString($"AdmissionForm_{studentId}");

            if (string.IsNullOrEmpty(relativePath))
                return NotFound("Admission form not generated yet.");

            var physicalPath = Path.Combine(
                _environment.WebRootPath,
                relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (!System.IO.File.Exists(physicalPath))
                return NotFound("Admission form file not found on disk.");

            return PhysicalFile(
                physicalPath,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                Path.GetFileName(physicalPath));
        }

        // ══════════════════════════════════════════════════════════════════
        // GET: Download Admission Form (.pdf)
        // ══════════════════════════════════════════════════════════════════
        public IActionResult ViewAdmissionFormPdf(int studentId)
        {
            var relativePath = HttpContext.Session
                                   .GetString($"AdmissionFormPdf_{studentId}");

            if (string.IsNullOrEmpty(relativePath))
                return NotFound("PDF has not been generated yet. Please try again.");

            var physicalPath = Path.Combine(
                _environment.WebRootPath,
                relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (!System.IO.File.Exists(physicalPath))
                return NotFound("PDF file not found on disk.");

            return PhysicalFile(
                physicalPath,
                "application/pdf",
                Path.GetFileName(physicalPath));
        }

        // ══════════════════════════════════════════
        // GET: Payment
        // ══════════════════════════════════════════
        public async Task<IActionResult> Payment(int studentId)
        {
            var student = await _context.Students
                .Include(s => s.FieldData)
                    .ThenInclude(fd => fd.EnrollmentField)
                .FirstOrDefaultAsync(s => s.StudentId == studentId);

            if (student == null) return NotFound("Student not found");

            var studentData = student.FieldData?.Any() == true
                ? student.FieldData
                    .Where(fd => fd.EnrollmentField != null)
                    .ToDictionary(
                        fd => fd.EnrollmentField!.FieldName,
                        fd => fd.FieldValue ?? "")
                : new Dictionary<string, string>();

            var dbProgram = (student.Program ?? "").Trim().ToUpper();
            var dbStudentType = (student.StudentType ?? "Freshmen").Trim();

            var tuitionFee = await _context.TuitionFees
                .FirstOrDefaultAsync(tf =>
                    tf.Program.Trim().ToUpper() == dbProgram &&
                    tf.StudentType.Trim() == dbStudentType &&
                    tf.YearLevel == student.CurrentYearLevel &&
                    tf.Semester == student.CurrentSemester &&
                    tf.IsActive);

            ViewBag.Student = student;
            ViewBag.StudentData = studentData;
            ViewBag.TuitionFee = tuitionFee;

            return View();
        }

        // ══════════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates the next sequential TEMP number in the format TEMP-0001,
        /// TEMP-0002, … TEMP-9999, TEMP-10000 (expands beyond 4 digits if needed).
        /// Scans all existing TEMP-nnnn StudentNumbers and increments the max.
        /// Thread-safe enough for normal enrollment loads; for high concurrency
        /// the unique index on StudentNumber will catch any rare duplicate.
        /// </summary>
        private async Task<string> GenerateTempStudentNumberAsync()
        {
            // Pull all current TEMP numbers from the DB
            var existingTemps = await _context.Students
                .Where(s => s.StudentNumber != null &&
                            s.StudentNumber.StartsWith("TEMP-"))
                .Select(s => s.StudentNumber)
                .ToListAsync();

            int maxSeq = 0;
            foreach (var num in existingTemps)
            {
                // Parse the digits after "TEMP-"
                var suffix = num.Substring(5); // everything after "TEMP-"
                if (int.TryParse(suffix, out int seq) && seq > maxSeq)
                    maxSeq = seq;
            }

            int next = maxSeq + 1;

            // Format: at least 4 digits, e.g. TEMP-0001, TEMP-0099, TEMP-1000
            return $"TEMP-{next:D4}";
        }

        private string ResolveFullName(Student student)
        {
            if (student.FieldData == null || !student.FieldData.Any())
                return student.StudentNumber;

            var byKey = student.FieldData.FirstOrDefault(fd =>
                fd.EnrollmentField != null &&
                string.Equals(fd.EnrollmentField.TemplateKey, "full_name",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(fd.FieldValue));
            if (byKey != null) return byKey.FieldValue!;

            var nameParts = student.FieldData
                .Where(fd =>
                    fd.EnrollmentField != null &&
                    new[] { "first name", "firstname", "middle name",
                            "last name",  "lastname",  "surname" }
                        .Any(p => fd.EnrollmentField.FieldName
                            .Contains(p, StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(fd.FieldValue))
                .OrderBy(fd => fd.EnrollmentField!.DisplayOrder)
                .Select(fd => fd.FieldValue!)
                .ToList();

            if (nameParts.Any()) return string.Join(" ", nameParts);

            var anyText = student.FieldData.FirstOrDefault(fd =>
                fd.EnrollmentField != null &&
                string.Equals(fd.EnrollmentField.FieldType, "text",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(fd.FieldValue));

            return anyText?.FieldValue ?? student.StudentNumber;
        }

        private int GetCurrentAcademicYearInt()
        {
            var now = DateTime.Now;
            return now.Month >= 6 ? now.Year : now.Year - 1;
        }

        private static string OrdinalLabel(int n) => n switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{n}th"
        };
    }

    // ── Request DTOs ─────────────────────────────────────────────────────
    public class GenerateFormRequest
    {
        public int StudentId { get; set; }
    }

    public class VerifyStudentRequest
    {
        public string StudentNumber { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;
    }
}