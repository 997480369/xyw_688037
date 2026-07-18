using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GitPullTool.Models;

namespace GitPullTool.Windows;

public partial class PullConflictWindow : Window
{
    private readonly PullConflictInfo conflictInfo;
    private readonly Func<PullConflictFileChange, Task<bool>> restoreFileAsync;
    private readonly Func<PullConflictFileChange, Task<bool>> deleteFileAsync;
    private bool isSyncingScroll;

    public PullConflictWindow(
        PullConflictInfo conflictInfo,
        Func<PullConflictFileChange, Task<bool>> restoreFileAsync,
        Func<PullConflictFileChange, Task<bool>> deleteFileAsync,
        string operationName)
    {
        InitializeComponent();

        this.conflictInfo = conflictInfo;
        this.restoreFileAsync = restoreFileAsync;
        this.deleteFileAsync = deleteFileAsync;

        Title = string.IsNullOrWhiteSpace(conflictInfo.RepoName)
            ? "\u786e\u8ba4\u8fd8\u539f\u672c\u5730\u66f4\u6539"
            : $"\u786e\u8ba4\u8fd8\u539f\u672c\u5730\u66f4\u6539 - {conflictInfo.RepoName}";

        RepoTextBlock.Text = $"\u4ed3\u5e93\uff1a{conflictInfo.RepoPath}";
        SummaryTextBlock.Text = $"\u68c0\u6d4b\u5230\u4ee5\u4e0b\u672c\u5730\u66f4\u6539\u4f1a\u88ab\u672c\u6b21{operationName}\u8986\u76d6\u3002";
        HintActionTextBlock.Text =
            $"\u53f3\u952e\u5df2\u8ddf\u8e2a\u6587\u4ef6\u53ef\u9009\u62e9\u8fd8\u539f\u6216\u5220\u9664\uff0c\u672a\u8ddf\u8e2a\u6587\u4ef6\u53ef\u5220\u9664\u3002\u786e\u5b9a\u4f1a\u5904\u7406\u5269\u4f59\u6587\u4ef6\u5e76\u7ee7\u7eed{operationName}\u3002";
        RefreshFileGroups();

        RefreshSelection();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender == TrackedFileList && TrackedFileList.SelectedItem is not null)
        {
            UntrackedFileList.SelectedItem = null;
        }
        else if (sender == UntrackedFileList && UntrackedFileList.SelectedItem is not null)
        {
            TrackedFileList.SelectedItem = null;
        }

