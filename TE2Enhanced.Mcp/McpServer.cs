using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using PbiBench.Core.Snapshot;

namespace TE2Enhanced.Mcp
{
    /// <summary>
    /// Read-only MCP server over newline-delimited JSON-RPC 2.0 (stdio transport). It exposes the
    /// snapshot published explicitly by TE2 Enhanced; it never connects to a model, a server or
    /// the network, and it has no write tools.
    /// </summary>
    public sealed class McpServer
    {
        public const string ServerVersion = "0.1.0";
        public const string InterfaceVersion = "te2enhanced-mcp/1";
        public const int MaxListLimit = 500;
        private static readonly string[] SupportedProtocols = { "2025-06-18", "2025-03-26", "2024-11-05" };

        private readonly string _snapshotPath;
        private readonly Func<DateTime> _utcNow;
        private ModelSnapshotV1? _cached;
        private DateTime _cachedStamp;
        private long _cachedLength = -1;

        public McpServer(string snapshotPath, Func<DateTime>? utcNow = null)
        {
            _snapshotPath = snapshotPath;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Handles one JSON-RPC message; returns null for notifications.</summary>
        public string? Handle(string line)
        {
            JsonNode? request;
            try { request = JsonNode.Parse(line); }
            catch (JsonException) { return Error(null, -32700, "Parse error"); }
            if (request is not JsonObject obj) return Error(null, -32600, "Invalid Request");

            var id = obj["id"]?.DeepClone();
            var isNotification = !obj.ContainsKey("id");
            if (id != null && !(id is JsonValue)) return Error(null, -32600, "Invalid Request: id must be a string or number.");
            if (!(obj["method"] is JsonValue methodNode) || !methodNode.TryGetValue<string>(out var method) || method == null)
                return isNotification ? null : Error(id, -32600, "Invalid Request: method must be a string.");
            var parameters = obj["params"];
            if (parameters != null && !(parameters is JsonObject))
                return isNotification ? null : Error(id, -32602, "Invalid params: params must be an object.");
            try
            {
                JsonNode? result = method switch
                {
                    "initialize" => Initialize(obj["params"] as JsonObject),
                    "ping" => new JsonObject(),
                    "tools/list" => ToolsList(),
                    "tools/call" => ToolsCall(obj["params"] as JsonObject),
                    _ when method.StartsWith("notifications/", StringComparison.Ordinal) => null,
                    _ => throw new RpcException(-32601, "Method not found: " + method)
                };
                if (isNotification) return null;
                return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result ?? new JsonObject() }.ToJsonString();
            }
            catch (RpcException error)
            {
                return isNotification ? null : Error(id, error.Code, error.Message);
            }
            catch (Exception error) when (error is InvalidOperationException || error is FormatException || error is OverflowException || error is ArgumentException || error is KeyNotFoundException)
            {
                // Type mismatch in params/arguments: a client error, never a reason to stop serving.
                return isNotification ? null : Error(id, -32602, "Invalid params: an argument has the wrong type.");
            }
            catch (Exception)
            {
                // Any other failure is reported without details (no stack traces on stdout).
                return isNotification ? null : Error(id, -32603, "Internal error");
            }
        }

        private static JsonObject Initialize(JsonObject? parameters)
        {
            var requested = parameters == null ? null : OptionalString(parameters, "protocolVersion");
            var version = requested != null && SupportedProtocols.Contains(requested) ? requested : SupportedProtocols[0];
            return new JsonObject
            {
                ["protocolVersion"] = version,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                ["serverInfo"] = new JsonObject { ["name"] = "te2-enhanced", ["version"] = ServerVersion },
                ["instructions"] = "Read-only view of the model snapshot that the user explicitly published from TE2 Enhanced " +
                                   "(PbiBench > Publish read-only snapshot for MCP). It can be stale: check publishedAtUtc and modelRevision. " +
                                   "No write tools exist; edits stay in TE2 Enhanced with its undo."
            };
        }

        private static JsonObject ToolsList()
        {
            static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
                    ["additionalProperties"] = false
                },
                ["annotations"] = new JsonObject { ["readOnlyHint"] = true, ["openWorldHint"] = false }
            };
            static JsonObject Str(string d) => new JsonObject { ["type"] = "string", ["description"] = d };

