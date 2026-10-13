using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PbiBench.Core.Project;

namespace PbiBench.Core.Report
{
    public sealed class ThemeApplyRefusedException : InvalidOperationException
    {
        public ThemeApplyRefusedException(string message) : base(message) { }
    }

    /// <summary>
    /// Reviewed custom-theme apply for a writable PBIR report, expressed as a <see cref="ReviewedFilePlan"/>:
    /// write StaticResources/RegisteredResources/&lt;theme&gt;.json, point report.json themeCollection.customTheme
    /// and its RegisteredResources item at it, and delete the previously registered custom theme file. report.json is
    /// spliced: only the themeCollection and resourcePackages values change, every other byte is kept. The
    /// report.json before-hash is the one read at index time, so any edit since then makes Apply refuse
    /// (stale). Restore goes through the plan journal and brings back the exact original bytes.
    /// </summary>
    public sealed class ThemeApplyPlan
    {
        public const int MaxThemeBytes = 1024 * 1024;

        private ThemeApplyPlan(ReviewedFilePlan plan) { Plan = plan; }

        public ReviewedFilePlan Plan { get; }
        public string ThemeName { get; private set; } = "";
        public string ThemeFileName { get; private set; } = "";
        public string? RemovedThemePath { get; private set; }
        public string BeforeSnippet { get; private set; } = "";
        public string AfterSnippet { get; private set; } = "";
        public string NewReportJson { get; private set; } = "";

        /// <summary>Builds the plan; throws <see cref="ThemeApplyRefusedException"/> when the report is read-only or the theme is invalid.</summary>
        public static ThemeApplyPlan Create(PbirReportIndex index, byte[] themeBytes)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (!index.IsWritable) throw new ThemeApplyRefusedException("Report is read-only: " + index.ReadOnlyReason);
            var report = index.ReportJsonRoot;
            var text = index.ReportJsonText;
            if (report == null || text == null || index.ReportJsonHash == null) throw new ThemeApplyRefusedException("report.json was not indexed.");
            var collection = report.Get("themeCollection");
            if (collection == null || !collection.IsObject) throw new ThemeApplyRefusedException("report.json has no themeCollection object.");
            var packages = report.Get("resourcePackages");
            if (packages != null && !packages.IsArray) throw new ThemeApplyRefusedException("report.json resourcePackages is not an array.");

            if (themeBytes == null || themeBytes.Length == 0) throw new ThemeApplyRefusedException("Theme file is empty.");
            if (themeBytes.Length > MaxThemeBytes) throw new ThemeApplyRefusedException("Theme file is larger than " + MaxThemeBytes + " bytes.");
            PbirJsonNode theme;
            try { theme = PbirJson.Parse(PbirJson.DecodeUtf8(themeBytes, out _)); }
            catch (Exception e) when (e is FormatException || e is ArgumentException) { throw new ThemeApplyRefusedException("Theme file is not valid UTF-8 JSON: " + e.Message); }
            var themeName = theme.GetString("name");
            if (!theme.IsObject || string.IsNullOrWhiteSpace(themeName)) throw new ThemeApplyRefusedException("Theme JSON must be an object with a non-empty \"name\".");

            var fileName = FileNameFor(themeName!);
            var themePath = PbirReportIndex.RegisteredResourcesFolder + "/" + fileName;
            var oldName = index.Theme.CustomThemeName;
            // The derived file name must not collide with another registered resource (an image, a different resource
            // type): replacing it would destroy that file and leave two items with the same name.
            foreach (var package in packages?.Items ?? Array.Empty<PbirJsonNode>())
            {
                if (package.GetString("type") != "RegisteredResources") continue;
                foreach (var item in package.Get("items")?.Items ?? Array.Empty<PbirJsonNode>())
                {
                    var type = item.GetString("type");
                    if (type == "CustomTheme" || (oldName != null && item.GetString("name") == oldName)) continue;
                    if (string.Equals(item.GetString("name"), fileName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.GetString("path"), fileName, StringComparison.OrdinalIgnoreCase))
                        throw new ThemeApplyRefusedException("The theme file name '" + fileName + "' (from the theme name '" + themeName +
                            "') is already used by a registered resource of type '" + (type ?? "unknown") + "'. Rename the theme (its \"name\") and try again.");
                }
            }
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var unit = DetectIndentUnit(text);
            var memberIndent = IndentOf(text, report.PropertyNameStarts.Count > 0 ? report.PropertyNameStarts[0] : 0);

            // themeCollection: keep members in order, replace (or append) customTheme.
            var oldCustom = collection.Get("customTheme");
            var importVersion = oldCustom?.Get("reportVersionAtImport") ?? collection.Get("baseTheme")?.Get("reportVersionAtImport");
            var custom = new List<KeyValuePair<string, string>> { Member("name", PbirJson.Quote(fileName)) };
            if (importVersion != null) custom.Add(Member("reportVersionAtImport", importVersion.Raw(text)));
            custom.Add(Member("type", PbirJson.Quote("RegisteredResources")));
            var collectionIndent = memberIndent + unit;
            var customText = WriteObject(custom, collectionIndent, unit, newline);
            var collectionMembers = collection.Properties.Select(p => Member(p.Key, p.Key == "customTheme" ? customText : p.Value.Raw(text))).ToList();
            if (oldCustom == null) collectionMembers.Add(Member("customTheme", customText));
            var newCollection = WriteObject(collectionMembers, memberIndent, unit, newline);

