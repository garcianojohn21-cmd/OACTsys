using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class Subject
    {
        [Key]
        public int SubjectId { get; set; }

        [Required, StringLength(50)]
        public string Program { get; set; }

        [Required, StringLength(20)]
        public string CourseCode { get; set; }

        [Required, StringLength(200)]
        public string DescriptiveTitle { get; set; }

        public int LectureHours { get; set; }

        public int LaboratoryHours { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal Units { get; set; }

        [Required]
        public int YearLevel { get; set; }

        [Required]
        public int Semester { get; set; }

        public int TotalHours { get; set; }

        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }

        public virtual ICollection<SubjectEnrollment> SubjectEnrollments { get; set; }
    }
}