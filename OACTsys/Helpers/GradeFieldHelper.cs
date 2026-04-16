// ============================================================
// FILE: Helpers/GradeFieldHelper.cs
// Place in: OACTsys/Helpers/GradeFieldHelper.cs
//
// Shared static helpers used by GradesController only.
// Keeping them here instead of inside the controller prevents
// CS0121 "ambiguous" errors if the compiler picks up duplicate
// class definitions from misplaced files.
// ============================================================

using OACTsys.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OACTsys.Helpers
{
    public static class GradeFieldHelper
    {
        // ─────────────────────────────────────────────────────────────
        // Normalise a key for comparison.
        // "Full Name" → "full_name"
        // "Phone Number" → "phone_number"
        // "EmailAddress" → "emailaddress"
        // ─────────────────────────────────────────────────────────────
        public static string NormaliseKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return Regex.Replace(s.Trim().ToLower(), @"[\s\-\.]+", "_");
        }

        // ─────────────────────────────────────────────────────────────
        // Build a flat lookup dictionary from a student's FieldData.
        //
        // Indexes each row under BOTH its normalised TemplateKey AND
        // its normalised FieldName so data is found regardless of
        // whether the admin filled in TemplateKey or not.
        //
        // Example:
        //   TemplateKey = "full_name",  FieldName = "Full Name"
        //   → dict["full_name"] = "Juan Dela Cruz"   (from TemplateKey)
        //   → dict["full_name"] = "Juan Dela Cruz"   (from FieldName — same after normalise, no duplicate)
        // ─────────────────────────────────────────────────────────────
        public static Dictionary<string, string> BuildFieldDict(
            ICollection<EnrollmentFieldData>? fieldData)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (fieldData == null) return dict;

            foreach (var fd in fieldData)
            {
                if (fd.EnrollmentField == null) continue;
                var value = fd.FieldValue?.Trim() ?? "";

                // Index by normalised TemplateKey (preferred)
                var tKey = fd.EnrollmentField.TemplateKey?.Trim() ?? "";
                if (!string.IsNullOrEmpty(tKey))
                    dict.TryAdd(NormaliseKey(tKey), value);

                // Index by normalised FieldName (fallback)
                var fName = fd.EnrollmentField.FieldName?.Trim() ?? "";
                if (!string.IsNullOrEmpty(fName))
                    dict.TryAdd(NormaliseKey(fName), value);
            }

            return dict;
        }

        // ─────────────────────────────────────────────────────────────
        // Philippine grading — maps a numeric FinalGrade to a status.
        //   1.0–3.0 = Passed
        //   4.0     = Incomplete
        //   5.0     = Failed
        // ─────────────────────────────────────────────────────────────
        public static string ComputeStatus(decimal grade) =>
            grade switch
            {
                >= 1.0m and <= 3.0m => "Passed",
                4.0m                => "Incomplete",
                _                   => "Failed"
            };
    }
}
