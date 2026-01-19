using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VideoTrim;

internal static class VideoProcessor
{
    internal static readonly ManualResetEventSlim GlobalPauseEvent = new(true);

    private static void CheckPause(CancellationToken cancellationToken)
    {
        // Wait here while UI has set a global pause.
        GlobalPauseEvent.Wait(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static Task RunAsync(
        string inputFolder,
        string outputFolder,
        string processedFolder,
        int minSegmentSeconds,
        int maxSegmentSeconds,
        ProcessingMode mode,
        int segmentsPerOutput,
        int outputsPerInput,
        bool autoDetectSimilar,
        double similarityThreshold,
        bool pauseForManualDelete,
        bool fastSplit,
        bool crossfade,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task> manualPauseAsync,
        Func<string, bool>? canProcessInput,
        Action<string>? registerProcessed,
        Action<string> log,
        Action<int, int, int, int, TimeSpan?>? updateProgress = null)
    {
        return Task.Run(() =>
        {
            var inputPath = new DirectoryInfo(inputFolder);
            var outputPath = new DirectoryInfo(outputFolder);
            var processedPath = new DirectoryInfo(processedFolder);

            if (!inputPath.Exists)
            {
                throw new DirectoryNotFoundException("Input folder does not exist.");
            }

            if (!outputPath.Exists)
            {
                outputPath.Create();
            }

            if (!processedPath.Exists)
            {
                processedPath.Create();
            }

            var ffmpeg = ResolveToolPath("ffmpeg.exe");
            var ffprobe = ResolveToolPath("ffprobe.exe");

            var random = new Random();

            // Collect supported video files once so we can estimate total time.
            var patterns = new[] { "*.mp4", "*.mov", "*.mkv", "*.avi" };
            var allVideos = new System.Collections.Generic.List<FileInfo>();
            foreach (var pattern in patterns)
            {
                allVideos.AddRange(inputPath.GetFiles(pattern));
            }

            if (allVideos.Count == 0)
            {
                if (!inputPath.Exists || inputPath.GetFiles().Length == 0)
                    log("Input folder is empty. Please add some video files and try again.");
                else
                    log("No supported video files found in input folder. Supported types: .mp4, .mov, .mkv, .avi.");
                return;
            }

            var totalVideos = allVideos.Count;
            log($"Found {totalVideos} video file(s) to process.");
            
            // Calculate total output files expected
            int totalOutputFiles;
            if (mode == ProcessingMode.SplitOnly)
            {
                // For SplitOnly, we can't know exact count, estimate based on average segment duration
                totalOutputFiles = 0; // Will be counted as we go
            }
            else
            {
                totalOutputFiles = outputsPerInput * totalVideos;
            }
            
            var startTime = DateTime.UtcNow;
            var processedVideos = 0;
            var initialOutputCount = outputPath.GetFiles().Length;
            
            // Initial progress update
            updateProgress?.Invoke(totalVideos, 0, totalOutputFiles, 0, null);

            foreach (var videoFile in allVideos)
            {
                if (canProcessInput != null && !canProcessInput(videoFile.FullName))
                {
                    log("Trial limit reached. Skipping remaining files.");
                    return;
                }

                CheckPause(cancellationToken);
                log($"Processing {videoFile.Name}...");
                try
                {
                    var timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                    var baseName = Path.GetFileNameWithoutExtension(videoFile.Name).Replace("_safe", "");
                    switch (mode)
                    {
                        case ProcessingMode.SplitReview:
                            SplitReview(
                                videoFile.FullName,
                                baseName,
                                minSegmentSeconds,
                                maxSegmentSeconds,
                                segmentsPerOutput,
                                outputsPerInput,
                                outputPath.FullName,
                                ffmpeg,
                                ffprobe,
                                autoDetectSimilar,
                                similarityThreshold,
                                pauseForManualDelete,
                                fastSplit,
                                crossfade,
                                random,
                                timeStamp,
                                cancellationToken,
                                manualPauseAsync,
                                log);
                            break;
                        case ProcessingMode.DirectMix:
                            DirectMix(
                                videoFile.FullName,
                                baseName,
                                minSegmentSeconds,
                                maxSegmentSeconds,
                                segmentsPerOutput,
                                outputsPerInput,
                                outputPath.FullName,
                                ffmpeg,
                                ffprobe,
                                fastSplit,
                                crossfade,
                                random,
                                timeStamp,
                                cancellationToken,
                                log);
                            break;
                        default:
                            CreateSegments(
                                videoFile.FullName,
                                baseName,
                                minSegmentSeconds,
                                maxSegmentSeconds,
                                outputPath.FullName,
                                ffmpeg,
                                ffprobe,
                                fastSplit,
                                random,
                                timeStamp,
                                cancellationToken,
                                log);
                            break;
                    }

                    log($"Done: {videoFile.Name}");
                    registerProcessed?.Invoke(videoFile.FullName);
                    processedVideos++;

                    // Move to processed folder only after successful completion.
                    MoveProcessed(videoFile.FullName, Path.Combine(processedPath.FullName, videoFile.Name));

                    // Count generated output files
                    var currentOutputCount = outputPath.GetFiles().Length;
                    var generatedFiles = currentOutputCount - initialOutputCount;

                    // Time estimation based on average per-video duration so far.
                    var elapsed = DateTime.UtcNow - startTime;
                    TimeSpan? estimatedRemaining = null;
                    if (processedVideos > 0 && totalVideos > processedVideos)
                    {
                        var avgPerVideoTicks = elapsed.Ticks / processedVideos;
                        var estimatedTotal = TimeSpan.FromTicks(avgPerVideoTicks * totalVideos);
                        var remaining = estimatedTotal - elapsed;
                        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                        estimatedRemaining = remaining;

                        var approxMinutes = (int)Math.Floor(remaining.TotalMinutes);
                        var approxSeconds = remaining.Seconds;
                        log($"Estimated remaining time: ~{approxMinutes}m {approxSeconds:D2}s for {totalVideos - processedVideos} more video(s).");
                    }
                    
                    // Update progress status
                    updateProgress?.Invoke(totalVideos, processedVideos, totalOutputFiles, generatedFiles, estimatedRemaining);
                }
                catch (Exception ex)
                {
                    log($"Error processing {videoFile.Name}: {ex.Message}");
                    // Do not move to processed folder if processing failed.
                }
            }

            // Final progress update
            var finalOutputCount = outputPath.GetFiles().Length;
            var finalGeneratedFiles = finalOutputCount - initialOutputCount;
            if (mode == ProcessingMode.SplitOnly)
            {
                totalOutputFiles = finalGeneratedFiles;
            }
            updateProgress?.Invoke(totalVideos, processedVideos, totalOutputFiles, finalGeneratedFiles, TimeSpan.Zero);

            log("All done!");
        });
    }

    private static string ResolveToolPath(string toolExe)
    {
        var exeDir = AppContext.BaseDirectory;
        var direct = Path.Combine(exeDir, toolExe);
        if (File.Exists(direct))
        {
            return direct;
        }

        var toolsDir = Path.Combine(exeDir, "tools", toolExe);
        if (File.Exists(toolsDir))
        {
            return toolsDir;
        }

        var envPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var envMatch = envPath.Split(Path.PathSeparator)
            .Select(p => Path.Combine(p, toolExe))
            .FirstOrDefault(File.Exists);

        if (!string.IsNullOrWhiteSpace(envMatch))
        {
            return envMatch;
        }

        throw new FileNotFoundException($"Cannot find {toolExe}. Place it next to the exe or in PATH.");
    }

    private static double GetDurationSeconds(string videoPath, string ffprobe)
    {
        var output = RunProcess(ffprobe,
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"");

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Could not read video duration.");
        }

        return double.Parse(output.Trim(), CultureInfo.InvariantCulture);
    }

    private static (int width, int height) GetDimensions(string videoPath, string ffprobe)
    {
        var output = RunProcess(ffprobe,
            $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{videoPath}\"");

        var parts = output.Trim().Split('x');
        if (parts.Length != 2)
        {
            throw new InvalidOperationException("Could not read video dimensions.");
        }

        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static string RecodeIfNeeded(string videoPath, string ffmpeg, string ffprobe, Action<string> log)
    {
        var (width, height) = GetDimensions(videoPath, ffprobe);
        if (width % 2 == 0 && height % 2 == 0)
        {
            return videoPath;
        }

        var dir = Path.GetDirectoryName(videoPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(videoPath);
        var ext = Path.GetExtension(videoPath);
        var safePath = Path.Combine(dir, $"{name}_safe{ext}");

        log($"Re-encoding {Path.GetFileName(videoPath)}...");
        RunProcess(ffmpeg,
            $"-y -i \"{videoPath}\" -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:v libx264 -preset fast -crf 23 -c:a aac -b:a 128k \"{safePath}\"");

        return safePath;
    }

    private static void CreateSegments(
        string videoPath,
        string baseName,
        int minTime,
        int maxTime,
        string outputFolder,
        string ffmpeg,
        string ffprobe,
        bool fastSplit,
        Random random,
        string timeStamp,
        CancellationToken cancellationToken,
        Action<string> log)
    {
        var total = GetDurationSeconds(videoPath, ffprobe);
        var current = 0.0;
        var index = 0;

        while (current < total)
        {
            CheckPause(cancellationToken);
            var segmentDuration = NextSegmentLength(random, minTime, maxTime);
            if (current + segmentDuration > total)
            {
                segmentDuration = Math.Floor(total - current);
            }

            if (segmentDuration < minTime)
            {
                break;
            }

            var outputFile = Path.Combine(outputFolder, $"{baseName}_scene_{index + 1}_{timeStamp}.mp4");
            log($"  -> Scene {index + 1}: start {Math.Floor(current)}s, duration {segmentDuration:0.0}s");

            if (fastSplit)
            {
                // Fastest: stream copy (keyframe-accurate).
                RunProcess(ffmpeg,
                    $"-y -ss {current.ToString(CultureInfo.InvariantCulture)} -t {segmentDuration.ToString("0.0", CultureInfo.InvariantCulture)} -i \"{videoPath}\" -c copy \"{outputFile}\"",
                    cancellationToken);
            }
            else
            {
                RunProcess(ffmpeg,
                    $"-y -i \"{videoPath}\" -ss {current.ToString(CultureInfo.InvariantCulture)} -t {segmentDuration.ToString("0.0", CultureInfo.InvariantCulture)} -c:v libx264 -preset fast -crf 23 -c:a aac -b:a 128k \"{outputFile}\"",
                    cancellationToken);
            }

            current += segmentDuration;
            index++;
        }
    }

    private static void SplitReview(
        string videoPath,
        string baseName,
        int minTime,
        int maxTime,
        int segmentsPerOutput,
        int outputsPerInput,
        string outputFolder,
        string ffmpeg,
        string ffprobe,
        bool autoDetectSimilar,
        double similarityThreshold,
        bool pauseForManualDelete,
        bool fastSplit,
        bool crossfade,
        Random random,
        string timeStamp,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task> manualPauseAsync,
        Action<string> log)
    {
        if (segmentsPerOutput < 1 || outputsPerInput < 1)
        {
            throw new ArgumentException("Mix values are invalid.");
        }

        var segmentsDir = Path.Combine(outputFolder, "_segments", baseName);
        Directory.CreateDirectory(segmentsDir);

        try
        {
            var segments = SplitSequentialRandom(
                videoPath,
                segmentsDir,
                minTime,
                maxTime,
                ffmpeg,
                ffprobe,
                fastSplit,
                random,
                timeStamp,
                cancellationToken,
                log);

            if (segments.Count < segmentsPerOutput)
            {
                throw new InvalidOperationException("Not enough segments for mixing.");
            }

            if (autoDetectSimilar)
            {
                log("Detecting similar segments...");
                segments = FilterSimilarSegments(segments, similarityThreshold, ffmpeg, cancellationToken, log);
                if (segments.Count < segmentsPerOutput)
                {
                    throw new InvalidOperationException("Not enough segments after filtering.");
                }
            }

            if (pauseForManualDelete)
            {
                manualPauseAsync(segmentsDir, cancellationToken)
                    .GetAwaiter()
                    .GetResult();

                segments = Directory.GetFiles(segmentsDir, "scene_*.mp4")
                    .OrderBy(p => p)
                    .ToList();

                if (segments.Count < segmentsPerOutput)
                {
                    throw new InvalidOperationException("Not enough segments after manual delete.");
                }
            }

            string? lastKey = null;
            for (var i = 0; i < outputsPerInput; i++)
            {
                CheckPause(cancellationToken);
                var indices = PickIndicesBucketed(segments.Count, segmentsPerOutput, lastKey, random);
                var key = string.Join(",", indices);
                lastKey = key;

                log($"Mix {i}: {key}");
                var outputPath = Path.Combine(outputFolder, $"{baseName}_mix_{i}_{timeStamp}.mp4");
                var selected = indices.Select(idx => segments[idx]).ToArray();
                ConcatSegments(selected, outputPath, ffmpeg, ffprobe, crossfade, cancellationToken, log);
            }
        }
        finally
        {
            if (Directory.Exists(segmentsDir))
            {
                Directory.Delete(segmentsDir, true);
            }
        }
    }

    private static List<string> SplitSequentialRandom(
        string videoPath,
        string segmentsDir,
        int minTime,
        int maxTime,
        string ffmpeg,
        string ffprobe,
        bool fastSplit,
        Random random,
        string timeStamp,
        CancellationToken cancellationToken,
        Action<string> log)
    {
        var total = GetDurationSeconds(videoPath, ffprobe);
        var current = 0.0;
        var index = 0;
        var segments = new System.Collections.Generic.List<string>();

        while (current < total)
        {
            CheckPause(cancellationToken);
            var duration = NextSegmentLength(random, minTime, maxTime);
            if (current + duration > total)
            {
                duration = Math.Floor(total - current);
            }

            if (duration < minTime)
            {
                break;
            }

            var segmentPath = Path.Combine(segmentsDir, $"scene_{index + 1}_{timeStamp}.mp4");
            log($"  -> Scene {index + 1}: start {Math.Floor(current)}s, duration {duration:0.0}s");

            if (fastSplit)
            {
                RunProcess(ffmpeg,
                    $"-y -ss {current.ToString(CultureInfo.InvariantCulture)} -t {duration.ToString("0.0", CultureInfo.InvariantCulture)} -i \"{videoPath}\" -c copy \"{segmentPath}\"",
                    cancellationToken);
            }
            else
            {
                RunProcess(ffmpeg,
                    $"-y -i \"{videoPath}\" -ss {current.ToString(CultureInfo.InvariantCulture)} -t {duration.ToString("0.0", CultureInfo.InvariantCulture)} -c:v libx264 -preset fast -crf 23 -c:a aac -b:a 128k \"{segmentPath}\"",
                    cancellationToken);
            }

            segments.Add(segmentPath);
            current += duration;
            index++;
        }

        return segments;
    }

    private static void DirectMix(
        string videoPath,
        string baseName,
        int minTime,
        int maxTime,
        int segmentsPerOutput,
        int outputsPerInput,
        string outputFolder,
        string ffmpeg,
        string ffprobe,
        bool fastSplit,
        bool crossfade,
        Random random,
        string timeStamp,
        CancellationToken cancellationToken,
        Action<string> log)
    {
        if (segmentsPerOutput < 1 || outputsPerInput < 1)
        {
            throw new ArgumentException("Mix values are invalid.");
        }

        var total = GetDurationSeconds(videoPath, ffprobe);
        var buckets = BuildTimeBuckets(total, segmentsPerOutput);

        foreach (var bucket in buckets)
        {
            if (bucket.length < minTime)
            {
                throw new InvalidOperationException("Video is too short for the selected min seconds.");
            }
        }

        var tempDir = Path.Combine(outputFolder, "_direct", baseName);
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var outIdx = 0; outIdx < outputsPerInput; outIdx++)
            {
                CheckPause(cancellationToken);
                log($"Creating video {outIdx + 1}/{outputsPerInput}...");
                var segments = new List<string>();

                for (var i = 0; i < buckets.Count; i++)
                {
                    var (start, length) = buckets[i];
                    var segLen = NextSegmentLength(random, minTime, maxTime);
                    if (segLen > length)
                    {
                        segLen = Math.Floor(length);
                    }

                    if (segLen < minTime)
                    {
                        throw new InvalidOperationException("Video is too short for the selected min seconds.");
                    }

                    var maxStart = start + (length - segLen);
                    var segStart = maxStart <= start ? start : start + (random.NextDouble() * (maxStart - start));
                    var segPath = Path.Combine(tempDir, $"scene_{i + 1}_{outIdx}_{timeStamp}.mp4");
                    CutSegment(videoPath, segStart, segLen, segPath, ffmpeg, fastSplit, cancellationToken);
                    segments.Add(segPath);
                }

                var outputPath = Path.Combine(outputFolder, $"{baseName}_mix_{outIdx}_{timeStamp}.mp4");
                log($"Mixing segments for video {outIdx + 1}/{outputsPerInput}...");
                ConcatSegments(segments.ToArray(), outputPath, ffmpeg, ffprobe, crossfade, cancellationToken, log);
                log($"Completed video {outIdx + 1}/{outputsPerInput}: {Path.GetFileName(outputPath)}");

                foreach (var seg in segments)
                {
                    try { File.Delete(seg); } catch { }
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private static List<(double start, double length)> BuildTimeBuckets(double totalSeconds, int count)
    {
        var buckets = new List<(double start, double length)>();
        for (var i = 0; i < count; i++)
        {
            var start = (totalSeconds * i) / count;
            var end = (totalSeconds * (i + 1)) / count;
            var length = Math.Max(0, end - start);
            buckets.Add((start, length));
        }

        return buckets;
    }

    private static void CutSegment(
        string videoPath,
        double start,
        double duration,
        string outputPath,
        string ffmpeg,
        bool fastSplit,
        CancellationToken cancellationToken)
    {
        var durArg = duration.ToString("0.0", CultureInfo.InvariantCulture);
        if (fastSplit)
        {
            RunProcess(ffmpeg,
                $"-y -ss {start.ToString(CultureInfo.InvariantCulture)} -t {durArg} -i \"{videoPath}\" -c copy \"{outputPath}\"",
                cancellationToken);
        }
        else
        {
            RunProcess(ffmpeg,
                $"-y -i \"{videoPath}\" -ss {start.ToString(CultureInfo.InvariantCulture)} -t {durArg} -c:v libx264 -preset fast -crf 23 -c:a aac -b:a 128k \"{outputPath}\"",
                cancellationToken);
        }
    }

    private static double NextSegmentLength(Random random, int minTime, int maxTime)
    {
        if (minTime >= maxTime)
        {
            return minTime;
        }

        var value = minTime + (maxTime - minTime) * random.NextDouble();
        // round to 0.1s
        var rounded = Math.Round(value * 10.0) / 10.0;
        if (rounded < minTime)
        {
            rounded = minTime;
        }
        if (rounded > maxTime)
        {
            rounded = maxTime;
        }

        return rounded;
    }

    private static List<string> FilterSimilarSegments(
        List<string> segments,
        double threshold,
        string ffmpeg,
        CancellationToken cancellationToken,
        Action<string> log)
    {
        var kept = new List<string>();
        var hashes = new Dictionary<string, bool[]>();

        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool[] hash;
            try
            {
                hash = ExtractFrameHash(segment, ffmpeg, cancellationToken);
            }
            catch (Exception ex)
            {
                log($"Skip hash {Path.GetFileName(segment)}: {ex.Message}");
                kept.Add(segment);
                continue;
            }

            var isSimilar = false;
            foreach (var keptSeg in kept)
            {
                if (!hashes.TryGetValue(keptSeg, out var other))
                {
                    continue;
                }

                if (Similarity(hash, other) >= threshold)
                {
                    isSimilar = true;
                    break;
                }
            }

            if (isSimilar)
            {
                log($"Delete similar segment: {Path.GetFileName(segment)}");
                try { File.Delete(segment); } catch { }
            }
            else
            {
                hashes[segment] = hash;
                kept.Add(segment);
            }
        }

        return kept;
    }

    private static bool[] ExtractFrameHash(string segmentPath, string ffmpeg, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"frame_{Guid.NewGuid():N}.png");
        try
        {
            RunProcess(ffmpeg,
                $"-y -v error -i \"{segmentPath}\" -vf \"scale=32:32:flags=bilinear,format=gray\" -frames:v 1 \"{tempFile}\"",
                cancellationToken);

            using var bmp = new Bitmap(tempFile);
            var pixels = new List<byte>(bmp.Width * bmp.Height);
            for (var y = 0; y < bmp.Height; y++)
            {
                for (var x = 0; x < bmp.Width; x++)
                {
                    var color = bmp.GetPixel(x, y);
                    pixels.Add(color.R);
                }
            }

            var avg = pixels.Average(b => b);
            return pixels.Select(p => p >= avg).ToArray();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static double Similarity(bool[] a, bool[] b)
    {
        if (a.Length != b.Length)
        {
            return 0;
        }

        var matches = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i])
            {
                matches++;
            }
        }

        return matches / (double)a.Length;
    }

    private static int[] PickIndicesBucketed(int total, int count, string? lastKey, Random random)
    {
        var buckets = BuildBuckets(total, count);
        var attempts = 0;
        while (attempts < 30)
        {
            var indices = buckets
                .Select(bucket => bucket[random.Next(bucket.Count)])
                .ToArray();

            var key = string.Join(",", indices);
            if (key != lastKey)
            {
                return indices;
            }

            attempts++;
        }

        return buckets
            .Select(bucket => bucket[random.Next(bucket.Count)])
            .ToArray();
    }

    private static List<List<int>> BuildBuckets(int total, int count)
    {
        var buckets = new List<List<int>>();
        for (var i = 0; i < count; i++)
        {
            var start = (int)Math.Floor(i * (double)total / count);
            var end = (int)Math.Floor((i + 1) * (double)total / count);
            if (end <= start)
            {
                end = start + 1;
            }

            var bucket = new List<int>();
            for (var idx = start; idx < Math.Min(end, total); idx++)
            {
                bucket.Add(idx);
            }
            buckets.Add(bucket);
        }

        return buckets;
    }

    private static void ConcatSegments(
        string[] segments,
        string outputPath,
        string ffmpeg,
        string ffprobe,
        bool crossfade,
        CancellationToken cancellationToken,
        Action<string> log)
    {
        var listFile = Path.Combine(Path.GetTempPath(), $"concat_{Guid.NewGuid():N}.txt");
        try
        {
            if (crossfade && segments.Length > 1)
            {
                var durations = segments.Select(seg => GetDurationSeconds(seg, ffprobe)).ToList();
                var fades = new List<double>();
                for (var i = 1; i < durations.Count; i++)
                {
                    fades.Add(CalcFadeDuration(durations[i - 1], durations[i]));
                }

                var filters = new List<string>();
                var currentLen = durations[0];
                filters.Add($"[0:v][1:v]xfade=transition=fade:duration={fades[0].ToString(CultureInfo.InvariantCulture)}:offset={(currentLen - fades[0]).ToString(CultureInfo.InvariantCulture)}[v1]");
                filters.Add($"[0:a][1:a]acrossfade=d={fades[0].ToString(CultureInfo.InvariantCulture)}[a1]");
                currentLen = currentLen + durations[1] - fades[0];

                for (var i = 2; i < segments.Length; i++)
                {
                    var fade = fades[i - 1];
                    var offset = currentLen - fade;
                    filters.Add($"[v{i - 1}][{i}:v]xfade=transition=fade:duration={fade.ToString(CultureInfo.InvariantCulture)}:offset={offset.ToString(CultureInfo.InvariantCulture)}[v{i}]");
                    filters.Add($"[a{i - 1}][{i}:a]acrossfade=d={fade.ToString(CultureInfo.InvariantCulture)}[a{i}]");
                    currentLen = currentLen + durations[i] - fade;
                }

                var vout = $"[v{segments.Length - 1}]";
                var aout = $"[a{segments.Length - 1}]";

                var cmd = $"{string.Join(" ", segments.Select(s => $"-i \"{s}\""))} -filter_complex \"{string.Join(";", filters)}\" -map {vout} -map {aout} -c:v libx264 -preset fast -crf 23 -c:a aac -b:a 128k -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";
                RunProcess(ffmpeg, "-y " + cmd, cancellationToken);
                return;
            }

            using (var writer = new StreamWriter(listFile))
            {
                foreach (var segment in segments)
                {
                    var safePath = segment.Replace("\\", "/").Replace("'", "'\\''");
                    writer.WriteLine($"file '{safePath}'");
                }
            }

            try
            {
                RunProcess(ffmpeg,
                    $"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{outputPath}\"",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                log($"Concat copy failed, re-encoding: {ex.Message}");
                RunProcess(ffmpeg,
                    $"-y -f concat -safe 0 -i \"{listFile}\" -c:v libx264 -preset fast -crf 23 -c:a aac -b:a 128k \"{outputPath}\"",
                    cancellationToken);
            }
        }
        finally
        {
            if (File.Exists(listFile))
            {
                File.Delete(listFile);
            }
        }
    }

    private static void MoveProcessed(string source, string destination)
    {
        try
        {
            File.Move(source, destination, true);
        }
        catch
        {
            File.Copy(source, destination, true);
            File.Delete(source);
        }
    }

    private static string RunProcess(string exe, string args, CancellationToken? cancellationToken = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Cannot start ffmpeg/ffprobe process.");
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        while (!process.WaitForExit(200))
        {
            if (cancellationToken.HasValue)
            {
                CheckPause(cancellationToken.Value);
            }

            if (cancellationToken.HasValue && cancellationToken.Value.IsCancellationRequested)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // ignore
                }

                throw new OperationCanceledException();
            }
        }

        var stdOut = stdOutTask.GetAwaiter().GetResult();
        var stdErr = stdErrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stdErr))
        {
            throw new InvalidOperationException(stdErr.Trim());
        }

        return string.IsNullOrWhiteSpace(stdOut) ? stdErr : stdOut;
    }

    private static double CalcFadeDuration(double prev, double next)
    {
        var baseVal = Math.Min(prev, next) * 0.2;
        baseVal = Math.Min(0.5, baseVal);
        baseVal = Math.Max(0.05, baseVal);
        return Math.Round(baseVal, 2);
    }
}
