using System;
using System.IO;
using System.Linq;
using PbiBench.Core.Dax;
using PbiBench.Core.Navigation;
using PbiBench.Core.Project;
using PbiBench.Core.Serialization;

internal static class Program
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "pbibench-core-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            BuildFixture(root);
            RunProjectDiscoveryChecks(root);
            RunGitChecks();
            RunDaxFormatterChecks();
            RunDaxPrivacyPolicyChecks();
            RunDaxQueryContractChecks();
            DaxRetentionSmoke.Run();
            RunQuickOpenChecks();
            SemanticDependencySmoke.Run();
            Console.WriteLine("PbiBench.Core smoke: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PbiBench.Core smoke: FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch { }
        }
    }

    private static void BuildFixture(string root)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "Contoso.pbip"), "{}");

        var semantic = Path.Combine(root, "Contoso.SemanticModel", "definition", "tables");
        Directory.CreateDirectory(semantic);
        File.WriteAllText(Path.Combine(semantic, "Sales.tmdl"), "table Sales");

        var report = Path.Combine(root, "Executive.Report", "definition", "pages");
        Directory.CreateDirectory(report);
        File.WriteAllText(Path.Combine(root, "Executive.Report", "definition.pbir"), "{}");

        // Ignored build folders must never be mistaken for project artifacts.
        Directory.CreateDirectory(Path.Combine(root, "obj", "Fake.Report", "definition"));
    }

    private static void RunProjectDiscoveryChecks(string root)
    {
        var source = Path.Combine(root, "Contoso.SemanticModel", "definition");
        var context = PbipProjectDiscovery.Discover(source);

        Assert(context.ProjectRoot == Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "Project root was not discovered.");
        Assert(context.PbipFile != null && context.PbipFile.EndsWith("Contoso.pbip", StringComparison.OrdinalIgnoreCase), "PBIP was not discovered.");
        Assert(context.SemanticModelFolders.Length == 1, "Expected exactly one semantic-model folder.");
        Assert(context.ReportFolders.Length == 1, "Expected exactly one report folder.");
        Assert(context.Capabilities.Pbip, "PBIP capability missing.");
        Assert(context.Capabilities.Tmdl, "TMDL capability missing.");
        Assert(context.Capabilities.Pbir, "PBIR capability missing.");
        Assert(context.Capabilities.Git, "Git capability missing.");

        var json = ProjectContextSerializer.Serialize(context);
        Assert(json.Contains("Contoso.pbip", StringComparison.Ordinal), "Serialized context lost PBIP identity.");
        Assert(!json.Contains("connectionString", StringComparison.OrdinalIgnoreCase), "Context contract must not contain connection strings.");

        var roundTrip = ProjectContextSerializer.Deserialize(json);
        Assert(roundTrip.SemanticModelFolders.SequenceEqual(context.SemanticModelFolders), "Context round trip changed semantic-model folders.");
    }

    private static void RunGitChecks()
    {
        Assert(GitStatusParser.ParseState(string.Empty) == GitState.Clean, "Empty porcelain output should be clean.");
        Assert(GitStatusParser.ParseState(" M file.txt") == GitState.Modified, "Dirty porcelain output should be modified.");
        Assert(GitStatusParser.ParseBranch("main\n") == "main", "Branch parser failed.");
        Assert(GitStatusParser.ParseBranch("HEAD") == null, "Detached HEAD should not be presented as a branch.");
        Assert(GitStatusParser.ParseHead("ABCDEF1234") == "abcdef1234", "Commit parser failed.");
        Assert(GitStatusParser.ParseHead("not-a-sha") == null, "Invalid commit value was accepted.");
    }

    private static void RunDaxFormatterChecks()
    {
        var capabilities = new DaxFormatterCapabilities();
        Assert(capabilities.RequiresNetwork, "TE2 SQLBI formatter must be identified as remote.");
        Assert(capabilities.SendsDaxOffDevice, "Formatter privacy boundary must be explicit.");
        Assert(!capabilities.IsOfflineFormatter, "Existing TE2 formatter must never be labelled offline.");
        Assert(capabilities.SupportsBatch, "Existing TE2 batch formatting capability should be preserved.");
    }

    private static void RunDaxPrivacyPolicyChecks()
    {
        var policy = new DaxFormatterRequestPolicy();
        Assert(!policy.CanSendDaxOffDevice, "Remote formatter policy must default to off.");
        Assert(!policy.CanSendModelTelemetry, "Telemetry must default to off.");

        policy.RemoteFormattingEnabled = true;
        Assert(policy.CanSendDaxOffDevice, "Explicit remote formatting consent should permit DAX transmission.");
        Assert(!policy.CanSendModelTelemetry, "Formatting consent must not imply telemetry consent.");

        policy.IncludeModelTelemetry = true;
        Assert(policy.CanSendModelTelemetry, "Telemetry should require both remote formatting and telemetry opt-in.");
    }

    private static void RunDaxQueryContractChecks()
    {
        var request = new DaxQueryRequest { QueryText = "EVALUATE ROW(\"Value\", 1)" };
        Assert(request.MaxRows == DaxQueryRequest.DefaultMaxRows, "DAX query default row bound changed unexpectedly.");
        Assert(request.TimeoutSeconds == DaxQueryRequest.DefaultTimeoutSeconds, "DAX query default timeout changed unexpectedly.");
        AssertThrows<ArgumentOutOfRangeException>(() => request.MaxRows = 0, "Zero row bound must be rejected.");
        AssertThrows<ArgumentOutOfRangeException>(() => request.MaxRows = DaxQueryRequest.MaxAllowedRows + 1, "Excessive row bound must be rejected.");
        AssertThrows<ArgumentOutOfRangeException>(() => request.TimeoutSeconds = 3601, "Excessive timeout must be rejected.");

        var history = new DaxQueryHistory(2);
        var t0 = new DateTime(2026, 9, 6, 18, 0, 0, DateTimeKind.Utc);
        history.Add("EVALUATE ROW(\"A\", 1)", true, 10, executedUtc: t0);
        history.Add("EVALUATE ROW(\"B\", 2)", true, 20, executedUtc: t0.AddMinutes(1));
        history.Add("EVALUATE ROW(\"C\", 3)", false, 30, "sample failure", t0.AddMinutes(2));

        Assert(history.Count == 2, "DAX query history must enforce its capacity.");
        Assert(history.Items[0].QueryText.Contains("\"C\"", StringComparison.Ordinal), "Newest DAX query must be first.");
        Assert(!history.Items[0].Succeeded && history.Items[0].ErrorMessage == "sample failure", "Failure metadata was lost from query history.");
        Assert(history.Items.All(i => !i.QueryText.Contains("\"A\"", StringComparison.Ordinal)), "Oldest history entry was not evicted.");

        history.Add("EVALUATE ROW(\"C\", 3)", true, 12, executedUtc: t0.AddMinutes(3));
        Assert(history.Count == 2, "Consecutive duplicate query should replace the newest entry, not grow history.");
        Assert(history.Items[0].Succeeded && history.Items[0].DurationMilliseconds == 12, "Collapsed duplicate did not keep newest execution metadata.");

        var result = new DaxQueryResult();
        Assert(result.Columns.Length == 0 && result.Rows.Length == 0, "Default query result collections must be empty, not null.");
        result.Rows = new[] { new object[] { 1 }, new object[] { 2 } };
        result.IsTruncated = true;
        var completed = history.Add("EVALUATE ROW(\"Value\", 1)", result);
        result.Rows = Array.Empty<object[]>();
        result.IsTruncated = false;
        Assert(completed.ReturnedRowCount == 2 && completed.IsTruncated, "History must snapshot result metadata without retaining rows.");
        history.Add(completed.QueryText, new DaxQueryResult { State = DaxQueryExecutionState.Cancelled });
        Assert(history.Items[0].State == DaxQueryExecutionState.Cancelled && !history.Items[0].Succeeded,
            "Duplicate collapse must preserve cancellation state.");
        AssertThrows<ArgumentException>(() => history.Add("query", new DaxQueryResult { State = DaxQueryExecutionState.Running }),
            "Unfinished execution must not enter history.");
    }

    private static void RunQuickOpenChecks()
    {
        var objects = new[]
        {
            new SemanticObjectDescriptor
            {
                Id = "measure:Sales.Revenue",
                Kind = SemanticObjectKind.Measure,
                Name = "Revenue",
                ParentName = "Sales",
                DisplayPath = "Sales[Revenue]",
                SearchTerms = new[] { "net sales", "turnover" }
            },
            new SemanticObjectDescriptor
            {
                Id = "column:Sales.RevenueCode",
                Kind = SemanticObjectKind.Column,
                Name = "Revenue Code",
                ParentName = "Sales",
                DisplayPath = "Sales[Revenue Code]"
            },
            new SemanticObjectDescriptor
            {
                Id = "table:Sales",
                Kind = SemanticObjectKind.Table,
                Name = "Sales",
                DisplayPath = "Sales"
            },
            new SemanticObjectDescriptor
            {
                Id = "function:Currency.Convert",
                Kind = SemanticObjectKind.Function,
                Name = "Currency Convert",
                DisplayPath = "Functions / Currency Convert",
                SearchTerms = new[] { "fx conversion" }
            },
            new SemanticObjectDescriptor
            {
                Id = "measure:Sales.HiddenRevenue",
                Kind = SemanticObjectKind.Measure,
                Name = "Revenue",
                ParentName = "Sales",
                DisplayPath = "Sales[Revenue hidden]",
                IsHidden = true
            }
        };

        var revenue = QuickOpenMatcher.Search(objects, "revenue", 10);
        Assert(revenue.Count >= 2, "Quick Open failed to find revenue objects.");
        Assert(revenue[0].Object.Id == "measure:Sales.Revenue", "Visible exact measure should rank first.");
        Assert(revenue[0].MatchKind == QuickOpenMatchKind.Exact, "Exact match was not classified correctly.");

        var tables = QuickOpenMatcher.Search(objects, "kind:table sal", 10);
        Assert(tables.Count == 1 && tables[0].Object.Kind == SemanticObjectKind.Table, "kind:table filter failed.");

        var measures = QuickOpenMatcher.Search(objects, "kind:measure revenue", 10);
        Assert(measures.Count == 2 && measures.All(r => r.Object.Kind == SemanticObjectKind.Measure), "kind:measure filter failed.");

        var alias = QuickOpenMatcher.Search(objects, "net sales", 10);
        Assert(alias.Count > 0 && alias[0].Object.Id == "measure:Sales.Revenue", "Search terms should participate in multi-token matching.");

        var fuzzy = QuickOpenMatcher.Search(objects, "rvn", 10);
        Assert(fuzzy.Any(r => r.Object.Id == "measure:Sales.Revenue"), "Subsequence matching failed.");

        var fn = QuickOpenMatcher.Search(objects, "kind:fn fx", 10);
        Assert(fn.Count == 1 && fn[0].Object.Kind == SemanticObjectKind.Function, "Function alias filter/search failed.");

        Assert(QuickOpenMatcher.Search(objects, "", 2).Count == 2, "Empty query should provide a bounded navigation list.");
        Assert(QuickOpenMatcher.Search(objects, "sales", 0).Count == 0, "Non-positive result limit should be empty.");
    }

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
