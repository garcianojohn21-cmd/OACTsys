// ============================================================
// FILE: Services/AdmissionFormService.cs
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using Spire.Doc;
using Spire.Doc.Documents;

namespace OACTsys.Services
{
    public class AdmissionFormService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        // ── Well-known XML namespaces ─────────────────────────────────────
        static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        static readonly XNamespace PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly XNamespace REL = "http://schemas.openxmlformats.org/package/2006/relationships";

        public AdmissionFormService(
            ApplicationDbContext context,
            IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // ══════════════════════════════════════════════════════════════════
        // PUBLIC: Generate .docx and return its relative URL
        // ══════════════════════════════════════════════════════════════════
        public async Task<string> GenerateAdmissionFormAsync(int studentId)
        {
            // 1. Load student
            var student = await _context.Students
                .Include(s => s.FieldData)
                    .ThenInclude(fd => fd.EnrollmentField)
                .FirstOrDefaultAsync(s => s.StudentId == studentId);

            if (student == null)
                throw new Exception($"Student ID {studentId} not found.");

            // 2. Build text replacements and capture image paths
            var replacements = new Dictionary<string, string>();
            string? photoPath = null;
            string? signaturePath = null;

            if (student.FieldData != null)
            {
                foreach (var fd in student.FieldData
                    .Where(fd => fd.EnrollmentField != null
                              && !string.IsNullOrWhiteSpace(fd.EnrollmentField.TemplateKey)))
                {
                    var key = fd.EnrollmentField.TemplateKey.Trim();
                    var ftype = fd.EnrollmentField.FieldType ?? "";

                    if (ftype == "file" || ftype == "image")
                    {
                        if (key == "photo_2x2" && !string.IsNullOrEmpty(fd.FilePath))
                            photoPath = ToPhysicalPath(fd.FilePath);
                        else if (key == "signature_image" && !string.IsNullOrEmpty(fd.FilePath))
                            signaturePath = ToPhysicalPath(fd.FilePath);

                        replacements[$"{{{{{key}}}}}"] = "";
                        continue;
                    }

                    var value = fd.FieldValue ?? "";
                    if (ftype == "date" && !string.IsNullOrEmpty(value))
                        value = FormatDateValue(value);

                    replacements[$"{{{{{key}}}}}"] = value;
                }
            }

            // 3. Auto-computed values
            var now = DateTime.Now;
            int ayStart = now.Month >= 6 ? now.Year : now.Year - 1;

            Ensure(replacements, "{{academic_year}}", $"{ayStart}-{ayStart + 1}");
            Ensure(replacements, "{{semester}}",
                student.CurrentSemester == 1 ? "1st Semester" :
                student.CurrentSemester == 2 ? "2nd Semester" :
                $"{student.CurrentSemester}");
            Ensure(replacements, "{{course_year}}", student.Program ?? "");
            Ensure(replacements, "{{applicant_type}}", student.StudentType ?? "");
            Ensure(replacements, "{{year_level}}",
                student.CurrentYearLevel switch
                {
                    1 => "1st Year",
                    2 => "2nd Year",
                    3 => "3rd Year",
                    4 => "4th Year",
                    _ => $"{student.CurrentYearLevel}th Year"
                });
            Ensure(replacements, "{{signature_date}}",
                (student.EnrollmentDate ?? now).ToString("MMMM d, yyyy"));
            Ensure(replacements, "{{email_address}}", student.Email ?? "");

            if (replacements.TryGetValue("{{full_name}}", out var fn))
                Ensure(replacements, "{{signature_over_printed_name}}", fn);

            // ── Transferee inclusive dates ────────────────────────────────
            // {{tf_from}} and {{tf_to}} are populated individually from FieldData
            // via the TemplateKey mapping. These Ensure calls only provide a
            // fallback empty string if the student has no transferee field data.
            Ensure(replacements, "{{tf_from}}", "");
            Ensure(replacements, "{{tf_to}}", "");

            replacements["{{photo_2x2}}"] = "";
            replacements["{{signature_image}}"] = "";

            // 4. Load template
            var templatePath = Path.Combine(
                _environment.WebRootPath, "templates", "admission_form_template.docx");
            if (!File.Exists(templatePath))
                throw new FileNotFoundException($"Template not found: {templatePath}");

            byte[] templateBytes = await File.ReadAllBytesAsync(templatePath);

            // 5. Build image embed list
            // ─────────────────────────────────────────────────────────────
            // IMPORTANT — Rid must be plain "rId" + number.
            // Spire.Doc PDF renderer fails silently when the relationship Id
            // contains uppercase letters or non-numeric suffixes, producing
            // blank image boxes. "rId100" / "rId101" are always safe.
            // ─────────────────────────────────────────────────────────────
            var images = new List<ImageSlot>();

            if (photoPath != null && File.Exists(photoPath))
                images.Add(new ImageSlot(
                    TemplateKey: "{{photo_2x2}}",
                    Rid: "rId100",
                    PhysPath: photoPath,
                    Cx: 1_353_312L,   // 1.48 in
                    Cy: 1_316_736L)); // 1.44 in

            if (signaturePath != null && File.Exists(signaturePath))
                images.Add(new ImageSlot(
                    TemplateKey: "{{signature_image}}",
                    Rid: "rId101",
                    PhysPath: signaturePath,
                    Cx: 1_828_800L,   // 2.00 in
                    Cy: 685_800L));   // 0.75 in

            // 6. Generate document
            byte[] output = await BuildDocxAsync(templateBytes, replacements, images);

            // 7. Save .docx
            var outDir = Path.Combine(_environment.WebRootPath, "uploads", "admission_forms");
            Directory.CreateDirectory(outDir);
            var fileName = $"admission_{student.StudentNumber}_{now:yyyyMMddHHmmss}.docx";
            var docxPath = Path.Combine(outDir, fileName);
            await File.WriteAllBytesAsync(docxPath, output);

            return $"/uploads/admission_forms/{fileName}";
        }

        // ══════════════════════════════════════════════════════════════════
        // PUBLIC: Generate .docx then convert to PDF using FreeSpire.Doc
        // ══════════════════════════════════════════════════════════════════
        public async Task<string> GenerateAdmissionFormPdfAsync(int studentId)
        {
            // 1. Produce the .docx first
            var docxRelativeUrl = await GenerateAdmissionFormAsync(studentId);

            // 2. Resolve physical path
            var docxPhysPath = Path.Combine(
                _environment.WebRootPath,
                docxRelativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(docxPhysPath))
                throw new FileNotFoundException($"Generated DOCX not found: {docxPhysPath}");

            // 3. Output PDF path (same folder, same stem)
            var pdfFileName = Path.GetFileNameWithoutExtension(docxPhysPath) + ".pdf";
            var pdfPhysPath = Path.Combine(
                Path.GetDirectoryName(docxPhysPath)!, pdfFileName);

            // 4. Convert — must use LoadFromFile so Spire can resolve word/media/
            //    (loading from MemoryStream loses the base path and images go blank)
            await Task.Run(() =>
            {
                var spireDoc = new Document();
                try
                {
                    spireDoc.LoadFromFile(docxPhysPath);
                    spireDoc.SaveToFile(pdfPhysPath, FileFormat.PDF);
                }
                finally
                {
                    spireDoc.Close();
                    spireDoc.Dispose();
                }
            });

            // 5. Return relative URL
            var folder = Path.GetDirectoryName(docxRelativeUrl)!.Replace('\\', '/');
            return $"{folder}/{pdfFileName}";
        }

        // ─────────────────────────────────────────────────────────────────
        private record ImageSlot(
            string TemplateKey,
            string Rid,
            string PhysPath,
            long Cx,
            long Cy);

        // ══════════════════════════════════════════════════════════════════
        // Core: build the filled DOCX
        // ══════════════════════════════════════════════════════════════════
        private static async Task<byte[]> BuildDocxAsync(
            byte[] template,
            Dictionary<string, string> textRep,
            List<ImageSlot> images)
        {
            // ── Pre-load + resize image bytes ─────────────────────────────
            // ROOT CAUSE OF CORRUPT/CROPPED PHOTO IN PDF:
            // Spire.Doc ignores <a:stretch><a:fillRect/> during PDF conversion
            // and uses the image's NATIVE pixel dimensions instead of the EMU
            // size declared in the XML. If the uploaded photo is e.g. 800×1000px
            // but the slot is 141×137px, Spire renders 800×1000 overflowing the
            // box and the photo looks cropped or broken.
            //
            // FIX: Pre-resize every image to exactly the pixel dimensions that
            // correspond to the declared EMU slot BEFORE embedding it in the DOCX.
            // Formula: pixels = EMU ÷ 9525  (96 DPI, 914400 EMU per inch)
            // After resize the native pixel size == slot size → Spire renders
            // the image correctly in the PDF without any scaling needed.
            // ─────────────────────────────────────────────────────────────
            var imgBytes = new Dictionary<string, byte[]>();
            foreach (var img in images)
            {
                var rawBytes = await File.ReadAllBytesAsync(img.PhysPath);
                imgBytes[img.TemplateKey] = ResizeImageToSlot(rawBytes, img.Cx, img.Cy);
            }

            using var input = new MemoryStream(template);
            using var output = new MemoryStream();

            using (var inZip = new ZipArchive(input, ZipArchiveMode.Read))
            using (var outZip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var existingNames = inZip.Entries
                    .Select(e => e.FullName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var relsNeeded = new Dictionary<string, List<ImageSlot>>(
                    StringComparer.OrdinalIgnoreCase);

                // ── First pass: find which XML parts have image placeholders ─
                foreach (var entry in inZip.Entries)
                {
                    if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var s = entry.Open();
                    var raw = new StreamReader(s, Encoding.UTF8).ReadToEnd();
                    var merged = CollapseRunSplitPlaceholders(raw);

                    foreach (var img in images)
                    {
                        if (!merged.Contains(img.TemplateKey)) continue;
                        var rp = RelsPathFor(entry.FullName);
                        if (!relsNeeded.ContainsKey(rp))
                            relsNeeded[rp] = new List<ImageSlot>();
                        if (!relsNeeded[rp].Contains(img))
                            relsNeeded[rp].Add(img);
                    }
                }

                // ── Second pass: write each entry ─────────────────────────
                foreach (var entry in inZip.Entries)
                {
                    var outEntry = outZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    using var ins = entry.Open();
                    using var outs = outEntry.Open();

                    bool isXml = entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
                    bool isRels = entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

                    if (!isXml && !isRels) { ins.CopyTo(outs); continue; }

                    var raw = new StreamReader(ins, Encoding.UTF8).ReadToEnd();

                    // Patch .rels
                    if (isRels && relsNeeded.TryGetValue(entry.FullName, out var slots))
                        raw = AddImageRelationships(raw, slots);

                    // Patch [Content_Types].xml — always register jpg
                    // (ResizeImageToSlot always outputs JPEG bytes)
                    if (entry.FullName == "[Content_Types].xml" && images.Any())
                    {
                        if (!raw.Contains("image/jpeg"))
                            raw = raw.Replace("</Types>",
                                "<Default Extension=\"jpg\" ContentType=\"image/jpeg\"/></Types>");
                    }

                    // Process XML
                    if (isXml)
                    {
                        try
                        {
                            raw = CollapseRunSplitPlaceholders(raw);
                            var xdoc = XDocument.Parse(raw, LoadOptions.PreserveWhitespace);

                            foreach (var img in images)
                                InjectImageIntoXml(xdoc, img);

                            ApplyTextReplacements(xdoc, textRep);
                            ReplaceRemainingPlaceholders(xdoc);

                            raw = SerializeXDoc(xdoc);
                        }
                        catch (XmlException)
                        {
                            raw = FallbackTextReplace(raw, textRep);
                        }
                    }

                    var bytes = Encoding.UTF8.GetBytes(raw);
                    outs.Write(bytes, 0, bytes.Length);
                }

                // ── Create missing rels files ─────────────────────────────
                foreach (var kvp in relsNeeded)
                {
                    if (existingNames.Contains(kvp.Key)) continue;
                    var newRels = BuildNewRelsFile(kvp.Value);
                    var e = outZip.CreateEntry(kvp.Key, CompressionLevel.Optimal);
                    using var s = e.Open();
                    var b = Encoding.UTF8.GetBytes(newRels);
                    s.Write(b, 0, b.Length);
                }

                // ── Add media files ───────────────────────────────────────
                // Always stored as .jpg because ResizeImageToSlot outputs JPEG.
                foreach (var img in images)
                {
                    var mediaPath = $"word/media/{img.Rid.ToLower()}.jpg";
                    var pe = outZip.CreateEntry(mediaPath, CompressionLevel.NoCompression);
                    using var ps = pe.Open();
                    ps.Write(imgBytes[img.TemplateKey], 0, imgBytes[img.TemplateKey].Length);
                }
            }

            return output.ToArray();
        }

        // ══════════════════════════════════════════════════════════════════
        // Resize image bytes to the exact pixel dimensions of the EMU slot.
        //
        // Why this fixes the PDF:
        //   Spire.Doc PDF renderer ignores <a:stretch><a:fillRect/> and
        //   renders the image at its native pixel size. Pre-resizing makes
        //   native pixel size == declared slot size, so no scaling is needed
        //   and the photo fits the box perfectly.
        //
        // Formula:  pixels = EMU ÷ 9525   (at 96 DPI, 1 inch = 914400 EMU)
        // Output:   always JPEG for maximum Spire compatibility.
        // ══════════════════════════════════════════════════════════════════
        private static byte[] ResizeImageToSlot(byte[] imageBytes, long cxEmu, long cyEmu)
        {
            int targetW = (int)Math.Round(cxEmu / 9525.0);
            int targetH = (int)Math.Round(cyEmu / 9525.0);

            try
            {
                using var ms = new MemoryStream(imageBytes);
                using var src = System.Drawing.Image.FromStream(ms);

                using var bmp = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);

                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                // Fill white background first (JPEG has no transparency)
                g.Clear(Color.White);
                g.DrawImage(src, 0, 0, targetW, targetH);

                // Save as JPEG — ImageFormat.Jpeg avoids all Encoder/EncoderParameter
                // ambiguity issues between System.Drawing.Imaging and System.Text.
                using var outMs = new MemoryStream();
                bmp.Save(outMs, ImageFormat.Jpeg);
                return outMs.ToArray();
            }
            catch
            {
                // Resize failed — return original bytes unchanged
                return imageBytes;
            }
        }


        // ══════════════════════════════════════════════════════════════════
        // XDocument-based image injection
        // ══════════════════════════════════════════════════════════════════
        private static void InjectImageIntoXml(XDocument xdoc, ImageSlot img)
        {
            var targets = xdoc.Descendants(W + "t")
                .Where(t => t.Value.Trim() == img.TemplateKey)
                .ToList();

            foreach (var wt in targets)
            {
                var wr = wt.Ancestors(W + "r").FirstOrDefault();
                if (wr == null) continue;
                wr.ReplaceWith(BuildDrawingRun(img));
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Build a valid <w:r><w:drawing>…</w:drawing></w:r> XElement
        // ══════════════════════════════════════════════════════════════════
        private static XElement BuildDrawingRun(ImageSlot img)
        {
            var docId = Math.Abs(img.Rid.GetHashCode()) % 0x7FFF + 1;

            return new XElement(W + "r",
                new XElement(W + "drawing",
                    new XElement(WP + "inline",
                        new XAttribute("distT", 0),
                        new XAttribute("distB", 0),
                        new XAttribute("distL", 0),
                        new XAttribute("distR", 0),
                        new XElement(WP + "extent",
                            new XAttribute("cx", img.Cx),
                            new XAttribute("cy", img.Cy)),
                        new XElement(WP + "effectExtent",
                            new XAttribute("l", 0),
                            new XAttribute("t", 0),
                            new XAttribute("r", 0),
                            new XAttribute("b", 0)),
                        new XElement(WP + "docPr",
                            new XAttribute("id", docId),
                            new XAttribute("name", img.Rid)),
                        new XElement(WP + "cNvGraphicFramePr"),
                        new XElement(A + "graphic",
                            new XElement(A + "graphicData",
                                new XAttribute("uri",
                                    "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                                new XElement(PIC + "pic",
                                    new XElement(PIC + "nvPicPr",
                                        new XElement(PIC + "cNvPr",
                                            new XAttribute("id", docId),
                                            new XAttribute("name", img.Rid)),
                                        new XElement(PIC + "cNvPicPr")),
                                    new XElement(PIC + "blipFill",
                                        new XElement(A + "blip",
                                            new XAttribute(R + "embed", img.Rid)),
                                        new XElement(A + "stretch",
                                            new XElement(A + "fillRect"))),
                                    new XElement(PIC + "spPr",
                                        new XElement(A + "xfrm",
                                            new XElement(A + "off",
                                                new XAttribute("x", 0),
                                                new XAttribute("y", 0)),
                                            new XElement(A + "ext",
                                                new XAttribute("cx", img.Cx),
                                                new XAttribute("cy", img.Cy))),
                                        new XElement(A + "prstGeom",
                                            new XAttribute("prst", "rect"),
                                            new XElement(A + "avLst")))))))));
        }

        // ══════════════════════════════════════════════════════════════════
        // Replace {{placeholder}} text in all <w:t> elements
        // ══════════════════════════════════════════════════════════════════
        private static void ApplyTextReplacements(
            XDocument xdoc, Dictionary<string, string> rep)
        {
            foreach (var wt in xdoc.Descendants(W + "t").ToList())
            {
                var original = wt.Value;
                var replaced = original;
                foreach (var kv in rep)
                    replaced = replaced.Replace(kv.Key, kv.Value);
                if (replaced != original)
                    wt.Value = replaced;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Replace any remaining {{...}} with N/A
        // ══════════════════════════════════════════════════════════════════
        private static void ReplaceRemainingPlaceholders(XDocument xdoc)
        {
            foreach (var wt in xdoc.Descendants(W + "t").ToList())
            {
                if (wt.Value.Contains("{{"))
                    wt.Value = Regex.Replace(wt.Value, @"\{\{[^}]+\}\}", "N/A");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Collapse Word-split {{placeholders}} across <w:r> run boundaries
        // ══════════════════════════════════════════════════════════════════
        private static string CollapseRunSplitPlaceholders(string xml)
        {
            return Regex.Replace(
                xml,
                @"\{\{((?:[^{}]|<[^>]+>)*?)\}\}",
                m =>
                {
                    var key = Regex.Replace(m.Groups[1].Value, @"<[^>]+>", "").Trim();
                    return string.IsNullOrEmpty(key) ? m.Value : $"{{{{{key}}}}}";
                },
                RegexOptions.Singleline);
        }

        // ══════════════════════════════════════════════════════════════════
        // Patch a .rels file to add image relationships
        // Always targets .jpg because ResizeImageToSlot outputs JPEG.
        // ══════════════════════════════════════════════════════════════════
        private static string AddImageRelationships(
            string relsXml, List<ImageSlot> slots)
        {
            try
            {
                var xdoc = XDocument.Parse(relsXml);
                var root = xdoc.Root!;

                foreach (var slot in slots)
                {
                    bool exists = root.Elements(REL + "Relationship")
                        .Any(e => (string?)e.Attribute("Id") == slot.Rid);
                    if (exists) continue;

                    root.Add(new XElement(REL + "Relationship",
                        new XAttribute("Id", slot.Rid),
                        new XAttribute("Type",
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                        new XAttribute("Target",
                            $"media/{slot.Rid.ToLower()}.jpg")));
                }

                return SerializeXDoc(xdoc);
            }
            catch
            {
                var sb = new StringBuilder(relsXml);
                foreach (var slot in slots)
                {
                    if (relsXml.Contains(slot.Rid)) continue;
                    sb.Replace("</Relationships>",
                        $"<Relationship Id=\"{slot.Rid}\" " +
                        $"Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" " +
                        $"Target=\"media/{slot.Rid.ToLower()}.jpg\"/>" +
                        "</Relationships>");
                }
                return sb.ToString();
            }
        }

        private static string BuildNewRelsFile(List<ImageSlot> slots)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            foreach (var slot in slots)
            {
                sb.Append(
                    $"<Relationship Id=\"{slot.Rid}\" " +
                    $"Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" " +
                    $"Target=\"media/{slot.Rid.ToLower()}.jpg\"/>");
            }
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string FallbackTextReplace(
            string xml, Dictionary<string, string> rep)
        {
            foreach (var kv in rep)
                xml = xml.Replace(kv.Key, XmlEscape(kv.Value));
            xml = Regex.Replace(xml, @"\{\{[^}]+\}\}", "N/A");
            return xml;
        }

        // ══════════════════════════════════════════════════════════════════
        // Serialize XDocument back to string preserving XML declaration
        // ══════════════════════════════════════════════════════════════════
        private static string SerializeXDoc(XDocument xdoc)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
                Indent = false,
            };
            using var ms = new MemoryStream();
            using (var writer = XmlWriter.Create(ms, settings))
                xdoc.WriteTo(writer);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private static string RelsPathFor(string xmlPath)
        {
            var slash = xmlPath.LastIndexOf('/');
            return slash >= 0
                ? $"{xmlPath[..slash]}/_rels/{xmlPath[(slash + 1)..]}.rels"
                : $"_rels/{xmlPath}.rels";
        }

        private string ToPhysicalPath(string filePath) =>
            Path.Combine(
                _environment.WebRootPath,
                filePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        private static string FormatDateValue(string value) =>
            DateTime.TryParse(value, out var d) ? d.ToString("MMMM d, yyyy") : value;

        private static void Ensure(Dictionary<string, string> d, string key, string val)
        {
            if (!d.ContainsKey(key)) d[key] = val;
        }

        private static string XmlEscape(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
        }
    }
}