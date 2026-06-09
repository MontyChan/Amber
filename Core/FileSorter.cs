using System.Security.Cryptography;

namespace Vault.Core;

public sealed class FileSorter
{
    private const int HistogramReadSize = 4096;
    private const int FullSimilarityThreshold = 128;
    private const int ChunkedSimilarityThreshold = 1024;
        private const int SimilarityChunkSize = 64;
    private const int ApproximationSampleSize = 24;
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
            sortedCompressed.AddRange(SortForArchive(group));
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

    private static IReadOnlyList<ArchiveCandidateFile> SortForArchive(IReadOnlyList<ArchiveCandidateFile> group)
    {
        if (group.Count <= 1)
        {
            return group;
        }

        List<ArchiveCandidateFile> baselineOrder = group
            .OrderBy(file => GetDirectoryKey(file.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Extension, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(file => file.Size)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (baselineOrder.Count <= FullSimilarityThreshold)
        {
            return SortBySimilarity(baselineOrder, useApproximation: false);
        }

        if (baselineOrder.Count <= ChunkedSimilarityThreshold)
        {
            return SortInChunksBySimilarity(baselineOrder, SimilarityChunkSize);
        }

        return baselineOrder;
    }

    private static IReadOnlyList<ArchiveCandidateFile> SortInChunksBySimilarity(IReadOnlyList<ArchiveCandidateFile> orderedGroup, int chunkSize)
    {
        List<ArchiveCandidateFile> result = new(orderedGroup.Count);

        for (int index = 0; index < orderedGroup.Count; index += chunkSize)
        {
            int currentChunkSize = Math.Min(chunkSize, orderedGroup.Count - index);
            List<ArchiveCandidateFile> chunk = new(currentChunkSize);
            for (int chunkIndex = 0; chunkIndex < currentChunkSize; chunkIndex++)
            {
                chunk.Add(orderedGroup[index + chunkIndex]);
            }

            result.AddRange(SortBySimilarity(chunk, useApproximation: currentChunkSize > 32));
        }

        return result;
    }

    private static IReadOnlyList<ArchiveCandidateFile> SortBySimilarity(IReadOnlyList<ArchiveCandidateFile> group, bool useApproximation)
    {
        if (group.Count <= 1)
        {
            return group;
        }

        List<ArchiveCandidateFile> remaining = group
            .Select(item => item with { Histogram = ReadHistogram(item.FullPath) })
            .ToList();

        List<ArchiveCandidateFile> ordered = new(group.Count);
        ArchiveCandidateFile current = remaining[0];
        ordered.Add(current);
        remaining.RemoveAt(0);

        while (remaining.Count > 0)
        {
            int bestIndex = FindBestNextIndex(current, remaining, useApproximation);
            current = remaining[bestIndex];
            ordered.Add(current);
            remaining.RemoveAt(bestIndex);
        }

        return ordered;
    }

    private static int FindBestNextIndex(ArchiveCandidateFile current, IReadOnlyList<ArchiveCandidateFile> candidates, bool useApproximation)
    {
        if (!useApproximation || candidates.Count <= ApproximationSampleSize)
        {
            return FindBestNextIndex(current, candidates, Enumerable.Range(0, candidates.Count));
        }

        HashSet<int> sampleIndexes = new();
        while (sampleIndexes.Count < ApproximationSampleSize && sampleIndexes.Count < candidates.Count)
        {
            sampleIndexes.Add(RandomNumberGenerator.GetInt32(candidates.Count));
        }

        return FindBestNextIndex(current, candidates, sampleIndexes);
    }

    private static int FindBestNextIndex(ArchiveCandidateFile current, IReadOnlyList<ArchiveCandidateFile> candidates, IEnumerable<int> indexes)
    {
        double bestScore = double.MinValue;
        int bestIndex = 0;

        foreach (int index in indexes)
        {
            double score = CosineSimilarity(current.Histogram, candidates[index].Histogram);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static double[] ReadHistogram(string filePath)
    {
        byte[] buffer = new byte[HistogramReadSize];
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: HistogramReadSize, FileOptions.SequentialScan);
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        double[] frequencies = new double[256];

        for (int index = 0; index < bytesRead; index++)
        {
            frequencies[buffer[index]] += 1;
        }

        if (bytesRead == 0)
        {
            return frequencies;
        }

        for (int index = 0; index < frequencies.Length; index++)
        {
            frequencies[index] /= bytesRead;
        }

        return frequencies;
    }

    private static double CosineSimilarity(double[] left, double[] right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;

        for (int index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
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
    string Extension)
{
    public double[] Histogram { get; init; } = Array.Empty<double>();
}


