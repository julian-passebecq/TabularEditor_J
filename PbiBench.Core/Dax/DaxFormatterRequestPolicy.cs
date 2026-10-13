namespace PbiBench.Core.Dax
{
    /// <summary>
    /// Explicit consent boundary for the inherited remote SQLBI DAX Formatter path.
    /// PbiBench hosts should require RemoteFormattingEnabled before sending DAX off-device.
    /// Model telemetry is a separate opt-in and must not be implied by formatting consent.
    /// </summary>
    public sealed class DaxFormatterRequestPolicy
    {
        public bool RemoteFormattingEnabled { get; set; }
        public bool IncludeModelTelemetry { get; set; }

        public bool CanSendDaxOffDevice => RemoteFormattingEnabled;
        public bool CanSendModelTelemetry => RemoteFormattingEnabled && IncludeModelTelemetry;

        public string PrivacySummary
        {
            get
            {
                if (!RemoteFormattingEnabled)
                    return "Remote DAX formatting is disabled; DAX must remain on-device.";

                return IncludeModelTelemetry
                    ? "Remote DAX formatting is enabled and model telemetry may be included."
                    : "Remote DAX formatting is enabled for DAX text and formatting protocol fields; model telemetry is disabled.";
            }
        }
    }
}
