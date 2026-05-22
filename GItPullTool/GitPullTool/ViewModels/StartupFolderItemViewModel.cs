using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GitPullTool.ViewModels;

public sealed partial class StartupFolderItemViewModel : ObservableObject
{
    public StartupFolderItemViewModel(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
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
    private string? projectNumber;

    [ObservableProperty]
    private bool isProjectNumberLoading;

    public string? StartupTargetPath => ResolveStartupTargetPath();

    public string ProjectNumberDisplay
        => IsProjectNumberLoading ? "[loading...]" : string.IsNullOrWhiteSpace(ProjectNumber) ? "[unknown]" : $"[{ProjectNumber}]";

    partial void OnProjectNumberChanged(string? value)
    {
        OnPropertyChanged(nameof(ProjectNumberDisplay));
    }

    partial void OnIsProjectNumberLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ProjectNumberDisplay));
    }

    public void RefreshValidation()
    {
        if (!Directory.Exists(Path))
        {
            IsValid = false;
            ValidationMessage = "Folder not found";
            return;
        }

        if (StartupTargetPath is null)
        {
            IsValid = false;
            ValidationMessage = "startup.bat or CTC\\Startup.cfg not found";
            return;
        }

        IsValid = true;
        ValidationMessage = null;
    }

    private string? ResolveStartupTargetPath()
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(Path, "startup.bat"),
            System.IO.Path.Combine(Path, "CTC", "startup.bat"),
            System.IO.Path.Combine(Path, "CTC", "Startup.cfg"),
            System.IO.Path.Combine(Path, "Startup.cfg")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
