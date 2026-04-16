// Data/DbSeeder.cs
using OACTsys.Helpers;
using OACTsys.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace OACTsys.Data
{
    public static class DbSeeder
    {
        public static void SeedDatabase(ApplicationDbContext context)
        {
            try
            {
                // Ensure database is created
                context.Database.EnsureCreated();

                // Check if any admins exist
                if (context.Admins.Any())
                {
                    Console.WriteLine("Database already has admin accounts. Skipping seed.");
                    return;
                }

                Console.WriteLine("Seeding initial admin account...");

                // Create initial admin account
                var initialAdmin = new Admin
                {
                    FullName = "System Administrator",
                    Username = "admin",
                    Email = "admin@oactsys.com",
                    PasswordHash = AdminHelper.HashPassword("Admin@123"), // Default password
                    RoleName = "System Administrator",
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                context.Admins.Add(initialAdmin);
                context.SaveChanges();

                Console.WriteLine($"Created admin account - ID: {initialAdmin.Id}");

                // Add all permissions to initial admin
                foreach (var permission in AdminHelper.AllPermissions)
                {
                    context.AdminPermissions.Add(new AdminPermission
                    {
                        AdminId = initialAdmin.Id,
                        PermissionName = permission
                    });
                }

                context.SaveChanges();

                Console.WriteLine("✅ Database seeded successfully!");
                Console.WriteLine("================================================");
                Console.WriteLine("Initial Admin Credentials:");
                Console.WriteLine("  Username: admin");
                Console.WriteLine("  Password: Admin@123");
                Console.WriteLine("================================================");
                Console.WriteLine("Super Admin Credentials (from appsettings.json):");
                Console.WriteLine("  Username: superadmin");
                Console.WriteLine("  Password: admin123");
                Console.WriteLine("================================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error seeding database: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}