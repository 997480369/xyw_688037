using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GitPullTool.Models;

public sealed class PullConflictFileChange
{
    public PullConflictFileChange(
        string relativePath,
        string statusText,
        string detailsText,
        string leftTitle,
        IEnumerable<PullConflictPreviewLine> leftLines,
        string rightTitle,
        IEnumerable<PullConflictPreviewLine> rightLines,
        bool isUntracked)
    {
        RelativePath = relativePath;
        StatusText = statusText;
        DetailsText = detailsText;
        LeftTitle = leftTitle;
        LeftLines = new ReadOnlyCollection<PullConflictPreviewLine>((leftLines ?? Enumerable.Empty<PullConflictPreviewLine>()).ToList());
        RightTitle = rightTitle;
        RightLines = new ReadOnlyCollection<PullConflictPreviewLine>((rightLines ?? Enumerable.Empty<PullConflictPreviewLine>()).ToList());
        IsUntracked = isUntracked;
    }

    public string RelativePath { get; }
    public string StatusText { get; }
    public string DetailsText { get; }
    public string LeftTitle { get; }
    public IReadOnlyList<PullConflictPreviewLine> LeftLines { get; }
    public string RightTitle { get; }
    public IReadOnlyList<PullConflictPreviewLine> RightLines { get; }
    public bool IsUntracked { get; }
    public string DisplayText => $"{RelativePath} ({StatusText})";
    public bool CanRestore => !IsUntracked;
    public string DeleteActionText => "\u5220\u9664\u6b64\u6587\u4ef6";
}
