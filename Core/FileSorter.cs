using System.Security.Cryptography;

namespace Vault.Core;

public sealed class FileSorter
{
    private static readonly HashSet<string> StoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".mkv", ".mov", ".avi", ".mp3", ".flac", ".aac", ".zip", ".7z", ".rar", ".gz", ".br", ".zst"
    };

        public FileClassificationResult ClassifyAndSort(string rootDirectory, IReadOnlyCollection<string> filePaths, bool includeHighEntropyFilesInCompression = false)

    {
        List<ArchiveCandidateFile> storedFiles = new();
        Dictionary<string, List<ArchiveCandidateFile>> compressibleByExtension = new(StringComparer.OrdinalIgnoreCase);

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
                continue;
            }

            string extensionKey = candidate.Extension.Length == 0 ? "<noext>" : candidate.Extension;
            if (!compressibleByExtension.TryGetValue(extensionKey, out List<ArchiveCandidateFile>? bucket))
            {
                bucket = new List<ArchiveCandidateFile>();
                compressibleByExtension[extensionKey] = bucket;
            }

            bucket.Add(candidate);
        }

        List<ArchiveCandidateFile> sortedCompressed = new();
        foreach ((string _, List<ArchiveCandidateFile> group) in compressibleByExtension.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            sortedCompressed.AddRange(SortBySimilarity(group));
        }

        return new FileClassificationResult(sortedCompressed, storedFiles.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToList());
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

    private static IReadOnlyList<ArchiveCandidateFile> SortBySimilarity(IReadOnlyList<ArchiveCandidateFile> group)
    {
        if (group.Count <= 1)
        {
            return group;
        }

        List<ArchiveCandidateFile> ordered = new(group.Count);
        List<ArchiveCandidateFile> remaining = group.Select(item => item with { Histogram = ReadHistogram(item.FullPath) }).ToList();
        ArchiveCandidateFile current = remaining[0];
        ordered.Add(current);
        remaining.RemoveAt(0);

        bool useApproximation = group.Count > 500;
        RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create();

        while (remaining.Count > 0)
        {
            int bestIndex = FindBestNextIndex(current, remaining, useApproximation, randomNumberGenerator);
            current = remaining[bestIndex];
            ordered.Add(current);
            remaining.RemoveAt(bestIndex);
        }

        randomNumberGenerator.Dispose();
        return ordered;
    }

    private static int FindBestNextIndex(
        ArchiveCandidateFile current,
        IReadOnlyList<ArchiveCandidateFile> candidates,
        bool useApproximation,
        RandomNumberGenerator randomNumberGenerator)
    {
        if (!useApproximation || candidates.Count <= 50)
        {
            return FindBestNextIndex(current, candidates, Enumerable.Range(0, candidates.Count));
        }

        HashSet<int> sampleIndexes = new();
        while (sampleIndexes.Count < 50 && sampleIndexes.Count < candidates.Count)
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
        const int readSize = 4096;
        byte[] histogram = new byte[readSize];
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int bytesRead = stream.Read(histogram, 0, readSize);
        double[] frequencies = new double[256];

        for (int index = 0; index < bytesRead; index++)
        {
            frequencies[histogram[index]] += 1;
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
