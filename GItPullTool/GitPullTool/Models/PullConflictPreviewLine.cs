using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GitPullTool.Models;

public sealed class PullConflictPreviewLine
{
    public PullConflictPreviewLine(char marker, int? lineNumber, string text, bool isChanged, IEnumerable<PullConflictPreviewSegment>? segments = null)
    {
        Marker = marker.ToString(CultureInfo.InvariantCulture);
        LineNumber = lineNumber;
        Text = text;
        IsChanged = isChanged;
        Segments = (segments ?? new[] { new PullConflictPreviewSegment(text, false) }).ToList();
    }

    public string Marker { get; }
    public int? LineNumber { get; }
    public string Text { get; }
    public bool IsChanged { get; }
    public IReadOnlyList<PullConflictPreviewSegment> Segments { get; }
    public string LineNumberText => LineNumber.HasValue ? LineNumber.Value.ToString("0000", CultureInfo.InvariantCulture) : string.Empty;
}
