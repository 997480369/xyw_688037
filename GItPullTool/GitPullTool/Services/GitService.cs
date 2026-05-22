using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GitPullTool.Models;

namespace GitPullTool.Services;

public sealed class GitService
{
    private const int PreviewLimit = 12000;
    private static readonly string[] SkipFolderNames =
    {
        ".git", ".vs", "bin", "obj", "node_modules", "packages"
    };

    public async Task<PullSummary> PullAsync(
        IEnumerable<string> repoPaths,
        GitExecutionOptions options,
        Action<string> log,
        CancellationToken cancellationToken,
        Func<PullConflictInfo, bool>? confirmConflict)
    {
        var repos = repoPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (repos.Count == 0)
        {
            log("No repositories selected.");
            return new PullSummary(Array.Empty<string>(), Array.Empty<string>());
        }

        var succeeded = new List<string>();
        var failed = new List<string>();
        var cancelled = false;

        foreach (var repoPath in repos)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                log("Operation canceled.");
                cancelled = true;
                break;
            }

            if (!IsGitRepository(repoPath))
            {
                log($"[SKIP] Not a git repository: {repoPath}");
                failed.Add(repoPath);
                continue;
            }

            log($"[START------->>>>>>>>>>>>>>>] git pull: {repoPath}");
            var result = await PullRepositoryAsync(repoPath, options, log, cancellationToken, confirmConflict);
            if (result == PullExecutionResult.Success)
            {
                succeeded.Add(repoPath);
                log($"[DONE-------<<<<<<<<<<<<<<] git pull: {repoPath}");
                continue;
            }

            if (result == PullExecutionResult.Cancelled)
            {
                cancelled = true;
                break;
            }

