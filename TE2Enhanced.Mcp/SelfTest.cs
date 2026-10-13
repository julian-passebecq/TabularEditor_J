using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using PbiBench.Core.Snapshot;

namespace TE2Enhanced.Mcp
{
    /// <summary>Synthetic, offline checks of the protocol surface and snapshot guards.</summary>
    internal static class SelfTest
    {
        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "te2e-mcp-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, ModelSnapshotSerializer.FileName);
                var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
                var server = new McpServer(path, () => now);

                // Missing snapshot: protocol works, tools report a clear tool error.
                var init = Call(server, 1, "initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" });
                Check(init["result"]!["protocolVersion"]!.GetValue<string>() == "2025-06-18", "protocol negotiation");
                Check(server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}") == null, "notification has no response");
                var tools = Call(server, 2, "tools/list", null)["result"]!["tools"]!.AsArray();
                Check(tools.Count == 6, "six read-only tools");
                foreach (var t in tools) Check(t!["annotations"]!["readOnlyHint"]!.GetValue<bool>(), "readOnlyHint on " + t["name"]);
                var missing = CallTool(server, 3, "get_model_summary", null);
                Check(missing["isError"]!.GetValue<bool>() && Text(missing).Contains("No snapshot published"), "missing snapshot message");

                Publish(path, Sample(includeExpressions: false));
                var summary = CallTool(server, 4, "get_model_summary", null)["structuredContent"]!;
                Check(summary["tables"]!.GetValue<int>() == 2 && summary["measures"]!.GetValue<int>() == 3, "summary counts");
                Check(summary["ageSeconds"]!.GetValue<double>() == 3600, "staleness age");

                var measures = CallTool(server, 5, "list_measures", new JsonObject { ["table"] = "Sales", ["limit"] = 1 })["structuredContent"]!;
                Check(measures["total"]!.GetValue<int>() == 2 && measures["truncated"]!.GetValue<bool>(), "bounded measure list");
                Check(CallTool(server, 6, "list_measures", new JsonObject { ["limit"] = 501 })["isError"]!.GetValue<bool>(), "limit bound");
                Check(CallTool(server, 7, "list_measures", new JsonObject { ["table"] = "Nope" })["isError"]!.GetValue<bool>(), "unknown table");

                var withheld = CallTool(server, 8, "get_measure", new JsonObject { ["table"] = "Sales", ["name"] = "Total ''Sales''" })["structuredContent"]!;
                Check(withheld["expressionWithheld"]!.GetValue<bool>() && withheld["expression"] == null, "expression withheld without opt-in");
                Check(CallTool(server, 9, "get_measure", new JsonObject { ["table"] = "Sales", ["name"] = "x" })["isError"]!.GetValue<bool>(), "measure not found");

                var rels = CallTool(server, 10, "list_relationships", new JsonObject { ["table"] = "Date" })["structuredContent"]!;
                Check(rels["relationships"]!.AsArray().Count == 1, "relationship filter");
                var sel = CallTool(server, 11, "get_selection", null)["structuredContent"]!;
                Check(sel["selection"]!.AsArray().Count == 1, "selection");
                var hidden = CallTool(server, 12, "list_tables", new JsonObject { ["includeHidden"] = false })["structuredContent"]!;
                Check(hidden["tables"]!.AsArray().Count == 1, "hidden table filter");

                // Opt-in expressions, and reload when the snapshot file changes.
                Publish(path, Sample(includeExpressions: true));
                var withExpr = CallTool(server, 13, "get_measure", new JsonObject { ["table"] = "Sales", ["name"] = "Total ''Sales''" })["structuredContent"]!;
                Check(withExpr["expression"]!.GetValue<string>() == "SUM('Sales'[Amount])", "expression after opt-in and reload");

                // Protocol errors.
                Check(Parse(server.Handle("not json"))["error"]!["code"]!.GetValue<int>() == -32700, "parse error");
                Check(Call(server, 14, "resources/list", null)["error"]!["code"]!.GetValue<int>() == -32601, "unknown method");
                Check(Call(server, 15, "tools/call", new JsonObject { ["name"] = "delete_measure" })["error"]!["code"]!.GetValue<int>() == -32602, "no write tools");

                // Guards: credential-looking text and expressions without opt-in are refused.
                var leaky = Sample(false); leaky.Tables[0].Description = "Server=x;Password=hunter2";
                Check(Throws(() => ModelSnapshotSerializer.Serialize(leaky)), "credential guard on write");
                File.WriteAllText(path, ModelSnapshotSerializer.Serialize(Sample(true)).Replace("\"IncludesExpressions\":true", "\"IncludesExpressions\":false"));
                Check(CallTool(server, 16, "get_model_summary", null)["isError"]!.GetValue<bool>(), "expressions without opt-in refused on read");
                File.WriteAllText(path, "{\"ContractVersion\":2}");
                Check(CallTool(server, 17, "get_model_summary", null)["isError"]!.GetValue<bool>(), "future contract refused");

