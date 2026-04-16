using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace OACTsys.Helpers
{
    public static class EnrollmentFieldMappingHelper
    {
        // Every {{placeholder}} that exists in admission_form_template.docx
        // Key   = the TemplateKey value stored in DB  (no {{ }})
        // Value = human-readable label shown in admin dropdown
        public static readonly List<(string Key, string Label, string Group)> AllKeys =
        [
            // ── Personal Data ─────────────────────────────────────────────
            ( "full_name",        "Full Name",                       "Personal Data" ),
            ( "gender",           "Gender",                          "Personal Data" ),
            ( "birthdate",        "Birthdate",                       "Personal Data" ),
            ( "birthplace",       "Birthplace",                      "Personal Data" ),
            ( "home_address",     "Home Address",                    "Personal Data" ),
            ( "email_address",    "Email Address",                   "Personal Data" ),
            ( "mobile_number",    "Mobile Number",                   "Personal Data" ),
            ( "photo_2x2",        "2x2 Photo",                       "Personal Data" ),

            // ── Educational Background ────────────────────────────────────
            ( "grade_school_name",          "Grade School – Name",          "Educational Background" ),
            ( "grade_school_address",       "Grade School – Address",       "Educational Background" ),
            ( "gs_from",                    "Grade School – Year From",     "Educational Background" ),
            ( "gs_to",                      "Grade School – Year To",       "Educational Background" ),
            ( "junior_high_school_name",    "Junior High School – Name",    "Educational Background" ),
            ( "junior_high_school_address", "Junior High School – Address", "Educational Background" ),
            ( "jhs_from",                   "JHS – Year From",              "Educational Background" ),
            ( "jhs_to",                     "JHS – Year To",                "Educational Background" ),
            ( "senior_high_school_name",    "Senior High School – Name",    "Educational Background" ),
            ( "senior_high_school_address", "Senior High School – Address", "Educational Background" ),
            ( "shs_from",                   "SHS – Year From",              "Educational Background" ),
            ( "shs_to",                     "SHS – Year To",                "Educational Background" ),

            // ── For Transferees ───────────────────────────────────────────
            ( "transferee_school_name",    "Previous School – Name",     "Transferee" ),
            ( "transferee_school_address", "Previous School – Address",  "Transferee" ),
            ( "tf_ds",                     "Previous School – Inclusive Dates", "Transferee" ),

            // ── Parents Information ───────────────────────────────────────
            ( "father_name",       "Father's Name",         "Parents Information" ),
            ( "father_occupation", "Father's Occupation",   "Parents Information" ),
            ( "father_email",      "Father's Email",        "Parents Information" ),
            ( "father_mobile",     "Father's Mobile No.",   "Parents Information" ),
            ( "mother_name",       "Mother's Name",         "Parents Information" ),
            ( "mother_occupation", "Mother's Occupation",   "Parents Information" ),
            ( "mother_email",      "Mother's Email",        "Parents Information" ),
            ( "mother_mobile",     "Mother's Mobile No.",   "Parents Information" ),

            // ── Signature ─────────────────────────────────────────────────
            ( "signature_image",             "Signature – Image Upload", "Signature" ),
            ( "signature_over_printed_name", "Signature – Printed Name", "Signature" ),

            // ── Auto-computed (informational only — admin should NOT map fields to these;
            //    they are filled automatically from the Student record) ─────
            // ( "academic_year",  "Academic Year [AUTO]",   "System" ),
            // ( "semester",       "Semester [AUTO]",        "System" ),
            // ( "course_year",    "Program [AUTO]",         "System" ),
            // ( "year_level",     "Year Level [AUTO]",      "System" ),
            // ( "applicant_type", "Applicant Type [AUTO]",  "System" ),
            // ( "signature_date", "Signature Date [AUTO]",  "System" ),
        ];

        /// <summary>
        /// Returns grouped SelectListItems for use in a &lt;select&gt; dropdown.
        /// Pass the field's current TemplateKey so the correct option is pre-selected.
        /// </summary>
        public static List<SelectListGroup> GetGroups() => new()
        {
            new SelectListGroup { Name = "Personal Data" },
            new SelectListGroup { Name = "Educational Background" },
            new SelectListGroup { Name = "Transferee" },
            new SelectListGroup { Name = "Parents Information" },
            new SelectListGroup { Name = "Signature" },
        };

        public static List<SelectListItem> GetOptions(string currentKey = null)
        {
            var groups = new Dictionary<string, SelectListGroup>();
            var items = new List<SelectListItem>
            {
                new SelectListItem
                {
                    Value    = "",
                    Text     = "— Not mapped (not in admission form) —",
                    Selected = string.IsNullOrEmpty(currentKey)
                }
            };

            foreach (var (key, label, group) in AllKeys)
            {
                if (!groups.ContainsKey(group))
                    groups[group] = new SelectListGroup { Name = group };

                items.Add(new SelectListItem
                {
                    Value = key,
                    Text = label,
                    Selected = key == currentKey,
                    Group = groups[group]
                });
            }

            return items;
        }
    }
}