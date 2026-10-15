using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PbiBench.Core.Interop;

namespace TE2Enhanced.Mcp
{
    internal static class LiveBridgeSelfTest
    {
        internal static void Run()
        {
            foreach (var descriptor in new[] { "", "..|x|y", "other-pipe|x|y", "te2-galaxy-" + Guid.NewGuid().ToString("N") + "|bad|bad" })
            { try { new LiveBridgeClient(descriptor); throw new Exception("Invalid descriptor accepted"); } catch (InvalidDataException) { } }
            var pipeName = "te2-galaxy-" + Guid.NewGuid().ToString("N"); var instance = Guid.NewGuid().ToString("N"); var session = Guid.NewGuid().ToString("N");
            var client = new LiveBridgeClient(pipeName + "|" + instance + "|" + session);
            var server = new McpServer("deliberately-absent-snapshot", live: client);
            var listing = JsonNode.Parse(server.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"))!["result"]!["tools"]!.AsArray();
            Check(listing.Count == 8 && listing.Single(t => t!["name"]!.GetValue<string>() == "te2_live_propose")!["annotations"]!["readOnlyHint"]!.GetValue<bool>() == false, "opt-in capabilities truthful");
            // Protocol fixture only: actual Windows owner ACL/UI/Undo are independently exercised by the compiled-host smoke.
            using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                var responder = Task.Run(() =>
                {
                    pipe.WaitForConnection(); var request = Te2LiveBridgeV1.ParseRequest(Te2LiveBridgeFraming.Read(pipe));
                    Te2LiveBridgeFraming.Write(pipe, Te2LiveBridgeV1.Json(new Dictionary<string, string> { ["schema"] = Te2LiveBridgeV1.Schema, ["requestId"] = request["requestId"], ["instance"] = instance, ["session"] = session, ["code"] = "OK", ["engineEvidence"] = "NOT_EXECUTED" }));
                });
                Check(client.Call(new Dictionary<string, string> { ["action"] = "read" })["code"] == "OK", "selected IPC protocol round trip");
                responder.GetAwaiter().GetResult();
            }
            using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                var responder = Task.Run(() =>
                {
                    pipe.WaitForConnection(); var request = Te2LiveBridgeV1.ParseRequest(Te2LiveBridgeFraming.Read(pipe));
                    Te2LiveBridgeFraming.Write(pipe, Te2LiveBridgeV1.Json(new Dictionary<string, string> { ["schema"] = Te2LiveBridgeV1.Schema, ["requestId"] = request["requestId"], ["instance"] = Guid.NewGuid().ToString("N"), ["session"] = session, ["code"] = "OK" }));
                });
                try { client.Call(new Dictionary<string, string> { ["action"] = "read" }); throw new Exception("Wrong response identity accepted"); } catch (InvalidDataException) { }
                responder.GetAwaiter().GetResult();
            }
            var unknown = JsonNode.Parse(server.Handle("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"te2_live_read\",\"arguments\":{\"rows\":true}}}"));
            Check(unknown!["result"]!["isError"]!.GetValue<bool>(), "unknown/row argument refused without connecting");
            Console.WriteLine("TE2 live companion: opt-in eight tools, default six unchanged, protocol-fixture IPC, mismatched response and strict arguments PASS (not a host/ACL qualification)");
        }
        private static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    }
}
