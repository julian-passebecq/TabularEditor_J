using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PbiBench.Core.Project;

namespace PbiBench.Core.Report
{
    public enum PbirReportFormat { Unknown, Pbir, PbirLegacy }

    public sealed class PbirIndexLimits
    {
        public int MaxPages { get; set; } = 500;
        public int MaxVisualsPerPage { get; set; } = 1000;
        public int MaxVisuals { get; set; } = 10000;
        public int MaxFieldReferences { get; set; } = 200000;
        public long MaxFileBytes { get; set; } = 8L * 1024 * 1024;
        public long MaxTotalBytes { get; set; } = 256L * 1024 * 1024;
    }

    public sealed class PbirVisual
    {
        public string Name { get; set; } = "";
        public string VisualType { get; set; } = "";
        public bool IsGroup { get; set; }
        public string File { get; set; } = "";
        public PbirSchemaInfo Schema { get; set; } = new PbirSchemaInfo();
        public IReadOnlyList<PbirFieldReference> Fields { get; set; } = Array.Empty<PbirFieldReference>();
    }

    public sealed class PbirPage
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int Ordinal { get; set; }
        public string File { get; set; } = "";
        public PbirSchemaInfo Schema { get; set; } = new PbirSchemaInfo();
        public IReadOnlyList<PbirVisual> Visuals { get; set; } = Array.Empty<PbirVisual>();
        public IReadOnlyList<PbirFieldReference> Filters { get; set; } = Array.Empty<PbirFieldReference>();
    }

    /// <summary>Theme state read from report.json (themeCollection + resourcePackages).</summary>
    public sealed class PbirThemeInfo
    {
        public string? BaseThemeName { get; set; }
        public string? CustomThemeName { get; set; }
        public string? CustomThemeType { get; set; }
        /// <summary>Report-folder-relative path of the registered custom theme file, when registered.</summary>
        public string? CustomThemePath { get; set; }
        public bool CustomThemeFileExists { get; set; }
    }

    /// <summary>
    /// Bounded, read-only index of a PBIR report folder (X.Report): definition.pbir, definition/version.json,
    /// report.json, pages/pages.json, pages/*/page.json and pages/*/visuals/*/visual.json. Malformed or
    /// oversized files become diagnostics; indexing continues. Unknown schemas are indexed best-effort
    /// and make the report read-only.
    /// </summary>
    public sealed class PbirReportIndex
    {
        public const string ReportJson = "definition/report.json";
        public const string RegisteredResourcesFolder = "StaticResources/RegisteredResources";

        public string ReportFolder { get; private set; } = "";
        public PbirReportFormat Format { get; private set; }
        public string? FormatVersion { get; private set; }
        public string? SemanticModelPath { get; private set; }
        public string? SemanticModelConnection { get; private set; }
        public IReadOnlyList<PbirSchemaInfo> Schemas { get; private set; } = Array.Empty<PbirSchemaInfo>();
        public PbirSchemaInfo ReportSchema { get; private set; } = new PbirSchemaInfo();
        public IReadOnlyList<PbirPage> Pages { get; private set; } = Array.Empty<PbirPage>();
        public IReadOnlyList<PbirFieldReference> ReportFilters { get; private set; } = Array.Empty<PbirFieldReference>();
        public PbirThemeInfo Theme { get; private set; } = new PbirThemeInfo();
        public IReadOnlyList<string> Diagnostics { get; private set; } = Array.Empty<string>();
        public bool Truncated { get; private set; }
        /// <summary>Exact report.json text and bytes hash at index time (stale guard for reviewed writes).</summary>
        public string? ReportJsonText { get; private set; }
        public string? ReportJsonHash { get; private set; }
        public bool ReportJsonHadBom { get; private set; }
        public PbirJsonNode? ReportJsonRoot { get; private set; }
        public string? ReadOnlyReason { get; private set; }
        public bool IsWritable => ReadOnlyReason == null;

        public int VisualCount => Pages.Sum(p => p.Visuals.Count);
        public IEnumerable<PbirFieldReference> AllFields =>
            ReportFilters.Concat(Pages.SelectMany(p => p.Filters.Concat(p.Visuals.SelectMany(v => v.Fields))));

        /// <summary>Finds X.Report folders whose definition.pbir points (byPath) at the given semantic model folder.</summary>
        public static IReadOnlyList<string> FindReportsFor(string semanticModelFolder, int maxFolders = 64)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(semanticModelFolder) || !Directory.Exists(semanticModelFolder)) return result;
            var model = ReviewedFilePlan.NormalizeRoot(semanticModelFolder);
            var parent = Path.GetDirectoryName(model);
            if (parent == null) return result;
            foreach (var folder in Directory.EnumerateDirectories(parent, "*.Report").Take(maxFolders))
            {
                try
                {
                    var pbir = Path.Combine(folder, "definition.pbir");
                    if (!File.Exists(pbir) || new FileInfo(pbir).Length > 1024 * 1024) continue;
                    var root = PbirJson.Parse(PbirJson.DecodeUtf8(File.ReadAllBytes(pbir), out _));
                    var path = root.Path("datasetReference", "byPath")?.GetString("path");
                    if (path == null) continue;
                    var target = ReviewedFilePlan.NormalizeRoot(Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar)));
                    if (string.Equals(target, model, StringComparison.OrdinalIgnoreCase)) result.Add(folder);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException || e is ArgumentException) { }
            }
            return result;
        }

        public static PbirReportIndex Load(string reportFolder, PbirIndexLimits? limits = null)
        {
            limits ??= new PbirIndexLimits();
            if (string.IsNullOrWhiteSpace(reportFolder)) throw new ArgumentException("A report folder is required.", nameof(reportFolder));
            var index = new PbirReportIndex { ReportFolder = ReviewedFilePlan.NormalizeRoot(reportFolder) };
            new Loader(index, limits).Run();
            return index;
        }

        private sealed class Loader
        {
            private readonly PbirReportIndex _index;
            private readonly PbirIndexLimits _limits;
            private readonly List<string> _diagnostics = new List<string>();
            private readonly List<PbirSchemaInfo> _schemas = new List<PbirSchemaInfo>();
            private long _bytes;
            private int _fields;
            private int _visuals;

            public Loader(PbirReportIndex index, PbirIndexLimits limits) { _index = index; _limits = limits; }

            private string Full(string relative) => Path.Combine(_index.ReportFolder, relative.Replace('/', Path.DirectorySeparatorChar));

            private bool TryRead(string relative, out PbirJsonNode? root, out string? text, out byte[]? bytes, out bool bom)
            {
                root = null; text = null; bytes = null; bom = false;
                var full = Full(relative);
                if (!File.Exists(full)) return false;
                try
                {
                    var length = new FileInfo(full).Length;
                    if (length > _limits.MaxFileBytes) { _diagnostics.Add(relative + ": skipped, larger than " + _limits.MaxFileBytes + " bytes."); return false; }
                    if (_bytes + length > _limits.MaxTotalBytes) { _diagnostics.Add(relative + ": skipped, index byte budget reached."); _index.Truncated = true; return false; }
                    _bytes += length;
                    bytes = File.ReadAllBytes(full);
                    text = PbirJson.DecodeUtf8(bytes, out bom);
                    root = PbirJson.Parse(text);
                    return true;
                }
                catch (FormatException e) { _diagnostics.Add(relative + ": not valid JSON (" + e.Message + ")."); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { _diagnostics.Add(relative + ": could not be read (" + e.Message + ")."); }
                root = null; return false;
            }

            private PbirSchemaInfo Schema(string relative, PbirJsonNode? root)
            {
                var info = PbirSchemaRegistry.Describe(relative, root?.GetString("$schema"));
                _schemas.Add(info);
                if (!info.IsKnown) _diagnostics.Add(relative + ": schema " + info.Describe() + " is not in this build's registry; indexed best-effort, report is read-only.");
                return info;
            }

            private void ReadOnly(string reason) { if (_index.ReadOnlyReason == null) _index.ReadOnlyReason = reason; }

            public void Run()
            {
                var folder = _index.ReportFolder;
                if (!Directory.Exists(folder)) { _index.Format = PbirReportFormat.Unknown; ReadOnly("Report folder does not exist."); Finish(); return; }

                if (TryRead("definition.pbir", out var pbir, out _, out _, out _) && pbir != null)
                {
                    Schema("definition.pbir", pbir);
                    var reference = pbir.Get("datasetReference");
                    _index.SemanticModelPath = reference?.Get("byPath")?.GetString("path");
                    var connection = reference?.Get("byConnection");
                    if (connection != null) _index.SemanticModelConnection = connection.GetString("connectionString") != null ? "(connection string not shown)" : "byConnection";
                }
                else _diagnostics.Add("definition.pbir missing or unreadable: semantic model link unknown.");

                if (!Directory.Exists(Full("definition")))
                {
                    if (File.Exists(Full("report.json")))
                    {
                        _index.Format = PbirReportFormat.PbirLegacy;
                        ReadOnly("PBIR-Legacy report (single report.json): indexing and writes need the PBIR folder format.");
                        _diagnostics.Add("report.json (PBIR-Legacy) is not indexed by this build.");
                    }
                    else { _index.Format = PbirReportFormat.Unknown; ReadOnly("No definition folder: not a PBIR report."); }
                    Finish();
                    return;
                }
                _index.Format = PbirReportFormat.Pbir;

                if (TryRead("definition/version.json", out var version, out _, out _, out _) && version != null)
                {
                    Schema("definition/version.json", version);
                    _index.FormatVersion = version.GetString("version");
                }
                if (_index.FormatVersion == null) ReadOnly("definition/version.json missing or without a version.");
                else if (!PbirSchemaRegistry.KnownFormatVersions.Contains(_index.FormatVersion)) ReadOnly("PBIR format version " + _index.FormatVersion + " is unknown to this build.");

                if (TryRead(ReportJson, out var report, out var reportText, out var reportBytes, out var bom) && report != null)
                {
                    _index.ReportSchema = Schema(ReportJson, report);
                    _index.ReportJsonText = reportText;
                    _index.ReportJsonHash = ReviewedFilePlan.Hash(reportBytes!);
                    _index.ReportJsonHadBom = bom;
                    _index.ReportJsonRoot = report;
                    var filters = new List<PbirFieldReference>();
                    var filterConfig = report.Get("filterConfig");
                    if (filterConfig != null) AddFilters(filterConfig, PbirFieldUsage.ReportFilter, "", "", ReportJson, filters);
                    _index.ReportFilters = filters;
                    ReadTheme(report);
                    if (!_index.ReportSchema.IsWritable) ReadOnly("report.json schema " + _index.ReportSchema.Describe() + " is not a writable version in this build.");
                }
                else ReadOnly("definition/report.json missing or unreadable.");

                ReadPages();
                if (_schemas.Any(s => !s.IsKnown)) ReadOnly("At least one file uses a schema unknown to this build (see diagnostics).");
                Finish();
            }

            private void ReadTheme(PbirJsonNode report)
            {
                var theme = new PbirThemeInfo();
                var collection = report.Get("themeCollection");
                theme.BaseThemeName = collection?.Get("baseTheme")?.GetString("name");
                var custom = collection?.Get("customTheme");
                theme.CustomThemeName = custom?.GetString("name");
                theme.CustomThemeType = custom?.GetString("type");
                if (theme.CustomThemeName != null)
                {
                    var packages = report.Get("resourcePackages");
                    foreach (var package in packages?.Items ?? Array.Empty<PbirJsonNode>())
                    {
                        if (package.GetString("type") != "RegisteredResources") continue;
                        foreach (var item in package.Get("items")?.Items ?? Array.Empty<PbirJsonNode>())
                            if (item.GetString("name") == theme.CustomThemeName && item.GetString("path") != null)
                                theme.CustomThemePath = RegisteredResourcesFolder + "/" + item.GetString("path");
                    }
                    if (theme.CustomThemePath != null)
                    {
                        try { theme.CustomThemeFileExists = File.Exists(ReviewedFilePlan.ResolveTarget(_index.ReportFolder, theme.CustomThemePath)); }
                        catch (ArgumentException) { _diagnostics.Add("Custom theme path is not a plain relative path: " + theme.CustomThemePath); theme.CustomThemePath = null; }
                    }
                }
                if (collection == null) ReadOnly("report.json has no themeCollection.");
                _index.Theme = theme;
            }

            private void AddFilters(PbirJsonNode filterConfig, PbirFieldUsage usage, string page, string visual, string file, List<PbirFieldReference> into)
            {
                foreach (var filter in filterConfig.Get("filters")?.Items ?? Array.Empty<PbirJsonNode>())
                {
                    var name = filter.GetString("displayName") ?? filter.GetString("name") ?? "";
                    var field = filter.Get("field");
                    if (field != null) Collect(field, usage, name, page, visual, file, into);
                }
            }

            private void Collect(PbirJsonNode node, PbirFieldUsage usage, string role, string page, string visual, string file, List<PbirFieldReference> into)
            {
                PbirFieldExtractor.Extract(node, usage, role, r =>
                {
                    if (_fields >= _limits.MaxFieldReferences) { _index.Truncated = true; return; }
                    _fields++;
                    r.Page = page; r.Visual = visual; r.File = file;
                    into.Add(r);
                });
            }

            private void ReadPages()
            {
                var pagesFolder = Full("definition/pages");
                if (!Directory.Exists(pagesFolder)) { _diagnostics.Add("definition/pages missing: no pages indexed."); return; }
                var order = new List<string>();
                if (TryRead("definition/pages/pages.json", out var meta, out _, out _, out _) && meta != null)
                {
                    Schema("definition/pages/pages.json", meta);
                    foreach (var name in meta.Get("pageOrder")?.Items ?? Array.Empty<PbirJsonNode>()) if (name.IsString) order.Add(name.Value!);
                }
                var folders = Directory.EnumerateDirectories(pagesFolder).Select(Path.GetFileName).Where(n => n != null).Select(n => n!)
                    .OrderBy(n => { var i = order.IndexOf(n); return i < 0 ? int.MaxValue : i; }).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                if (folders.Count > _limits.MaxPages) { _diagnostics.Add("Only the first " + _limits.MaxPages + " of " + folders.Count + " pages are indexed."); _index.Truncated = true; folders = folders.Take(_limits.MaxPages).ToList(); }
                var pages = new List<PbirPage>();
                foreach (var folderName in folders)
                {
                    var pageFile = "definition/pages/" + folderName + "/page.json";
                    if (!TryRead(pageFile, out var pageRoot, out _, out _, out _) || pageRoot == null) continue;
                    var page = new PbirPage {
                        Name = pageRoot.GetString("name") ?? folderName, DisplayName = pageRoot.GetString("displayName") ?? folderName,
                        Ordinal = pages.Count, File = pageFile, Schema = Schema(pageFile, pageRoot)
                    };
                    var filters = new List<PbirFieldReference>();
                    var filterConfig = pageRoot.Get("filterConfig");
                    if (filterConfig != null) AddFilters(filterConfig, PbirFieldUsage.PageFilter, page.DisplayName, "", pageFile, filters);
                    page.Filters = filters;
                    page.Visuals = ReadVisuals(folderName, page.DisplayName);
                    pages.Add(page);
                }
                _index.Pages = pages;
            }

            private IReadOnlyList<PbirVisual> ReadVisuals(string pageFolder, string pageName)
            {
                var visualsFolder = Full("definition/pages/" + pageFolder + "/visuals");
                if (!Directory.Exists(visualsFolder)) return Array.Empty<PbirVisual>();
                var folders = Directory.EnumerateDirectories(visualsFolder).Select(Path.GetFileName).Where(n => n != null).Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                if (folders.Count > _limits.MaxVisualsPerPage)
                { _diagnostics.Add(pageName + ": only the first " + _limits.MaxVisualsPerPage + " of " + folders.Count + " visuals are indexed."); _index.Truncated = true; folders = folders.Take(_limits.MaxVisualsPerPage).ToList(); }
                var visuals = new List<PbirVisual>();
                foreach (var folderName in folders)
                {
                    if (_visuals >= _limits.MaxVisuals) { _diagnostics.Add("Visual limit " + _limits.MaxVisuals + " reached; remaining visuals not indexed."); _index.Truncated = true; break; }
                    var file = "definition/pages/" + pageFolder + "/visuals/" + folderName + "/visual.json";
                    if (!TryRead(file, out var root, out _, out _, out _) || root == null) continue;
                    _visuals++;
                    var visualNode = root.Get("visual");
                    var visual = new PbirVisual {
                        Name = root.GetString("name") ?? folderName, File = file, Schema = Schema(file, root),
                        VisualType = visualNode?.GetString("visualType") ?? (root.Get("visualGroup") != null ? "group" : ""),
                        IsGroup = visualNode == null && root.Get("visualGroup") != null
                    };
                    var fields = new List<PbirFieldReference>();
                    if (visualNode != null)
                    {
                        foreach (var role in visualNode.Path("query", "queryState")?.Properties ?? Array.Empty<KeyValuePair<string, PbirJsonNode>>())
                            Collect(role.Value, PbirFieldUsage.Projection, role.Key, pageName, visual.Name, file, fields);
                        var sort = visualNode.Path("query", "sortDefinition");
                        if (sort != null) Collect(sort, PbirFieldUsage.Sort, "sort", pageName, visual.Name, file, fields);
                        foreach (var key in new[] { "objects", "visualContainerObjects" })
                        {
                            var formatting = visualNode.Get(key);
                            if (formatting != null) Collect(formatting, PbirFieldUsage.Formatting, key, pageName, visual.Name, file, fields);
                        }
                    }
                    var filterConfig = root.Get("filterConfig");
                    if (filterConfig != null) AddFilters(filterConfig, PbirFieldUsage.VisualFilter, pageName, visual.Name, file, fields);
                    visual.Fields = fields;
                    visuals.Add(visual);
                }
                return visuals;
            }

            private void Finish()
            {
                _index.Schemas = _schemas.ToArray();
                _index.Diagnostics = _diagnostics.Take(500).ToArray();
            }
        }
    }
}
