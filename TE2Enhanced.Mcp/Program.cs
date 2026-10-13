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
            var selfTest = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--snapshot" when i + 1 < args.Length: snapshot = args[++i]; break;
                    case "--self-test": selfTest = true; break;
                    case "--version": Console.WriteLine($"TE2Enhanced.Mcp {McpServer.ServerVersion} ({McpServer.InterfaceVersion})"); return 0;
                    default:
                        Console.Error.WriteLine("Usage: TE2Enhanced.Mcp [--snapshot <path>] [--self-test] [--version]");
                        return 2;
                }
            }
            if (selfTest) return SelfTest.Run();

            snapshot ??= Environment.GetEnvironmentVariable("TE2ENHANCED_MCP_SNAPSHOT")
                ?? ModelSnapshotSerializer.DefaultPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            Console.Error.WriteLine($"TE2Enhanced.Mcp {McpServer.ServerVersion}: read-only, snapshot {snapshot}");

            var server = new McpServer(snapshot);
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            string? line;
            while ((line = input.ReadLine()) != null)
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
    }
}