                // D3: every snapshot string is scanned, with broadened (heuristic) patterns.
                Publish(path, Sample(false));
                foreach (var secret in new[]
                {
                    "client_secret=zzz", "a;Password=hunter2", "Password = x", "password: x", "PWD=abc", "User ID = admin",
                    "Authorization: Bearer abcdefghijkl", "Bearer abcdefghijklmnop", "https://x.blob.core.windows.net/c?sv=1&sig=abc",
                    "Data Source=srv;Initial Catalog=db", "Server = tcp:x,1433;Database=y", "AccountKey=abc", "SharedAccessSignature=x"
                })
                {
                    Check(ModelSnapshotSerializer.LooksLikeCredential(secret), "credential pattern: " + secret);
                    foreach (var place in new Action<ModelSnapshotV1>[]
                    {
                        x => x.ModelRevision = secret, x => x.Producer = secret, x => x.PublishedAtUtc = secret,
                        x => x.Relationships[0].FromTable = secret, x => x.Relationships[0].CrossFilter = secret,
                        x => x.Selection[0].Name = secret, x => x.Tables[0].Columns[0].DataType = secret,
                        x => x.Tables[0].Measures[0].DisplayFolder = secret, x => x.Tables[0].Measures[0].Description = secret
                    })
                    {
                        var s = Sample(false); place(s);
                        Check(Throws(() => ModelSnapshotSerializer.Serialize(s)), "credential guard on write: " + secret);
                    }
                }
                foreach (var benign in new[] { "Total Sales", "#,0.00;(#,0.00)", "Sales Amount = SUM", "Server count", "Bearer bonds" })
                    Check(!ModelSnapshotSerializer.LooksLikeCredential(benign), "no false positive: " + benign);
                var leakyRel = ModelSnapshotSerializer.Serialize(Sample(false)).Replace("\"FromTable\":\"Sales\"", "\"FromTable\":\"a;Password=hunter2\"");
                File.WriteAllText(path, leakyRel);
                Check(CallTool(server, 18, "list_relationships", null)["isError"]!.GetValue<bool>(), "credential guard on read (relationship)");

                // D5: garbage, null elements and null strings are tool errors, and the server keeps serving.
                foreach (var bad in new[]
                {
                    new string('\0', 100), "{\"ContractVersion\":1,\"Tables\":[null]}", "{\"ContractVersion\":1,\"ModelName\":null}",
                    "{\"ContractVersion\":1,\"Tables\":[{\"Name\":\"T\",\"Measures\":[null]}]}", "[1,2]", "{\"ContractVersion\":\"x\"}"
                })
                {
                    File.WriteAllText(path, bad);
                    Check(CallTool(server, 19, "get_model_summary", null)["isError"]!.GetValue<bool>(), "bad snapshot is a tool error: " + bad.Replace("\0", "\\0").Substring(0, Math.Min(bad.Length, 40)));
                }
                Check(Call(server, 20, "ping", null)["result"] != null, "alive after bad snapshots");

                // D1: type mismatches are JSON-RPC errors; the next request is still answered.
                Publish(path, Sample(false));
                foreach (var mismatch in new[]
                {
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"list_measures\",\"arguments\":{\"limit\":\"5\"}}}",
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"list_measures\",\"arguments\":{\"limit\":1.5}}}",
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"list_tables\",\"arguments\":{\"includeHidden\":\"no\"}}}",
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":5}}",
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"list_tables\",\"arguments\":[1]}}",
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":[1]}",
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":5}",
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":{}}",
                    "{\"jsonrpc\":\"2.0\",\"id\":{},\"method\":\"ping\"}",
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":5}}"
                })
                {
                    var code = Parse(server.Handle(mismatch))["error"]!["code"]!.GetValue<int>();
                    Check(code == -32602 || code == -32600, "type mismatch is -32602/-32600: " + mismatch);
                }
                Check(Call(server, 21, "ping", null)["result"] != null, "alive after type mismatches");

