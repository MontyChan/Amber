using System.Diagnostics;
using System.Globalization;
using Spectre.Console;
using Vault.Data;
using Vault.Models;

namespace Vault.Core;

public sealed class ArchiveSession
{
    private readonly ArchiveRepository _repository;
    private readonly FileSorter _fileSorter;
    private readonly SevenZipRunner _sevenZipRunner;

    public ArchiveSession(ArchiveRepository repository)
    {
        _repository = repository;
        _fileSorter = new FileSorter();
        _sevenZipRunner = new SevenZipRunner();
    }

    public Task<long> RunAsync(string targetDirectory, string? note, string? tags, CancellationToken cancellationToken = default)
    {
        return RunAsync(targetDirectory, note, tags, new ArchiveCompressionOptions(), includeHighEntropyFilesInCompression: false, cancellationToken);
    }

    public Task<long> RunAsync(
        string targetDirectory,
        string? note,
        string? tags,
        ArchiveCompressionOptions compressionOptions,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(targetDirectory, note, tags, compressionOptions, includeHighEntropyFilesInCompression: false, cancellationToken);
    }

    public async Task<long> RunAsync(
        string targetDirectory,
        string? note,
        string? tags,
        ArchiveCompressionOptions compressionOptions,
        bool includeHighEntropyFilesInCompression,
        CancellationToken cancellationToken = default)
    {
        string fullTargetDirectory = Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(fullTargetDirectory))
        {
            throw new DirectoryNotFoundException($"目标目录不存在：{fullTargetDirectory}");
        }

        compressionOptions = compressionOptions.Validate();
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        TimeSpan scanDuration = TimeSpan.Zero;
        TimeSpan classifyDuration = TimeSpan.Zero;
        TimeSpan compressDuration = TimeSpan.Zero;
        TimeSpan persistDuration = TimeSpan.Zero;
        string[] allFiles = Array.Empty<string>();
        FileClassificationResult? classificationResult = null;
        string? compressedArchivePath = null;
        string? storedArchivePath = null;
        int completedProgressTitleWidth = 0;
        int completedProgressDescriptionWidth = 0;

