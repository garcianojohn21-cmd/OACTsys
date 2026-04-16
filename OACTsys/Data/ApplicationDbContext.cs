using Microsoft.EntityFrameworkCore;
using OACTsys.Models;

namespace OACTsys.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Student> Students { get; set; }
        public DbSet<Enrollment> Enrollment { get; set; }
        public DbSet<EnrollmentForm> EnrollmentForms { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Subject> Subjects { get; set; }
        public DbSet<SubjectEnrollment> SubjectEnrollments { get; set; }
        public DbSet<EnrollmentField> EnrollmentFields { get; set; }
        public DbSet<TuitionFee> TuitionFees { get; set; }
        public DbSet<EnrollmentFieldData> EnrollmentFieldData { get; set; }

        // Admin tables
        public DbSet<Admin> Admins { get; set; }
        public DbSet<AdminPermission> AdminPermissions { get; set; }

        public DbSet<GCashConfig> GCashConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ===== STUDENT CONFIGURATIONS =====

            modelBuilder.Entity<Student>()
                .HasOne(s => s.EnrollmentForm)
                .WithOne(e => e.Student)
                .HasForeignKey<EnrollmentForm>(e => e.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Student>()
                .HasMany(s => s.Payments)
                .WithOne(p => p.Student)
                .HasForeignKey(p => p.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Student>()
                .HasMany(s => s.SubjectEnrollments)
                .WithOne(se => se.Student)
                .HasForeignKey(se => se.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Subject>()
                .HasMany(s => s.SubjectEnrollments)
                .WithOne(se => se.Subject)
                .HasForeignKey(se => se.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // ✅ PostgreSQL-compatible default values (NOW() instead of GETDATE())
            modelBuilder.Entity<Student>()
                .Property(s => s.EnrollmentDate)
                .HasDefaultValueSql("NOW()");

            modelBuilder.Entity<Student>()
                .Property(s => s.HasAccount)
                .HasDefaultValue(false);

            modelBuilder.Entity<Student>()
                .Property(s => s.TokenUsed)
                .HasDefaultValue(false);

            modelBuilder.Entity<EnrollmentForm>()
                .Property(e => e.SubmittedDate)
                .HasDefaultValueSql("NOW()");

            modelBuilder.Entity<Payment>()
                .Property(p => p.PaymentDate)
                .HasDefaultValueSql("NOW()");

            modelBuilder.Entity<SubjectEnrollment>()
                .Property(se => se.EnrolledDate)
                .HasDefaultValueSql("NOW()");

            // ===== INDEXES =====

            modelBuilder.Entity<Student>()
                .HasIndex(s => s.StudentNumber)
                .IsUnique();

            modelBuilder.Entity<Student>()
                .HasIndex(s => s.Email);

            modelBuilder.Entity<Payment>()
                .HasIndex(p => new { p.StudentId, p.Status });

            // ===== ADMIN CONFIGURATIONS =====

            modelBuilder.Entity<Admin>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Username)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.Email)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.FullName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.PasswordHash)
                    .IsRequired();

                entity.Property(e => e.RoleName)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.IsActive)
                    .HasDefaultValue(true);

                // ✅ PostgreSQL-compatible
                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
            });

            modelBuilder.Entity<AdminPermission>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.PermissionName)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.HasOne(e => e.Admin)
                    .WithMany(a => a.AdminPermissions)
                    .HasForeignKey(e => e.AdminId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.AdminId, e.PermissionName });
            });
        }
    }
}