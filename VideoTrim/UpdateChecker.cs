using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace VideoTrim;

internal static class UpdateChecker
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        // GitHub API requires a User-Agent header
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SplitThatShitUpdateChecker/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(1, 0, 0);
    }

    public static async Task<UpdateInfo?> CheckForUpdatesAsync(string githubOwner, string githubRepo)
    {
        try
        {
            var url = $"https://api.github.com/repos/{githubOwner}/{githubRepo}/releases/latest";
            var response = await HttpClient.GetStringAsync(url);
            var release = JsonSerializer.Deserialize<GitHubRelease>(response);

            if (release == null || string.IsNullOrEmpty(release.TagName))
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateChecker] Release is null or TagName is empty");
                return null;
            }

            // Remove 'v' prefix if present
            var tagVersion = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tagVersion, out var latestVersion))
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateChecker] Failed to parse version from tag: {release.TagName}");
                return null;
            }

            var currentVersion = GetCurrentVersion();
            System.Diagnostics.Debug.WriteLine($"[UpdateChecker] Current version: {currentVersion}, Latest version: {latestVersion}");
            
            if (latestVersion > currentVersion)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateChecker] Update found! {currentVersion} -> {latestVersion}");
                return new UpdateInfo
                {
                    Version = latestVersion,
                    TagName = release.TagName,
                    ReleaseUrl = release.HtmlUrl,
                    ReleaseName = release.Name ?? release.TagName
                };
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateChecker] No update needed. Current: {currentVersion}, Latest: {latestVersion}");
            }

            return null;
        }
        catch (Exception ex)
        {
            // Log error for debugging
            System.Diagnostics.Debug.WriteLine($"[UpdateChecker] Error checking for updates: {ex.Message}");
            return null;
        }
    }

    private sealed class GitHubRelease
    {
        public string TagName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
    }

    private sealed class GitHubAsset
    {
        public string Name { get; set; } = string.Empty;
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }

    public sealed class UpdateInfo
    {
        public Version Version { get; set; } = new(1, 0, 0);
        public string TagName { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string ReleaseName { get; set; } = string.Empty;
    }
}
