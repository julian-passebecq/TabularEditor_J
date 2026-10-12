using System;
using System.Collections.Generic;
using System.Linq;

namespace PbiBench.Core.Report
{
    public sealed class PbirSchemaInfo
    {
        public string File { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>Schema item kind from the URL (report, page, visualContainer, versionMetadata, ...), or "" when unparsable.</summary>
        public string Kind { get; set; } = "";
        public string Version { get; set; } = "";
        public bool IsKnown { get; set; }
        /// <summary>True only for (kind, version) pairs whose theme structure this build writes.</summary>
        public bool IsWritable { get; set; }
        public string Describe() => Url.Length == 0 ? "no $schema" : (Kind.Length == 0 ? Url : Kind + " " + Version) + (IsKnown ? "" : " (unknown)");
    }

    /// <summary>
    /// Version-aware registry of the public PBIR JSON schema URLs
    /// (https://developer.microsoft.com/json-schemas/fabric/item/report/definition/{kind}/{version}/schema.json).
    /// The list is curated: anything not listed is "unknown" and the report stays read-only.
    /// Reading/indexing is best-effort for every version; writing needs a listed writable version.
    /// </summary>
    public static class PbirSchemaRegistry
    {
        public const string UrlPrefix = "https://developer.microsoft.com/json-schemas/fabric/item/report/";

        private static readonly Dictionary<string, string[]> Known = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["report"] = new[] { "1.0.0", "1.1.0", "1.2.0", "2.0.0", "3.0.0" },
            ["page"] = new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0", "1.4.0", "2.0.0" },
            ["visualContainer"] = new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0", "1.4.0", "1.5.0", "1.6.0", "1.7.0", "2.0.0", "2.1.0" },
            ["pagesMetadata"] = new[] { "1.0.0" },
            ["versionMetadata"] = new[] { "1.0.0" },
            ["bookmark"] = new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0", "1.4.0" },
            ["bookmarksMetadata"] = new[] { "1.0.0" },
            ["reportExtension"] = new[] { "1.0.0" },
            ["definitionProperties"] = new[] { "1.0.0", "2.0.0" },
        };

        /// <summary>report.json versions whose themeCollection/resourcePackages layout the theme writer understands.</summary>
        private static readonly string[] WritableReport = { "1.0.0", "1.1.0", "1.2.0", "2.0.0", "3.0.0" };

        /// <summary>definition/version.json "version" values (PBIR format version) accepted for writes.</summary>
        public static IReadOnlyList<string> KnownFormatVersions { get; } = new[] { "1.0.0", "2.0.0" };

        public static IReadOnlyDictionary<string, string[]> KnownVersions => Known;

        public static PbirSchemaInfo Describe(string file, string? url)
        {
            var info = new PbirSchemaInfo { File = file ?? "", Url = url ?? "" };
            if (string.IsNullOrEmpty(url)) return info;
            var u = url!;
            // .../item/report/definition/{kind}/{version}/schema.json (definition files) or
            // .../item/report/{kind}/{version}/schema.json (definition.pbir = definitionProperties).
            if (!u.StartsWith(UrlPrefix, StringComparison.OrdinalIgnoreCase)) return info;
            var parts = u.Substring(UrlPrefix.Length).Split('/');
            if (parts.Length == 4 && parts[0] == "definition") parts = parts.Skip(1).ToArray();
            if (parts.Length != 3 || !string.Equals(parts[2], "schema.json", StringComparison.OrdinalIgnoreCase)) return info;
            info.Kind = parts[0];
            info.Version = parts[1];
            info.IsKnown = Known.TryGetValue(info.Kind, out var versions) && versions.Contains(info.Version, StringComparer.Ordinal);
            info.IsWritable = info.IsKnown && info.Kind == "report" && WritableReport.Contains(info.Version, StringComparer.Ordinal);
            return info;
        }
    }
}
