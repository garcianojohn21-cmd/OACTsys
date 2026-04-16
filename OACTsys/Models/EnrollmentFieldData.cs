using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class EnrollmentFieldData
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int StudentId { get; set; }
        [ForeignKey("StudentId")]
        public virtual Student Student { get; set; }

        public int EnrollmentFieldId { get; set; }
        [ForeignKey("EnrollmentFieldId")]
        public virtual EnrollmentField EnrollmentField { get; set; }

        // ✅ Fixed: PostgreSQL uses "text" instead of "nvarchar(max)"
        [Column(TypeName = "text")]
        public string FieldValue { get; set; }

        [StringLength(500)]
        public string FilePath { get; set; }

        public DateTime SubmittedDate { get; set; }
    }
}