            return new JsonObject
            {
                ["tools"] = new JsonArray(
                    Tool("get_model_summary", "Model name, revision, publication time/age and object counts of the published snapshot.", new JsonObject()),
                    Tool("list_tables", "Tables with column/measure counts.", new JsonObject
                    {
                        ["filter"] = Str("Case-insensitive substring on the table name."),
                        ["includeHidden"] = new JsonObject { ["type"] = "boolean", ["description"] = "Default true." }
                    }),
                    Tool("list_measures", "Measures (name, table, folder, format string, hidden). Limit 1-500, default 100.", new JsonObject
                    {
                        ["table"] = Str("Exact table name."),
                        ["filter"] = Str("Case-insensitive substring on name or display folder."),
                        ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = MaxListLimit }
                    }),
                    Tool("get_measure", "One measure. The DAX expression is returned only if the user opted in when publishing.", new JsonObject
                    {
                        ["table"] = Str("Exact table name."),
                        ["name"] = Str("Exact measure name.")
                    }, "table", "name"),
                    Tool("list_relationships", "Relationships, optionally those touching one table.", new JsonObject
                    {
                        ["table"] = Str("Exact table name.")
                    }),
                    Tool("get_selection", "Objects selected in TE2 Enhanced when the snapshot was published.", new JsonObject()))
            };
        }

        private JsonObject ToolsCall(JsonObject? parameters)
        {
            var name = (parameters == null ? null : OptionalString(parameters, "name")) ?? throw new RpcException(-32602, "Tool name is required.");
            var argumentsNode = parameters!["arguments"];
            if (argumentsNode != null && !(argumentsNode is JsonObject)) throw new RpcException(-32602, "arguments must be an object.");
            var args = argumentsNode as JsonObject ?? new JsonObject();
            ModelSnapshotV1 snapshot;
            try { snapshot = Load(); }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is System.Runtime.Serialization.SerializationException)
            {
                return ToolError(error is FileNotFoundException
                    ? "No snapshot published. In TE2 Enhanced use PbiBench > Publish read-only snapshot for MCP, then retry."
                    : "Snapshot unavailable: " + error.Message);
            }

            JsonNode payload = name switch
            {
                "get_model_summary" => Summary(snapshot),
                "list_tables" => ListTables(snapshot, args),
                "list_measures" => ListMeasures(snapshot, args),
                "get_measure" => GetMeasure(snapshot, args),
                "list_relationships" => ListRelationships(snapshot, args),
                "get_selection" => new JsonObject { ["selection"] = new JsonArray(snapshot.Selection.Select(s => (JsonNode)new JsonObject { ["kind"] = s.Kind, ["table"] = s.Table, ["name"] = s.Name }).ToArray()) },
                _ => throw new RpcException(-32602, "Unknown tool: " + name)
            };
            if (payload is JsonObject o && o["error"] is JsonValue message) return ToolError(message.GetValue<string>());
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = payload.ToJsonString() }),
                ["structuredContent"] = payload.DeepClone(),
                ["isError"] = false
            };
        }

        private JsonObject Summary(ModelSnapshotV1 s)
        {
            double? age = DateTime.TryParse(s.PublishedAtUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var published)
                ? Math.Round((_utcNow() - published).TotalSeconds) : null;
            return new JsonObject
            {
                ["interface"] = InterfaceVersion,
                ["modelName"] = s.ModelName,
                ["modelRevision"] = s.ModelRevision,
                ["publishedAtUtc"] = s.PublishedAtUtc,
                ["ageSeconds"] = age,
                ["compatibilityLevel"] = s.CompatibilityLevel,
                ["includesExpressions"] = s.IncludesExpressions,
                ["tables"] = s.Tables.Count,
                ["columns"] = s.Tables.Sum(t => t.Columns.Count),
                ["measures"] = s.Tables.Sum(t => t.Measures.Count),
                ["relationships"] = s.Relationships.Count
            };
        }

        private static JsonObject ListTables(ModelSnapshotV1 s, JsonObject args)
        {
            var filter = OptionalString(args, "filter");
            var includeHidden = OptionalBool(args, "includeHidden") ?? true;
            var tables = s.Tables.Where(t => (includeHidden || !t.IsHidden) && Matches(t.Name, filter));
            return new JsonObject
            {
                ["tables"] = new JsonArray(tables.Select(t => (JsonNode)new JsonObject
                {
                    ["name"] = t.Name, ["isHidden"] = t.IsHidden, ["columns"] = t.Columns.Count, ["measures"] = t.Measures.Count
                }).ToArray())
            };
        }

        private static JsonObject ListMeasures(ModelSnapshotV1 s, JsonObject args)
        {
            var table = OptionalString(args, "table");
            var filter = OptionalString(args, "filter");
            var limit = OptionalInt(args, "limit") ?? 100;
            if (limit < 1 || limit > MaxListLimit) return new JsonObject { ["error"] = $"limit must be 1-{MaxListLimit}." };
            if (table != null && s.Tables.All(t => t.Name != table)) return new JsonObject { ["error"] = "Unknown table: " + table };
            var all = s.Tables.Where(t => table == null || t.Name == table)
                .SelectMany(t => t.Measures.Select(m => (t, m)))
                .Where(x => Matches(x.m.Name, filter) || Matches(x.m.DisplayFolder, filter) && filter != null)
                .ToList();
            return new JsonObject
            {
                ["total"] = all.Count,
                ["truncated"] = all.Count > limit,
                ["measures"] = new JsonArray(all.Take(limit).Select(x => (JsonNode)new JsonObject
                {
                    ["table"] = x.t.Name, ["name"] = x.m.Name, ["displayFolder"] = x.m.DisplayFolder,
                    ["formatString"] = x.m.FormatString, ["isHidden"] = x.m.IsHidden
                }).ToArray())
            };
        }

        private static JsonObject GetMeasure(ModelSnapshotV1 s, JsonObject args)
        {
            var tableName = OptionalString(args, "table");
            var name = OptionalString(args, "name");
            if (tableName == null || name == null) throw new RpcException(-32602, "table and name are required.");
            var table = s.Tables.FirstOrDefault(t => t.Name == tableName);
            var measure = table?.Measures.FirstOrDefault(m => m.Name == name);
            if (measure == null) return new JsonObject { ["error"] = $"Measure not found: '{tableName}'[{name}]" };
            return new JsonObject
            {
                ["table"] = tableName, ["name"] = measure.Name, ["displayFolder"] = measure.DisplayFolder,
                ["formatString"] = measure.FormatString, ["isHidden"] = measure.IsHidden, ["description"] = measure.Description,
                ["expression"] = s.IncludesExpressions ? measure.Expression : null,
                ["expressionWithheld"] = !s.IncludesExpressions
            };
        }

        private static JsonObject ListRelationships(ModelSnapshotV1 s, JsonObject args)
        {
            var table = OptionalString(args, "table");
            var rels = s.Relationships.Where(r => table == null || r.FromTable == table || r.ToTable == table);
            return new JsonObject
            {
                ["relationships"] = new JsonArray(rels.Select(r => (JsonNode)new JsonObject
                {
                    ["from"] = $"'{r.FromTable}'[{r.FromColumn}]", ["to"] = $"'{r.ToTable}'[{r.ToColumn}]",
                    ["isActive"] = r.IsActive, ["crossFilter"] = r.CrossFilter
                }).ToArray())
            };
        }

        private ModelSnapshotV1 Load()
        {
            var info = new FileInfo(_snapshotPath);
            if (!info.Exists) { _cached = null; throw new FileNotFoundException("No snapshot.", _snapshotPath); }
            if (_cached != null && info.LastWriteTimeUtc == _cachedStamp && info.Length == _cachedLength) return _cached;
            var snapshot = ModelSnapshotSerializer.ReadFile(_snapshotPath);
            _cached = snapshot; _cachedStamp = info.LastWriteTimeUtc; _cachedLength = info.Length;
            return snapshot;
        }

        private static string? OptionalString(JsonObject args, string name)
        {
            var node = args[name];
            if (node == null) return null;
            if (node is JsonValue v && v.TryGetValue<string>(out var text)) return string.IsNullOrEmpty(text) ? null : text;
            throw new RpcException(-32602, name + " must be a string.");
        }

        private static bool? OptionalBool(JsonObject args, string name)
        {
            var node = args[name];
            if (node == null) return null;
            if (node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False) return v.GetValue<bool>();
            throw new RpcException(-32602, name + " must be a boolean.");
        }

        private static int? OptionalInt(JsonObject args, string name)
        {
            var node = args[name];
            if (node == null) return null;
            if (node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var number)) return number;
            throw new RpcException(-32602, name + " must be an integer.");
        }

        private static bool Matches(string value, string? filter) =>
            filter == null || (value ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        private static JsonObject ToolError(string message) => new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = message }),
            ["isError"] = true
        };

        private static string Error(JsonNode? id, int code, string message) => new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }.ToJsonString();

        private sealed class RpcException : Exception
        {
            public RpcException(int code, string message) : base(message) { Code = code; }
            public int Code { get; }
        }
    }
}
