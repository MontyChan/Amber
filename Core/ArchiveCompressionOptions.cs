namespace Vault.Core;

public sealed class ArchiveCompressionOptions
{
    public static ArchiveCompressionOptions Store { get; } = new()
    {
        Level = SevenZipCompressionLevel.Store
    };

    public SevenZipCompressionLevel Level { get; init; } = SevenZipCompressionLevel.Ultra;

    public string? DictionarySize { get; init; }

    public int? FastBytes { get; init; }

    public string? MatchFinder { get; init; }

    public string? SolidMode { get; init; }

    public bool IsStore => Level == SevenZipCompressionLevel.Store;

    public ArchiveCompressionOptions Validate()
    {
        if (IsStore)
        {
            if (!string.IsNullOrWhiteSpace(DictionarySize) || FastBytes.HasValue || !string.IsNullOrWhiteSpace(MatchFinder) || !string.IsNullOrWhiteSpace(SolidMode))
            {
                throw new InvalidOperationException("仅存储模式不能再设置字典、Fast bytes、匹配器或固实块参数。");
            }

            return this;
        }

        if (FastBytes is < 5 or > 273)
        {
            throw new InvalidOperationException("Fast bytes 必须在 5 到 273 之间。");
        }

        if (!string.IsNullOrWhiteSpace(MatchFinder))
        {
            string normalized = MatchFinder.Trim().ToLowerInvariant();
            if (normalized is not ("bt2" or "bt3" or "bt4" or "hc4"))
            {
                throw new InvalidOperationException("Match finder 仅支持 bt2、bt3、bt4、hc4。");
            }
        }

        return this;
    }
}

public enum SevenZipCompressionLevel
{
    Store = 0,
    Fastest = 1,
    Fast = 3,
    Normal = 5,
    Maximum = 7,
    Ultra = 9
}
