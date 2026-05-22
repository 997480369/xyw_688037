using System;
using System.IO;
using System.Text.Json;
using GitPullTool.Models;

namespace GitPullTool.Services;

public sealed class SettingsService
{
    private const string AppFolderName = "GitPullTool";
    private const string SettingsFileName = "settings.json";
    private const string FixedConfigRoot = @"D:\Soft_shxy\VS2022\Tool\GItPullTool";

    public string SettingsPath { get; }
    public string LegacySettingsPath { get; }
    public string? LastLoadedPath { get; private set; }

    public SettingsService()
    {
        var configDir = ResolveConfigDirectory();
        SettingsPath = Path.Combine(configDir, SettingsFileName);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var legacyFolder = Path.Combine(appData, AppFolderName);
        LegacySettingsPath = Path.Combine(legacyFolder, SettingsFileName);
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                LastLoadedPath = SettingsPath;
                return settings ?? new AppSettings();
            }

            if (File.Exists(LegacySettingsPath))
            {
                var json = File.ReadAllText(LegacySettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                LastLoadedPath = LegacySettingsPath;
                return settings ?? new AppSettings();
            }

            LastLoadedPath = SettingsPath;
            return new AppSettings();
        }
        catch
        {
            LastLoadedPath = SettingsPath;
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (directory is null)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(SettingsPath, json);
    }

    private static string ResolveConfigDirectory()
    {
        if (Directory.Exists(FixedConfigRoot))
        {
            return Path.Combine(FixedConfigRoot, "Config");
        }

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            return Path.Combine(repoRoot, "Config");
        }

        return Path.Combine(AppContext.BaseDirectory, "Config");
    }

    private static string? FindRepoRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        for (var i = 0; i < 8 && current is not null; i++)
        {
            var sln = Path.Combine(current.FullName, "GitPullTool.sln");
            var csproj = Path.Combine(current.FullName, "GitPullTool", "GitPullTool.csproj");
            if (File.Exists(sln) || File.Exists(csproj))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
