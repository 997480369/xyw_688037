using CommunityToolkit.Mvvm.ComponentModel;

namespace GitPullTool.ViewModels;

public sealed partial class NestedRepoItemViewModel : ObservableObject
{
    public NestedRepoItemViewModel(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
    }

    [ObservableProperty]
    private string path;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string? branchName;

    [ObservableProperty]
    private bool isBranchLoading;

    [ObservableProperty]
    private bool isSearchVisible = true;

    public string BranchDisplay
        => IsBranchLoading ? "[loading...]" : string.IsNullOrWhiteSpace(BranchName) ? "[unknown]" : $"[{BranchName}]";

    partial void OnBranchNameChanged(string? value)
    {
        OnPropertyChanged(nameof(BranchDisplay));
    }

    partial void OnIsBranchLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(BranchDisplay));
    }
}
