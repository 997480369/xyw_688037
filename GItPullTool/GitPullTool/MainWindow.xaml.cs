using System.Windows;
using GitPullTool.Services;
using GitPullTool.ViewModels;

namespace GitPullTool;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();

        viewModel = new MainViewModel(new DialogService(), new GitService(), new SettingsService(), new UserCodeDialogService());
        DataContext = viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        viewModel.Initialize();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        viewModel.Save();
    }
}
