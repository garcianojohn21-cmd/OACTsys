using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class Payment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PaymentId { get; set; }

        public int StudentId { get; set; }

        [ForeignKey("StudentId")]
        public virtual Student Student { get; set; }

        [Required, StringLength(50)]
        public string PaymentType { get; set; }     // Enrollment, Tuition, Miscellaneous

        [Required, StringLength(50)]
        public string PaymentMethod { get; set; }   // GCash, Cash, Bank Transfer

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [StringLength(100)]
        public string? ReferenceNumber { get; set; }    // null until student submits ref#

        [StringLength(500)]
        public string? ProofOfPaymentPath { get; set; } // null until student uploads proof

        [StringLength(200)]
        public string? PaymentLocation { get; set; }    // null for GCash, set for manual/cash

        [Required, StringLength(50)]
        public string Status { get; set; }          // Pending, Verified, Rejected

        public DateTime PaymentDate { get; set; }

        public DateTime? VerifiedDate { get; set; }

        [StringLength(100)]
        public string? VerifiedBy { get; set; }         // null until admin verifies

        [StringLength(500)]
        public string? Remarks { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedDate { get; set; }
    }
}