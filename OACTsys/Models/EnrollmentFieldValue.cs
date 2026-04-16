using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class EnrollmentFieldValue
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EnrollmentFieldId { get; set; }

        [Required]
        public int StudentId { get; set; }

        [StringLength(2000)]
        public string Value { get; set; } // text / selected option / file path

        public DateTime SubmittedDate { get; set; }

        [ForeignKey("EnrollmentFieldId")]
        public EnrollmentField EnrollmentField { get; set; }

        [ForeignKey("StudentId")]
        public Student Student { get; set; }
    }
}