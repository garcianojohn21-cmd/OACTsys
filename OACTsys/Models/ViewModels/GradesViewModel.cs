// ============================================================
// FILE: Models/ViewModels/GradesViewModel.cs
// ============================================================

using System.Collections.Generic;

namespace OACTsys.Models.ViewModels
{
    // ── Top-level view model ─────────────────────────────────────────
    public class GradesViewModel
    {
        public List<GradeStudentRow> Students { get; set; } = new();
    }

    // ── One row in the student list table ────────────────────────────
    public class GradeStudentRow
    {
        public int StudentId { get; set; }
        public string StudentNumber { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Gender { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Course { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public string Semester { get; set; } = "";
        public string PhotoUrl { get; set; } = "";

        public List<GradeSubjectRow> Subjects { get; set; } = new();
    }

    // ── One subject row inside the Upload Grades modal ───────────────
    public class GradeSubjectRow
    {
        public int SubjectEnrollmentId { get; set; }
        public int SubjectId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public decimal Units { get; set; }
        public string Semester { get; set; } = "";

        // Grading columns — empty string = not yet graded
        public string PrelimGrade { get; set; } = "";
        public string MidtermGrade { get; set; } = "";
        public string FinalGrade { get; set; } = "";

        public string Status { get; set; } = "";
    }

    // ── POST body for SaveGrades ─────────────────────────────────────
    public class SaveGradesRequest
    {
        public int StudentId { get; set; }
        public List<GradeEntryDto> Grades { get; set; } = new();
    }

    // ── One grade entry in the SaveGrades payload ────────────────────
    public class GradeEntryDto
    {
        public int SubjectEnrollmentId { get; set; }
        public string PrelimGrade { get; set; } = "";
        public string MidtermGrade { get; set; } = "";
        public string FinalGrade { get; set; } = "";
    }

    // ── Bulk upload DTO (Excel rows) ─────────────────────────────────
    public class BulkGradeEntryDto
    {
        public string StudentNumber { get; set; }
        public string SubjectCode { get; set; }
        public decimal? PrelimGrade { get; set; }
        public decimal? MidtermGrade { get; set; }
        public decimal? FinalGrade { get; set; }
    }
}