using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class EnrollmentField
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string FieldName { get; set; }

        [Required, StringLength(50)]
        public string FieldType { get; set; }

        public bool IsRequired { get; set; }

        // ✅ Fixed: PostgreSQL uses "text" instead of "nvarchar(max)"
        [Column(TypeName = "text")]
        public string Options { get; set; }

        [StringLength(200)]
        public string AcceptedFileTypes { get; set; }

        public int MaxFileSize { get; set; }

        [StringLength(500)]
        public string HelperText { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedDate { get; set; }

        [StringLength(100)]
        public string Category { get; set; }

        [StringLength(100)]
        public string TemplateKey { get; set; }

        public int? MinLimit { get; set; }
        public int? MaxLimit { get; set; }
    }
}