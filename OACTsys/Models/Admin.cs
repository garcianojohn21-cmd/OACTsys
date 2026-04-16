// Models/Admin.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OACTsys.Models
{
    public class Admin
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string FullName { get; set; }

        [Required]
        [StringLength(50)]
        public string Username { get; set; }

        [Required]
        [StringLength(100)]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        [Required]
        [StringLength(50)]
        public string RoleName { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLogin { get; set; }

        // Navigation property
        public virtual ICollection<AdminPermission> AdminPermissions { get; set; }
    }
}