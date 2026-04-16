using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OACTsys.Models
{
    public class GCashConfig
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string AccountName { get; set; }

        [Required, StringLength(20)]
        public string GCashNumber { get; set; } // e.g. 09171234567

        [StringLength(500)]
        public string QrCodePath { get; set; }

        [StringLength(500)]
        public string PaymentDescription { get; set; } // shown on GCash

        public bool IsActive { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string UpdatedBy { get; set; }
    }
}