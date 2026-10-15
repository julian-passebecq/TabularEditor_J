using System;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;
using TabularEditor.TOMWrapper;

/// <summary>
/// Remote XMLA gate (2026-10-15): the query session cloned from a service-principal connection lost its password.
/// Synthetic strings only; the secret below is a marker, not a credential.
/// </summary>
internal static class CredentialCloneSmoke
{
    private const string Secret = "SMOKE-NOT-A-SECRET-7f3a";
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("CredentialCloneSmoke: " + message); }

    internal static void Run()
    {
        // What AMO reports back after an SP connect: everything but the password.
        const string amo = "Data Source=powerbi://api.powerbi.com/v1.0/myorg/ws;User ID=app:cid@tid;Provider=MSOLAP;Application Name=TabularEditor-x;Initial Catalog=Gate";
        var opened = "Data Source=powerbi://api.powerbi.com/v1.0/myorg/ws;User ID=app:cid@tid;Password=" + Secret + ";Application Name=TabularEditor-x;Initial Catalog=Gate";

        var clone = new DbConnectionStringBuilder { ConnectionString = PbiBenchAmoDaxQueryExecutor.BuildQueryConnectionString(amo, opened) };
        Assert(Convert.ToString(clone["Password"]) == Secret, "password not carried into the query clone");
        Assert(Convert.ToString(clone["User ID"]) == "app:cid@tid" && Convert.ToString(clone["Initial Catalog"]) == "Gate"
            && Convert.ToString(clone["Provider"]) == "MSOLAP", "other AMO keys changed");

        // PWD alias, and a password containing separators/quotes, survive the round trip.
        var tricky = Secret + ";x='y\"";
        var pwd = new DbConnectionStringBuilder { ["Data Source"] = "asazure://x", ["PWD"] = tricky }.ConnectionString;
        clone = new DbConnectionStringBuilder { ConnectionString = PbiBenchAmoDaxQueryExecutor.BuildQueryConnectionString(amo, pwd) };
        Assert(Convert.ToString(clone["Password"]) == tricky && !clone.ContainsKey("PWD"), "PWD alias or quoted password not cloned");

        // Nothing to add: local/Windows auth, plain server names, AMO already carrying a password, bad input.
        Assert(PbiBenchAmoDaxQueryExecutor.BuildQueryConnectionString(amo, "Data Source=localhost;Application Name=a") == amo, "added a key without a password");
        Assert(PbiBenchAmoDaxQueryExecutor.BuildQueryConnectionString(amo, "localhost") == amo, "plain server name changed the clone");
        Assert(PbiBenchAmoDaxQueryExecutor.BuildQueryConnectionString(amo, null) == amo, "null opened string changed the clone");
        Assert(PbiBenchAmoDaxQueryExecutor.BuildQueryConnectionString(amo, "Data Source=x;Password=") == amo, "empty password copied");
        var withPassword = amo + ";Password=amo-own";
        Assert(PbiBenchAmoDaxQueryExecutor.BuildQueryConnectionString(withPassword, opened) == withPassword, "AMO's own password overwritten");
        Assert(PbiBenchAmoDaxQueryExecutor.BuildQueryConnectionString(amo, "Data Source=x;Password='unterminated") == amo, "malformed opened string threw or changed the clone");

        // Labels carry the server and catalog only.
        Assert(PbiBenchAmoDaxQueryExecutor.BuildConnectionLabel("powerbi://api.powerbi.com/v1.0/myorg/ws", "Gate") == "powerbi://api.powerbi.com/v1.0/myorg/ws / Gate", "label format changed");
        Assert(!PbiBenchAmoDaxQueryExecutor.BuildConnectionLabel(opened, "Gate").Contains(Secret), "connection string leaked into the label");

        // The connection string a session receives never comes back in a result, whatever the provider says.
        var session = new ThrowingSession();
        var result = new PbiBenchAmoDaxQueryExecutor(() => session)
            .ExecuteAsync(new DaxQueryRequest { QueryText = "EVALUATE ROW(\"x\",1)" }, CancellationToken.None).GetAwaiter().GetResult();
        Assert(result.State == DaxQueryExecutionState.Failed && result.Reason == DaxQueryReason.Connection, "connect failure not reported as Connection");
        Assert((result.ErrorMessage ?? "").IndexOf(Secret, StringComparison.Ordinal) < 0, "provider message with the secret reached the result");

        // The credentialed handler string stays out of the public/scripting surface.
        var property = typeof(TabularModelHandler).GetProperty("OpenedConnectionString", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert(property != null && property.GetMethod != null && !property.GetMethod.IsPublic, "OpenedConnectionString must exist and stay internal");

        // The conflict check keeps server and credentials and drops the catalog (the database is looked up by ID).
        var check = new DbConnectionStringBuilder { ConnectionString = TabularModelHandler.GetConflictCheckConnectionString(opened) };
        Assert(!check.ContainsKey("Initial Catalog") && Convert.ToString(check["Password"]) == Secret
            && Convert.ToString(check["Data Source"]) == "powerbi://api.powerbi.com/v1.0/myorg/ws", "conflict-check connection string wrong");
        Assert(TabularModelHandler.GetConflictCheckConnectionString("localhost") == "localhost", "plain server name changed");
        Assert(TabularModelHandler.ConflictCheckAttempts >= 2, "conflict check must retry a missing database");
    }

    private sealed class ThrowingSession : IDaxQuerySession
    {
        public void Connect(string connectionString, int timeoutSeconds) =>
            throw new InvalidOperationException("provider echo: " + connectionString + " Password=" + Secret);
        public IDataReader Execute(string query, string catalog) => throw new InvalidOperationException("unreachable");
        public void Cancel() { }
        public void Abort() { }
        public void Disconnect() { }
        public void Dispose() { }
    }
}
