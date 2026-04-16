// ============================================================
// FILE: Models/ViewModels/StudentGradesViewModel.cs
// Place in: OACTsys/Models/ViewModels/StudentGradesViewModel.cs
// ============================================================

using OACTsys.Models;
using System.Collections.Generic;

namespace OACTsys.Models.ViewModels
{
    public class StudentGradesViewModel
    {
        public int    StudentId     { get; set; }
        public string StudentNumber { get; set; } = "";
        public string FullName      { get; set; } = "";
        public string Course        { get; set; } = "";
        public string YearLevel     { get; set; } = "";
        public string Semester      { get; set; } = "";
        public string AcademicYear  { get; set; } = "";

        // All SubjectEnrollments with Subject included
        public List<SubjectEnrollment> Enrollments { get; set; } = new();
    }
}
