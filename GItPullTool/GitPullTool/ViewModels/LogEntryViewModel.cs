using System;

namespace GitPullTool.ViewModels;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(string message)
    {
        Timestamp = DateTime.Now;
        Message = message;
        IsError = IsErrorMessage(message);
    }

    public DateTime Timestamp { get; }
    public string Message { get; }
    public bool IsError { get; }

    public string Display => $"{Timestamp:HH:mm:ss} {Message}";

    private static bool IsErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (message.IndexOf("[FAIL", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (message.IndexOf("[ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (message.IndexOf("error:", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (message.IndexOf("fatal:", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }
}
