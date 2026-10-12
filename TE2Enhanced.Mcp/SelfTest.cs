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

                ModelSnapshotSerializer.WriteFile(path, Sample(includeExpressions: false));
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
                ModelSnapshotSerializer.WriteFile(path, Sample(includeExpressions: true));
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
            ModelSnapshotSerializer.WriteFile(path, Sample(false));
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
            var start = new ProcessStartInfo(exe) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("--snapshot"); start.ArgumentList.Add(path);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start server.");
            process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");
            process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"list_tables\"}}");
            process.StandardInput.Close();
            var first = Parse(process.StandardOutput.ReadLine());
            var second = Parse(process.StandardOutput.ReadLine());
            if (!process.WaitForExit(15000)) { process.Kill(); throw new Exception("stdio server did not exit on EOF"); }
            Check(first["id"]!.GetValue<int>() == 1 && second["id"]!.GetValue<int>() == 2, "stdio ordering, no response to notification");
            Check(second["result"]!["structuredContent"]!["tables"]!.AsArray().Count == 2, "stdio tool call");
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
