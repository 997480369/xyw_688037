using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPullTool.Models;
using GitPullTool.Services;

namespace GitPullTool.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DialogService dialogService;
    private readonly GitService gitService;
    private readonly SettingsService settingsService;
    private readonly UserCodeDialogService userCodeDialogService;
    private readonly ICollectionView reposView;
    private readonly ICollectionView startupFoldersView;
    private AppSettings currentSettings = new();
    private CancellationTokenSource? pullCts;
    private bool isInitializing;

    public MainViewModel(
        DialogService dialogService,
        GitService gitService,
        SettingsService settingsService,
        UserCodeDialogService userCodeDialogService)
    {
        this.dialogService = dialogService;
        this.gitService = gitService;
        this.settingsService = settingsService;
        this.userCodeDialogService = userCodeDialogService;

        Repos = new ObservableCollection<RepoItemViewModel>();
        reposView = CollectionViewSource.GetDefaultView(Repos);
        reposView.Filter = FilterRepo;
        StartupFolders = new ObservableCollection<StartupFolderItemViewModel>();
        startupFoldersView = CollectionViewSource.GetDefaultView(StartupFolders);
        startupFoldersView.Filter = FilterStartupFolder;
        LogEntries = new ObservableCollection<LogEntryViewModel>();

        AddRepoCommand = new RelayCommand(AddRepo);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => Repos.Any(r => r.IsSelected));
        SelectAllCommand = new RelayCommand(SelectAll);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        BrowseKeyCommand = new RelayCommand(BrowseKey);
        BrowsePlinkCommand = new RelayCommand(BrowsePlink);
        BrowseGitCommand = new RelayCommand(BrowseGit);
        PullSelectedCommand = new AsyncRelayCommand(PullSelectedAsync, CanPull);
        RefreshRepoBranchesCommand = new AsyncRelayCommand(RefreshRepoBranchesAsync, () => !IsBusy);
        OpenRepoCommand = new RelayCommand<RepoItemViewModel>(OpenRepo);
        RefreshSingleRepoBranchCommand = new AsyncRelayCommand<RepoItemViewModel>(RefreshSingleRepoBranchAsync, CanRefreshRepoBranch);
        SwitchBranchCommand = new AsyncRelayCommand<RepoItemViewModel>(SwitchBranchAsync, CanSwitchBranch);
        SwitchNestedBranchCommand = new AsyncRelayCommand<RepoItemViewModel>(SwitchNestedBranchAsync, CanSwitchBranch);
        OpenNestedRepoCommand = new RelayCommand<NestedRepoItemViewModel>(OpenNestedRepo);
        SwitchNestedRepoBranchCommand = new AsyncRelayCommand<NestedRepoItemViewModel>(SwitchNestedRepoBranchAsync, CanSwitchNestedRepoBranch);
        AddStartupFolderCommand = new RelayCommand(AddStartupFolder);
        AddSelectedReposToStartupCommand = new RelayCommand(AddSelectedReposToStartup, () => Repos.Any(r => r.IsSelected));
        RemoveSelectedStartupCommand = new RelayCommand(RemoveSelectedStartup, () => StartupFolders.Any(f => f.IsSelected));
        RefreshStartupBranchesCommand = new AsyncRelayCommand(RefreshStartupBranchesAsync, () => !IsBusy);
        MapStartupProjectCommand = new AsyncRelayCommand(() => RunStartupScriptsAsync(UserCodeDialogChoice.MapOnly), CanRunStartupScripts);
        StartStartupProjectCommand = new AsyncRelayCommand(() => RunStartupScriptsAsync(UserCodeDialogChoice.StartSystem), CanRunStartupScripts);
    }

    public ObservableCollection<RepoItemViewModel> Repos { get; }
    public ICollectionView ReposView => reposView;
    public ObservableCollection<StartupFolderItemViewModel> StartupFolders { get; }
    public ICollectionView StartupFoldersView => startupFoldersView;
    public ObservableCollection<LogEntryViewModel> LogEntries { get; }

    public IRelayCommand AddRepoCommand { get; }
    public IRelayCommand RemoveSelectedCommand { get; }
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand ClearSelectionCommand { get; }
    public IRelayCommand BrowseKeyCommand { get; }
    public IRelayCommand BrowsePlinkCommand { get; }
    public IRelayCommand BrowseGitCommand { get; }
    public IAsyncRelayCommand PullSelectedCommand { get; }
    public IAsyncRelayCommand RefreshRepoBranchesCommand { get; }
    public IRelayCommand<RepoItemViewModel> OpenRepoCommand { get; }
    public IAsyncRelayCommand<RepoItemViewModel> RefreshSingleRepoBranchCommand { get; }
    public IAsyncRelayCommand<RepoItemViewModel> SwitchBranchCommand { get; }
    public IAsyncRelayCommand<RepoItemViewModel> SwitchNestedBranchCommand { get; }
    public IRelayCommand<NestedRepoItemViewModel> OpenNestedRepoCommand { get; }
    public IAsyncRelayCommand<NestedRepoItemViewModel> SwitchNestedRepoBranchCommand { get; }
    public IRelayCommand AddStartupFolderCommand { get; }
    public IRelayCommand AddSelectedReposToStartupCommand { get; }
    public IRelayCommand RemoveSelectedStartupCommand { get; }
    public IAsyncRelayCommand RefreshStartupBranchesCommand { get; }
    public IAsyncRelayCommand MapStartupProjectCommand { get; }
    public IAsyncRelayCommand StartStartupProjectCommand { get; }

    [ObservableProperty]
    private string? sshKeyPath;

    [ObservableProperty]
    private string? sshUser;

    [ObservableProperty]
    private string? plinkPath;

    [ObservableProperty]
    private string? gitPath;

    [ObservableProperty]
    private bool autoPullOnSelection;

    [ObservableProperty]
    private bool singleSelection;

    [ObservableProperty]
    private bool includeNestedRepos;

    [ObservableProperty]
    private string? repoSearchText;

    [ObservableProperty]
    private string? startupSearchText;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = "Ready.";

    public void Initialize()
    {
        isInitializing = true;
        currentSettings = settingsService.Load();
        var shouldSeedStartupFolders = currentSettings.StartupFolderPaths.Count == 0 && currentSettings.RepoPaths.Count > 0;
        SshKeyPath = currentSettings.SshKeyPath;
        SshUser = currentSettings.SshUser;
        PlinkPath = currentSettings.PlinkPath;
        GitPath = currentSettings.GitPath;
        AutoPullOnSelection = currentSettings.AutoPullOnSelection;
        SingleSelection = currentSettings.SingleSelection;
        IncludeNestedRepos = currentSettings.IncludeNestedRepos;

        foreach (var path in currentSettings.RepoPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddRepoInternal(path, false);
        }

        var startupFolderPaths = shouldSeedStartupFolders
            ? currentSettings.RepoPaths
            : currentSettings.StartupFolderPaths;
        foreach (var path in startupFolderPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddStartupFolderInternal(path, false);
        }

        SortRepos();
        SortStartupFolders();
        ApplyCachedRepoState();
        SortRepos();
        ApplyCachedStartupState();

        var selected = currentSettings.SelectedRepoPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var repo in Repos)
        {
            repo.IsSelected = selected.Contains(repo.Path);
        }

        var selectedStartupFolders = currentSettings.SelectedStartupFolderPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var startupFolder in StartupFolders)
        {
            startupFolder.IsSelected = selectedStartupFolders.Contains(startupFolder.Path);
        }

        isInitializing = false;
        var loadedPath = settingsService.LastLoadedPath ?? settingsService.SettingsPath;
        AppendLog($"Loaded settings: {loadedPath}");
        if (shouldSeedStartupFolders)
        {
            AppendLog("[INFO] Startup script list was empty. Seeded it from repository list.");
            Save();
        }
        else
        {
            AppendLog("[CACHE] Loaded cached repository branches and startup project numbers.");
        }
    }

    public void Save()
    {
        Save(appendLog: true);
    }

    private void Save(bool appendLog)
    {
        currentSettings = new AppSettings
        {
            RepoPaths = Repos.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SelectedRepoPaths = Repos.Where(r => r.IsSelected).Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StartupFolderPaths = StartupFolders.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SelectedStartupFolderPaths = StartupFolders.Where(f => f.IsSelected).Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SshKeyPath = SshKeyPath,
            SshUser = SshUser,
            PlinkPath = PlinkPath,
            GitPath = GitPath,
            AutoPullOnSelection = AutoPullOnSelection,
            SingleSelection = SingleSelection,
            IncludeNestedRepos = IncludeNestedRepos,
            RepoBranchCache = BuildRepoBranchCache(),
            RepoNestedPathsCache = BuildRepoNestedPathsCache(),
            NestedRepoBranchCache = BuildNestedRepoBranchCache(),
            StartupProjectNumberCache = BuildStartupProjectNumberCache()
        };

        settingsService.Save(currentSettings);
        if (appendLog)
        {
            AppendLog($"Saved settings: {settingsService.SettingsPath}");
        }
    }

    private void AddRepo()
    {
        var initial = Repos.FirstOrDefault()?.Path;
        var selected = dialogService.PickFolder(initial);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        AddRepoInternal(selected, true);
    }

    private void AddRepoInternal(string path, bool select)
    {
        if (Repos.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            AppendLog($"Repo already exists: {path}");
            return;
        }

        var repo = new RepoItemViewModel(path);
        repo.PropertyChanged += OnRepoPropertyChanged;
        Repos.Add(repo);
        SortRepos();

        if (select)
        {
            repo.IsSelected = true;
        }

        RemoveSelectedCommand.NotifyCanExecuteChanged();
        PullSelectedCommand.NotifyCanExecuteChanged();

        if (!isInitializing)
        {
            Save();
            _ = RefreshBranchAsync(repo);
        }
    }

    private void AddStartupFolder()
    {
        var initial = StartupFolders.FirstOrDefault()?.Path ?? Repos.FirstOrDefault()?.Path;
        var selected = dialogService.PickFolder(initial);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        AddStartupFolderInternal(selected, true);
    }

    private void AddSelectedReposToStartup()
    {
        var selectedRepos = Repos.Where(r => r.IsSelected).ToList();
        if (selectedRepos.Count == 0)
        {
            AppendLog("No repositories selected.");
            return;
        }

        foreach (var repo in selectedRepos)
        {
            AddStartupFolderInternal(repo.Path, true);
        }
    }

    private void AddStartupFolderInternal(string path, bool select)
    {
        if (StartupFolders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            AppendLog($"Startup folder already exists: {path}");
            return;
        }

        var startupFolder = new StartupFolderItemViewModel(path);
        startupFolder.PropertyChanged += OnStartupFolderPropertyChanged;
        StartupFolders.Add(startupFolder);
        SortStartupFolders();

        if (select)
        {
            startupFolder.IsSelected = true;
        }

        RemoveSelectedStartupCommand.NotifyCanExecuteChanged();
        NotifyStartupCommandsCanExecuteChanged();

        if (!isInitializing)
        {
            Save();
            _ = RefreshStartupProjectNumberAsync(startupFolder);
        }
    }

    private void RemoveSelected()
    {
        var selected = Repos.Where(r => r.IsSelected).ToList();
        foreach (var repo in selected)
        {
            repo.PropertyChanged -= OnRepoPropertyChanged;
            Repos.Remove(repo);
        }

        RemoveSelectedCommand.NotifyCanExecuteChanged();
        PullSelectedCommand.NotifyCanExecuteChanged();
        AddSelectedReposToStartupCommand.NotifyCanExecuteChanged();

        if (!isInitializing)
        {
            Save();
        }
    }

    private void RemoveSelectedStartup()
    {
        var selected = StartupFolders.Where(f => f.IsSelected).ToList();
        foreach (var startupFolder in selected)
        {
            startupFolder.PropertyChanged -= OnStartupFolderPropertyChanged;
            StartupFolders.Remove(startupFolder);
        }

        RemoveSelectedStartupCommand.NotifyCanExecuteChanged();
        NotifyStartupCommandsCanExecuteChanged();

        if (!isInitializing)
        {
            Save();
        }
    }

    private void SelectAll()
    {
        if (SingleSelection)
        {
            var first = Repos.FirstOrDefault();
            if (first is null)
            {
                return;
            }

            foreach (var repo in Repos)
            {
                repo.IsSelected = repo == first;
            }

            AppendLog("Single selection enabled. Only the first repo is selected.");
        }
        else
        {
            foreach (var repo in Repos)
            {
                repo.IsSelected = true;
            }
        }
    }

    private void ClearSelection()
    {
        foreach (var repo in Repos)
        {
            repo.IsSelected = false;
        }
    }

    private void BrowseKey()
    {
        var selected = dialogService.PickFile("PPK Files (*.ppk)|*.ppk|All Files (*.*)|*.*", SshKeyPath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SshKeyPath = selected;
            AppendLog($"SSH key set: {selected}");
            Save();
        }
    }

    private void BrowsePlink()
    {
        var selected = dialogService.PickFile("EXE Files (*.exe)|*.exe|All Files (*.*)|*.*", PlinkPath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            PlinkPath = selected;
            AppendLog($"Plink set: {selected}");
            Save();
        }
    }

    private void BrowseGit()
    {
        var selected = dialogService.PickFile("EXE Files (*.exe)|*.exe|All Files (*.*)|*.*", GitPath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            GitPath = selected;
            AppendLog($"Git path set: {selected}");
            Save();
        }
    }

    private async Task PullSelectedAsync()
    {
        var selected = Repos.Where(r => r.IsSelected).Select(r => r.Path).ToList();
        if (selected.Count == 0)
        {
            AppendLog("No repositories selected.");
            return;
        }

        var pullTargets = new List<string>(selected);
        if (IncludeNestedRepos)
        {
            foreach (var repoPath in selected)
            {
                var nested = gitService.FindNestedRepositories(repoPath);
                if (nested.Count > 0)
                {
                    AppendLog($"Found {nested.Count} nested repositories under {repoPath}");
                    pullTargets.AddRange(nested);
                }
            }
        }

        pullTargets = pullTargets
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IsBusy = true;
        StatusText = "Pulling...";
        PullSelectedCommand.NotifyCanExecuteChanged();
        pullCts?.Cancel();
        pullCts = new CancellationTokenSource();

        try
        {
            var summary = await gitService.PullAsync(pullTargets, new GitExecutionOptions
            {
                GitPath = GitPath,
                SshKeyPath = SshKeyPath,
                SshUser = SshUser,
                PlinkPath = PlinkPath
            }, AppendLog, pullCts.Token, conflictInfo => dialogService.ConfirmPullConflict(
                conflictInfo,
                file => gitService.RestoreConflictFileAsync(
                    conflictInfo.RepoPath,
                    new GitExecutionOptions
                    {
                        GitPath = GitPath,
                        SshKeyPath = SshKeyPath,
                        SshUser = SshUser,
                        PlinkPath = PlinkPath
                    },
                    file,
                    AppendLog,
                    pullCts.Token),
                file => gitService.DeleteConflictFileAsync(
                    conflictInfo.RepoPath,
                    new GitExecutionOptions
                    {
                        GitPath = GitPath,
                        SshKeyPath = SshKeyPath,
                        SshUser = SshUser,
                        PlinkPath = PlinkPath
                    },
                    file,
                    AppendLog,
                    pullCts.Token)));

            AppendPullSummary(summary);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Ready.";
            PullSelectedCommand.NotifyCanExecuteChanged();
            _ = RefreshBranchesAsync(Repos.Where(r => r.IsSelected).ToList());
        }
    }

    private async Task RefreshRepoBranchesAsync()
    {
        IsBusy = true;
        StatusText = "Refreshing branches...";

        try
        {
            await RefreshBranchesAsync(Repos.ToList());
            AppendLog("[REFRESH] Repository branches refreshed.");
        }
        finally
        {
            IsBusy = false;
            StatusText = "Ready.";
        }
    }

    private async Task RefreshSingleRepoBranchAsync(RepoItemViewModel? repo)
    {
        if (repo is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = $"Refreshing branch: {repo.Name}";

        try
        {
            await RefreshBranchAsync(repo);
            AppendLog($"[REFRESH] Repository branch refreshed: {repo.Path}");
        }
        finally
        {
            IsBusy = false;
            StatusText = "Ready.";
        }
    }

    private async Task RunStartupScriptsAsync(UserCodeDialogChoice choice)
    {
        var selected = StartupFolders.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AppendLog("No startup folders selected.");
            return;
        }

        IsBusy = true;
        StatusText = choice == UserCodeDialogChoice.MapOnly ? "Mapping project..." : "Starting project...";
        NotifyStartupCommandsCanExecuteChanged();

        try
        {
            foreach (var startupFolder in selected)
            {
                startupFolder.RefreshValidation();
                if (!startupFolder.IsValid)
                {
                    AppendLog($"[SKIP] {startupFolder.ValidationMessage}: {startupFolder.Path}");
                    continue;
                }

                await RefreshStartupProjectNumberAsync(startupFolder);
                var projectNumber = startupFolder.ProjectNumber;
                if (string.IsNullOrWhiteSpace(projectNumber))
                {
                    AppendLog($"[WARN] Project number not found from current branch: {startupFolder.Path}");
                }

                try
                {
                    var startupTargetPath = startupFolder.StartupTargetPath;
                    if (string.IsNullOrWhiteSpace(startupTargetPath))
                    {
                        AppendLog($"[SKIP] Startup target not found: {startupFolder.Path}");
                        continue;
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = startupTargetPath,
                        WorkingDirectory = Path.GetDirectoryName(startupTargetPath) ?? startupFolder.Path,
                        UseShellExecute = true
                    };

                    if (!string.IsNullOrWhiteSpace(projectNumber) && IsBatchFile(startupTargetPath))
                    {
                        startInfo.Arguments = QuoteArgument(projectNumber);
                    }

                    var process = Process.Start(startInfo);
                    var targetName = Path.GetRelativePath(startupFolder.Path, startupTargetPath);
                    AppendLog(string.IsNullOrWhiteSpace(projectNumber)
                        ? $"[START] {targetName}: {startupFolder.Path}"
                        : $"[START] {targetName}: {startupFolder.Path}; ProjectNo={projectNumber}");

                    var clicked = await userCodeDialogService.ClickUserCodeDialogAsync(
                        choice,
                        TimeSpan.FromSeconds(120),
                        CancellationToken.None,
                        () => process is not null && HasExited(process));
                    if (clicked)
                    {
                        AppendLog(choice == UserCodeDialogChoice.MapOnly
                            ? "[DIALOG] Clicked No. Project mapped only."
                            : "[DIALOG] Clicked Yes. System start confirmed.");

                        if (choice == UserCodeDialogChoice.MapOnly)
                        {
                            await CloseMappedStartupWindowAsync(process);
                        }
                    }
                    else
                    {
                        AppendLog(process is not null && HasExited(process)
                            ? "[CANCEL] Startup console was closed before confirmation completed."
                            : "[WARN] User Code Error dialog was not found within 120 seconds.");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"[FAIL] startup target: {startupFolder.Path}; {ex.Message}");
                }
            }
        }
        finally
        {
            IsBusy = false;
            StatusText = "Ready.";
            NotifyStartupCommandsCanExecuteChanged();
        }
    }

    private async Task RefreshStartupBranchesAsync()
    {
        IsBusy = true;
        StatusText = "Refreshing startup project branches...";
        NotifyStartupCommandsCanExecuteChanged();

        try
        {
            await RefreshStartupProjectNumbersAsync(StartupFolders.ToList());
            AppendLog("[REFRESH] Startup project branches refreshed.");
        }
        finally
        {
            IsBusy = false;
            StatusText = "Ready.";
            NotifyStartupCommandsCanExecuteChanged();
        }
    }

    private async Task CloseMappedStartupWindowAsync(Process? process)
    {
        var processId = TryGetProcessId(process);
        var closedWindow = await userCodeDialogService.CloseUserCodeErrorConsoleAsync(processId, TimeSpan.FromSeconds(5), CancellationToken.None);
        if (!closedWindow)
        {
            closedWindow = await userCodeDialogService.CloseAnyUserCodeErrorConsoleAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        }

        if (closedWindow)
        {
            AppendLog("[WINDOW] Closed User Code Error console.");
            return;
        }

        if (process is null || HasExited(process))
        {
            return;
        }

        try
        {
            if (process.CloseMainWindow())
            {
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
                AppendLog("[WINDOW] Closed startup console.");
                return;
            }
        }
        catch
        {
            // Fall back to killing the paused mapping process below.
        }

        try
        {
            if (!HasExited(process))
            {
                process.Kill(true);
                AppendLog("[WINDOW] Terminated paused startup console.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[WARN] Unable to close startup console: {ex.Message}");
        }
    }

    private static int? TryGetProcessId(Process? process)
    {
        try
        {
            return process?.Id;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private bool CanPull() => !IsBusy && Repos.Any(r => r.IsSelected);
    private bool CanSwitchBranch(RepoItemViewModel? repo) => !IsBusy && repo is not null;
    private bool CanSwitchNestedRepoBranch(NestedRepoItemViewModel? repo) => !IsBusy && repo is not null;
    private bool CanRunStartupScripts() => !IsBusy && StartupFolders.Any(f => f.IsSelected);

    private bool FilterRepo(object obj)
    {
        if (obj is not RepoItemViewModel repo)
        {
            return false;
        }

        var keyword = RepoSearchText?.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            foreach (var nestedRepo in repo.NestedRepos)
            {
                nestedRepo.IsSearchVisible = true;
            }

            return true;
        }

        var repoMatches = MatchesSearch(repo.BranchName, keyword)
            || MatchesSearch(repo.Name, keyword)
            || MatchesSearch(repo.Path, keyword)
            || MatchesSearch(repo.ValidationMessage, keyword);
        var nestedMatches = false;

        foreach (var nestedRepo in repo.NestedRepos)
        {
            var nestedMatch = MatchesSearch(nestedRepo.Name, keyword)
                || MatchesSearch(nestedRepo.Path, keyword)
                || MatchesSearch(nestedRepo.BranchName, keyword);
            nestedRepo.IsSearchVisible = repoMatches || nestedMatch;
            nestedMatches |= nestedMatch;
        }

        return repoMatches || nestedMatches;
    }

    private void RefreshRepoFilter()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(reposView.Refresh);
            return;
        }

        reposView.Refresh();
    }

    private static bool MatchesSearch(string? value, string keyword)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterStartupFolder(object obj)
    {
        if (obj is not StartupFolderItemViewModel folder)
        {
            return false;
        }

        var keyword = StartupSearchText?.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return MatchesSearch(folder.ProjectNumber, keyword)
            || MatchesSearch(folder.ProjectNumberDisplay, keyword)
            || MatchesSearch(folder.Name, keyword)
            || MatchesSearch(folder.Path, keyword)
            || MatchesSearch(folder.ValidationMessage, keyword);
    }

    private void RefreshStartupFolderFilter()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(startupFoldersView.Refresh);
            return;
        }

        startupFoldersView.Refresh();
    }

    private void SortRepos()
    {
        var ordered = Repos
            .OrderBy(repo => TryGetRepositoryNumber(repo.BranchName ?? string.Empty, repo.Path, out _) ? 0 : 1)
            .ThenBy(repo => TryGetRepositoryNumber(repo.BranchName ?? string.Empty, repo.Path, out var number) ? number : int.MaxValue)
            .ThenBy(repo => repo.BranchName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(repo => repo.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var currentIndex = Repos.IndexOf(ordered[targetIndex]);
            if (currentIndex != targetIndex)
            {
                Repos.Move(currentIndex, targetIndex);
            }
        }
    }

    private void SortStartupFolders()
    {
        SortByRepositoryNumber(StartupFolders, folder => folder.Name, folder => folder.Path);
    }

    private static void SortByRepositoryNumber<T>(
        ObservableCollection<T> collection,
        Func<T, string> nameSelector,
        Func<T, string> pathSelector)
    {
        var ordered = collection
            .OrderBy(item => TryGetRepositoryNumber(nameSelector(item), pathSelector(item), out _) ? 0 : 1)
            .ThenBy(item => TryGetRepositoryNumber(nameSelector(item), pathSelector(item), out var number) ? number : int.MaxValue)
            .ThenBy(nameSelector, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pathSelector, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var currentIndex = collection.IndexOf(ordered[targetIndex]);
            if (currentIndex != targetIndex)
            {
                collection.Move(currentIndex, targetIndex);
            }
        }
    }

    private static bool TryGetRepositoryNumber(string name, string path, out int number)
    {
        return TryGetRepositoryNumber(name, out number) || TryGetRepositoryNumber(path, out number);
    }

    private static bool TryGetRepositoryNumber(string value, out int number)
    {
        const int digitCount = 5;

        for (var i = 0; i <= value.Length - digitCount - 1; i++)
        {
            if (value[i] is not ('S' or 's'))
            {
                continue;
            }

            var allDigits = true;
            for (var j = 1; j <= digitCount; j++)
            {
                if (!char.IsDigit(value[i + j]))
                {
                    allDigits = false;
                    break;
                }
            }

            if (!allDigits)
            {
                continue;
            }

            return int.TryParse(value.AsSpan(i + 1, digitCount), out number);
        }

        number = 0;
        return false;
    }

    private void NotifyStartupCommandsCanExecuteChanged()
    {
        MapStartupProjectCommand.NotifyCanExecuteChanged();
        StartStartupProjectCommand.NotifyCanExecuteChanged();
    }

    private void OnRepoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepoItemViewModel.IsSelected))
        {
            if (SingleSelection && sender is RepoItemViewModel changed && changed.IsSelected)
            {
                foreach (var repo in Repos)
                {
                    if (!ReferenceEquals(repo, changed))
                    {
                        repo.IsSelected = false;
                    }
                }
            }

            RemoveSelectedCommand.NotifyCanExecuteChanged();
            PullSelectedCommand.NotifyCanExecuteChanged();
            AddSelectedReposToStartupCommand.NotifyCanExecuteChanged();

            if (!isInitializing)
            {
                Save();

                if (AutoPullOnSelection && !IsBusy)
                {
                    _ = PullSelectedAsync();
                }
            }
        }
    }

    private void OnStartupFolderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartupFolderItemViewModel.IsSelected))
        {
            if (sender is StartupFolderItemViewModel changed && changed.IsSelected)
            {
                foreach (var startupFolder in StartupFolders)
                {
                    if (!ReferenceEquals(startupFolder, changed))
                    {
                        startupFolder.IsSelected = false;
                    }
                }
            }

            RemoveSelectedStartupCommand.NotifyCanExecuteChanged();
            NotifyStartupCommandsCanExecuteChanged();

            if (!isInitializing)
            {
                Save();
            }
        }
    }

    partial void OnSingleSelectionChanged(bool value)
    {
        if (value)
        {
            var selected = Repos.FirstOrDefault(r => r.IsSelected);
            foreach (var repo in Repos)
            {
                repo.IsSelected = repo == selected;
            }
        }

        RemoveSelectedCommand.NotifyCanExecuteChanged();
        PullSelectedCommand.NotifyCanExecuteChanged();
        AddSelectedReposToStartupCommand.NotifyCanExecuteChanged();
        if (!isInitializing)
        {
            Save();
        }
    }

    partial void OnIncludeNestedReposChanged(bool value)
    {
        if (!isInitializing)
        {
            Save();
        }
    }

    partial void OnRepoSearchTextChanged(string? value)
    {
        RefreshRepoFilter();
    }

    partial void OnStartupSearchTextChanged(string? value)
    {
        RefreshStartupFolderFilter();
    }

    partial void OnAutoPullOnSelectionChanged(bool value)
    {
        if (!isInitializing)
        {
            Save();
        }
    }

    partial void OnSshKeyPathChanged(string? value)
    {
        if (!isInitializing)
        {
            Save();
        }
    }

    partial void OnSshUserChanged(string? value)
    {
        if (!isInitializing)
        {
            Save();
        }
    }

    partial void OnPlinkPathChanged(string? value)
    {
        if (!isInitializing)
        {
            Save();
        }
    }

    partial void OnGitPathChanged(string? value)
    {
        if (!isInitializing)
        {
            Save();
        }
    }

    private void AppendLog(string message)
    {
        void Add()
        {
            LogEntries.Add(new LogEntryViewModel(message));
            if (LogEntries.Count > 2000)
            {
                LogEntries.RemoveAt(0);
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Add);
        }
        else
        {
            Add();
        }
    }

    private void AppendPullSummary(PullSummary summary)
    {
        if (summary.Succeeded.Count == 0 && summary.Failed.Count == 0 && !summary.Cancelled)
        {
            return;
        }

        AppendLog($"[SUMMARY] Pull completed. Success: {summary.Succeeded.Count}, Failed: {summary.Failed.Count}.");
        if (summary.Cancelled)
        {
            AppendLog("[SUMMARY] Pull was cancelled by user.");
        }

        if (summary.Succeeded.Count > 0)
        {
            AppendLog($"[SUMMARY] Success list:");
            foreach (var repo in summary.Succeeded)
            {
                AppendLog($"[SUMMARY] {repo}");
            }
        }
        else
        {
            AppendLog("[SUMMARY] Success list: (none)");
        }

        if (summary.Failed.Count > 0)
        {
            AppendLog("[SUMMARY] Failed list:");
            foreach (var repo in summary.Failed)
            {
                AppendLog($"[SUMMARY][FAIL] {repo}");
            }
        }
        else
        {
            AppendLog("[SUMMARY] Failed list: (none)");
        }
    }

    private void OpenRepo(RepoItemViewModel? repo)
    {
        if (repo is null)
        {
            return;
        }

        if (!Directory.Exists(repo.Path))
        {
            AppendLog($"[SKIP] Repo not found: {repo.Path}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = repo.Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[FAIL] Open repo: {ex.Message}");
        }
    }

    private void OpenNestedRepo(NestedRepoItemViewModel? repo)
    {
        if (repo is null)
        {
            return;
        }

        if (!Directory.Exists(repo.Path))
        {
            AppendLog($"[SKIP] Repo not found: {repo.Path}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = repo.Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[FAIL] Open nested repo: {ex.Message}");
        }
    }

    private async Task SwitchBranchAsync(RepoItemViewModel? repo)
    {
        if (repo is null)
        {
            return;
        }

        if (!Directory.Exists(repo.Path))
        {
            AppendLog($"[SKIP] Repo not found: {repo.Path}");
            return;
        }

        IsBusy = true;
        StatusText = "切换分支中...";
        PullSelectedCommand.NotifyCanExecuteChanged();
        SwitchBranchCommand.NotifyCanExecuteChanged();

        try
        {
            var branches = await gitService.GetBranchesAsync(repo.Path, new GitExecutionOptions
            {
                GitPath = GitPath,
                SshKeyPath = SshKeyPath,
                SshUser = SshUser,
                PlinkPath = PlinkPath
            }, CancellationToken.None);

            if (branches.Count == 0)
            {
                AppendLog($"[SKIP] No branches found: {repo.Path}");
                return;
            }

            var selectedBranch = dialogService.PickBranch(branches, repo.Name);
            if (string.IsNullOrWhiteSpace(selectedBranch))
            {
                return;
            }

            AppendLog($"[START] git checkout {selectedBranch}: {repo.Path}");
            var checkoutOptions = new GitExecutionOptions
            {
                GitPath = GitPath,
                SshKeyPath = SshKeyPath,
                SshUser = SshUser,
                PlinkPath = PlinkPath
            };
            var rc = await gitService.CheckoutBranchAsync(
                repo.Path,
                checkoutOptions,
                AppendLog,
                CancellationToken.None,
                selectedBranch,
                conflictInfo => dialogService.ConfirmCheckoutConflict(
                    conflictInfo,
                    file => gitService.RestoreConflictFileAsync(
                        conflictInfo.RepoPath,
                        checkoutOptions,
                        file,
                        AppendLog,
                        CancellationToken.None),
                    file => gitService.DeleteConflictFileAsync(
                        conflictInfo.RepoPath,
                        checkoutOptions,
                        file,
                        AppendLog,
                        CancellationToken.None)));

            AppendLog(rc == 0
                ? $"[DONE] git checkout {selectedBranch}: {repo.Path}"
                : $"[FAIL] git checkout ({rc}): {repo.Path}");

            await RefreshBranchAsync(repo);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Ready.";
            PullSelectedCommand.NotifyCanExecuteChanged();
            SwitchBranchCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SwitchNestedBranchAsync(RepoItemViewModel? repo)
    {
        if (repo is null)
        {
            return;
        }

        if (!Directory.Exists(repo.Path))
        {
            AppendLog($"[SKIP] Repo not found: {repo.Path}");
            return;
        }

        IsBusy = true;
        StatusText = "Switching nested repository branch...";

        try
        {
            var nestedRepos = gitService.FindNestedRepositories(repo.Path);
            if (nestedRepos.Count == 0)
            {
                AppendLog($"[SKIP] No nested repositories found: {repo.Path}");
                return;
            }

            var selectedRepoPath = dialogService.PickNestedRepository(nestedRepos, repo.Name);
            if (string.IsNullOrWhiteSpace(selectedRepoPath))
            {
                return;
            }

            var checkoutOptions = new GitExecutionOptions
            {
                GitPath = GitPath,
                SshKeyPath = SshKeyPath,
                SshUser = SshUser,
                PlinkPath = PlinkPath
            };
            var nestedRepoName = Path.GetFileName(selectedRepoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var branches = await gitService.GetBranchesAsync(selectedRepoPath, checkoutOptions, CancellationToken.None);
            if (branches.Count == 0)
            {
                AppendLog($"[SKIP] No branches found: {selectedRepoPath}");
                return;
            }

            var selectedBranch = dialogService.PickBranch(branches, nestedRepoName);
            if (string.IsNullOrWhiteSpace(selectedBranch))
            {
                return;
            }

            AppendLog($"[START] git checkout {selectedBranch}: {selectedRepoPath}");
            var rc = await gitService.CheckoutBranchAsync(
                selectedRepoPath,
                checkoutOptions,
                AppendLog,
                CancellationToken.None,
                selectedBranch,
                conflictInfo => dialogService.ConfirmCheckoutConflict(
                    conflictInfo,
                    file => gitService.RestoreConflictFileAsync(
                        conflictInfo.RepoPath,
                        checkoutOptions,
                        file,
                        AppendLog,
                        CancellationToken.None),
                    file => gitService.DeleteConflictFileAsync(
                        conflictInfo.RepoPath,
                        checkoutOptions,
                        file,
                        AppendLog,
                        CancellationToken.None)));

            AppendLog(rc == 0
                ? $"[DONE] git checkout {selectedBranch}: {selectedRepoPath}"
                : $"[FAIL] git checkout ({rc}): {selectedRepoPath}");
        }
        finally
        {
            IsBusy = false;
            StatusText = "Ready.";
        }
    }

    private async Task SwitchNestedRepoBranchAsync(NestedRepoItemViewModel? repo)
    {
        if (repo is null)
        {
            return;
        }

        if (!Directory.Exists(repo.Path))
        {
            AppendLog($"[SKIP] Repo not found: {repo.Path}");
            return;
        }

        IsBusy = true;
        StatusText = "Switching nested repository branch...";

        try
        {
            var checkoutOptions = new GitExecutionOptions
            {
                GitPath = GitPath,
                SshKeyPath = SshKeyPath,
                SshUser = SshUser,
                PlinkPath = PlinkPath
            };
            var branches = await gitService.GetBranchesAsync(repo.Path, checkoutOptions, CancellationToken.None);
            if (branches.Count == 0)
            {
                AppendLog($"[SKIP] No branches found: {repo.Path}");
                return;
            }

            var selectedBranch = dialogService.PickBranch(branches, repo.Name);
            if (string.IsNullOrWhiteSpace(selectedBranch))
            {
                return;
            }

            AppendLog($"[START] git checkout {selectedBranch}: {repo.Path}");
            var rc = await gitService.CheckoutBranchAsync(
                repo.Path,
                checkoutOptions,
                AppendLog,
                CancellationToken.None,
                selectedBranch,
                conflictInfo => dialogService.ConfirmCheckoutConflict(
                    conflictInfo,
                    file => gitService.RestoreConflictFileAsync(
                        conflictInfo.RepoPath,
                        checkoutOptions,
                        file,
                        AppendLog,
                        CancellationToken.None),
                    file => gitService.DeleteConflictFileAsync(
                        conflictInfo.RepoPath,
                        checkoutOptions,
                        file,
                        AppendLog,
                        CancellationToken.None)));

            AppendLog(rc == 0
                ? $"[DONE] git checkout {selectedBranch}: {repo.Path}"
                : $"[FAIL] git checkout ({rc}): {repo.Path}");

            await RefreshNestedBranchAsync(repo);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Ready.";
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        PullSelectedCommand.NotifyCanExecuteChanged();
        RefreshRepoBranchesCommand.NotifyCanExecuteChanged();
        RefreshSingleRepoBranchCommand.NotifyCanExecuteChanged();
        SwitchBranchCommand.NotifyCanExecuteChanged();
        SwitchNestedBranchCommand.NotifyCanExecuteChanged();
        SwitchNestedRepoBranchCommand.NotifyCanExecuteChanged();
        RefreshStartupBranchesCommand.NotifyCanExecuteChanged();
        NotifyStartupCommandsCanExecuteChanged();
    }

    private async Task RefreshBranchesAsync(IEnumerable<RepoItemViewModel> repos)
    {
        foreach (var repo in repos)
        {
            await RefreshBranchAsync(repo);
        }
    }

    private async Task RefreshBranchAsync(RepoItemViewModel repo)
    {
        void SetLoading(bool value)
        {
            repo.IsBranchLoading = value;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => SetLoading(true));
        }
        else
        {
            SetLoading(true);
        }

        string? branch;
        try
        {
            branch = await gitService.GetCurrentBranchAsync(repo.Path, new GitExecutionOptions
            {
                GitPath = GitPath
            }, CancellationToken.None);
        }
        catch
        {
            branch = null;
        }

        void Apply()
        {
            repo.BranchName = branch;
            repo.IsBranchLoading = false;
            SortRepos();
        }

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Apply);
        }
        else
        {
            Apply();
        }

        await RefreshNestedRepositoriesAsync(repo);
        Save(appendLog: false);
        RefreshRepoFilter();
    }

    private async Task RefreshNestedRepositoriesAsync(RepoItemViewModel repo)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        IReadOnlyList<string> nestedPaths;
        try
        {
            nestedPaths = gitService.FindNestedRepositories(repo.Path);
        }
        catch
        {
            nestedPaths = Array.Empty<string>();
        }

        var nestedSet = nestedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        void ApplyNestedRepos()
        {
            for (var i = repo.NestedRepos.Count - 1; i >= 0; i--)
            {
                if (!nestedSet.Contains(repo.NestedRepos[i].Path))
                {
                    repo.NestedRepos.RemoveAt(i);
                }
            }

            foreach (var nestedPath in nestedPaths)
            {
                if (repo.NestedRepos.Any(item => string.Equals(item.Path, nestedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                repo.NestedRepos.Add(new NestedRepoItemViewModel(nestedPath));
            }
        }

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(ApplyNestedRepos);
        }
        else
        {
            ApplyNestedRepos();
        }

        foreach (var nestedRepo in repo.NestedRepos.ToList())
        {
            await RefreshNestedBranchAsync(nestedRepo);
        }

        Save(appendLog: false);
        RefreshRepoFilter();
    }

    private async Task RefreshNestedBranchAsync(NestedRepoItemViewModel repo)
    {
        void SetLoading(bool value)
        {
            repo.IsBranchLoading = value;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => SetLoading(true));
        }
        else
        {
            SetLoading(true);
        }

        string? branch;
        try
        {
            branch = await gitService.GetCurrentBranchAsync(repo.Path, new GitExecutionOptions
            {
                GitPath = GitPath
            }, CancellationToken.None);
        }
        catch
        {
            branch = null;
        }

        void Apply()
        {
            repo.BranchName = branch;
            repo.IsBranchLoading = false;
        }

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Apply);
        }
        else
        {
            Apply();
        }

        Save(appendLog: false);
        RefreshRepoFilter();
    }

    private async Task RefreshStartupProjectNumbersAsync(IEnumerable<StartupFolderItemViewModel> startupFolders)
    {
        foreach (var startupFolder in startupFolders)
        {
            await RefreshStartupProjectNumberAsync(startupFolder);
        }
    }

    private async Task RefreshStartupProjectNumberAsync(StartupFolderItemViewModel startupFolder)
    {
        void SetLoading(bool value)
        {
            startupFolder.IsProjectNumberLoading = value;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => SetLoading(true));
        }
        else
        {
            SetLoading(true);
        }

        string? projectNumber;
        try
        {
            projectNumber = await gitService.GetCurrentBranchAsync(startupFolder.Path, new GitExecutionOptions
            {
                GitPath = GitPath
            }, CancellationToken.None);
        }
        catch
        {
            projectNumber = null;
        }

        void Apply()
        {
            startupFolder.ProjectNumber = projectNumber;
            startupFolder.IsProjectNumberLoading = false;
            startupFolder.RefreshValidation();
        }

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Apply);
        }
        else
        {
            Apply();
        }

        Save(appendLog: false);
        RefreshStartupFolderFilter();
    }

    private bool CanRefreshRepoBranch(RepoItemViewModel? repo)
        => !IsBusy && repo is not null;

    private void ApplyCachedRepoState()
    {
        foreach (var repo in Repos)
        {
            if (currentSettings.RepoBranchCache.TryGetValue(repo.Path, out var branch))
            {
                repo.BranchName = branch;
            }

            if (!currentSettings.RepoNestedPathsCache.TryGetValue(repo.Path, out var nestedPaths) || nestedPaths is null)
            {
                continue;
            }

            foreach (var nestedPath in nestedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (repo.NestedRepos.Any(item => string.Equals(item.Path, nestedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var nestedRepo = new NestedRepoItemViewModel(nestedPath);
                if (currentSettings.NestedRepoBranchCache.TryGetValue(nestedPath, out var nestedBranch))
                {
                    nestedRepo.BranchName = nestedBranch;
                }

                repo.NestedRepos.Add(nestedRepo);
            }
        }
    }

    private void ApplyCachedStartupState()
    {
        foreach (var startupFolder in StartupFolders)
        {
            if (currentSettings.StartupProjectNumberCache.TryGetValue(startupFolder.Path, out var projectNumber))
            {
                startupFolder.ProjectNumber = projectNumber;
            }

            startupFolder.RefreshValidation();
        }
    }

    private Dictionary<string, string?> BuildRepoBranchCache()
    {
        return Repos.ToDictionary(repo => repo.Path, repo => repo.BranchName, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, List<string>> BuildRepoNestedPathsCache()
    {
        return Repos.ToDictionary(
            repo => repo.Path,
            repo => repo.NestedRepos.Select(nested => nested.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string?> BuildNestedRepoBranchCache()
    {
        return Repos
            .SelectMany(repo => repo.NestedRepos)
            .GroupBy(repo => repo.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().BranchName, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string?> BuildStartupProjectNumberCache()
    {
        return StartupFolders.ToDictionary(folder => folder.Path, folder => folder.ProjectNumber, StringComparer.OrdinalIgnoreCase);
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static bool IsBatchFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase);
    }
}
