using System.Collections.Generic;

namespace GitPullTool.Models;

public sealed class AppSettings
{
    public List<string> RepoPaths { get; set; } = new();
    public List<string> SelectedRepoPaths { get; set; } = new();
    public List<string> StartupFolderPaths { get; set; } = new();
    public List<string> SelectedStartupFolderPaths { get; set; } = new();
    public string? SshKeyPath { get; set; }
    public string? SshUser { get; set; }
    public string? PlinkPath { get; set; }
    public string? GitPath { get; set; }
    public bool AutoPullOnSelection { get; set; }
    public bool SingleSelection { get; set; }
    public bool IncludeNestedRepos { get; set; }
    public Dictionary<string, string?> RepoBranchCache { get; set; } = new();
    public Dictionary<string, List<string>> RepoNestedPathsCache { get; set; } = new();
    public Dictionary<string, string?> NestedRepoBranchCache { get; set; } = new();
    public Dictionary<string, string?> StartupProjectNumberCache { get; set; } = new();
}