        UpdatePreview();
    }

    private void OnFileListRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var item = ItemsControl.ContainerFromElement(TrackedFileList, source) as ListBoxItem
            ?? ItemsControl.ContainerFromElement(UntrackedFileList, source) as ListBoxItem;
        if (item?.DataContext is PullConflictFileChange file)
        {
            SelectFile(file);
        }
    }

    private async void OnRestoreFileClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFile() is not PullConflictFileChange file)
        {
            return;
        }

        if (file.IsUntracked)
        {
            return;
        }

        await ResolveSelectedFileAsync(file, "\u8fd8\u539f", restoreFileAsync);
    }

    private async void OnDeleteFileClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedFile() is not PullConflictFileChange file)
        {
            return;
        }

        await ResolveSelectedFileAsync(file, "\u5220\u9664", deleteFileAsync);
    }

    private async void OnRestoreTrackedAllClick(object sender, RoutedEventArgs e)
    {
        await ResolveFileGroupAsync(
            conflictInfo.Files.Where(file => !file.IsUntracked).ToList(),
            "\u8fd8\u539f",
            restoreFileAsync);
    }

    private async void OnDeleteTrackedAllClick(object sender, RoutedEventArgs e)
    {
        await ResolveFileGroupAsync(
            conflictInfo.Files.Where(file => !file.IsUntracked).ToList(),
            "\u5220\u9664",
            deleteFileAsync);
    }

    private async void OnDeleteUntrackedAllClick(object sender, RoutedEventArgs e)
    {
        await ResolveFileGroupAsync(
            conflictInfo.Files.Where(file => file.IsUntracked).ToList(),
            "\u5220\u9664",
            deleteFileAsync);
    }

    private async Task ResolveSelectedFileAsync(
        PullConflictFileChange file,
        string actionName,
        Func<PullConflictFileChange, Task<bool>> actionAsync)
    {
        var confirmed = System.Windows.MessageBox.Show(
            this,
            $"\u786e\u5b9a\u8981{actionName} {file.RelativePath} \u5417\uff1f",
            $"{actionName}\u6587\u4ef6",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.OK)
        {
            return;
        }

        IsEnabled = false;
        try
        {
            var resolved = await actionAsync(file);
            if (!resolved)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"{actionName}\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002",
                    $"{actionName}\u6587\u4ef6",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            conflictInfo.Files.Remove(file);
            if (!CloseIfResolved())
            {
                RefreshFileGroups();
                RefreshSelection();
            }
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async Task ResolveFileGroupAsync(
        System.Collections.Generic.IReadOnlyList<PullConflictFileChange> files,
        string actionName,
        Func<PullConflictFileChange, Task<bool>> actionAsync)
    {
        if (files.Count == 0)
        {
            return;
        }

        var confirmed = System.Windows.MessageBox.Show(
            this,
            $"\u786e\u5b9a\u8981{actionName}\u8fd9 {files.Count} \u4e2a\u6587\u4ef6\u5417\uff1f",
            $"{actionName}\u6587\u4ef6",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.OK)
        {
            return;
        }

        IsEnabled = false;
        try
        {
            foreach (var file in files.ToList())
            {
                var resolved = await actionAsync(file);
                if (!resolved)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        $"{actionName}\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002",
                        $"{actionName}\u6587\u4ef6",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    break;
                }

                conflictInfo.Files.Remove(file);
            }

            RefreshFileGroups();
            if (!CloseIfResolved())
            {
                RefreshSelection();
            }
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private bool CloseIfResolved()
    {
        if (conflictInfo.Files.Count != 0)
        {
            return false;
        }

        DialogResult = true;
        Close();
        return true;
    }

    private void RefreshSelection()
    {
        if (conflictInfo.Files.Count == 0)
        {
            TrackedFileList.SelectedItem = null;
            UntrackedFileList.SelectedItem = null;
            UpdatePreview();
            ConfirmButton.IsEnabled = false;
            RestoreTrackedButton.IsEnabled = false;
            DeleteTrackedButton.IsEnabled = false;
            DeleteUntrackedButton.IsEnabled = false;
            return;
        }

        if (GetSelectedFile() is not PullConflictFileChange)
        {
            SelectFile(conflictInfo.Files.First());
        }

        ConfirmButton.IsEnabled = true;
        RestoreTrackedButton.IsEnabled = conflictInfo.Files.Any(file => !file.IsUntracked);
        DeleteTrackedButton.IsEnabled = conflictInfo.Files.Any(file => !file.IsUntracked);
        DeleteUntrackedButton.IsEnabled = conflictInfo.Files.Any(file => file.IsUntracked);
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (GetSelectedFile() is not PullConflictFileChange file)
        {
            DetailsTextBlock.Text = "\u6ca1\u6709\u53ef\u5904\u7406\u7684\u51b2\u7a81\u6587\u4ef6\u3002";
            HintTextBlock.Text = string.Empty;
            LeftGroupBox.Header = "HEAD";
            RightGroupBox.Header = "Working Tree";
            LeftPreviewItems.ItemsSource = null;
            RightPreviewItems.ItemsSource = null;
            return;
        }

        DetailsTextBlock.Text = $"{file.RelativePath} ({file.StatusText})";
        HintTextBlock.Text = file.DetailsText;
        LeftGroupBox.Header = file.LeftTitle;
        RightGroupBox.Header = file.RightTitle;
        LeftPreviewItems.ItemsSource = file.LeftLines;
        RightPreviewItems.ItemsSource = file.RightLines;
    }

    private void RefreshFileGroups()
    {
        TrackedFileList.ItemsSource = conflictInfo.Files.Where(file => !file.IsUntracked).ToList();
        UntrackedFileList.ItemsSource = conflictInfo.Files.Where(file => file.IsUntracked).ToList();
    }

    private PullConflictFileChange? GetSelectedFile()
    {
        return TrackedFileList.SelectedItem as PullConflictFileChange
            ?? UntrackedFileList.SelectedItem as PullConflictFileChange;
    }

    private void SelectFile(PullConflictFileChange file)
    {
        if (file.IsUntracked)
        {
            TrackedFileList.SelectedItem = null;
            UntrackedFileList.SelectedItem = file;
        }
        else
        {
            UntrackedFileList.SelectedItem = null;
            TrackedFileList.SelectedItem = file;
        }
    }

    private void OnLeftScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncScroll(LeftScrollViewer, RightScrollViewer, e);
    }

    private void OnRightScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncScroll(RightScrollViewer, LeftScrollViewer, e);
    }

    private void SyncScroll(ScrollViewer source, ScrollViewer target, ScrollChangedEventArgs e)
    {
        if (isSyncingScroll)
        {
            return;
        }

        if (e.HorizontalChange == 0 && e.VerticalChange == 0)
        {
            return;
        }

        isSyncingScroll = true;
        try
        {
            if (!AreClose(target.HorizontalOffset, source.HorizontalOffset))
            {
                target.ScrollToHorizontalOffset(source.HorizontalOffset);
            }

            if (!AreClose(target.VerticalOffset, source.VerticalOffset))
            {
                target.ScrollToVerticalOffset(source.VerticalOffset);
            }
        }
        finally
        {
            isSyncingScroll = false;
        }
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) < 0.5;
    }
}
