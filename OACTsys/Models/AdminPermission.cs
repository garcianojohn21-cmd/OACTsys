// Models/AdminPermission.cs
using System.ComponentModel.DataAnnotations;

namespace OACTsys.Models
{
    public class AdminPermission
    {
        public int Id { get; set; }

        [Required]
        public int AdminId { get; set; }

        [Required]
        [StringLength(50)]
        public string PermissionName { get; set; }

        // Navigation property
        public virtual Admin Admin { get; set; }
    }
}