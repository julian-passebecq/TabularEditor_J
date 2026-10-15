using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace PbiBench.Core.Interop
{
    /// <summary>Flat, string-valued JSON protocol. Exact fields/types/identities; no executable payload.</summary>
    public static class Te2LiveBridgeV1
    {
        public const string Schema = "te2enhanced.live/1";
        public const int MaxValueChars = 16384;
        public static readonly string[] RequestFields = { "schema", "requestId", "instance", "session", "action", "modelId", "revision", "selection", "table", "measure", "changes", "includeExpressions" };
        public static readonly string[] ResponseFields = { "schema", "requestId", "code", "instance", "session", "modelId", "revision", "selection", "snapshot", "diskRevision", "loadedRevision", "liveRevision", "gitRevision", "engineEvidence" };

        public static Dictionary<string, string> ParseRequest(string json)
        {
            var fields = Parse(json, RequestFields);
            foreach (var key in new[] { "schema", "requestId", "instance", "session", "action" }) Required(fields, key);
            if (fields["schema"] != Schema) throw new InvalidDataException("SCHEMA");
            foreach (var key in new[] { "requestId", "instance", "session" }) GuidField(fields[key]);
            if (fields["action"] == "read")
            {
                if (fields.Keys.Any(k => !new[] { "schema", "requestId", "instance", "session", "action", "includeExpressions" }.Contains(k))) throw new InvalidDataException("READ_FIELDS");
                if (fields.TryGetValue("includeExpressions", out var value) && value != "true" && value != "false") throw new InvalidDataException("BOOLEAN");
            }
            else if (fields["action"] == "propose")
            {
                foreach (var key in new[] { "modelId", "revision", "selection", "table", "measure", "changes" })
                    if (!fields.ContainsKey(key)) throw new InvalidDataException("MISSING_FIELD");
                GuidField(fields["modelId"]);
                foreach (var key in new[] { "revision", "selection", "table", "measure" }) Required(fields, key);
                if (fields["table"].Length > 512 || fields["measure"].Length > 512 || fields.ContainsKey("includeExpressions")) throw new InvalidDataException("BUDGET_OR_FIELDS");
                Changes(fields);
            }
            else throw new InvalidDataException("ACTION");
            return fields;
        }

        public static Dictionary<string, string> Changes(IDictionary<string, string> request)
        {
            var values = Parse(request["changes"], new[] { "description", "expression", "folder" });
            if (values.Count == 0 || values.Values.Sum(v => (long)v.Length) > MaxValueChars) throw new InvalidDataException("CHANGES_BUDGET");
            return values;
        }

        public static Dictionary<string, string> Parse(string json, IEnumerable<string> allowed)
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(json ?? throw new InvalidDataException("JSON"));
            if (bytes.Length > Te2LiveBridgeFraming.MaxFrameBytes) throw new InvalidDataException("BUDGET");
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = new HashSet<string>(allowed, StringComparer.Ordinal);
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, new XmlDictionaryReaderQuotas { MaxDepth = 4, MaxStringContentLength = Te2LiveBridgeFraming.MaxFrameBytes, MaxArrayLength = 16, MaxBytesPerRead = 4096, MaxNameTableCharCount = 4096 }))
            {
                reader.MoveToContent();
                if (reader.LocalName != "root" || reader.GetAttribute("type") != "object") throw new InvalidDataException("OBJECT_REQUIRED");
                if (reader.IsEmptyElement) { reader.Read(); return fields; }
                reader.ReadStartElement();
                while (reader.MoveToContent() != XmlNodeType.EndElement)
                {
                    if (reader.NodeType != XmlNodeType.Element) throw new InvalidDataException("JSON_FIELD");
                    var name = reader.LocalName;
                    if (!names.Contains(name) || fields.ContainsKey(name) || reader.GetAttribute("type") != "string") throw new InvalidDataException("UNKNOWN_DUPLICATE_OR_TYPE");
                    fields.Add(name, reader.ReadElementContentAsString());
                }
                reader.ReadEndElement();
                if (reader.MoveToContent() != XmlNodeType.None) throw new InvalidDataException("TRAILING_JSON");
            }
            return fields;
        }

        public static string Json(IDictionary<string, string> fields)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false))
                {
                    writer.WriteStartElement("root"); writer.WriteAttributeString("type", "object");
                    foreach (var pair in fields)
                    { writer.WriteStartElement(pair.Key); writer.WriteAttributeString("type", "string"); writer.WriteString(pair.Value); writer.WriteEndElement(); }
                    writer.WriteEndElement(); writer.Flush();
                }
                if (stream.Length > Te2LiveBridgeFraming.MaxFrameBytes) throw new InvalidDataException("BUDGET");
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static void GuidField(string value)
        { if (value == null || value.Length != 32 || !Guid.TryParseExact(value, "N", out _)) throw new InvalidDataException("IDENTITY"); }
        private static void Required(IDictionary<string, string> fields, string key)
        { if (!fields.TryGetValue(key, out var value) || string.IsNullOrEmpty(value)) throw new InvalidDataException("MISSING_FIELD"); }
    }
}
