// ============================================================
// FILE: Services/GradesService.cs
// Place in: OACTsys/Services/GradesService.cs
// ============================================================

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Helpers;
using OACTsys.Models;
using Spire.Doc;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace OACTsys.Services
{
    public class GradesService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public GradesService(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ════════════════════════════════════════════════════════════════
        // PUBLIC: Generate filled DOCX → convert to PDF → return bytes
        // ════════════════════════════════════════════════════════════════
        public async Task<(byte[] Bytes, string FileName)> GenerateGradesPdfAsync(int studentId)
        {
            var (docxBytes, fileName) = await GenerateGradesDocxAsync(studentId);

            var tmpDir = Path.Combine(_env.WebRootPath, "uploads", "grades_tmp");
            Directory.CreateDirectory(tmpDir);
            var docxPath = Path.Combine(tmpDir, fileName + ".docx");
            var pdfPath = Path.Combine(tmpDir, fileName + ".pdf");

            await File.WriteAllBytesAsync(docxPath, docxBytes);

            await Task.Run(() =>
            {
                var doc = new Document();
                try { doc.LoadFromFile(docxPath); doc.SaveToFile(pdfPath, FileFormat.PDF); }
                finally { doc.Close(); doc.Dispose(); }
            });

            var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
            try { File.Delete(docxPath); File.Delete(pdfPath); } catch { }

            return (pdfBytes, fileName + ".pdf");
        }

        // ════════════════════════════════════════════════════════════════
        // PUBLIC: Generate filled DOCX → return bytes
        // ════════════════════════════════════════════════════════════════
        public async Task<(byte[] Bytes, string FileName)> GenerateGradesDocxAsync(int studentId)
        {
            var student = await _context.Students
                .Include(s => s.FieldData)
                    .ThenInclude(fd => fd.EnrollmentField)
                .FirstOrDefaultAsync(s => s.StudentId == studentId)
                ?? throw new Exception($"Student {studentId} not found.");

            var subjectEnrollments = await _context.SubjectEnrollments
                .Include(se => se.Subject)
                .Where(se => se.StudentId == studentId && se.Subject != null && se.Subject.IsActive)
                .OrderBy(se => se.YearLevel).ThenBy(se => se.Semester).ThenBy(se => se.Subject!.CourseCode)
                .ToListAsync();

            var fd = GradeFieldHelper.BuildFieldDict(student.FieldData);

            string Resolve(params string[] keys)
            {
                foreach (var k in keys)
                    if (fd.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                        return v.Trim();
                return "";
            }

            var fullName = Resolve("full_name", "fullname", "name", "student_name", "complete_name");
            if (string.IsNullOrEmpty(fullName)) fullName = student.StudentNumber ?? "";
            fullName = fullName.ToUpper();

            var now = DateTime.Now;
            int ayStart = now.Month >= 6 ? now.Year : now.Year - 1;
            var ay = $"{ayStart}-{ayStart + 1}";

            var yearLabel = student.CurrentYearLevel switch
            {
                1 => "1st Year",
                2 => "2nd Year",
                3 => "3rd Year",
                4 => "4th Year",
                _ => $"{student.CurrentYearLevel}th Year"
            };
            var courseYear = $"{student.Program} - {yearLabel}";

            var rep = new Dictionary<string, string>
            {
                ["{{student_number}}"] = student.StudentNumber ?? "",
                ["{{full_name}}"] = fullName,
                ["{{course_year}}"] = courseYear,
                ["{{academic_year}}"] = ay,
            };

            var templatePath = Path.Combine(_env.WebRootPath, "templates", "grades_template.docx");
            if (!File.Exists(templatePath))
                throw new FileNotFoundException($"Template not found: {templatePath}");

            var templateBytes = await File.ReadAllBytesAsync(templatePath);
            var docxBytes = BuildDocx(templateBytes, rep, subjectEnrollments);

            var fileName = $"grades_{student.StudentNumber}_{now:yyyyMMddHHmmss}";
            return (docxBytes, fileName);
        }

        // ─────────────────────────────────────────────────────────────
        // Core DOCX builder
        // ─────────────────────────────────────────────────────────────
        private static byte[] BuildDocx(
            byte[] template,
            Dictionary<string, string> textRep,
            List<SubjectEnrollment> enrollments)
        {
            using var input = new MemoryStream(template);
            using var output = new MemoryStream();

            using (var inZip = new ZipArchive(input, ZipArchiveMode.Read))
            using (var outZip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (var entry in inZip.Entries)
                {
                    var outEntry = outZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    using var ins = entry.Open();
                    using var outs = outEntry.Open();

                    bool isDocXml = entry.FullName == "word/document.xml";
                    if (!isDocXml) { ins.CopyTo(outs); continue; }

                    var raw = new StreamReader(ins, Encoding.UTF8).ReadToEnd();
                    raw = CollapseRunSplits(raw);

                    try
                    {
                        var xdoc = XDocument.Parse(raw, LoadOptions.PreserveWhitespace);

                        ApplyArialToAllRuns(xdoc);
                        InjectGradeRows(xdoc, enrollments);
                        ApplyTextRep(xdoc, textRep);
                        ClearPlaceholders(xdoc);

                        raw = Serialize(xdoc);
                    }
                    catch (XmlException)
                    {
                        foreach (var kv in textRep)
                            raw = raw.Replace(kv.Key, XmlEsc(kv.Value));
                        raw = Regex.Replace(raw, @"\{\{[^}]+\}\}", "");
                    }

                    var bytes = Encoding.UTF8.GetBytes(raw);
                    outs.Write(bytes, 0, bytes.Length);
                }
            }

            return output.ToArray();
        }

        // ─────────────────────────────────────────────────────────────
        // Apply Arial font to every run in the document
        // ─────────────────────────────────────────────────────────────
        private static void ApplyArialToAllRuns(XDocument xdoc)
        {
            foreach (var rPr in xdoc.Descendants(W + "rPr"))
            {
                rPr.Elements(W + "rFonts").Remove();
                rPr.AddFirst(new XElement(W + "rFonts",
                    new XAttribute(W + "ascii", "Arial"),
                    new XAttribute(W + "hAnsi", "Arial"),
                    new XAttribute(W + "cs", "Arial"),
                    new XAttribute(W + "eastAsia", "Arial")));
            }
            foreach (var pPr in xdoc.Descendants(W + "pPr"))
            {
                var rPr = pPr.Element(W + "rPr");
                if (rPr == null) { rPr = new XElement(W + "rPr"); pPr.Add(rPr); }
                rPr.Elements(W + "rFonts").Remove();
                rPr.AddFirst(new XElement(W + "rFonts",
                    new XAttribute(W + "ascii", "Arial"),
                    new XAttribute(W + "hAnsi", "Arial"),
                    new XAttribute(W + "cs", "Arial"),
                    new XAttribute(W + "eastAsia", "Arial")));
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Inject grade rows + total units + grade average
        //
        // From the screenshot the Total row looks like:
        //   [empty] [empty] [empty] [Total Units :] [empty(units)] [empty(grade)]
        // OR it may be a merged cell row like:
        //   [colspan=4: "Total Units :"] [units] [grade]
        //
        // So we find the Total row, then:
        //  - scan cells for the one whose text contains "Total" → that is
        //    the label cell. The cell immediately AFTER it = units value.
        //    The cell AFTER that = grade average value.
        // ─────────────────────────────────────────────────────────────
        private static void InjectGradeRows(XDocument xdoc, List<SubjectEnrollment> enrollments)
        {
            var tables = xdoc.Descendants(W + "tbl").ToList();
            if (!tables.Any()) return;

            var tbl = tables[0];

            var semCounter = 0;
            var sectionRows = new List<(XElement Row, int YearLevel, int Semester)>();

            foreach (var row in tbl.Elements(W + "tr").ToList())
            {
                var text = GetRowText(row);
                if (text.Contains("1st Semester"))
                {
                    semCounter++;
                    sectionRows.Add((row, semCounter <= 1 ? 1 : 2, 1));
                }
                else if (text.Contains("2nd Semester"))
                {
                    semCounter++;
                    sectionRows.Add((row, semCounter <= 2 ? 1 : 2, 2));
                }
            }

            var grouped = enrollments
                .GroupBy(e => new { e.YearLevel, e.Semester })
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (headerRow, yl, sem) in sectionRows)
            {
                var key = new { YearLevel = yl, Semester = sem };
                var seList = grouped.TryGetValue(key, out var list) ? list : new List<SubjectEnrollment>();

                // Re-read live rows each pass
                var liveRows = tbl.Elements(W + "tr").ToList();
                var headerIndex = liveRows.IndexOf(headerRow);
                if (headerIndex < 0) continue;

                var rowsAfter = liveRows.Skip(headerIndex + 1).ToList();
                var totalRow = rowsAfter.FirstOrDefault(r => GetRowText(r).Contains("Total"));
                if (totalRow == null) continue;

                // Remove blank placeholder rows between header and Total
                rowsAfter
                    .TakeWhile(r => r != totalRow)
                    .Where(r => string.IsNullOrWhiteSpace(GetRowText(r)))
                    .ToList()
                    .ForEach(r => r.Remove());

                // Insert subject rows and accumulate totals
                decimal totalUnits = 0;
                decimal gradeSum = 0;
                int gradeCount = 0;

                foreach (var se in seList)
                {
                    if (se.Subject == null) continue;
                    totalUnits += se.Subject.Units;

                    if (!string.IsNullOrWhiteSpace(se.FinalGrade) &&
                        decimal.TryParse(se.FinalGrade, out var g))
                    {
                        gradeSum += g;
                        gradeCount++;
                    }

                    totalRow.AddBeforeSelf(BuildGradeRow(
                        program: se.Subject.Program ?? "",
                        code: se.Subject.CourseCode ?? "",
                        description: se.Subject.DescriptiveTitle ?? "",
                        units: se.Subject.Units,
                        grade: se.FinalGrade ?? ""));
                }

                string avgText = gradeCount > 0
                    ? (gradeSum / gradeCount).ToString("0.00")
                    : "";

                // ── Write into the Total row ──────────────────────────
                // From your screenshot the row has these cells (5 total):
                //   [0] empty  [1] empty  [2] "Total Units :"  [3] units-value  [4] grade-value
                //
                // We locate the label cell by text, then fill the two cells after it.
                // This way we are immune to merged/spanning layout differences.
                var cells = totalRow.Elements(W + "tc").ToList();

                // Find the index of the cell that contains "Total"
                int labelIdx = -1;
                for (int i = 0; i < cells.Count; i++)
                {
                    if (GetCellText(cells[i]).Contains("Total"))
                    {
                        labelIdx = i;
                        break;
                    }
                }

                if (labelIdx >= 0)
                {
                    // Units value cell = label + 1
                    if (labelIdx + 1 < cells.Count)
                        WriteTextToCell(cells[labelIdx + 1], totalUnits.ToString("0.##"));

                    // Grade average cell = label + 2
                    if (labelIdx + 2 < cells.Count)
                        WriteTextToCell(cells[labelIdx + 2], avgText);
                }
                else
                {
                    // Fallback: no label cell found — just use last two cells
                    if (cells.Count >= 2)
                        WriteTextToCell(cells[cells.Count - 2], totalUnits.ToString("0.##"));
                    if (cells.Count >= 1)
                        WriteTextToCell(cells[cells.Count - 1], avgText);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Get plain text of a single cell
        // ─────────────────────────────────────────────────────────────
        private static string GetCellText(XElement cell) =>
            string.Concat(cell.Descendants(W + "t").Select(t => t.Value));

        // ─────────────────────────────────────────────────────────────
        // Write a value into a table cell
        // ─────────────────────────────────────────────────────────────
        private static void WriteTextToCell(XElement cell, string value)
        {
            var runs = cell.Descendants(W + "r").ToList();

            if (runs.Any())
            {
                var firstT = runs.First().Descendants(W + "t").FirstOrDefault();
                if (firstT != null) firstT.Value = value;
                foreach (var extra in runs.Skip(1)) extra.Remove();
            }
            else
            {
                var para = cell.Descendants(W + "p").FirstOrDefault();
                if (para == null) { para = new XElement(W + "p"); cell.Add(para); }

                para.Add(new XElement(W + "r",
                    new XElement(W + "rPr",
                        new XElement(W + "rFonts",
                            new XAttribute(W + "ascii", "Arial"),
                            new XAttribute(W + "hAnsi", "Arial"),
                            new XAttribute(W + "cs", "Arial"),
                            new XAttribute(W + "eastAsia", "Arial")),
                        new XElement(W + "b"),
                        new XElement(W + "sz", new XAttribute(W + "val", "18")),
                        new XElement(W + "szCs", new XAttribute(W + "val", "18"))),
                    new XElement(W + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"),
                        value)));
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Build one grade data row
        // ─────────────────────────────────────────────────────────────
        private static XElement BuildGradeRow(
            string program, string code, string description,
            decimal units, string grade)
        {
            static XElement ArialFonts() =>
                new XElement(W + "rFonts",
                    new XAttribute(W + "ascii", "Arial"),
                    new XAttribute(W + "hAnsi", "Arial"),
                    new XAttribute(W + "cs", "Arial"),
                    new XAttribute(W + "eastAsia", "Arial"));

            static XElement Cell(string text, bool bold = false)
            {
                var rPr = new XElement(W + "rPr");
                rPr.Add(ArialFonts());
                if (bold) rPr.Add(new XElement(W + "b"));
                rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", "18")));
                rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", "18")));

                return new XElement(W + "tc",
                    new XElement(W + "tcPr",
                        new XElement(W + "tcW",
                            new XAttribute(W + "w", "0"),
                            new XAttribute(W + "type", "auto")),
                        new XElement(W + "tcBorders",
                            TcBorder("top"), TcBorder("left"),
                            TcBorder("bottom"), TcBorder("right"))),
                    new XElement(W + "p",
                        new XElement(W + "pPr",
                            new XElement(W + "spacing",
                                new XAttribute(W + "after", "30")),
                            new XElement(W + "rPr", ArialFonts())),
                        new XElement(W + "r", rPr,
                            new XElement(W + "t",
                                new XAttribute(XNamespace.Xml + "space", "preserve"),
                                text))));
            }

            return new XElement(W + "tr",
                new XElement(W + "trPr",
                    new XElement(W + "trHeight",
                        new XAttribute(W + "val", "360"))),
                Cell(program),
                Cell(code),
                Cell(description),
                Cell(units > 0 ? units.ToString("0.##") : ""),
                Cell(grade));
        }

        private static XElement TcBorder(string side) =>
            new XElement(W + side,
                new XAttribute(W + "val", "single"),
                new XAttribute(W + "sz", "4"),
                new XAttribute(W + "space", "0"),
                new XAttribute(W + "color", "AAAAAA"));

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────
        private static string GetRowText(XElement row) =>
            string.Concat(row.Descendants(W + "t").Select(t => t.Value));

        private static void ApplyTextRep(XDocument xdoc, Dictionary<string, string> rep)
        {
            foreach (var wt in xdoc.Descendants(W + "t").ToList())
            {
                var v = wt.Value;
                foreach (var kv in rep) v = v.Replace(kv.Key, kv.Value);
                if (v != wt.Value) wt.Value = v;
            }
        }

        private static void ClearPlaceholders(XDocument xdoc)
        {
            foreach (var wt in xdoc.Descendants(W + "t").ToList())
                if (wt.Value.Contains("{{"))
                    wt.Value = Regex.Replace(wt.Value, @"\{\{[^}]+\}\}", "");
        }

        private static string CollapseRunSplits(string xml) =>
            Regex.Replace(xml,
                @"\{\{((?:[^{}]|<[^>]+>)*?)\}\}",
                m => {
                    var key = Regex.Replace(m.Groups[1].Value, @"<[^>]+>", "").Trim();
                    return string.IsNullOrEmpty(key) ? m.Value : $"{{{{{key}}}}}";
                },
                RegexOptions.Singleline);

        private static string Serialize(XDocument xdoc)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
                Indent = false
            };
            using var ms = new MemoryStream();
            using (var w = XmlWriter.Create(ms, settings)) xdoc.WriteTo(w);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static string XmlEsc(string v) =>
            string.IsNullOrEmpty(v) ? "" :
            v.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&apos;");
    }
}