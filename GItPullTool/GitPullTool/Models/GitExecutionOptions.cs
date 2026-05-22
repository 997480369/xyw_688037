namespace GitPullTool.Models;

public sealed class GitExecutionOptions
{
    public string? GitPath { get; set; }
    public string? SshKeyPath { get; set; }
    public string? SshUser { get; set; }
    public string? PlinkPath { get; set; }
}