            // resourcePackages: replace custom-theme items inside RegisteredResources (or add that package).
            var itemIndent = memberIndent + unit + unit + unit;
            var newItem = WriteObject(new List<KeyValuePair<string, string>> {
                Member("name", PbirJson.Quote(fileName)), Member("path", PbirJson.Quote(fileName)), Member("type", PbirJson.Quote("CustomTheme")) },
                itemIndent, unit, newline);
            string? removedPath = null;
            var packageTexts = new List<string>();
            var registeredSeen = false;
            foreach (var package in packages?.Items ?? Array.Empty<PbirJsonNode>())
            {
                if (package.GetString("type") != "RegisteredResources" || registeredSeen) { packageTexts.Add(package.Raw(text)); continue; }
                registeredSeen = true;
                var items = new List<string>();
                foreach (var item in package.Get("items")?.Items ?? Array.Empty<PbirJsonNode>())
                {
                    var isOldTheme = item.GetString("type") == "CustomTheme" || (oldName != null && item.GetString("name") == oldName);
                    if (!isOldTheme) { items.Add(item.Raw(text)); continue; }
                    var path = item.GetString("path");
                    if (path != null && !string.Equals(path, fileName, StringComparison.OrdinalIgnoreCase)) removedPath ??= PbirReportIndex.RegisteredResourcesFolder + "/" + path;
                }
                items.Add(newItem);
                var itemsText = WriteArray(items, memberIndent + unit + unit, unit, newline);
                var packageMembers = package.Properties.Select(p => Member(p.Key, p.Key == "items" ? itemsText : p.Value.Raw(text))).ToList();
                if (package.Get("items") == null) packageMembers.Add(Member("items", itemsText));
                packageTexts.Add(WriteObject(packageMembers, memberIndent + unit, unit, newline));
            }
            if (!registeredSeen)
                packageTexts.Add(WriteObject(new List<KeyValuePair<string, string>> {
                    Member("name", PbirJson.Quote("RegisteredResources")), Member("type", PbirJson.Quote("RegisteredResources")),
                    Member("items", WriteArray(new List<string> { newItem }, memberIndent + unit + unit, unit, newline)) }, memberIndent + unit, unit, newline));
            var newPackages = WriteArray(packageTexts, memberIndent, unit, newline);

            // Splice from the end so earlier offsets stay valid.
            var edits = new List<(int start, int end, string value)> { (collection.Start, collection.End, newCollection) };
            if (packages != null) edits.Add((packages.Start, packages.End, newPackages));
            else edits.Add((collection.End, collection.End, "," + newline + memberIndent + PbirJson.Quote("resourcePackages") + ": " + newPackages));
            var sb = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.start)) { sb.Remove(edit.start, edit.end - edit.start); sb.Insert(edit.start, edit.value); }
            var newText = sb.ToString();
            PbirJson.Parse(newText); // never write JSON this build cannot read back

            var plan = new ReviewedFilePlan(index.ReportFolder, "Apply report theme '" + themeName + "'");
            plan.Add(new ReviewedFileChange(PbirReportIndex.ReportJson, ReviewedFileChangeKind.Replace, index.ReportJsonHash, PbirJson.EncodeUtf8(newText, index.ReportJsonHadBom)));
            plan.AddFromCurrent(themePath, themeBytes);
            if (removedPath != null)
            {
                var full = ReviewedFilePlan.ResolveTarget(index.ReportFolder, removedPath);
                if (File.Exists(full)) plan.Add(new ReviewedFileChange(removedPath, ReviewedFileChangeKind.Delete, ReviewedFilePlan.HashFile(full), null));
                else removedPath = null;
            }
            var after = PbirJson.Parse(newText).Get("themeCollection")!;
            return new ThemeApplyPlan(plan) {
                ThemeName = themeName!, ThemeFileName = fileName, RemovedThemePath = removedPath,
                BeforeSnippet = collection.Raw(text), AfterSnippet = after.Raw(newText), NewReportJson = newText
            };
        }

        /// <summary>Theme file name derived from the theme's name: letters, digits, '-', '_' only, at most 64 chars, plus .json.</summary>
        public static string FileNameFor(string themeName)
        {
            var sb = new StringBuilder();
            foreach (var c in themeName.Trim()) if (sb.Length < 64) sb.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_');
            var stem = sb.ToString().Trim('_');
            return (stem.Length == 0 ? "CustomTheme" : stem) + ".json";
        }

        private static KeyValuePair<string, string> Member(string name, string raw) => new KeyValuePair<string, string>(name, raw);

        private static string WriteObject(IList<KeyValuePair<string, string>> members, string indent, string unit, string newline)
        {
            if (members.Count == 0) return "{}";
            var sb = new StringBuilder("{").Append(newline);
            for (var i = 0; i < members.Count; i++)
                sb.Append(indent).Append(unit).Append(PbirJson.Quote(members[i].Key)).Append(": ").Append(members[i].Value).Append(i < members.Count - 1 ? "," : "").Append(newline);
            return sb.Append(indent).Append('}').ToString();
        }

        private static string WriteArray(IList<string> items, string indent, string unit, string newline)
        {
            if (items.Count == 0) return "[]";
            var sb = new StringBuilder("[").Append(newline);
            for (var i = 0; i < items.Count; i++) sb.Append(indent).Append(unit).Append(items[i]).Append(i < items.Count - 1 ? "," : "").Append(newline);
            return sb.Append(indent).Append(']').ToString();
        }

        private static string IndentOf(string text, int position)
        {
            var lineStart = position;
            while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;
            var end = lineStart;
            while (end < position && (text[end] == ' ' || text[end] == '\t')) end++;
            return text.Substring(lineStart, end - lineStart);
        }

        private static string DetectIndentUnit(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("\t", StringComparison.Ordinal)) return "\t";
                var spaces = line.Length - line.TrimStart(' ').Length;
                if (spaces > 0 && spaces <= 8 && line.Trim().Length > 0) return new string(' ', spaces);
            }
            return "  ";
        }
    }
}
