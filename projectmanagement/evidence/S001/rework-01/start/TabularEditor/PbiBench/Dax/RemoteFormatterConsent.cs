using System;
using System.Threading;
using TabularEditor.UIServices;

namespace TabularEditor.PbiBench.Dax
{
    internal static class RemoteFormatterConsent
    {
        private static int _enabled;
        private static int _revision;
        internal static int Revision => Volatile.Read(ref _revision);
        internal static bool Enabled => Volatile.Read(ref _enabled) != 0 && !Policies.Instance.DisableWebDaxFormatter;
        internal static void SetEnabled(bool enabled)
        {
            Volatile.Write(ref _enabled, enabled ? 1 : 0);
            Interlocked.Increment(ref _revision);
        }
        internal static void RequireEnabled()
        {
            if (!Enabled) throw new InvalidOperationException(Policies.Instance.DisableWebDaxFormatter
                ? "Remote DAX formatting is disabled by administrative policy."
                : "Remote DAX formatting is disabled. Enable session consent in PbiBench DAX Workbench > Remote formatting.");
        }
    }
}
