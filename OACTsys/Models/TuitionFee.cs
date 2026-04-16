using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class TuitionFee
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Program { get; set; } // BSAMT, BSAE, etc.

        [Required]
        [StringLength(50)]
        public string StudentType { get; set; } // Freshmen, Transferee, OldStudent

        [Required]
        public int YearLevel { get; set; } // 1, 2, 3, 4

        [Required]
        public int Semester { get; set; } // 1, 2

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TuitionFees { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Miscellaneous { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Laboratory { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        public bool HasDiscount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountPercent { get; set; } // Usually 10%

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal FinalTotal { get; set; }

        // Payment Schedule
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal DownPayment { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal OtherFees { get; set; } // Uniform, books, etc.

        [StringLength(1000)]
        public string OtherFeesDescription { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalPaymentUponEnrollment { get; set; }

        // Monthly Breakdown
        public int NumberOfMonths { get; set; } // Usually 4 (Prelim, Midterm, SemiFinal, Final)

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrelimPayment { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MidtermPayment { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SemiFinalPayment { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal FinalPayment { get; set; }

        [StringLength(2000)]
        public string Requirements { get; set; } // JSON or comma-separated

        public bool IsActive { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime? UpdatedDate { get; set; }
    }
}