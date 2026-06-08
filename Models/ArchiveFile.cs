namespace Vault.Models;

public sealed class ArchiveFile
{
    public long Id { get; set; }
    public long ArchiveId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime ModifiedAt { get; set; }
    public bool IsStored { get; set; }
}
