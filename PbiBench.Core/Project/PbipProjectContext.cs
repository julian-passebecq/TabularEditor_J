using System;
using System.Runtime.Serialization;

namespace PbiBench.Core.Project
{
    [DataContract]
    public sealed class PbipProjectContext
    {
        public const int CurrentContractVersion = 1;

        [DataMember(Name = "contractVersion", Order = 1)]
        public int ContractVersion { get; set; } = CurrentContractVersion;

        [DataMember(Name = "product", Order = 2)]
        public string Product { get; set; } = "PbiBench";

        [DataMember(Name = "upstreamTe2Commit", Order = 3)]
        public string UpstreamTe2Commit { get; set; } = "7029129aa3f45d35f987d8f6ac7e5a971f28771c";

        [DataMember(Name = "sourcePath", Order = 4, EmitDefaultValue = false)]
        public string? SourcePath { get; set; }

        [DataMember(Name = "projectRoot", Order = 5)]
        public string ProjectRoot { get; set; } = string.Empty;

        [DataMember(Name = "pbipFile", Order = 6, EmitDefaultValue = false)]
        public string? PbipFile { get; set; }

        [DataMember(Name = "semanticModelFolders", Order = 7)]
        public string[] SemanticModelFolders { get; set; } = Array.Empty<string>();

        [DataMember(Name = "reportFolders", Order = 8)]
        public string[] ReportFolders { get; set; } = Array.Empty<string>();

        [DataMember(Name = "git", Order = 9)]
        public GitProjectSummary Git { get; set; } = new GitProjectSummary();

        [DataMember(Name = "capabilities", Order = 10)]
        public ProjectCapabilities Capabilities { get; set; } = new ProjectCapabilities();
    }

    [DataContract]
    public sealed class GitProjectSummary
    {
        [DataMember(Name = "detected", Order = 1)]
        public bool Detected { get; set; }

        [DataMember(Name = "root", Order = 2, EmitDefaultValue = false)]
        public string? Root { get; set; }

        [DataMember(Name = "branch", Order = 3, EmitDefaultValue = false)]
        public string? Branch { get; set; }

        [DataMember(Name = "state", Order = 4)]
        public string State { get; set; } = GitState.Unknown;

        [DataMember(Name = "head", Order = 5, EmitDefaultValue = false)]
        public string? Head { get; set; }
    }

    public static class GitState
    {
        public const string Clean = "Clean";
        public const string Modified = "Modified";
        public const string Unknown = "Unknown";
    }

    [DataContract]
    public sealed class ProjectCapabilities
    {
        [DataMember(Name = "pbip", Order = 1)]
        public bool Pbip { get; set; }

        [DataMember(Name = "tmdl", Order = 2)]
        public bool Tmdl { get; set; }

        [DataMember(Name = "pbir", Order = 3)]
        public bool Pbir { get; set; }

        [DataMember(Name = "git", Order = 4)]
        public bool Git { get; set; }
    }
}
