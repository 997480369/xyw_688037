using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GitPullTool.Models;

public sealed class PullSummary
{
    public PullSummary(IEnumerable<string> succeeded, IEnumerable<string> failed, bool cancelled = false)
    {
        Succeeded = new ReadOnlyCollection<string>((succeeded ?? Enumerable.Empty<string>()).ToList());
        Failed = new ReadOnlyCollection<string>((failed ?? Enumerable.Empty<string>()).ToList());
        Cancelled = cancelled;
    }

    public IReadOnlyList<string> Succeeded { get; }
    public IReadOnlyList<string> Failed { get; }
    public bool Cancelled { get; }
}
