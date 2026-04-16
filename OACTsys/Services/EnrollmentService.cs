using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Models;

namespace OACTsys.Services
{
    public class EnrollmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;

        public EnrollmentService(ApplicationDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        /// <summary>
        /// Generate unique student number: YEAR-0001
        /// </summary>
        public async Task<string> GenerateStudentNumber()
        {
            int currentYear = DateTime.Now.Year;
            string yearPrefix = currentYear.ToString();

            var lastStudent = await _context.Students
                .Where(s => s.StudentNumber.StartsWith(yearPrefix))
                .OrderByDescending(s => s.StudentNumber)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (lastStudent != null)
            {
                string numberPart = lastStudent.StudentNumber.Substring(5); // After "YEAR-"
                if (int.TryParse(numberPart, out int lastNumber))
                {
                    nextNumber = lastNumber + 1;
                }
            }

            return $"{yearPrefix}-{nextNumber:D4}"; // e.g., 2026-0001
        }

        /// <summary>
        /// Generate a 6-character alphanumeric token for account creation
        /// </summary>
        public string GenerateAccountToken()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        /// <summary>
        /// Check if student can enroll (Old Student logic)
        /// </summary>
        public async Task<(bool CanEnroll, string Message)> CanOldStudentEnroll(string email)
        {
            var student = await _context.Students
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.Email == email && s.HasAccount);

            if (student == null)
                return (false, "No account found. Please complete initial enrollment first.");

            // Check if they have outstanding balance from previous semester
            var unpaidPayments = student.Payments
                .Where(p => p.Status != "Verified" && p.PaymentType == "Tuition")
                .Any();

            if (unpaidPayments)
                return (false, "You have outstanding balance. Please settle your payments before enrolling.");

            // Check enrollment status
            if (student.EnrollmentStatus == "Pending")
                return (false, "Your previous enrollment is still pending approval.");

            return (true, "You can proceed with enrollment.");
        }

