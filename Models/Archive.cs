namespace Vault.Models;

public sealed class Archive
{
    public long Id { get; set; }
    public string? CompressedPath { get; set; }
    public string? StoredPath { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
    public string? Tags { get; set; }
}

public sealed class ArchiveSummary
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
    public string? Tags { get; set; }
    public int FileCount { get; set; }
}

public sealed class SearchResultItem
{
    public long ArchiveId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
    public string? Tags { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public bool IsStored { get; set; }
}
