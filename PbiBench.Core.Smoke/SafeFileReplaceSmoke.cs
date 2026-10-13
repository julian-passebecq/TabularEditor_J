using System;
using System.IO;
using PbiBench.Core.IO;

// OBS-001 opt-in retry: off unless TE2ENHANCED_REPLACE_RETRY=1; only error 1175 qualifies; a normal replace is unchanged.
internal static class SafeFileReplaceSmoke
{
    public static void Run()
    {
        var expectedDefault = Environment.GetEnvironmentVariable(SafeFileReplace.EnvironmentVariable) == "1";
        Check(SafeFileReplace.RetryEnabled == expectedDefault, "retry follows the environment variable (default off)");
        Check(SafeFileReplace.IsUnableToRemoveReplaced(new IOException("x", unchecked((int)0x80070497))), "HRESULT 0x80070497 is OBS-001");
        Check(SafeFileReplace.IsUnableToRemoveReplaced(new IOException("Unable to remove the file to be replaced.")), "message form is OBS-001");
        Check(!SafeFileReplace.IsUnableToRemoveReplaced(new IOException("The process cannot access the file", unchecked((int)0x80070020))), "sharing violation is not OBS-001");
        Check(!SafeFileReplace.IsUnableToRemoveReplaced(new UnauthorizedAccessException("Unable to remove the file to be replaced.")), "only IOException qualifies");

        var dir = Path.Combine(Path.GetTempPath(), "pbibench-safereplace-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        var previous = SafeFileReplace.RetryEnabled;
        try
        {
            foreach (var enabled in new[] { false, true })
            {
                SafeFileReplace.RetryEnabled = enabled;
                var target = Path.Combine(dir, "t" + enabled + ".txt"); var temp = target + ".tmp";
                File.WriteAllText(target, "old"); File.WriteAllText(temp, "new");
                SafeFileReplace.Replace(temp, target, null);
                Check(File.ReadAllText(target) == "new" && !File.Exists(temp), "replace result unchanged (retry " + enabled + ")");
                bool threw = false;
                try { SafeFileReplace.Replace(Path.Combine(dir, "missing.tmp"), target, null); }
                catch (FileNotFoundException) { threw = true; }
                Check(threw && File.ReadAllText(target) == "new", "other errors surface unchanged (retry " + enabled + ")");
            }
        }
        finally { SafeFileReplace.RetryEnabled = previous; try { Directory.Delete(dir, true); } catch (Exception) { } }
        Console.WriteLine("SafeFileReplace (OBS-001 opt-in retry, default off) PASS; retries used in this process: " + SafeFileReplace.RetriesUsed);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception("SafeFileReplace: " + message); }
}