        /// <summary>
        /// Submit enrollment (Freshmen/Transferee) - UPDATED FOR DYNAMIC FIELDS
        /// </summary>
        public async Task<(bool Success, string Message, Student Student)> SubmitNewEnrollment(Student student)
        {
            try
            {
                // Generate student number
                student.StudentNumber = await GenerateStudentNumber();
                student.EnrollmentStatus = "Pending";
                student.PaymentStatus = "Unpaid";
                student.HasAccount = false;
                student.EnrollmentDate = DateTime.Now;
                student.IsActive = true;
                student.CreatedDate = DateTime.Now;

                _context.Students.Add(student);
                await _context.SaveChangesAsync();

                // Note: EnrollmentFieldData is saved separately in the controller
                // This service only handles the base Student record

                // Send confirmation email (extract email from FieldData if needed)
                if (!string.IsNullOrEmpty(student.Email))
                {
                    // Get student name from field data
                    string fullName = await GetStudentFullName(student.StudentId);

                    await _emailService.SendEnrollmentConfirmationEmail(
                        student.Email,
                        fullName,
                        student.StudentNumber
                    );
                }

                return (true, "Enrollment submitted successfully! Please proceed to payment.", student);
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Get student's full name from dynamic field data
        /// </summary>
        public async Task<string> GetStudentFullName(int studentId)
        {
            var nameField = await _context.EnrollmentFieldData
                .Include(fd => fd.EnrollmentField)
                .Where(fd => fd.StudentId == studentId &&
                       (fd.EnrollmentField.FieldName.ToLower().Contains("name") ||
                        fd.EnrollmentField.FieldName.ToLower().Contains("full name")))
                .FirstOrDefaultAsync();

            return nameField?.FieldValue ?? "Student";
        }

        /// <summary>
        /// Get specific field value for a student
        /// </summary>
        public async Task<string> GetStudentFieldValue(int studentId, string fieldName)
        {
            var fieldData = await _context.EnrollmentFieldData
                .Include(fd => fd.EnrollmentField)
                .Where(fd => fd.StudentId == studentId &&
                       fd.EnrollmentField.FieldName.ToLower() == fieldName.ToLower())
                .FirstOrDefaultAsync();

            return fieldData?.FieldValue;
        }

        /// <summary>
        /// Submit old student re-enrollment
        /// </summary>
        public async Task<(bool Success, string Message)> SubmitOldStudentEnrollment(
            int studentId,
            int yearLevel,
            int semester,
            int[] selectedSubjectIds)
        {
            try
            {
                var student = await _context.Students.FindAsync(studentId);
                if (student == null)
                    return (false, "Student not found.");

                // Update student's current level
                student.CurrentYearLevel = yearLevel;
                student.CurrentSemester = semester;
                student.EnrollmentStatus = "Pending";
                student.PaymentStatus = "Unpaid";
                student.LastModifiedDate = DateTime.Now;

                // Get current academic year as INT
                int academicYear = GetCurrentAcademicYear();

                // Enroll in subjects using SubjectEnrollment
                foreach (var subjectId in selectedSubjectIds)
                {
                    var enrollment = new SubjectEnrollment
                    {
                        StudentId = studentId,
                        SubjectId = subjectId,
                        YearLevel = yearLevel,
                        Semester = semester,
                        AcademicYear = academicYear, // Now using int
                        Status = "Pending",
                        EnrolledDate = DateTime.Now
                    };
                    _context.SubjectEnrollments.Add(enrollment);
                }

                await _context.SaveChangesAsync();

                return (true, "Re-enrollment submitted successfully! Please proceed to payment.");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get current academic year as INT (e.g., 2025 for "2025-2026")
        /// </summary>
        private int GetCurrentAcademicYear()
        {
            var now = DateTime.Now;
            var currentYear = now.Year;

            // If current month is June or later, it's the start of new academic year
            if (now.Month >= 6)
                return currentYear;
            else
                return currentYear - 1;
        }

        /// <summary>
        /// Get academic year string for display (e.g., "2025-2026")
        /// </summary>
        public string GetAcademicYearString(int academicYear)
        {
            return $"{academicYear}-{academicYear + 1}";
        }

        /// <summary>
        /// Verify payment and generate account token
        /// </summary>
        public async Task<(bool Success, string Message)> VerifyPaymentAndGenerateToken(int paymentId, string verifiedBy)
        {
            try
            {
                var payment = await _context.Payments
                    .Include(p => p.Student)
                        .ThenInclude(s => s.FieldData)
                            .ThenInclude(fd => fd.EnrollmentField)
                    .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

                if (payment == null)
                    return (false, "Payment not found.");

                // Update payment status
                payment.Status = "Verified";
                payment.VerifiedDate = DateTime.Now;
                payment.VerifiedBy = verifiedBy;

                // Update student payment status
                var student = payment.Student;
                student.PaymentStatus = "Paid";

                // If this is the first enrollment payment and student doesn't have account
                if (!student.HasAccount && payment.PaymentType == "Enrollment")
                {
                    // Generate account token
                    string accountToken = GenerateAccountToken();
                    student.TokenUsed = accountToken; // Store token temporarily (will be cleared after use)

                    // Get student's full name from field data
                    string fullName = await GetStudentFullName(student.StudentId);

                    // Send email with token
                    if (!string.IsNullOrEmpty(student.Email))
                    {
                        await _emailService.SendAccountCreationTokenEmail(
                            student.Email,
                            fullName,
                            accountToken,
                            student.StudentNumber
                        );
                    }
                }

                // Update enrollment status
                student.EnrollmentStatus = "Approved";
                student.LastModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                return (true, "Payment verified successfully!");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate account creation token
        /// </summary>
        public async Task<(bool Valid, Student Student)> ValidateAccountToken(string token, string email)
        {
            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Email == email &&
                                        s.TokenUsed == token &&
                                        !s.HasAccount);

            if (student == null)
                return (false, null);

            // Token is valid for 30 days from enrollment date
            if (student.EnrollmentDate.HasValue)
            {
                var daysSinceEnrollment = (DateTime.Now - student.EnrollmentDate.Value).TotalDays;
                if (daysSinceEnrollment > 30)
                    return (false, null);
            }

            return (true, student);
        }

        /// <summary>
        /// Mark token as used after account creation
        /// </summary>
        public async Task MarkTokenAsUsed(int studentId, string hashedPassword)
        {
            var student = await _context.Students.FindAsync(studentId);
            if (student != null)
            {
                student.HasAccount = true;
                student.PasswordHash = hashedPassword;
                student.TokenUsed = null; // Clear the token
                student.LastModifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Get all enrollment field data for a student (for display purposes)
        /// </summary>
        public async Task<System.Collections.Generic.List<EnrollmentFieldData>> GetStudentEnrollmentData(int studentId)
        {
            return await _context.EnrollmentFieldData
                .Include(fd => fd.EnrollmentField)
                .Where(fd => fd.StudentId == studentId)
                .OrderBy(fd => fd.EnrollmentField.DisplayOrder)
                .ToListAsync();
        }

        /// <summary>
        /// Get student's enrolled subjects for current semester
        /// </summary>
        public async Task<System.Collections.Generic.List<SubjectEnrollment>> GetStudentEnrolledSubjects(
            int studentId,
            int yearLevel,
            int semester)
        {
            int academicYear = GetCurrentAcademicYear(); // Now returns int

            return await _context.SubjectEnrollments
                .Include(se => se.Subject)
                .Where(se => se.StudentId == studentId &&
                           se.YearLevel == yearLevel &&
                           se.Semester == semester &&
                           se.AcademicYear == academicYear)
                .ToListAsync();
        }

        /// <summary>
        /// Update student email (extracted from field data)
        /// </summary>
        public async Task UpdateStudentEmail(int studentId)
        {
            var student = await _context.Students.FindAsync(studentId);
            if (student != null)
            {
                var emailField = await _context.EnrollmentFieldData
                    .Include(fd => fd.EnrollmentField)
                    .Where(fd => fd.StudentId == studentId &&
                           fd.EnrollmentField.FieldName.ToLower().Contains("email"))
                    .FirstOrDefaultAsync();

                if (emailField != null && !string.IsNullOrEmpty(emailField.FieldValue))
                {
                    student.Email = emailField.FieldValue;
                    await _context.SaveChangesAsync();
                }
            }
        }

        /// <summary>
        /// Approve student enrollment (Admin action)
        /// </summary>
        public async Task<(bool Success, string Message)> ApproveEnrollment(int studentId, string approvedBy)
        {
            try
            {
                var student = await _context.Students.FindAsync(studentId);
                if (student == null)
                    return (false, "Student not found.");

                student.EnrollmentStatus = "Approved";
                student.LastModifiedDate = DateTime.Now;

                // Update all subject enrollments to "Enrolled"
                var enrollments = await _context.SubjectEnrollments
                    .Where(se => se.StudentId == studentId && se.Status == "Pending")
                    .ToListAsync();

                foreach (var enrollment in enrollments)
                {
                    enrollment.Status = "Enrolled";
                }

                await _context.SaveChangesAsync();

                return (true, "Enrollment approved successfully!");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Archive student enrollment
        /// </summary>
        public async Task<(bool Success, string Message)> ArchiveEnrollment(int studentId)
        {
            try
            {
                var student = await _context.Students.FindAsync(studentId);
                if (student == null)
                    return (false, "Student not found.");

                student.EnrollmentStatus = "Archived";
                student.IsActive = false;
                student.LastModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                return (true, "Enrollment archived successfully!");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }
    }
}