using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using PbiBench.Core.Interop;

namespace TE2Enhanced.Mcp
{
    /// <summary>Explicit local selected-instance client. No discovery, credentials, Save, query or retry.</summary>
    public sealed class LiveBridgeClient
    {
        private readonly string _pipe;
        private readonly string _instance;
        private readonly string _session;
        public LiveBridgeClient(string descriptor)
        {
            var parts = descriptor.Split('|');
            if (parts.Length != 3 || !Regex.IsMatch(parts[0], "^te2-galaxy-[a-f0-9]{32}$")) throw new InvalidDataException("Invalid selected-instance descriptor.");
            Te2LiveBridgeV1.GuidField(parts[1]); Te2LiveBridgeV1.GuidField(parts[2]);
            _pipe = parts[0]; _instance = parts[1]; _session = parts[2];
        }
        public Dictionary<string, string> Call(IDictionary<string, string> arguments, int timeoutMs = 120000)
        {
            var request = new Dictionary<string, string>(arguments, StringComparer.Ordinal)
            { ["schema"] = Te2LiveBridgeV1.Schema, ["requestId"] = Guid.NewGuid().ToString("N"), ["instance"] = _instance, ["session"] = _session };
            var json = Te2LiveBridgeV1.Json(request); Te2LiveBridgeV1.ParseRequest(json);
            using var client = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            using var deadline = new CancellationTokenSource(timeoutMs);
            using var registration = deadline.Token.Register(() => client.Dispose());
            client.Connect(Math.Min(timeoutMs, 2000));
            Te2LiveBridgeFraming.Write(client, json);
            var response = Te2LiveBridgeV1.Parse(Te2LiveBridgeFraming.Read(client), Te2LiveBridgeV1.ResponseFields);
            if (!response.TryGetValue("schema", out var schema) || schema != Te2LiveBridgeV1.Schema ||
                !response.TryGetValue("requestId", out var id) || id != request["requestId"] ||
                !response.TryGetValue("instance", out var instance) || instance != _instance ||
                !response.TryGetValue("session", out var session) || session != _session || !response.ContainsKey("code")) throw new InvalidDataException("Live response identity mismatch; outcome UNKNOWN, inspect TE2 before retrying.");
            return response;
        }
        internal static IEnumerable<JsonObject> Tools()
        {
            static JsonObject Text() => new JsonObject { ["type"] = "string" };
            static JsonObject Tool(string name, string description, JsonObject properties, string[] required, bool readOnly) => new JsonObject
            {
                ["name"] = name, ["description"] = description,
                ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(required.Select(x => (JsonNode)x).ToArray()), ["additionalProperties"] = false },
                ["annotations"] = new JsonObject { ["readOnlyHint"] = readOnly, ["destructiveHint"] = !readOnly, ["idempotentHint"] = readOnly, ["openWorldHint"] = false }
            };
            yield return Tool("te2_live_read", "Read the selected TE2 instance's unsaved model and current selection on its UI thread. Expressions need the host opt-in. No rows, query or Save.",
                new JsonObject { ["includeExpressions"] = new JsonObject { ["type"] = "boolean" } }, Array.Empty<string>(), true);
            yield return Tool("te2_live_propose", "Propose exact description/expression/folder changes for one measure at a current read stamp. A TE2 host dialog requires human approval. One Undo batch, no Save/deploy. Lost response means UNKNOWN; never retry implicitly.",
                new JsonObject { ["modelId"] = Text(), ["revision"] = Text(), ["selection"] = Text(), ["table"] = Text(), ["measure"] = Text(), ["description"] = Text(), ["expression"] = Text(), ["folder"] = Text() },
                new[] { "modelId", "revision", "selection", "table", "measure" }, false);
        }
        internal JsonObject ToolCall(string name, JsonObject arguments)
        {
            try
            {
                var request = new Dictionary<string, string>();
                if (name == "te2_live_read")
                {
                    if (arguments.Any(x => x.Key != "includeExpressions")) throw new InvalidDataException("Unknown live-read argument.");
                    request["action"] = "read";
                    if (arguments.TryGetPropertyValue("includeExpressions", out var v))
                    { if (v is not JsonValue b || !b.TryGetValue<bool>(out var flag)) throw new InvalidDataException("includeExpressions must be boolean."); request["includeExpressions"] = flag ? "true" : "false"; }
                }
                else if (name == "te2_live_propose")
                {
                    var allowed = new[] { "modelId", "revision", "selection", "table", "measure", "description", "expression", "folder" };
                    if (arguments.Any(x => !allowed.Contains(x.Key))) throw new InvalidDataException("Unknown typed proposal argument.");
                    request["action"] = "propose";
                    foreach (var key in allowed.Take(5)) request[key] = Text(arguments, key);
                    var changes = new Dictionary<string, string>();
                    foreach (var key in allowed.Skip(5)) if (arguments.ContainsKey(key)) changes[key] = Text(arguments, key);
                    request["changes"] = Te2LiveBridgeV1.Json(changes);
                }
                else throw new InvalidDataException("Unknown live tool.");
                var response = Call(request);
                var payload = new JsonObject();
                foreach (var pair in response) payload[pair.Key] = pair.Key == "snapshot" ? JsonNode.Parse(pair.Value) : JsonValue.Create(pair.Value);
                return new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = payload.ToJsonString() }), ["structuredContent"] = payload, ["isError"] = response["code"] != "OK" && response["code"] != "APPLIED_LOADED_MODEL_ONLY" };
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is InvalidOperationException || error is UnauthorizedAccessException || error is TimeoutException || error is OperationCanceledException)
            {
                return new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Live bridge unavailable or request refused. A lost proposal response has UNKNOWN outcome; inspect TE2 before retrying. No automatic fallback, Save or retry." }), ["isError"] = true };
            }
        }
        private static string Text(JsonObject args, string key) => args[key] is JsonValue v && v.TryGetValue<string>(out var text) ? text : throw new InvalidDataException("String field required.");
    }
}