            failed.Add(repoPath);
            log($"[FAIL-------<<<<<<<<<<<<<<] git pull: {repoPath}");
        }

        return new PullSummary(succeeded, failed, cancelled);
    }

    public async Task<string?> GetCurrentBranchAsync(string repoPath, GitExecutionOptions options, CancellationToken cancellationToken)
    {
        var repoRoot = FindRepositoryRoot(repoPath);
        if (repoRoot is null)
        {
            return null;
        }

        var branch = await RunGitCaptureAsync(repoRoot, options, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD");
        if (string.IsNullOrWhiteSpace(branch))
        {
            return null;
        }

        branch = branch.Trim();
        if (string.Equals(branch, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            var hash = await RunGitCaptureAsync(repoRoot, options, cancellationToken, "rev-parse", "--short", "HEAD");
            if (!string.IsNullOrWhiteSpace(hash))
            {
                return $"detached@{hash.Trim()}";
            }
        }

        return branch;
    }

    public async Task<IReadOnlyList<string>> GetBranchesAsync(string repoPath, GitExecutionOptions options, CancellationToken cancellationToken)
    {
        if (!IsGitRepository(repoPath))
        {
            return Array.Empty<string>();
        }

        var localOutput = await RunGitCaptureAsync(repoPath, options, cancellationToken, "for-each-ref", "--format=%(refname:short)", "refs/heads");
        var remoteOutput = await RunGitCaptureAsync(repoPath, options, cancellationToken, "for-each-ref", "--format=%(refname:short)", "refs/remotes");

        var locals = SplitLines(localOutput).ToList();
        var remotes = SplitLines(remoteOutput)
            .Where(line => !string.Equals(line, "origin/HEAD", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return locals
            .Concat(remotes)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<int> CheckoutBranchAsync(
        string repoPath,
        GitExecutionOptions options,
        Action<string> log,
        CancellationToken cancellationToken,
        string branchName,
        Func<PullConflictInfo, bool>? confirmConflict)
    {
        if (!IsGitRepository(repoPath))
        {
            log($"[SKIP] Not a git repository: {repoPath}");
            return -1;
        }

        if (string.IsNullOrWhiteSpace(branchName))
        {
            log("Branch name is empty.");
            return -1;
        }

        if (branchName.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
        {
            var localName = branchName.Substring("origin/".Length);
            var rc = await RunCheckoutWithConflictResolutionAsync(
                repoPath,
                options,
                log,
                cancellationToken,
                confirmConflict,
                "checkout",
                localName);
            if (rc == 0)
            {
                return rc;
            }

            return await RunCheckoutWithConflictResolutionAsync(
                repoPath,
                options,
                log,
                cancellationToken,
                confirmConflict,
                "checkout",
                "-t",
                branchName);
        }

        return await RunCheckoutWithConflictResolutionAsync(
            repoPath,
            options,
            log,
            cancellationToken,
            confirmConflict,
            "checkout",
            branchName);
    }

    public IReadOnlyList<string> FindNestedRepositories(string rootPath, int maxDepth = 6)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath) || maxDepth < 1)
        {
            return results;
        }

        void Walk(string currentPath, int depth)
        {
            if (depth > maxDepth)
            {
                return;
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(currentPath);
            }
            catch
            {
                return;
            }

            foreach (var dir in subDirs)
            {
                var name = Path.GetFileName(dir);
                if (SkipFolderNames.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (IsGitRepository(dir))
                {
                    results.Add(dir);
                    continue;
                }

                Walk(dir, depth + 1);
            }
        }

        Walk(rootPath, 1);
        return results;
    }

    private async Task<PullExecutionResult> PullRepositoryAsync(
        string repoPath,
        GitExecutionOptions options,
        Action<string> log,
        CancellationToken cancellationToken,
        Func<PullConflictInfo, bool>? confirmConflict)
    {
        var attemptedDiscard = false;

        while (true)
        {
            var pullResult = await RunGitCommandAsync(repoPath, options, log, cancellationToken, true, "pull");
            if (pullResult.ExitCode == 0)
            {
                return PullExecutionResult.Success;
            }

            if (attemptedDiscard || confirmConflict is null)
            {
                return PullExecutionResult.Failed;
            }

            var conflictInfo = await TryBuildPullConflictInfoAsync(repoPath, options, pullResult.Lines, cancellationToken);
            if (conflictInfo is null)
            {
                return PullExecutionResult.Failed;
            }

            log($"[WARN] Local changes would be overwritten by pull: {repoPath}");
            if (!confirmConflict(conflictInfo))
            {
                log($"[CANCEL] Pull cancelled by user: {repoPath}");
                return PullExecutionResult.Cancelled;
            }

            var discardExitCode = await DiscardConflictingChangesAsync(repoPath, options, conflictInfo, log, cancellationToken);
            if (discardExitCode != 0)
            {
                log($"[FAIL] Failed to discard local changes ({discardExitCode}): {repoPath}");
                return PullExecutionResult.Failed;
            }

            attemptedDiscard = true;
            log($"[RETRY] Retrying git pull after discarding local changes: {repoPath}");
        }
    }

    private async Task<int> RunCheckoutWithConflictResolutionAsync(
        string repoPath,
        GitExecutionOptions options,
        Action<string> log,
        CancellationToken cancellationToken,
        Func<PullConflictInfo, bool>? confirmConflict,
        params string[] args)
    {
        var attemptedDiscard = false;

        while (true)
        {
            var checkoutResult = await RunGitCommandAsync(repoPath, options, log, cancellationToken, true, args);
            if (checkoutResult.ExitCode == 0 || attemptedDiscard || confirmConflict is null)
            {
                return checkoutResult.ExitCode;
            }

            var conflictInfo = await TryBuildPullConflictInfoAsync(repoPath, options, checkoutResult.Lines, cancellationToken);
            if (conflictInfo is null)
            {
                return checkoutResult.ExitCode;
            }

            log($"[WARN] Local changes would be overwritten by checkout: {repoPath}");
            if (!confirmConflict(conflictInfo))
            {
                log($"[CANCEL] Checkout cancelled by user: {repoPath}");
                return checkoutResult.ExitCode;
            }

            var discardExitCode = await DiscardConflictingChangesAsync(repoPath, options, conflictInfo, log, cancellationToken);
            if (discardExitCode != 0)
            {
                log($"[FAIL] Failed to discard local changes ({discardExitCode}): {repoPath}");
                return discardExitCode;
            }

            attemptedDiscard = true;
            log($"[RETRY] Retrying git checkout after discarding local changes: {repoPath}");
        }
    }

    private async Task<int> RunGitAsync(string workingDirectory, GitExecutionOptions options, Action<string> log, CancellationToken cancellationToken, params string[] args)
    {
        var result = await RunGitCommandAsync(workingDirectory, options, log, cancellationToken, true, args);
        return result.ExitCode;
    }

    private async Task<GitCommandResult> RunGitCommandAsync(
        string workingDirectory,
        GitExecutionOptions options,
        Action<string> log,
        CancellationToken cancellationToken,
        bool streamToLog,
        params string[] args)
    {
        var gitExe = ResolveGitPath(options.GitPath);
        if (gitExe is null)
        {
            log("Git executable not found. Set Git path or ensure git is in PATH.");
            return new GitCommandResult(-1, Array.Empty<string>());
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = gitExe,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputEncoding = GetGitOutputEncoding();
        startInfo.StandardOutputEncoding = outputEncoding;
        startInfo.StandardErrorEncoding = outputEncoding;

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var sshCommand = BuildSshCommand(options, log);
        if (!string.IsNullOrWhiteSpace(sshCommand))
        {
            startInfo.Environment["GIT_SSH_COMMAND"] = sshCommand;
        }

        using var process = new Process { StartInfo = startInfo };
        var lines = new List<string>();
        var stdoutClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutClosed.TrySetResult(true);
                return;
            }

            if (e.Data.Length == 0)
            {
                return;
            }

            lock (gate)
            {
                lines.Add(e.Data);
            }

            if (streamToLog)
            {
                log(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrClosed.TrySetResult(true);
                return;
            }

            if (e.Data.Length == 0)
            {
                return;
            }

            lock (gate)
            {
                lines.Add(e.Data);
            }

            if (streamToLog)
            {
                log(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                log("Failed to start git process.");
                return new GitCommandResult(-1, Array.Empty<string>());
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                    // Ignore kill failures.
                }
            });

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutClosed.Task, stderrClosed.Task);
            return new GitCommandResult(process.ExitCode, lines.ToArray());
        }
        catch (OperationCanceledException)
        {
            return new GitCommandResult(-1, lines.ToArray());
        }
    }

    private async Task<PullConflictInfo?> TryBuildPullConflictInfoAsync(
        string repoPath,
        GitExecutionOptions options,
        IReadOnlyList<string> commandOutput,
        CancellationToken cancellationToken)
    {
        var conflictFiles = ParseOverwriteConflictFiles(commandOutput);
        if (conflictFiles.Count == 0)
        {
            return null;
        }

        var items = new List<PullConflictFileChange>();
        foreach (var conflictFile in conflictFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            items.Add(await BuildConflictFileChangeAsync(repoPath, options, conflictFile, cancellationToken));
        }

        return new PullConflictInfo(repoPath, items);
    }

    public async Task<bool> RestoreConflictFileAsync(
        string repoPath,
        GitExecutionOptions options,
        PullConflictFileChange file,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return false;
        }

        var exitCode = await DiscardConflictFilesAsync(repoPath, options, new[] { file }, log, cancellationToken);
        return exitCode == 0;
    }

    public Task<bool> DeleteConflictFileAsync(
        string repoPath,
        PullConflictFileChange file,
        Action<string> log)
    {
        if (file is null)
        {
            return Task.FromResult(false);
        }

        if (!TryDeleteRepoRelativePath(repoPath, file.RelativePath, log, out var deleteError))
        {
            log(deleteError ?? $"[FAIL] Unable to delete file: {file.RelativePath}");
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private async Task<PullConflictFileChange> BuildConflictFileChangeAsync(
        string repoPath,
        GitExecutionOptions options,
        ConflictFileRef conflictFile,
        CancellationToken cancellationToken)
    {
        var statusText = conflictFile.IsUntracked
            ? "\u672a\u8ddf\u8e2a\u6587\u4ef6"
            : await GetStatusTextAsync(repoPath, options, conflictFile.RelativePath, cancellationToken);

        if (conflictFile.IsUntracked)
        {
            var currentText = ReadWorkingTreeText(repoPath, conflictFile.RelativePath);
            var (leftLines, rightLines) = BuildSideBySidePreview(string.Empty, currentText);
            return new PullConflictFileChange(
                conflictFile.RelativePath,
                statusText,
                "\u53f3\u4fa7\u4e3a\u5f53\u524d\u672a\u8ddf\u8e2a\u6587\u4ef6\u5185\u5bb9\u3002",
                "\u7a7a",
                leftLines,
                "\u5f53\u524d\u6587\u4ef6",
                rightLines,
                true);
        }

        var headText = await ReadHeadVersionTextAsync(repoPath, options, conflictFile.RelativePath, cancellationToken);
        var workingTreeText = ReadWorkingTreeText(repoPath, conflictFile.RelativePath);
        var (leftPaneLines, rightPaneLines) = BuildSideBySidePreview(headText, workingTreeText);

        return new PullConflictFileChange(
            conflictFile.RelativePath,
            statusText,
            "\u5de6\u4fa7\u4e3a HEAD\uff0c\u53f3\u4fa7\u4e3a\u5f53\u524d\u5de5\u4f5c\u533a\u5185\u5bb9\u3002",
            "HEAD",
            leftPaneLines,
            "\u5de5\u4f5c\u533a",
            rightPaneLines,
            false);
    }

    private async Task<string> GetStatusTextAsync(string repoPath, GitExecutionOptions options, string relativePath, CancellationToken cancellationToken)
    {
        var statusOutput = await RunGitCaptureAsync(repoPath, options, cancellationToken, "status", "--porcelain=v1", "--", relativePath);
        var statusLine = SplitLines(statusOutput).FirstOrDefault();
        return DescribeStatus(statusLine);
    }

    private async Task<string> ReadHeadVersionTextAsync(
        string repoPath,
        GitExecutionOptions options,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var headText = await RunGitCaptureAsync(repoPath, options, cancellationToken, "show", $"HEAD:{relativePath}");
        if (string.IsNullOrWhiteSpace(headText) || headText.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase))
        {
            return "\u6587\u4ef6\u5728 HEAD \u4e2d\u4e0d\u5b58\u5728\u6216\u65e0\u6cd5\u9884\u89c8\u3002";
        }

        return PrepareTextForPreview(headText);
    }

    private static string ReadWorkingTreeText(string repoPath, string relativePath)
    {
        var fullPath = Path.Combine(repoPath, relativePath);
        if (!File.Exists(fullPath))
        {
            return "\u5f53\u524d\u5de5\u4f5c\u533a\u6587\u4ef6\u4e0d\u5b58\u5728\u3002";
        }

        try
        {
            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length == 0)
            {
                return "\u7a7a\u6587\u4ef6";
            }

            if (bytes.Any(b => b == 0))
            {
                return "\u4e8c\u8fdb\u5236\u6216\u975e\u6587\u672c\u6587\u4ef6\uff0c\u65e0\u6cd5\u9884\u89c8\u5185\u5bb9\u3002";
            }

            return PrepareTextForPreview(Encoding.Default.GetString(bytes));
        }
        catch (Exception ex)
        {
            return $"\u65e0\u6cd5\u8bfb\u53d6\u6587\u4ef6\u5185\u5bb9: {ex.Message}";
        }
    }

    private async Task<int> DiscardConflictingChangesAsync(
        string repoPath,
        GitExecutionOptions options,
        PullConflictInfo conflictInfo,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        return await DiscardConflictFilesAsync(repoPath, options, conflictInfo.Files, log, cancellationToken);
    }

    private async Task<int> DiscardConflictFilesAsync(
        string repoPath,
        GitExecutionOptions options,
        IEnumerable<PullConflictFileChange> files,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var trackedFiles = files
            .Where(file => !file.IsUntracked)
            .Select(file => file.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (trackedFiles.Count > 0)
        {
            var restoreArgs = new List<string> { "restore", "--source=HEAD", "--staged", "--worktree", "--" };
            restoreArgs.AddRange(trackedFiles);
            var restoreCode = await RunGitAsync(repoPath, options, log, cancellationToken, restoreArgs.ToArray());
            if (restoreCode != 0)
            {
                var resetArgs = new List<string> { "reset", "-q", "HEAD", "--" };
                resetArgs.AddRange(trackedFiles);
                var resetCode = await RunGitAsync(repoPath, options, log, cancellationToken, resetArgs.ToArray());
                if (resetCode != 0)
                {
                    return resetCode;
                }

                var checkoutArgs = new List<string> { "checkout", "--" };
                checkoutArgs.AddRange(trackedFiles);
                var checkoutCode = await RunGitAsync(repoPath, options, log, cancellationToken, checkoutArgs.ToArray());
                if (checkoutCode != 0)
                {
                    return checkoutCode;
                }
            }
        }

        foreach (var file in files.Where(file => file.IsUntracked))
        {
            if (!TryDeleteRepoRelativePath(repoPath, file.RelativePath, log, out var deleteError))
            {
                log(deleteError ?? $"[FAIL] Unable to delete file: {file.RelativePath}");
                return -1;
            }
        }

        return 0;
    }

    private static string PrepareTextForPreview(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "\u7a7a\u6587\u4ef6";
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalized.Length <= PreviewLimit)
        {
            return normalized;
        }

        return $"{normalized[..PreviewLimit]}\n\n... \u5185\u5bb9\u5df2\u622a\u65ad ...";
    }

    private static (IReadOnlyList<PullConflictPreviewLine> LeftLines, IReadOnlyList<PullConflictPreviewLine> RightLines) BuildSideBySidePreview(string leftText, string rightText)
    {
        var leftLines = NormalizePreviewLines(leftText);
        var rightLines = NormalizePreviewLines(rightText);
        if (leftLines.Count == 0)
        {
            leftLines.Add(string.Empty);
        }

        if (rightLines.Count == 0)
        {
            rightLines.Add(string.Empty);
        }

        if ((long)leftLines.Count * rightLines.Count > 160000)
        {
            return (BuildRawPreviewLines(leftLines), BuildRawPreviewLines(rightLines));
        }

        var operations = BuildLineOperations(leftLines, rightLines);
        var rows = BuildPreviewRows(operations);
        return ConvertPreviewRows(rows);
    }

    private static List<string> NormalizePreviewLines(string text)
    {
        var prepared = PrepareTextForPreview(text);
        return prepared
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
    }

    private static List<DiffOp> BuildLineOperations(IReadOnlyList<string> leftLines, IReadOnlyList<string> rightLines)
    {
        var lcs = new int[leftLines.Count + 1, rightLines.Count + 1];
        for (var i = leftLines.Count - 1; i >= 0; i--)
        {
            for (var j = rightLines.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(leftLines[i], rightLines[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var ops = new List<DiffOp>();
        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < leftLines.Count && rightIndex < rightLines.Count)
        {
            if (string.Equals(leftLines[leftIndex], rightLines[rightIndex], StringComparison.Ordinal))
            {
                ops.Add(new DiffOp(DiffKind.Equal, leftLines[leftIndex], rightLines[rightIndex]));
                leftIndex++;
                rightIndex++;
            }
            else if (lcs[leftIndex + 1, rightIndex] >= lcs[leftIndex, rightIndex + 1])
            {
                ops.Add(new DiffOp(DiffKind.Delete, leftLines[leftIndex], null));
                leftIndex++;
            }
            else
            {
                ops.Add(new DiffOp(DiffKind.Insert, null, rightLines[rightIndex]));
                rightIndex++;
            }
        }

        while (leftIndex < leftLines.Count)
        {
            ops.Add(new DiffOp(DiffKind.Delete, leftLines[leftIndex], null));
            leftIndex++;
        }

        while (rightIndex < rightLines.Count)
        {
            ops.Add(new DiffOp(DiffKind.Insert, null, rightLines[rightIndex]));
            rightIndex++;
        }

        return ops;
    }

    private static List<PreviewRow> BuildPreviewRows(IReadOnlyList<DiffOp> operations)
    {
        var rows = new List<PreviewRow>();
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op.Kind == DiffKind.Equal)
            {
                rows.Add(new PreviewRow(DiffKind.Equal, op.LeftLine, op.RightLine));
                continue;
            }

            var leftChunk = new List<string>();
            var rightChunk = new List<string>();
            while (i < operations.Count && operations[i].Kind != DiffKind.Equal)
            {
                if (operations[i].Kind == DiffKind.Delete && operations[i].LeftLine is not null)
                {
                    leftChunk.Add(operations[i].LeftLine!);
                }
                else if (operations[i].Kind == DiffKind.Insert && operations[i].RightLine is not null)
                {
                    rightChunk.Add(operations[i].RightLine!);
                }

                i++;
            }

            i--;

            var max = Math.Max(leftChunk.Count, rightChunk.Count);
            for (var index = 0; index < max; index++)
            {
                var leftLine = index < leftChunk.Count ? leftChunk[index] : null;
                var rightLine = index < rightChunk.Count ? rightChunk[index] : null;
                var kind = leftLine is not null && rightLine is not null
                    ? DiffKind.Replace
                    : leftLine is not null
                        ? DiffKind.Delete
                        : DiffKind.Insert;
                rows.Add(new PreviewRow(kind, leftLine, rightLine));
            }
        }

        return rows;
    }

    private static (IReadOnlyList<PullConflictPreviewLine> LeftLines, IReadOnlyList<PullConflictPreviewLine> RightLines) ConvertPreviewRows(IReadOnlyList<PreviewRow> rows)
    {
        var leftLines = new List<PullConflictPreviewLine>();
        var rightLines = new List<PullConflictPreviewLine>();
        var leftNumber = 1;
        var rightNumber = 1;

        foreach (var row in rows)
        {
            switch (row.Kind)
            {
                case DiffKind.Equal:
                    leftLines.Add(new PullConflictPreviewLine(' ', leftNumber++, row.LeftLine ?? string.Empty, false));
                    rightLines.Add(new PullConflictPreviewLine(' ', rightNumber++, row.RightLine ?? string.Empty, false));
                    break;
                case DiffKind.Delete:
                    leftLines.Add(new PullConflictPreviewLine(
                        '-',
                        leftNumber++,
                        row.LeftLine ?? string.Empty,
                        true,
                        BuildSingleHighlightSegments(row.LeftLine ?? string.Empty)));
                    rightLines.Add(new PullConflictPreviewLine(' ', null, string.Empty, true));
                    break;
                case DiffKind.Insert:
                    leftLines.Add(new PullConflictPreviewLine(' ', null, string.Empty, true));
                    rightLines.Add(new PullConflictPreviewLine(
                        '+',
                        rightNumber++,
                        row.RightLine ?? string.Empty,
                        true,
                        BuildSingleHighlightSegments(row.RightLine ?? string.Empty)));
                    break;
                default:
                    var leftText = row.LeftLine ?? string.Empty;
                    var rightText = row.RightLine ?? string.Empty;
                    var (leftSegments, rightSegments) = BuildInlineDiffSegments(leftText, rightText);
                    leftLines.Add(new PullConflictPreviewLine('~', leftNumber++, leftText, true, leftSegments));
                    rightLines.Add(new PullConflictPreviewLine('~', rightNumber++, rightText, true, rightSegments));
                    break;
            }
        }

        return (leftLines, rightLines);
    }

    private static IReadOnlyList<PullConflictPreviewLine> BuildRawPreviewLines(IReadOnlyList<string> lines)
    {
        var result = new List<PullConflictPreviewLine>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            result.Add(new PullConflictPreviewLine(' ', index + 1, lines[index], false));
        }

        return result;
    }

    private static IReadOnlyList<PullConflictPreviewSegment> BuildSingleHighlightSegments(string text)
    {
        return new[] { new PullConflictPreviewSegment(text, !string.IsNullOrEmpty(text)) };
    }

    private static (IReadOnlyList<PullConflictPreviewSegment> LeftSegments, IReadOnlyList<PullConflictPreviewSegment> RightSegments)
        BuildInlineDiffSegments(string leftText, string rightText)
    {
        var prefix = 0;
        var maxPrefix = Math.Min(leftText.Length, rightText.Length);
        while (prefix < maxPrefix && leftText[prefix] == rightText[prefix])
        {
            prefix++;
        }

        var leftSuffix = leftText.Length - 1;
        var rightSuffix = rightText.Length - 1;
        while (leftSuffix >= prefix && rightSuffix >= prefix && leftText[leftSuffix] == rightText[rightSuffix])
        {
            leftSuffix--;
            rightSuffix--;
        }

        return (
            BuildSegmentList(leftText, prefix, leftSuffix),
            BuildSegmentList(rightText, prefix, rightSuffix));
    }

    private static IReadOnlyList<PullConflictPreviewSegment> BuildSegmentList(string text, int prefixLength, int suffixIndex)
    {
        var result = new List<PullConflictPreviewSegment>();
        if (prefixLength > 0)
        {
            result.Add(new PullConflictPreviewSegment(text[..prefixLength], false));
        }

        var highlightStart = prefixLength;
        var highlightLength = suffixIndex >= prefixLength ? suffixIndex - prefixLength + 1 : 0;
        if (highlightLength > 0)
        {
            result.Add(new PullConflictPreviewSegment(text.Substring(highlightStart, highlightLength), true));
        }

        var suffixStart = suffixIndex + 1;
        if (suffixStart < text.Length)
        {
            result.Add(new PullConflictPreviewSegment(text[suffixStart..], false));
        }

        if (result.Count == 0)
        {
            result.Add(new PullConflictPreviewSegment(text, false));
        }

        return result;
    }

    private async Task<string?> RunGitCaptureAsync(string workingDirectory, GitExecutionOptions options, CancellationToken cancellationToken, params string[] args)
    {
        var gitExe = ResolveGitPath(options.GitPath);
        if (gitExe is null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = gitExe,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputEncoding = GetGitOutputEncoding();
        startInfo.StandardOutputEncoding = outputEncoding;
        startInfo.StandardErrorEncoding = outputEncoding;

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var sshCommand = BuildSshCommand(options, _ => { });
        if (!string.IsNullOrWhiteSpace(sshCommand))
        {
            startInfo.Environment["GIT_SSH_COMMAND"] = sshCommand;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return null;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = await stdoutTask;
            if (!string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }

            var error = await stderrTask;
            return string.IsNullOrWhiteSpace(error) ? null : error.Trim();
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore kill failures.
            }

            return null;
        }
    }

    private static string? ResolveGitPath(string? gitPath)
    {
        if (!string.IsNullOrWhiteSpace(gitPath))
        {
            return File.Exists(gitPath) ? gitPath : null;
        }

        return "git";
    }

    private static string? BuildSshCommand(GitExecutionOptions options, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(options.SshKeyPath))
        {
            return null;
        }

        if (!File.Exists(options.SshKeyPath))
        {
            log($"SSH key not found: {options.SshKeyPath}");
            return null;
        }

        var plink = ResolvePlinkPath(options.PlinkPath);
        if (plink is null)
        {
            log("Plink not found. Set TortoiseGitPlink.exe or plink.exe path.");
            return null;
        }

        var userArg = string.IsNullOrWhiteSpace(options.SshUser) ? string.Empty : $" -l \"{options.SshUser}\"";
        return $"\"{plink}\" -batch -i \"{options.SshKeyPath}\"{userArg}";
    }

    private static string? ResolvePlinkPath(string? plinkPath)
    {
        if (!string.IsNullOrWhiteSpace(plinkPath) && File.Exists(plinkPath))
        {
            return plinkPath;
        }

        var candidates = new[]
        {
            @"C:\\Program Files\\TortoiseGit\\bin\\TortoiseGitPlink.exe",
            @"C:\\Program Files (x86)\\TortoiseGit\\bin\\TortoiseGitPlink.exe",
            @"C:\\Program Files\\PuTTY\\plink.exe",
            @"C:\\Program Files (x86)\\PuTTY\\plink.exe"
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static Encoding GetGitOutputEncoding()
    {
        try
        {
            var codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(codePage);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static List<ConflictFileRef> ParseOverwriteConflictFiles(IReadOnlyList<string> lines)
    {
        var results = new List<ConflictFileRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collecting = false;
        var isUntrackedSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (IsOverwriteConflictStart(trimmed))
            {
                collecting = true;
                isUntrackedSection = trimmed.Contains("untracked working tree files", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!collecting)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("Updating ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (char.IsWhiteSpace(line[0]) || LooksLikeConflictPath(trimmed))
            {
                if (seen.Add(trimmed))
                {
                    results.Add(new ConflictFileRef(trimmed, isUntrackedSection));
                }

                continue;
            }

            if (IsConflictSectionTerminator(trimmed))
            {
                collecting = false;
                continue;
            }

            collecting = false;
        }

        return results;
    }

    private static bool IsOverwriteConflictStart(string line)
    {
        return line.Contains("would be overwritten by", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("error: Your local changes to the following files", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("error: The following untracked working tree files", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConflictSectionTerminator(string line)
    {
        return line.StartsWith("Please commit your changes", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Aborting", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Fast-forward", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Merge made by", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Already up to date", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeConflictPath(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.Contains(": ", StringComparison.Ordinal))
        {
            return false;
        }

        if (line.StartsWith("Please commit", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Aborting", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Path.IsPathRooted(line))
        {
            return false;
        }

        return line.Contains(Path.DirectorySeparatorChar)
            || line.Contains(Path.AltDirectorySeparatorChar)
            || Path.HasExtension(line);
    }

    private static bool TryDeleteRepoRelativePath(string repoPath, string relativePath, Action<string> log, out string? error)
    {
        error = null;

        try
        {
            var repoRoot = Path.GetFullPath(repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(repoPath, relativePath));

            if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                error = $"[FAIL] Refusing to delete path outside repository: {relativePath}";
                return false;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                log($"[CLEAN] Deleted untracked file: {relativePath}");
                return true;
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
                log($"[CLEAN] Deleted untracked folder: {relativePath}");
                return true;
            }

            log($"[CLEAN] Untracked path already missing: {relativePath}");
            return true;
        }
        catch (Exception ex)
        {
            error = $"[FAIL] Unable to delete {relativePath}: {ex.Message}";
            return false;
        }
    }

    private static string DescribeStatus(string? statusLine)
    {
        if (string.IsNullOrWhiteSpace(statusLine) || statusLine.Length < 2)
        {
            return "\u672c\u5730\u4fee\u6539";
        }

        var code = statusLine[..2];
        return code switch
        {
            "??" => "\u672a\u8ddf\u8e2a\u6587\u4ef6",
            " M" => "\u5de5\u4f5c\u533a\u5df2\u4fee\u6539",
            "M " => "\u5df2\u6682\u5b58\u4fee\u6539",
            "MM" => "\u5df2\u6682\u5b58\u4e14\u5de5\u4f5c\u533a\u5df2\u4fee\u6539",
            "A " => "\u5df2\u6682\u5b58\u65b0\u589e",
            "AM" => "\u5df2\u6682\u5b58\u65b0\u589e\u4e14\u5de5\u4f5c\u533a\u5df2\u4fee\u6539",
            " D" => "\u5de5\u4f5c\u533a\u5df2\u5220\u9664",
            "D " => "\u5df2\u6682\u5b58\u5220\u9664",
            "R " => "\u5df2\u6682\u5b58\u91cd\u547d\u540d",
            "RM" => "\u5df2\u91cd\u547d\u540d\u4e14\u5de5\u4f5c\u533a\u5df2\u4fee\u6539",
            "UU" => "\u5b58\u5728\u672a\u89e3\u51b3\u51b2\u7a81",
            _ => $"\u672c\u5730\u72b6\u6001: {code}"
        };
    }

    private static string LimitPreview(string text)
    {
        if (text.Length <= PreviewLimit)
        {
            return text;
        }

        return $"{text[..PreviewLimit]}\r\n\r\n... diff \u5df2\u622a\u65ad ...";
    }

    private static IEnumerable<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsGitRepository(string repoPath)
    {
        if (!Directory.Exists(repoPath))
        {
            return false;
        }

        var gitPath = Path.Combine(repoPath, ".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath) || !Directory.Exists(startPath))
        {
            return null;
        }

        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (IsGitRepository(current.FullName))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private sealed record GitCommandResult(int ExitCode, IReadOnlyList<string> Lines);
    private sealed record ConflictFileRef(string RelativePath, bool IsUntracked);
    private sealed record DiffOp(DiffKind Kind, string? LeftLine, string? RightLine);
    private sealed record PreviewRow(DiffKind Kind, string? LeftLine, string? RightLine);

    private enum PullExecutionResult
    {
        Success,
        Failed,
        Cancelled
    }

    private enum DiffKind
    {
        Equal,
        Delete,
        Insert,
        Replace
    }
}
