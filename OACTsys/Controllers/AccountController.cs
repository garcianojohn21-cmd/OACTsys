using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BC = BCrypt.Net.BCrypt;

namespace OACTsys.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ── Sign In page ──────────────────────────────────────────
        public IActionResult SignIn() => View();

        // ── Create Account page ───────────────────────────────────
        public IActionResult CreateAccount() => View();

        // ── Select Role page ──────────────────────────────────────
        public IActionResult SelectRole() => View();

        // ─────────────────────────────────────────────────────────
        // AJAX: Verify PIN
        // POST /Account/VerifyPin
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> VerifyPin([FromBody] VerifyPinDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Pin) || dto.Pin.Length != 6)
                return Json(new { success = false, message = "Please enter a valid 6-digit PIN." });

            try
            {
                var student = await _context.Students
                    .Include(s => s.FieldData)
                        .ThenInclude(fd => fd.EnrollmentField)
                    .FirstOrDefaultAsync(s =>
                        s.TokenUsed == dto.Pin.Trim() &&
                        s.PaymentStatus == "Verified" &&
                        s.IsActive &&
                        !s.HasAccount);

                if (student == null)
                    return Json(new
                    {
                        success = false,
                        message = "Invalid PIN or account already exists. Please check your email for the correct PIN."
                    });

                var personalFields = new List<object>();

                if (student.FieldData != null)
                {
                    var skipTypes = new[] { "file", "image", "signature" };

                    foreach (var fd in student.FieldData
                        .Where(fd =>
                            fd.EnrollmentField != null &&
                            !skipTypes.Contains(fd.EnrollmentField.FieldType?.ToLower() ?? "") &&
                            !string.IsNullOrWhiteSpace(fd.FieldValue))
                        .OrderBy(fd => fd.EnrollmentField?.DisplayOrder ?? 999))
                    {
                        personalFields.Add(new
                        {
                            label = fd.EnrollmentField!.FieldName,
                            value = fd.FieldValue,
                            key = fd.EnrollmentField.TemplateKey ?? "",
                            fieldType = fd.EnrollmentField.FieldType
                        });
                    }
                }

                string fullName = GetFieldValue(student, "full_name", "full name")
                    ?? $"{GetFieldValue(student, "last name", "lastname")} {GetFieldValue(student, "first name", "firstname")}".Trim()
                    ?? student.StudentNumber
                    ?? "Student";

                string email = ResolveEmail(student) ?? student.Email ?? "";

                return Json(new
                {
                    success = true,
                    studentId = student.StudentId,
                    studentNumber = student.StudentNumber,
                    fullName,
                    email,
                    program = student.Program,
                    yearLevel = student.CurrentYearLevel,
                    studentType = student.StudentType,
                    personalFields
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error verifying PIN: {ex.Message}" });
            }
        }

        // ─────────────────────────────────────────────────────────
        // POST: Register Student Account
        // POST /Account/RegisterUser
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> RegisterUser(RegisterUserDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Pin))
                {
                    TempData["Error"] = "PIN is required.";
                    return RedirectToAction("CreateAccount");
                }

                var student = await _context.Students
                    .Include(s => s.FieldData)
                        .ThenInclude(fd => fd.EnrollmentField)
                    .FirstOrDefaultAsync(s =>
                        s.TokenUsed == dto.Pin.Trim() &&
                        s.PaymentStatus == "Verified" &&
                        s.IsActive &&
                        !s.HasAccount);

                if (student == null)
                {
                    TempData["Error"] = "Invalid PIN or this account has already been created.";
                    return RedirectToAction("CreateAccount");
                }

                if (dto.Password != dto.ConfirmPassword)
                {
                    TempData["Error"] = "Passwords do not match.";
                    return RedirectToAction("CreateAccount");
                }

                if (dto.Password.Length < 8)
                {
                    TempData["Error"] = "Password must be at least 8 characters.";
                    return RedirectToAction("CreateAccount");
                }

                // ── Check username uniqueness ──────────────────────────
                bool usernameTaken = await _context.Students
                    .AnyAsync(s => s.Username == dto.Username.Trim());

                if (usernameTaken)
                {
                    TempData["Error"] = "That username is already taken. Please choose another.";
                    return RedirectToAction("CreateAccount");
                }

                // ── Hash password & activate account ──────────────────
                student.PasswordHash = BC.HashPassword(dto.Password);
                student.HasAccount = true;
                student.Username = dto.Username.Trim();
                student.LastModifiedDate = DateTime.Now;
                student.EnrollmentStatus = student.EnrollmentStatus ?? "Active";

                // Clear the PIN so it can't be reused
                // Only set to null if your DB column allows null — otherwise use empty string
                student.TokenUsed = string.Empty;

                await _context.SaveChangesAsync();

                // ── Set session ────────────────────────────────────────
                HttpContext.Session.SetInt32("StudentId", student.StudentId);
                HttpContext.Session.SetString("StudentNumber", student.StudentNumber ?? "");
                HttpContext.Session.SetString("UserRole", "Student");
                HttpContext.Session.SetString("UserEmail", student.Email ?? "");
                HttpContext.Session.SetString("Username", student.Username ?? "");

                return RedirectToAction("Index", "Student");
            }
            catch (DbUpdateException ex)
            {
                // Expose the innermost exception to reveal the real DB error
                var inner = ex.InnerException;
                while (inner?.InnerException != null) inner = inner.InnerException;
                TempData["Error"] = $"Registration failed: {inner?.Message ?? ex.Message}";
                return RedirectToAction("CreateAccount");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Registration failed: {ex.Message}";
                return RedirectToAction("CreateAccount");
            }
        }

        // ─────────────────────────────────────────────────────────
        // POST: Login  (accepts username OR email OR student number)
        // ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> LoginLogic(string identifier, string password)
        {
            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password))
            {
                TempData["LoginError"] = "Please enter your username/email and password.";
                return View("SignIn");
            }

            try
            {
                var id = identifier.Trim();

                // Match by Username, Email, or StudentNumber
                var student = await _context.Students
                    .FirstOrDefaultAsync(s =>
                        s.HasAccount &&
                        s.IsActive &&
                        (s.Username == id ||
                         s.Email == id ||
                         s.StudentNumber == id));

                if (student == null || string.IsNullOrEmpty(student.PasswordHash) ||
                    !BC.Verify(password, student.PasswordHash))
                {
                    TempData["LoginError"] = "Invalid username/email or password.";
                    return View("SignIn");
                }

                HttpContext.Session.SetInt32("StudentId", student.StudentId);
                HttpContext.Session.SetString("StudentNumber", student.StudentNumber ?? "");
                HttpContext.Session.SetString("UserRole", "Student");
                HttpContext.Session.SetString("UserEmail", student.Email ?? "");
                HttpContext.Session.SetString("Username", student.Username ?? "");

                return RedirectToAction("Index", "Student");
            }
            catch (Exception ex)
            {
                TempData["LoginError"] = $"Login error: {ex.Message}";
                return View("SignIn");
            }
        }

        // ─────────────────────────────────────────────────────────
        // Google OAuth
        // ─────────────────────────────────────────────────────────
        public IActionResult GoogleLogin() => RedirectToAction("SelectRole");

        [HttpPost]
        public IActionResult FinalizeRole(string selectedRole)
        {
            HttpContext.Session.SetString("UserEmail", "user@gmail.com");
            HttpContext.Session.SetString("UserRole", selectedRole);
            return selectedRole == "Student"
                ? RedirectToAction("Index", "Student")
                : RedirectToAction("Dashboard", "Licensure");
        }

        // ─────────────────────────────────────────────────────────
        // Sign Out
        // ─────────────────────────────────────────────────────────
        public IActionResult SignOut()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        // ══════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ══════════════════════════════════════════════════════════
        private static string? GetFieldValue(Student student, params string[] keys)
        {
            if (student.FieldData == null) return null;
            foreach (var key in keys)
            {
                var match = student.FieldData.FirstOrDefault(fd =>
                    fd.EnrollmentField != null && (
                        string.Equals(fd.EnrollmentField.TemplateKey, key, StringComparison.OrdinalIgnoreCase) ||
                        fd.EnrollmentField.FieldName.Contains(key, StringComparison.OrdinalIgnoreCase)
                    ) && !string.IsNullOrWhiteSpace(fd.FieldValue));
                if (match != null) return match.FieldValue;
            }
            return null;
        }

        private static string? ResolveEmail(Student student)
        {
            if (student.FieldData == null) return null;
            var byType = student.FieldData.FirstOrDefault(fd =>
                fd.EnrollmentField != null &&
                string.Equals(fd.EnrollmentField.FieldType, "email", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(fd.FieldValue));
            if (byType != null) return byType.FieldValue;

            var byKey = student.FieldData.FirstOrDefault(fd =>
                fd.EnrollmentField != null &&
                string.Equals(fd.EnrollmentField.TemplateKey, "email_address", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(fd.FieldValue));
            return byKey?.FieldValue;
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────
    public class VerifyPinDto
    {
        public string Pin { get; set; } = "";
    }

    public class RegisterUserDto
    {
        public string Pin { get; set; } = "";
        public string StudentId { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
        public bool AgreeToTerms { get; set; }
    }
}