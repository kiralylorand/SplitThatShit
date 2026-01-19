using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VideoTrim;

internal sealed class TrialManager
{
    private const int MaxVideos = 10;

    private readonly AppSettings _settings;
    private readonly Action<string> _log;
    private readonly HashSet<string> _ids;

    public TrialManager(AppSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
        _ids = new HashSet<string>(_settings.TrialFileIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    public bool IsLicensed => LicenseManager.IsLicensed(_settings);

    public int UsedSlots => _settings.TrialUsedVideos;

    public int MaxSlots => MaxVideos;

    public bool CanProcess(string path)
    {
        if (IsLicensed)
        {
            return true;
        }

        var id = ComputeFileId(path);
        if (_ids.Contains(id))
        {
            // Already counted for trial.
            return true;
        }

        if (_settings.TrialUsedVideos >= MaxVideos)
        {
            return false;
        }

        return true;
    }

    public void RegisterProcessed(string path)
    {
        if (IsLicensed)
        {
            return;
        }

        var id = ComputeFileId(path);
        if (_ids.Contains(id))
        {
            return;
        }

        if (_settings.TrialUsedVideos >= MaxVideos)
        {
            return;
        }

        _ids.Add(id);
        _settings.TrialUsedVideos++;
        _settings.TrialFileIds = new List<string>(_ids).ToArray();
        _settings.Save();

        _log($"Trial usage: {_settings.TrialUsedVideos}/{MaxVideos} videos.");
    }

    private static string ComputeFileId(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var payload = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(payload);
            var hash = sha.ComputeHash(bytes);

            var sb = new StringBuilder(16);
            for (var i = 0; i < 8 && i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }

            return sb.ToString();
        }
        catch
        {
            // Fallback: use path only.
            return path.ToUpperInvariant();
        }
    }
}

