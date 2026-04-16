using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class Student
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int StudentId { get; set; }

        [Required, StringLength(50)]
        public string StudentNumber { get; set; }

        [Required, StringLength(20)]
        public string StudentType { get; set; } // Freshmen, Transferee, Old Student

        [Required, StringLength(20)]
        public string Program { get; set; } // AMT, AVT

        public int CurrentYearLevel { get; set; }
        public int CurrentSemester { get; set; }

        [StringLength(50)]
        public string EnrollmentStatus { get; set; } // Pending, Approved, Enrolled, etc.

        [StringLength(50)]
        public string PaymentStatus { get; set; } // Pending, Verified, etc.

        public DateTime? EnrollmentDate { get; set; }

        // Email for login/contact (extracted from EnrollmentFieldData)
        [StringLength(100)]
        public string Email { get; set; }

        public string? Username { get; set; }

        // Token for account activation
        [StringLength(100)]
        public string TokenUsed { get; set; }


        public bool IsActive { get; set; }
        public bool HasAccount { get; set; }

        [StringLength(100)]
        public string PasswordHash { get; set; }

        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }

        // Navigation properties
        public virtual ICollection<EnrollmentFieldData> FieldData { get; set; }
        public virtual ICollection<Enrollment> Enrollments { get; set; }  // FIXED: Changed from EnrollmentField to Enrollment
        public virtual ICollection<Payment> Payments { get; set; }
        public virtual ICollection<SubjectEnrollment> SubjectEnrollments { get; set; }
        public virtual EnrollmentForm EnrollmentForm { get; set; }
    }
}