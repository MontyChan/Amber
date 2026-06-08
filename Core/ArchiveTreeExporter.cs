using Vault.Models;

namespace Vault.Core;

public sealed class ArchiveTreeExporter
{
    public async Task<string> ExportPlaceholderTreeAsync(
        Archive archive,
        IReadOnlyList<ArchiveFile> files,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        string rootDirectory = BuildRootDirectory(outputDirectory, archive);
        Directory.CreateDirectory(rootDirectory);

        foreach (ArchiveFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string destinationPath = BuildDestinationPath(rootDirectory, file.RelativePath);
            string? parentDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            await using FileStream stream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        }

        return rootDirectory;
    }

    private static string BuildRootDirectory(string outputDirectory, Archive archive)
    {
        string safeTimestamp = archive.CreatedAt.ToLocalTime().ToString("yyyyMMdd_HHmmss");
        return Path.Combine(Path.GetFullPath(outputDirectory), $"Archive_{archive.Id}_{safeTimestamp}");
    }

    private static string BuildDestinationPath(string rootDirectory, string relativePath)
    {
        string[] segments = relativePath
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizePathSegment)
            .ToArray();

        return segments.Aggregate(rootDirectory, Path.Combine);
    }

    private static string SanitizePathSegment(string segment)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] sanitized = segment
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray();

        string value = new string(sanitized).Trim();
        return string.IsNullOrWhiteSpace(value) ? "_" : value;
    }
}
