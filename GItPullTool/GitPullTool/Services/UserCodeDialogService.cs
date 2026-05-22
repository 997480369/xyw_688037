using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GitPullTool.Services;

public enum UserCodeDialogChoice
{
    MapOnly,
    StartSystem
}

public sealed class UserCodeDialogService
{
    private const int ButtonClick = 0x00F5;
    private const int WindowClose = 0x0010;

    public async Task<bool> ClickUserCodeDialogAsync(
        UserCodeDialogChoice choice,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<bool>? shouldStopWaiting = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (shouldStopWaiting?.Invoke() == true)
            {
                return false;
            }

            var authorization = FindAuthorizationWarningDialog();
            if (authorization != IntPtr.Zero && TryClickAuthorizationWarningButton(authorization))
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            var confirmation = FindEngineVersionConfirmation();
            if (confirmation != IntPtr.Zero && TryClickConfirmationButton(confirmation, choice))
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            var dialog = FindUserCodeDialog();
            if (dialog != IntPtr.Zero && TryClickDialogButton(dialog, choice))
            {
                return true;
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    public async Task<bool> CloseUserCodeErrorConsoleAsync(int? processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var window = FindUserCodeErrorConsole(processId);
            if (window != IntPtr.Zero)
            {
                SendMessage(window, WindowClose, IntPtr.Zero, IntPtr.Zero);
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    public async Task<bool> CloseAnyUserCodeErrorConsoleAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return await CloseUserCodeErrorConsoleAsync(null, timeout, cancellationToken);
    }

    private static IntPtr FindUserCodeDialog()
    {
        var found = IntPtr.Zero;

        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window))
            {
                return true;
            }

            var title = GetWindowText(window);
            if (!title.Contains("User Code Error", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var text = GetChildText(window);
            if (!text.Contains("Start System", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            found = window;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private static IntPtr FindAuthorizationWarningDialog()
    {
        var found = IntPtr.Zero;

        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window))
            {
                return true;
            }

            var title = GetWindowText(window);
            if (!title.Contains("Authorization Warning", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var text = GetChildText(window);
            if (!text.Contains("No valid dongle found", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("trial mode", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            found = window;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private static IntPtr FindEngineVersionConfirmation()
    {
        var found = IntPtr.Zero;

        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window))
            {
                return true;
            }

            var title = GetWindowText(window);
            if (!title.Contains("Confirmation", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var text = GetChildText(window);
            if (!text.Contains("Engine module", StringComparison.OrdinalIgnoreCase)
                || !text.Contains("not the latest version", StringComparison.OrdinalIgnoreCase)
                || !text.Contains("continue", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            found = window;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private static IntPtr FindUserCodeErrorConsole(int? processId)
    {
        var found = IntPtr.Zero;

        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window))
            {
                return true;
            }

            var title = GetWindowText(window);
            if (!title.Contains("User Code Error", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (processId.HasValue)
            {
                GetWindowThreadProcessId(window, out var windowProcessId);
                if (windowProcessId != processId.Value)
                {
                    return true;
                }
            }

            var className = GetClassName(window);
            if (!string.Equals(className, "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            found = window;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private static bool TryClickDialogButton(IntPtr dialog, UserCodeDialogChoice choice)
    {
        return TryClickButton(dialog, caption => IsTargetButton(caption, choice));
    }

    private static bool TryClickAuthorizationWarningButton(IntPtr dialog)
    {
        return TryClickButton(dialog, caption => IsYesLikeButton(caption));
    }

    private static bool TryClickConfirmationButton(IntPtr dialog, UserCodeDialogChoice choice)
    {
        return TryClickButton(dialog, caption => IsConfirmationTargetButton(caption, choice));
    }

    private static bool TryClickButton(IntPtr dialog, Func<string, bool> isTargetButton)
    {
        var clicked = false;

        EnumChildWindows(dialog, (child, _) =>
        {
            var className = GetClassName(child);
            if (!string.Equals(className, "Button", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var caption = GetWindowText(child);
            if (!isTargetButton(caption))
            {
                return true;
            }

            SendMessage(child, ButtonClick, IntPtr.Zero, IntPtr.Zero);
            clicked = true;
            return false;
        }, IntPtr.Zero);

        return clicked;
    }

    private static bool IsTargetButton(string caption, UserCodeDialogChoice choice)
    {
        return choice == UserCodeDialogChoice.StartSystem
            ? IsYesLikeButton(caption)
            : IsNoLikeButton(caption);
    }

    private static bool IsConfirmationTargetButton(string caption, UserCodeDialogChoice choice)
    {
        return choice == UserCodeDialogChoice.StartSystem
            ? IsYesLikeButton(caption, includeOk: true)
            : IsNoLikeButton(caption);
    }

    private static bool IsYesLikeButton(string caption, bool includeOk = false)
    {
        var normalized = NormalizeCaption(caption);
        return normalized.Contains("是", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("yes", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("&y", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("(y)", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("y", StringComparison.OrdinalIgnoreCase)
            || (includeOk && normalized.Contains("ok", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNoLikeButton(string caption)
    {
        var normalized = NormalizeCaption(caption);
        return normalized.Contains("否", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("no", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("&n", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("(n)", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("n", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cancel", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("取消", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCaption(string caption)
    {
        return caption.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string GetChildText(IntPtr window)
    {
        var builder = new StringBuilder();
        EnumChildWindows(window, (child, _) =>
        {
            var text = GetWindowText(child);
            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.Append(' ').Append(text);
            }

            return true;
        }, IntPtr.Zero);

        return builder.ToString();
    }

    private static string GetWindowText(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(IntPtr window)
    {
        var builder = new StringBuilder(256);
        _ = GetClassName(window, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
}
