using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace VideoTrim;

internal static class ToolDownloader
{
    private const string FfmpegZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    public static bool ToolsExist(string baseDir)
    {
        var toolsDir = Path.Combine(baseDir, "tools");
        var ffmpeg = Path.Combine(toolsDir, "ffmpeg.exe");
        var ffprobe = Path.Combine(toolsDir, "ffprobe.exe");
        return File.Exists(ffmpeg) && File.Exists(ffprobe);
    }

    public static async Task DownloadAsync(string baseDir, Action<string> log)
    {
        var toolsDir = Path.Combine(baseDir, "tools");
        Directory.CreateDirectory(toolsDir);

        var tempZip = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}.zip");
        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(FfmpegZipUrl);
            response.EnsureSuccessStatusCode();

            await using (var fs = File.Create(tempZip))
            {
                await response.Content.CopyToAsync(fs);
            }

            log("Extrage ffmpeg...");
            using var archive = ZipFile.OpenRead(tempZip);
            ExtractExe(archive, "ffmpeg.exe", Path.Combine(toolsDir, "ffmpeg.exe"));
            ExtractExe(archive, "ffprobe.exe", Path.Combine(toolsDir, "ffprobe.exe"));

            log("Download complet.");
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    private static void ExtractExe(ZipArchive archive, string exeName, string targetPath)
    {
        var entry = FindEntry(archive, exeName);
        if (entry == null)
        {
            throw new InvalidOperationException($"Nu gasesc {exeName} in arhiva.");
        }

        entry.ExtractToFile(targetPath, true);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string exeName)
    {
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/bin/" + exeName, StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith("\\bin\\" + exeName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }
}
