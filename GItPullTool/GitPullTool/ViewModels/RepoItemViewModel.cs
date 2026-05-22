using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GitPullTool.ViewModels;

public sealed partial class RepoItemViewModel : ObservableObject
{
    public RepoItemViewModel(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        NestedRepos = new ObservableCollection<NestedRepoItemViewModel>();
        RefreshValidation();
    }

    [ObservableProperty]
    private string path;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isValid;

    [ObservableProperty]
    private string? validationMessage;

    [ObservableProperty]
    private string? branchName;

    [ObservableProperty]
    private bool isBranchLoading;

    public ObservableCollection<NestedRepoItemViewModel> NestedRepos { get; }

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

    public void RefreshValidation()
    {
        var gitPath = System.IO.Path.Combine(Path, ".git");
        if (Directory.Exists(Path) && (Directory.Exists(gitPath) || File.Exists(gitPath)))
        {
            IsValid = true;
            ValidationMessage = null;
        }
        else
        {
            IsValid = false;
            ValidationMessage = "Not a git repository";
        }
    }
}
