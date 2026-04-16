// ============================================================
// FILE: Models/SubjectEnrollment.cs
// ============================================================

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class SubjectEnrollment
    {
        [Key]
        public int SubjectEnrollmentId { get; set; }

        [Required]
        public int StudentId { get; set; }

        [Required]
        public int SubjectId { get; set; }

        public int AcademicYear { get; set; }
        public int Semester { get; set; }
        public int YearLevel { get; set; }

        public DateTime EnrolledDate { get; set; } = DateTime.Now;

        [StringLength(20)]
        public string Status { get; set; }

        // ── Grade columns ──────────────────────────────────────────
        [StringLength(5)]
        public string PrelimGrade { get; set; }   // e.g. "1.50"

        [StringLength(5)]
        public string MidtermGrade { get; set; }   // e.g. "1.75"

        [StringLength(5)]
        public string FinalGrade { get; set; }   // e.g. "2.00"
        // ──────────────────────────────────────────────────────────

        [ForeignKey(nameof(StudentId))]
        public Student Student { get; set; }

        [ForeignKey(nameof(SubjectId))]
        public Subject Subject { get; set; }
    }
}