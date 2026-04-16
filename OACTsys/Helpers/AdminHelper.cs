using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace OACTsys.Helpers
{
    public static class AdminHelper
    {
        // =========================
        // PRIVATE FIELDS (Loaded from Configuration)
        // =========================
        private static string _superUsername;
        private static string _superPasswordHash;
        private static string _superFullName;

        // =========================
        // PUBLIC PROPERTY
        // =========================
        public static string SuperFullName => _superFullName ?? "Super Administrator";

        // =========================
        // ALL PERMISSIONS
        // =========================
        public static readonly List<string> AllPermissions = new List<string>
        {
            "Dashboard",
            "StudentRecords",
            "Enrollment",
            "Licensure",
            "Grades",
            "Payments",
            "Reports",
            "UserManagement",
            "CreateAdmin",
            "SystemSettings",
            "Messages",
            "AuditLogs",
        };

        // =========================
        // INITIALIZATION (Called from Program.cs)
        // =========================
        public static void Initialize(string username, string password, string fullName)
        {
            _superUsername = username;
            _superPasswordHash = HashPassword(password);
            _superFullName = fullName;
        }

        // =========================
        // PASSWORD HASHING
        // =========================
        public static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        public static bool VerifyPassword(string inputPassword, string storedHash)
        {
            var inputHash = HashPassword(inputPassword);
            return inputHash == storedHash;
        }

        // =========================
        // VALIDATION
        // =========================
        public static bool ValidateSuperAdmin(string username, string password)
        {
            if (string.IsNullOrEmpty(_superUsername) || string.IsNullOrEmpty(_superPasswordHash))
                return false;

            return username == _superUsername && VerifyPassword(password, _superPasswordHash);
        }

        // =========================
        // PERMISSION MANAGEMENT
        // =========================
        public static bool HasPermission(HttpContext context, string permission)
        {
            var perms = context.Session.GetString("Permissions");
            if (string.IsNullOrEmpty(perms))
                return false;

            return perms.Split(',').Contains(permission);
        }

        public static void SetPermissionsSession(HttpContext context, List<string> permissions)
        {
            var permsString = string.Join(",", permissions);
            context.Session.SetString("Permissions", permsString);
        }

        public static List<string> GetSessionPermissions(HttpContext context)
        {
            var perms = context.Session.GetString("Permissions");
            if (string.IsNullOrEmpty(perms))
                return new List<string>();

            return perms.Split(',').ToList();
        }

        // =========================
        // RANDOM PASSWORD GENERATOR
        // =========================
        public static string GenerateRandomPassword(int length = 12)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%";
            using (var rng = new RNGCryptoServiceProvider())
            {
                var data = new byte[length];
                rng.GetBytes(data);
                var result = new StringBuilder(length);

                foreach (byte b in data)
                {
                    result.Append(chars[b % chars.Length]);
                }

                return result.ToString();
            }
        }
    }
}