                StdioRoundTrip(path);
                Console.WriteLine("TE2Enhanced.Mcp self-test: PASS");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("TE2Enhanced.Mcp self-test: FAIL");
                Console.Error.WriteLine(error);
                return 1;
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        /// <summary>Runs the real executable over stdio, as an MCP client would.</summary>
        private static void StdioRoundTrip(string path)
        {
            Publish(path, Sample(false));
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
            var start = new ProcessStartInfo(exe) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("--snapshot"); start.ArgumentList.Add(path);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start server.");
            process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");
            process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            // A type-mismatched request must not kill the process (D1).
            process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"list_measures\",\"arguments\":{\"limit\":\"5\"}}}");
            process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"list_tables\"}}");
            process.StandardInput.Close();
            var first = Parse(process.StandardOutput.ReadLine());
            var mismatch = Parse(process.StandardOutput.ReadLine());
            Check(mismatch["id"]!.GetValue<int>() == 3 && mismatch["error"]!["code"]!.GetValue<int>() == -32602, "stdio type mismatch answered");
            var second = Parse(process.StandardOutput.ReadLine());
            if (!process.WaitForExit(15000)) { process.Kill(); throw new Exception("stdio server did not exit on EOF"); }
            Check(first["id"]!.GetValue<int>() == 1 && second["id"]!.GetValue<int>() == 2, "stdio ordering, no response to notification");
            Check(second["result"]!["structuredContent"]!["tables"]!.AsArray().Count == 2, "stdio tool call");
        }

        private static int _obs001;

        /// <summary>
        /// The product writer is atomic (File.Replace) and never retries. On this machine a fresh file
        /// under %TEMP% is occasionally held by another process (OBS-001, ERROR_UNABLE_TO_REMOVE_REPLACED);
        /// the test tolerates exactly one such observation per run, reports it, and fails on a second.
        /// </summary>
        private static void Publish(string path, ModelSnapshotV1 snapshot)
        {
            try { ModelSnapshotSerializer.WriteFile(path, snapshot); }
            catch (IOException error) when ((error.HResult & 0xFFFF) == 1175 && _obs001++ == 0)
            {
                Console.WriteLine("OBS-001 observed once (File.Replace refused, previous snapshot intact): " + error.Message);
                ModelSnapshotSerializer.WriteFile(path, snapshot);
            }
        }

        private static ModelSnapshotV1 Sample(bool includeExpressions)
        {
            var s = new ModelSnapshotV1
            {
                PublishedAtUtc = "2026-10-13T11:00:00Z", ModelName = "Synthetic", ModelRevision = "r1",
                CompatibilityLevel = 1601, IncludesExpressions = includeExpressions
            };
            var sales = new SnapshotTable { Name = "Sales" };
            sales.Columns.Add(new SnapshotColumn { Name = "Amount", DataType = "Decimal" });
            sales.Columns.Add(new SnapshotColumn { Name = "DateKey", DataType = "Int64", IsHidden = true });
            sales.Measures.Add(new SnapshotMeasure { Name = "Total ''Sales''", FormatString = "#,0", DisplayFolder = "KPI", Expression = includeExpressions ? "SUM('Sales'[Amount])" : null });
            sales.Measures.Add(new SnapshotMeasure { Name = "Margin", DisplayFolder = "KPI", Expression = includeExpressions ? "1" : null });
            var date = new SnapshotTable { Name = "Date", IsHidden = true };
            date.Columns.Add(new SnapshotColumn { Name = "DateKey", DataType = "Int64" });
            date.Measures.Add(new SnapshotMeasure { Name = "Days", Expression = includeExpressions ? "COUNTROWS('Date')" : null });
            s.Tables.Add(sales); s.Tables.Add(date);
            s.Relationships.Add(new SnapshotRelationship { FromTable = "Sales", FromColumn = "DateKey", ToTable = "Date", ToColumn = "DateKey", IsActive = true, CrossFilter = "OneDirection" });
            s.Selection.Add(new SnapshotSelection { Kind = "Measure", Table = "Sales", Name = "Margin" });
            return s;
        }

        private static JsonObject Call(McpServer server, int id, string method, JsonObject? parameters)
        {
            var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
            if (parameters != null) request["params"] = parameters;
            return Parse(server.Handle(request.ToJsonString()));
        }

        private static JsonObject CallTool(McpServer server, int id, string name, JsonObject? arguments)
        {
            var p = new JsonObject { ["name"] = name };
            if (arguments != null) p["arguments"] = arguments;
            var response = Call(server, id, "tools/call", p);
            return response["result"] as JsonObject ?? throw new Exception($"{name}: expected result, got {response.ToJsonString()}");
        }

        private static string Text(JsonObject result) => result["content"]![0]!["text"]!.GetValue<string>();
        private static JsonObject Parse(string? json) => JsonNode.Parse(json ?? throw new Exception("No response"))!.AsObject();
        private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
        private static void Check(bool condition, string what) { if (!condition) throw new Exception("Check failed: " + what); }
    }
}
