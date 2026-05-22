namespace GitPullTool.Models;

public sealed class PullConflictPreviewSegment
{
    public PullConflictPreviewSegment(string text, bool isHighlighted)
    {
        Text = text;
        IsHighlighted = isHighlighted;
    }

    public string Text { get; }
    public bool IsHighlighted { get; }
}
