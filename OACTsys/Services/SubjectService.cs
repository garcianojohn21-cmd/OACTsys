// ============================================================
// FILE: Services/SubjectService.cs
// Place in: OACTsys/Services/SubjectService.cs
// ============================================================

using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OACTsys.Services
{
    public class SubjectService
    {
        private readonly ApplicationDbContext _context;

        static readonly XNamespace W =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        // ── Row-header keywords to skip ──────────────────────────────
        private static readonly HashSet<string> _headerKeywords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "COURSE CODE", "DESCRIPTIVE TITLE", "LEC", "LAB",
                "HOURS", "UNITS", "TOTAL", "TOTAL HOURS"
            };

        public SubjectService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ════════════════════════════════════════════════════════════════
        // PUBLIC: Parse .docx stre        am → save subjects → return result
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reads the uploaded .docx curriculum file, extracts every subject row,
        /// deduplicates against existing DB records (matched by Program + CourseCode
        /// + YearLevel + Semester), inserts new ones, and returns a result summary.
        ///
        /// Template layout expected (matches subjects_template.docx):
        ///   Heading paragraphs: "TWO-YEAR AMT COURSE" / "TWO-YEAR AVT COURSE"
        ///   Section headings:   "First Year (First Semester)" etc.
        ///   Table columns:      COURSE CODE | DESCRIPTIVE TITLE | LEC | LAB | HOURS | UNITS
        /// </summary>
        public async Task<SubjectUploadResult> UploadSubjectsFromDocxAsync(
            Stream docxStream,
            bool replaceExisting = false)
        {
            // ── 1. Parse subjects from docx ──────────────────────────
            List<Subject> parsed;
            try
            {
                parsed = ParseDocx(docxStream);
            }
            catch (Exception ex)
            {
                return Fail($"Failed to read the document: {ex.Message}");
            }

            if (parsed.Count == 0)
                return Fail(
                    "No subjects were found in the uploaded document. " +
                    "Please make sure it matches the expected curriculum template format.");

            // ── 2. Apply to database ─────────────────────────────────
            try
            {
                // FIX: Load ALL subjects (active AND inactive) so we can
                // correctly resurrect soft-deleted records instead of skipping them.
                var existing = await _context.Subjects.ToListAsync();

                int inserted = 0, updated = 0, skipped = 0;

                foreach (var subject in parsed)
                {
                    var match = existing.FirstOrDefault(s =>
                        string.Equals(s.Program?.Trim(), subject.Program?.Trim(),
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(s.CourseCode?.Trim(), subject.CourseCode?.Trim(),
                            StringComparison.OrdinalIgnoreCase) &&
                        s.YearLevel == subject.YearLevel &&
                        s.Semester == subject.Semester);

                    if (match != null)
                    {
                        bool wasInactive = !match.IsActive;

                        if (replaceExisting || wasInactive)
                        {
                            // FIX: Always resurrect soft-deleted subjects.
                            // Also update all fields when replaceExisting = true.
                            match.DescriptiveTitle = subject.DescriptiveTitle;
                            match.LectureHours = subject.LectureHours;
                            match.LaboratoryHours = subject.LaboratoryHours;
                            match.TotalHours = subject.TotalHours;
                            match.Units = subject.Units;
                            match.IsActive = true;

                            if (wasInactive)
                                inserted++; // was soft-deleted — treat as new
                            else
                                updated++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    else
                    {
                        subject.IsActive = true;
                        subject.CreatedDate = DateTime.Now;
                        await _context.Subjects.AddAsync(subject);
                        inserted++;
                    }
                }

                await _context.SaveChangesAsync();

                return new SubjectUploadResult
                {
                    Success = true,
                    TotalParsed = parsed.Count,
                    Inserted = inserted,
                    Updated = updated,
                    Skipped = skipped,
                    Message = BuildSummary(inserted, updated, skipped),
                    ParsedSubjects = parsed
                };
            }
            catch (Exception ex)
            {
                return Fail($"Database error while saving subjects: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // PUBLIC: Preview only – parse without saving
        // ════════════════════════════════════════════════════════════════

        public SubjectUploadResult PreviewDocx(Stream docxStream)
        {
            try
            {
                var parsed = ParseDocx(docxStream);
                return new SubjectUploadResult
                {
                    Success = true,
                    TotalParsed = parsed.Count,
                    Message = $"Preview: {parsed.Count} subject(s) found in the document.",
                    ParsedSubjects = parsed
                };
            }
            catch (Exception ex)
            {
                return Fail($"Failed to read the document: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // PRIVATE: Open the .docx ZIP and parse word/document.xml
        // ════════════════════════════════════════════════════════════════

        private List<Subject> ParseDocx(Stream stream)
        {
            XDocument xdoc;

            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var entry = zip.GetEntry("word/document.xml")
                    ?? throw new InvalidOperationException(
                           "word/document.xml not found — is this a valid .docx file?");

                using var es = entry.Open();
                using var sr = new StreamReader(es, Encoding.UTF8);
                xdoc = XDocument.Parse(sr.ReadToEnd());
            }

            var body = xdoc.Descendants(W + "body").FirstOrDefault()
                       ?? throw new InvalidOperationException(
                              "No <w:body> element found in document.xml.");

            var subjects = new List<Subject>();
            string currentProgram = "";
            int currentYear = 1;
            int currentSemester = 1;

            foreach (var child in body.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "p":
                        {
                            var text = GetParaText(child).Trim();
                            if (string.IsNullOrWhiteSpace(text)) break;

                            // Detect program heading e.g. "TWO-YEAR AMT COURSE"
                            var prog = DetectProgram(text);
                            if (prog != null)
                            {
                                currentProgram = prog;
                                break;
                            }

                            // Detect year/semester heading e.g. "First Year (First Semester)"
                            var (yr, sem) = DetectYearSemester(text);
                            if (yr > 0)
                            {
                                currentYear = yr;
                                currentSemester = sem;
                            }
                            break;
                        }

                    case "tbl":
                        {
                            if (string.IsNullOrWhiteSpace(currentProgram)) break;

                            foreach (var row in child.Elements(W + "tr"))
                            {
                                var subject = ParseRow(row, currentProgram,
                                                       currentYear, currentSemester);
                                if (subject != null)
                                    subjects.Add(subject);
                            }
                            break;
                        }
                }
            }

            return subjects;
        }

        // ════════════════════════════════════════════════════════════════
        // Parse a single table row into a Subject (returns null to skip)
        // Expected columns: [0] CourseCode [1] Title [2] Lec [3] Lab [4] Hours [5] Units
        // ════════════════════════════════════════════════════════════════

        private static Subject? ParseRow(
            XElement row, string program, int yearLevel, int semester)
        {
            var cells = row.Elements(W + "tc").ToList();
            if (cells.Count < 5) return null;

            var col0 = GetCellText(cells[0]).Trim();   // Course Code
            var col1 = GetCellText(cells[1]).Trim();   // Descriptive Title
            var col2 = GetCellText(cells[2]).Trim();   // Lec
            var col3 = GetCellText(cells[3]).Trim();   // Lab
            var col4 = GetCellText(cells[4]).Trim();   // Hours / Total Hours
            var col5 = cells.Count > 5
                       ? GetCellText(cells[5]).Trim()  // Units
                       : "";

            // Skip header rows and empty / TOTAL rows
            if (IsHeaderOrEmptyRow(col0, col1)) return null;

            decimal.TryParse(col2, out decimal lec);
            decimal.TryParse(col3, out decimal lab);
            if (!decimal.TryParse(col4, out decimal hours)) hours = lec + lab;
            decimal.TryParse(col5, out decimal units);

            return new Subject
            {
                Program = program,
                CourseCode = col0,
                DescriptiveTitle = col1,
                LectureHours = (int)lec,
                LaboratoryHours = (int)lab,
                TotalHours = (int)hours,
                Units = units,
                YearLevel = yearLevel,
                Semester = semester,
            };
        }

        // ════════════════════════════════════════════════════════════════
        // Detect program abbreviation from a heading paragraph
        // "TWO-YEAR AMT COURSE"  → "AMT"
        // "TWO-YEAR AVT COURSE"  → "AVT"
        // Returns null if the text is not a program heading.
        // ════════════════════════════════════════════════════════════════

        private static string? DetectProgram(string text)
        {
            // Match "XXX COURSE" where XXX is 2-6 uppercase letters
            var m = Regex.Match(text,
                @"\b([A-Z]{2,6})\s+COURSE\b",
                RegexOptions.IgnoreCase);

            return m.Success ? m.Groups[1].Value.ToUpper() : null;
        }

        // ════════════════════════════════════════════════════════════════
        // Detect year level + semester from a section heading
        // "First Year (First Semester)"   → (1, 1)
        // "Second Year (Second Semester)" → (2, 2)
        // "First Year (Summer)"           → (1, 3)
        // Returns (0, 0) if not a year/semester heading.
        // ════════════════════════════════════════════════════════════════

        private static (int Year, int Semester) DetectYearSemester(string text)
        {
            var t = text.ToLower();

            int year = 0;
            if (t.Contains("first year") || t.Contains("1st year")) year = 1;
            else if (t.Contains("second year") || t.Contains("2nd year")) year = 2;
            else if (t.Contains("third year") || t.Contains("3rd year")) year = 3;
            else if (t.Contains("fourth year") || t.Contains("4th year")) year = 4;

            if (year == 0) return (0, 0);

            int sem = 1;
            if (t.Contains("summer")) sem = 3;
            else if (t.Contains("second semester") || t.Contains("2nd")) sem = 2;

            return (year, sem);
        }

        // ════════════════════════════════════════════════════════════════
        // XML helpers
        // ════════════════════════════════════════════════════════════════

        private static string GetParaText(XElement para) =>
            string.Concat(para.Descendants(W + "t").Select(t => t.Value));

        private static string GetCellText(XElement cell) =>
            string.Concat(cell.Descendants(W + "t").Select(t => t.Value));

        private static bool IsHeaderOrEmptyRow(string col0, string col1)
        {
            if (string.IsNullOrWhiteSpace(col0) && string.IsNullOrWhiteSpace(col1))
                return true;

            if (_headerKeywords.Contains(col0.Trim()))
                return true;

            // "TOTAL" summary rows
            if (col0.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        // ════════════════════════════════════════════════════════════════
        // Result helpers
        // ════════════════════════════════════════════════════════════════

        private static SubjectUploadResult Fail(string message) =>
            new() { Success = false, Message = message };

        private static string BuildSummary(int inserted, int updated, int skipped)
        {
            var parts = new List<string>();
            if (inserted > 0) parts.Add($"{inserted} subject(s) added");
            if (updated > 0) parts.Add($"{updated} subject(s) updated");
            if (skipped > 0) parts.Add($"{skipped} subject(s) skipped (already exist)");

            return parts.Any()
                ? string.Join(", ", parts) + "."
                : "No changes were made.";
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Result DTO
    // ════════════════════════════════════════════════════════════════

    public class SubjectUploadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int TotalParsed { get; set; }
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }

        /// <summary>All subjects parsed from the document (useful for preview grid).</summary>
        public List<Subject> ParsedSubjects { get; set; } = new();
    }
}