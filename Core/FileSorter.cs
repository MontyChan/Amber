namespace Vault.Core;

public sealed class FileSorter
{
    private const int ProgressReportInterval = 256;

    private static readonly HashSet<string> StoredExtensions = new(StringComparer.OrdinalIgnoreCase)


    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".mkv", ".mov", ".avi", ".mp3", ".flac", ".aac", ".zip", ".7z", ".rar", ".gz", ".br", ".zst"
    };

            public FileClassificationResult ClassifyAndSort(
        string rootDirectory,
        IReadOnlyCollection<string> filePaths,
        bool includeHighEntropyFilesInCompression = false,
        Action<int>? progressCallback = null)
    {
        List<ArchiveCandidateFile> storedFiles = new();
        Dictionary<string, List<ArchiveCandidateFile>> compressibleByExtension = new(StringComparer.OrdinalIgnoreCase);
        int processedCount = 0;

        foreach (string filePath in filePaths)
        {


            FileInfo fileInfo = new(filePath);
            string relativePath = NormalizeRelativePath(rootDirectory, filePath);
            ArchiveCandidateFile candidate = new(
                filePath,
                relativePath,
                ToArchiveEntryPath(relativePath),
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                Path.GetExtension(filePath));

                                    if (StoredExtensions.Contains(candidate.Extension) && !includeHighEntropyFilesInCompression)
            {
                storedFiles.Add(candidate);
                processedCount++;
                ReportProgress(processedCount, filePaths.Count, progressCallback);
                continue;
            }

            string extensionKey = candidate.Extension.Length == 0 ? "<noext>" : candidate.Extension;

            if (!compressibleByExtension.TryGetValue(extensionKey, out List<ArchiveCandidateFile>? bucket))
            {
                bucket = new List<ArchiveCandidateFile>();
                compressibleByExtension[extensionKey] = bucket;
            }

                                    bucket.Add(candidate);
            processedCount++;
            ReportProgress(processedCount, filePaths.Count, progressCallback);

        }

        if (processedCount < filePaths.Count)
        {
            ReportProgress(filePaths.Count, filePaths.Count, progressCallback);
        }

        List<ArchiveCandidateFile> sortedCompressed = new(filePaths.Count - storedFiles.Count);

        foreach ((string _, List<ArchiveCandidateFile> group) in compressibleByExtension.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            sortedCompressed.AddRange(group
                .OrderBy(file => GetDirectoryKey(file.RelativePath), StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(file => file.Size)
                .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase));
        }

        return new FileClassificationResult(
            sortedCompressed,
            storedFiles.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static string NormalizeRelativePath(string rootDirectory, string filePath)
    {
        string relativePath = Path.GetRelativePath(rootDirectory, filePath);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    public static string ToArchiveEntryPath(string relativePath)
    {
        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

        private static void ReportProgress(int processedCount, int totalCount, Action<int>? progressCallback)

    {
        if (progressCallback is null)
        {
            return;
        }

        if (processedCount < totalCount && processedCount % ProgressReportInterval != 0)
        {
            return;
        }

        progressCallback(processedCount);
    }

    private static string GetDirectoryKey(string relativePath)
    {

        int separatorIndex = relativePath.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : relativePath[..separatorIndex];
    }
}

public sealed class FileClassificationResult
{
    public FileClassificationResult(IReadOnlyList<ArchiveCandidateFile> compressedFiles, IReadOnlyList<ArchiveCandidateFile> storedFiles)
    {
        CompressedFiles = compressedFiles;
        StoredFiles = storedFiles;
    }

    public IReadOnlyList<ArchiveCandidateFile> CompressedFiles { get; }

    public IReadOnlyList<ArchiveCandidateFile> StoredFiles { get; }
}

public sealed record ArchiveCandidateFile(
    string FullPath,
    string RelativePath,
    string ArchiveEntryPath,
    long Size,
    DateTime ModifiedAt,
    string Extension);


