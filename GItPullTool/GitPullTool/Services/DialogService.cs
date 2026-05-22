using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GitPullTool.Models;
using GitPullTool.Windows;

namespace GitPullTool.Services;

public sealed class DialogService
{
    public string? PickFolder(string? initialPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a Git repository folder",
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.SelectedPath = initialPath;
        }

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? PickFile(string filter, string? initialPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(initialPath);
            dialog.FileName = Path.GetFileName(initialPath);
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickBranch(IEnumerable<string> branches, string? repoName)
    {
        var window = new BranchPickerWindow(branches, repoName)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return window.ShowDialog() == true ? window.SelectedBranch : null;
    }

    public string? PickNestedRepository(IEnumerable<string> repoPaths, string? repoName)
    {
        var window = new BranchPickerWindow(
            repoPaths,
            repoName,
            "\u9009\u62e9\u5b50\u4ed3\u5e93",
            "\u8bf7\u9009\u62e9\u5b50\u4ed3\u5e93\uff1a")
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return window.ShowDialog() == true ? window.SelectedBranch : null;
    }

    public bool ConfirmPullConflict(
        PullConflictInfo conflictInfo,
        Func<PullConflictFileChange, Task<bool>> restoreFileAsync,
        Func<PullConflictFileChange, Task<bool>> deleteFileAsync)
    {
        return ConfirmConflict(conflictInfo, restoreFileAsync, deleteFileAsync, "\u62c9\u53d6");
    }

    public bool ConfirmCheckoutConflict(
        PullConflictInfo conflictInfo,
        Func<PullConflictFileChange, Task<bool>> restoreFileAsync,
        Func<PullConflictFileChange, Task<bool>> deleteFileAsync)
    {
        return ConfirmConflict(conflictInfo, restoreFileAsync, deleteFileAsync, "\u5207\u6362\u5206\u652f");
    }

    private static bool ConfirmConflict(
        PullConflictInfo conflictInfo,
        Func<PullConflictFileChange, Task<bool>> restoreFileAsync,
        Func<PullConflictFileChange, Task<bool>> deleteFileAsync,
        string operationName)
    {
        var window = new PullConflictWindow(conflictInfo, restoreFileAsync, deleteFileAsync, operationName)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return window.ShowDialog() == true;
    }
}