                await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async context =>
            {
                Stopwatch phaseStopwatch = Stopwatch.StartNew();

                ProgressTask scanTask = context.AddTask("[cyan]扫描文件中[/]", maxValue: 100);
                ProgressTask sortTask = context.AddTask("[grey]等待整理归档顺序[/]", maxValue: 100, autoStart: false);
                ProgressTask compressTask = context.AddTask("[grey]等待生成归档包[/]", maxValue: 100, autoStart: false);
                ProgressTask persistTask = context.AddTask("[grey]等待写入数据库[/]", maxValue: 100, autoStart: false);

                scanTask.IsIndeterminate = true;

                allFiles = Directory.EnumerateFiles(fullTargetDirectory, "*", SearchOption.AllDirectories).ToArray();
                scanDuration = phaseStopwatch.Elapsed;
                if (allFiles.Length == 0)
                {
                    throw new InvalidOperationException("目标目录中没有可归档的文件。");
                }

                completedProgressTitleWidth = GetCompletedProgressTitleWidth();
                completedProgressDescriptionWidth = GetCompletedProgressDescriptionWidth(allFiles.Length, completedProgressTitleWidth);
                scanTask.IsIndeterminate = false;
                SetTaskValue(scanTask, 100);
                scanTask.Description = FormatCompletedProgressDescription("扫描完成", $"{allFiles.Length:N0} files", "green", completedProgressTitleWidth, completedProgressDescriptionWidth);

                sortTask.MaxValue = allFiles.Length;
                sortTask.Description = FormatActiveProgressDescription("整理归档顺序", $"0/{allFiles.Length:N0}", "cyan");
                phaseStopwatch.Restart();
                classificationResult = _fileSorter.ClassifyAndSort(
                    fullTargetDirectory,
                    allFiles,
                    includeHighEntropyFilesInCompression,
                    processed =>
                    {
                        SetTaskValue(sortTask, processed);
                        sortTask.Description = FormatActiveProgressDescription("整理归档顺序", $"{processed:N0}/{allFiles.Length:N0}", "cyan");
                    });
                classifyDuration = phaseStopwatch.Elapsed;
                sortTask.Description = FormatCompletedProgressDescription(
                    "整理完成",
                    $"compressed: {classificationResult.CompressedFiles.Count:N0}, stored: {classificationResult.StoredFiles.Count:N0}",
                    "green",
                    completedProgressTitleWidth,
                    completedProgressDescriptionWidth);
                SetTaskValue(sortTask, allFiles.Length);

                string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
                compressedArchivePath = Path.Combine(_repository.ArchiveOutputDirectory, $"{timestamp}_compressed.7z");
                storedArchivePath = Path.Combine(_repository.ArchiveOutputDirectory, $"{timestamp}_stored.7z");

                compressTask.MaxValue = 100;
                compressTask.Description = "[grey]等待生成归档包[/]";
                phaseStopwatch.Restart();

                if (classificationResult.CompressedFiles.Count > 0)
                {
                    compressTask.Description = FormatActiveProgressDescription("生成压缩包", $"compressed: {classificationResult.CompressedFiles.Count:N0} files", "cyan");
                    await _sevenZipRunner.CreateArchiveAsync(
                        compressedArchivePath,
                        fullTargetDirectory,
                        classificationResult.CompressedFiles.Select(file => file.ArchiveEntryPath).ToList(),
                        compressionOptions,
                        progress => SetTaskValue(compressTask, progress),
                        cancellationToken);
                }
                else
                {
                    compressedArchivePath = null;
                    SetTaskValue(compressTask, 50);
                }

                if (classificationResult.StoredFiles.Count > 0)
                {
                    compressTask.Description = FormatActiveProgressDescription("生成存储包", $"stored: {classificationResult.StoredFiles.Count:N0} files", "cyan");
                    SetTaskValue(compressTask, classificationResult.CompressedFiles.Count > 0 ? 50 : 0);
                    await _sevenZipRunner.CreateArchiveAsync(
                        storedArchivePath,
                        fullTargetDirectory,
                        classificationResult.StoredFiles.Select(file => file.ArchiveEntryPath).ToList(),
                        ArchiveCompressionOptions.Store,
                        progress => SetTaskValue(compressTask, classificationResult.CompressedFiles.Count > 0 ? 50 + (progress / 2d) : progress),
                        cancellationToken);
                }
                else
                {
                    storedArchivePath = null;
                }

                compressDuration = phaseStopwatch.Elapsed;
                compressTask.Description = FormatCompletedProgressDescription("归档包已生成", null, "green", completedProgressTitleWidth, completedProgressDescriptionWidth);
                SetTaskValue(compressTask, 100);

                List<ArchiveFile> files = BuildArchiveFiles(classificationResult);
                Archive archive = new()
                {
                    CompressedPath = compressedArchivePath,
                    StoredPath = storedArchivePath,
                    CreatedAt = DateTime.UtcNow,
                    Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                    Tags = NormalizeTags(tags)
                };

                persistTask.MaxValue = Math.Max(files.Count, 1);
                persistTask.Description = FormatActiveProgressDescription("写入数据库", $"0/{files.Count:N0}", "cyan");
                phaseStopwatch.Restart();

                try
                {
                    long archiveId = await _repository.InsertArchiveAsync(
                        archive,
                        files,
                        processed =>
                        {
                            SetTaskValue(persistTask, processed);
                            persistTask.Description = FormatActiveProgressDescription("写入数据库", $"{processed:N0}/{files.Count:N0}", "cyan");
                        },
                        cancellationToken);
                    persistDuration = phaseStopwatch.Elapsed;
                    persistTask.Description = FormatCompletedProgressDescription("数据库写入完成", $"{files.Count:N0} rows", "green", completedProgressTitleWidth, completedProgressDescriptionWidth);
                    SetTaskValue(persistTask, Math.Max(files.Count, 1));
                    CreatedArchiveId = archiveId;
                }
                catch
                {
                    CleanupArchive(compressedArchivePath);
                    CleanupArchive(storedArchivePath);
                    throw;
                }
            });



        totalStopwatch.Stop();
        WritePerformanceSummary(allFiles.Length, classificationResult, scanDuration, classifyDuration, compressDuration, persistDuration, totalStopwatch.Elapsed);

        return CreatedArchiveId;
    }

    public long CreatedArchiveId { get; private set; }

    private static void WritePerformanceSummary(
        int totalFiles,
        FileClassificationResult? classificationResult,
        TimeSpan scanDuration,
        TimeSpan classifyDuration,
        TimeSpan compressDuration,
        TimeSpan persistDuration,
        TimeSpan totalDuration)
    {
        if (classificationResult is null)
        {
            return;
        }

        AnsiConsole.MarkupLine($"[grey]Archive pipeline: scan {FormatDuration(scanDuration)}, classify {FormatDuration(classifyDuration)}, pack {FormatDuration(compressDuration)}, db {FormatDuration(persistDuration)}, total {FormatDuration(totalDuration)}[/]");
        AnsiConsole.MarkupLine($"[grey]归档阶段耗时：扫描 {FormatDuration(scanDuration)}，整理 {FormatDuration(classifyDuration)}，打包 {FormatDuration(compressDuration)}，写库 {FormatDuration(persistDuration)}，总计 {FormatDuration(totalDuration)}[/]");
        AnsiConsole.MarkupLine($"[grey]Files: total {totalFiles:N0}, compressed {classificationResult.CompressedFiles.Count:N0}, stored {classificationResult.StoredFiles.Count:N0}[/]");
        AnsiConsole.MarkupLine($"[grey]文件统计：总计 {totalFiles:N0}，压缩包 {classificationResult.CompressedFiles.Count:N0}，仅存储包 {classificationResult.StoredFiles.Count:N0}[/]");
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return $"{duration.TotalSeconds:F2}s";
    }

    private static void SetTaskValue(ProgressTask task, double value)
    {
        double delta = value - task.Value;
        if (delta > 0)
        {
            task.Increment(delta);
        }
    }

    private static string FormatActiveProgressDescription(string title, string? detail, string color)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return $"[{color}]{Markup.Escape(title)}[/]";
        }

        return $"[{color}]{Markup.Escape(title)}[/] [grey]({Markup.Escape(detail)})[/]";
    }

    private static string FormatCompletedProgressDescription(string title, string? detail, string color, int titleWidth, int targetWidth)
    {
        string paddedTitle = PadToDisplayWidth(title, titleWidth);
        string plainText = string.IsNullOrWhiteSpace(detail)
            ? paddedTitle
            : $"{paddedTitle} ({detail})";
        int trailingPadding = Math.Max(targetWidth - GetDisplayWidth(plainText), 0);

        if (string.IsNullOrWhiteSpace(detail))
        {
            return $"[{color}]{Markup.Escape(paddedTitle + new string(' ', trailingPadding))}[/]";
        }

        string paddedDetail = $"({detail})" + new string(' ', trailingPadding);
        return $"[{color}]{Markup.Escape(paddedTitle)}[/] [grey]{Markup.Escape(paddedDetail)}[/]";
    }

    private static int GetCompletedProgressTitleWidth()
    {
        string[] titles =
        {
            "扫描完成",
            "整理完成",
            "归档包已生成",
            "数据库写入完成"
        };

        return titles.Max(GetDisplayWidth);
    }

    private static int GetCompletedProgressDescriptionWidth(int totalFiles, int titleWidth)
    {
        string fileCount = $"{totalFiles:N0}";
        string[] candidates =
        {
            FormatCompletedProgressPlainText("扫描完成", $"{fileCount} files", titleWidth),
            FormatCompletedProgressPlainText("整理完成", $"compressed: {fileCount}, stored: {fileCount}", titleWidth),
            FormatCompletedProgressPlainText("归档包已生成", null, titleWidth),
            FormatCompletedProgressPlainText("数据库写入完成", $"{fileCount} rows", titleWidth)
        };

        return candidates.Max(GetDisplayWidth);
    }

    private static string FormatCompletedProgressPlainText(string title, string? detail, int titleWidth)
    {
        string paddedTitle = PadToDisplayWidth(title, titleWidth);
        return string.IsNullOrWhiteSpace(detail) ? paddedTitle : $"{paddedTitle} ({detail})";
    }

    private static string PadToDisplayWidth(string text, int targetWidth)
    {
        int padding = targetWidth - GetDisplayWidth(text);
        return padding > 0 ? text + new string(' ', padding) : text;
    }

    private static int GetDisplayWidth(string text)
    {
        int width = 0;
        foreach (char character in text)
        {
            width += GetCharDisplayWidth(character);
        }

        return width;
    }

    private static int GetCharDisplayWidth(char character)
    {
        if (char.IsControl(character))
        {
            return 0;
        }

        return IsWideCharacter(character) ? 2 : 1;
    }

    private static bool IsWideCharacter(char character)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
        if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark)
        {
            return false;
        }

        return character switch
        {
            >= '\u1100' and <= '\u115F' => true,
            >= '\u2329' and <= '\u232A' => true,
            >= '\u2E80' and <= '\uA4CF' => true,
            >= '\uAC00' and <= '\uD7A3' => true,
            >= '\uF900' and <= '\uFAFF' => true,
            >= '\uFE10' and <= '\uFE19' => true,
            >= '\uFE30' and <= '\uFE6F' => true,
            >= '\uFF00' and <= '\uFF60' => true,
            >= '\uFFE0' and <= '\uFFE6' => true,
            _ => false
        };
    }

    private static List<ArchiveFile> BuildArchiveFiles(FileClassificationResult classificationResult)
    {
        List<ArchiveFile> files = new(classificationResult.CompressedFiles.Count + classificationResult.StoredFiles.Count);

        foreach (ArchiveCandidateFile file in classificationResult.CompressedFiles)
        {
            files.Add(new ArchiveFile
            {
                RelativePath = file.RelativePath,
                Size = file.Size,
                ModifiedAt = file.ModifiedAt,
                IsStored = false
            });
        }

        foreach (ArchiveCandidateFile file in classificationResult.StoredFiles)
        {
            files.Add(new ArchiveFile
            {
                RelativePath = file.RelativePath,
                Size = file.Size,
                ModifiedAt = file.ModifiedAt,
                IsStored = true
            });
        }

        return files;
    }

    private static string? NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return null;
        }

        string[] normalized = tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? null : string.Join(',', normalized);
    }

    private static void CleanupArchive(string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return;
        }

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }
    }
}


