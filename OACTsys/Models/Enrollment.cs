using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class Enrollment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int EnrollmentId { get; set; }

        public int StudentId { get; set; }
        [ForeignKey("StudentId")]
        public virtual Student Student { get; set; }

        public int SubjectId { get; set; }
        [ForeignKey("SubjectId")]
        public virtual Subject Subject { get; set; }

        public int YearLevel { get; set; }
        public int Semester { get; set; }

        [Required, StringLength(20)]
        public string AcademicYear { get; set; } // e.g., "2024-2025"

        [StringLength(50)]
        public string Status { get; set; } // Pending, Enrolled, Dropped, Completed

        public DateTime EnrolledDate { get; set; }

        // Grades (optional - for future use)
        public decimal? PrelimGrade { get; set; }
        public decimal? MidtermGrade { get; set; }
        public decimal? SemiFinalGrade { get; set; }
        public decimal? FinalGrade { get; set; }
        public decimal? FinalRating { get; set; }

        [StringLength(20)]
        public string Remarks { get; set; } // Passed, Failed, Incomplete, etc.

        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
}