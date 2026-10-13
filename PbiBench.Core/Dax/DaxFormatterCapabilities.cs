namespace PbiBench.Core.Dax
{
    /// <summary>
    /// Describes the formatter behavior inherited from Tabular Editor 2. This is kept
    /// explicit so the future PbiBench UI cannot accidentally label the SQLBI service as local/offline.
    /// </summary>
    public sealed class DaxFormatterCapabilities
    {
        public const string ProviderId = "sqlbi-daxformatter-service";
        public const string ProviderDisplayName = "SQLBI DAX Formatter";
        public const string LongFormatShortcut = "F6";
        public const string ShortFormatShortcut = "Ctrl+F6";

        public bool RequiresNetwork => true;
        public bool SendsDaxOffDevice => true;
        public bool SupportsBatch => true;
        public bool PerformsSyntaxParsing => true;
        public bool IsOfflineFormatter => false;

        public string PrivacySummary => "Formatting uses the SQLBI DAX Formatter service; DAX text leaves the local process.";
    }
}
