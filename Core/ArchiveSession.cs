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
        string[] allFiles = Array.Empty<string>();
        FileClassificationResult? classificationResult = null;
        string? compressedArchivePath = null;
        string? storedArchivePath = null;

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async context =>
            {
                ProgressTask scanTask = context.AddTask("[cyan]扫描文件[/]", maxValue: 1);
                ProgressTask sortTask = context.AddTask("[cyan]排序可压缩文件[/]", maxValue: 1);
                ProgressTask compressTask = context.AddTask("[cyan]生成压缩包[/]", maxValue: 2);
                ProgressTask persistTask = context.AddTask("[cyan]写入数据库[/]", maxValue: 1);

                allFiles = Directory.GetFiles(fullTargetDirectory, "*", SearchOption.AllDirectories);
                if (allFiles.Length == 0)
                {
                    throw new InvalidOperationException("目标目录中没有可归档的文件。");
                }

                scanTask.Increment(1);

                                classificationResult = _fileSorter.ClassifyAndSort(fullTargetDirectory, allFiles, includeHighEntropyFilesInCompression);

                sortTask.Increment(1);

                string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
                compressedArchivePath = Path.Combine(_repository.ArchiveOutputDirectory, $"{timestamp}_compressed.7z");
                storedArchivePath = Path.Combine(_repository.ArchiveOutputDirectory, $"{timestamp}_stored.7z");

                if (classificationResult.CompressedFiles.Count > 0)
                {
                    await _sevenZipRunner.CreateArchiveAsync(
                        compressedArchivePath,
                        fullTargetDirectory,
                        classificationResult.CompressedFiles.Select(file => file.ArchiveEntryPath).ToList(),
                        compressionOptions,
                        cancellationToken);
                }
                else
                {
                    compressedArchivePath = null;
                }

                compressTask.Increment(1);

                if (classificationResult.StoredFiles.Count > 0)
                {
                    await _sevenZipRunner.CreateArchiveAsync(
                        storedArchivePath,
                        fullTargetDirectory,
                        classificationResult.StoredFiles.Select(file => file.ArchiveEntryPath).ToList(),
                        ArchiveCompressionOptions.Store,
                        cancellationToken);
                }
                else
                {
                    storedArchivePath = null;
                }

                compressTask.Increment(1);

                List<ArchiveFile> files = BuildArchiveFiles(classificationResult);
                Archive archive = new()
                {
                    CompressedPath = compressedArchivePath,
                    StoredPath = storedArchivePath,
                    CreatedAt = DateTime.UtcNow,
                    Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                    Tags = NormalizeTags(tags)
                };

                try
                {
                    long archiveId = await _repository.InsertArchiveAsync(archive, files, cancellationToken);
                    persistTask.Increment(1);
                    CreatedArchiveId = archiveId;
                }
                catch
                {
                    CleanupArchive(compressedArchivePath);
                    CleanupArchive(storedArchivePath);
                    throw;
                }
            });

        return CreatedArchiveId;
    }

    public long CreatedArchiveId { get; private set; }

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

