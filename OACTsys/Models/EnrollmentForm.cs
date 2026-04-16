using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class EnrollmentForm
    {
        [Key]
        public int EnrollmentFormId { get; set; }

        [Required]
        public int StudentId { get; set; }

        // Educational Background (for Freshmen/Transferee)
        [StringLength(200)]
        public string GradeSchool { get; set; }
        
        [StringLength(200)]
        public string GradeSchoolAddress { get; set; }

        [StringLength(100)]
        public string JuniorHighSchool { get; set; }
        
        [StringLength(200)]
        public string JuniorHighSchoolAddress { get; set; }

        [StringLength(100)]
        public string SeniorHighSchool { get; set; }
        
        [StringLength(200)]
        public string SeniorHighSchoolAddress { get; set; }

        [StringLength(50)]
        public string SeniorHighDates { get; set; }

        // For Transferees Only
        [StringLength(200)]
        public string PreviousCollege { get; set; }
        
        [StringLength(100)]
        public string PreviousCourse { get; set; }
        
        [StringLength(50)]
        public string YearsAttended { get; set; }

        // Document Uploads (store file paths)
        [StringLength(500)]
        public string Form138Path { get; set; } // Report Card

        [StringLength(500)]
        public string GoodMoralPath { get; set; }

        [StringLength(500)]
        public string Form137Path { get; set; } // Permanent Record

        [StringLength(500)]
        public string PSABirthCertPath { get; set; }

        [StringLength(500)]
        public string IDPhotoPath { get; set; }

        // Transferee Documents
        [StringLength(500)]
        public string TranscriptOfRecordsPath { get; set; }

        [StringLength(500)]
        public string HonorableDismissalPath { get; set; }

        // Submission Details
        public DateTime SubmittedDate { get; set; }

        public bool TermsAccepted { get; set; }

        [StringLength(1000)]
        public string AdminRemarks { get; set; }

        // Navigation Property
        [ForeignKey("StudentId")]
        public virtual Student Student { get; set; }
    }
}
