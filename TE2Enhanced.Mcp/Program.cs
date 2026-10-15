using System;
using System.IO;
using System.Text;
using PbiBench.Core.Snapshot;

namespace TE2Enhanced.Mcp
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string? snapshot = null;
            string? live = null;
            var selfTest = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--snapshot" when i + 1 < args.Length: snapshot = args[++i]; break;
                    case "--live" when i + 1 < args.Length: live = args[++i]; break;
                    case "--self-test": selfTest = true; break;
                    case "--version": Console.WriteLine($"TE2Enhanced.Mcp {McpServer.ServerVersion} ({McpServer.InterfaceVersion})"); return 0;
                    default:
                        Console.Error.WriteLine("Usage: TE2Enhanced.Mcp [--snapshot <path> | --live <pipe|instance|session>] [--self-test] [--version]");
                        return 2;
                }
            }
            if (selfTest) return SelfTest.Run();
            if (snapshot != null && live != null) { Console.Error.WriteLine("Choose snapshot or explicitly selected live mode."); return 2; }

            snapshot ??= Environment.GetEnvironmentVariable("TE2ENHANCED_MCP_SNAPSHOT")
                ?? ModelSnapshotSerializer.DefaultPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            Console.Error.WriteLine(live == null ? $"TE2Enhanced.Mcp {McpServer.ServerVersion}: read-only snapshot mode, {snapshot}" : $"TE2Enhanced.Mcp {McpServer.ServerVersion}: explicitly selected LIVE instance; read and typed proposals with TE2 human review, no Save/query");

            LiveBridgeClient? liveClient;
            try { liveClient = live == null ? null : new LiveBridgeClient(live); }
            catch (Exception) { Console.Error.WriteLine("Invalid selected-instance descriptor."); return 2; }
            var server = new McpServer(snapshot, live: liveClient);
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            string? line;
            while ((line = ReadBoundedLine(input)) != null)
            {
                if (line.Length == 0) continue;
                string? response;
                try { response = server.Handle(line); }
                catch (Exception)
                {
                    // Never let one request stop the server; no details or stack traces on stdout.
                    Console.Error.WriteLine("TE2Enhanced.Mcp: request failed internally.");
                    response = "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32603,\"message\":\"Internal error\"}}";
                }
                if (response != null) output.WriteLine(response);
            }
            return 0;
        }
        private static string? ReadBoundedLine(TextReader input)
        {
            var text = new StringBuilder(); int c;
            while ((c = input.Read()) >= 0 && c != '\n')
            { if (text.Length >= PbiBench.Core.Interop.Te2LiveBridgeFraming.MaxFrameBytes) throw new InvalidDataException("MCP input budget exceeded."); text.Append((char)c); }
            return c < 0 && text.Length == 0 ? null : text.ToString().TrimEnd('\r');
        }
    }
}
