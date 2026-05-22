using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace GitPullTool.Models;

public sealed class PullConflictInfo
{
    public PullConflictInfo(string repoPath, IEnumerable<PullConflictFileChange> files)
    {
        RepoPath = repoPath;
        Files = new ObservableCollection<PullConflictFileChange>((files ?? Enumerable.Empty<PullConflictFileChange>()).ToList());
    }

    public string RepoPath { get; }
    public ObservableCollection<PullConflictFileChange> Files { get; }
    public string RepoName => Path.GetFileName(RepoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}
