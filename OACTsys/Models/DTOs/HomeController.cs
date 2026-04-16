using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace OACTsys.Models
{
    public class FreshmenRegistrationDto
    {
        // Program Selection
        [Required]
        public string ChosenProgram { get; set; }

        // Dynamic Fields (will be populated from form)
        public Dictionary<string, string> DynamicFields { get; set; } = new Dictionary<string, string>();

        // File Uploads (will be populated from form files)
        public Dictionary<string, IFormFile> FileUploads { get; set; } = new Dictionary<string, IFormFile>();

        // Selected Subjects
        public List<int> SelectedSubjects { get; set; } = new List<int>();

        // Terms Acceptance
        [Required]
        public bool AgreeTerms { get; set; }
    }
}