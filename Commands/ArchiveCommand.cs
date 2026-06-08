using System.CommandLine;
using System.CommandLine.Invocation;
using Spectre.Console;
using Vault.Core;
using Vault.Data;

namespace Vault.Commands;

public static class ArchiveCommand
{
    public static Command Create(ArchiveRepository repository)
    {
        Argument<string> directoryArgument = new("directory", "Directory to archive / 要归档的目录路径");
        Option<string?> noteOption = new("--note", "Archive note / 归档备注");
        Option<string?> tagsOption = new("--tags", "Comma-separated tags / 标签，使用逗号分隔");
        Option<string> levelOption = new("--level", () => "ultra", "Compression level: store, fastest, fast, normal, maximum, ultra / 压缩等级");
        Option<string?> dictionaryOption = new("--dict", "7z dictionary size, e.g. 32m, 64m, 128m / 字典大小");
        Option<int?> fastBytesOption = new("--fast-bytes", "7z fast bytes, 5-273 / Fast bytes");
        Option<string?> matchFinderOption = new("--match-finder", "7z match finder: bt2, bt3, bt4, hc4 / 匹配器");
        Option<string?> solidOption = new("--solid", "7z solid mode, e.g. on, off, 512m, 2g / solid 模式");
        Option<bool> compressHighEntropyOption = new("--compress-high-entropy", () => false, "Also compress high-entropy/pre-compressed files / 高熵或已压缩文件也进入压缩包");

        Command command = new("archive", "Archive a directory / 归档指定目录");
        command.AddArgument(directoryArgument);
        command.AddOption(noteOption);
        command.AddOption(tagsOption);
        command.AddOption(levelOption);
        command.AddOption(dictionaryOption);
        command.AddOption(fastBytesOption);
        command.AddOption(matchFinderOption);
        command.AddOption(solidOption);
        command.AddOption(compressHighEntropyOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            try
            {
                string directory = context.ParseResult.GetValueForArgument(directoryArgument);
                string? note = context.ParseResult.GetValueForOption(noteOption);
                string? tags = context.ParseResult.GetValueForOption(tagsOption);
                string level = context.ParseResult.GetValueForOption(levelOption) ?? "ultra";
                string? dictionary = context.ParseResult.GetValueForOption(dictionaryOption);
                int? fastBytes = context.ParseResult.GetValueForOption(fastBytesOption);
                string? matchFinder = context.ParseResult.GetValueForOption(matchFinderOption);
                string? solid = context.ParseResult.GetValueForOption(solidOption);
                bool compressHighEntropy = context.ParseResult.GetValueForOption(compressHighEntropyOption);

                ArchiveCompressionOptions compressionOptions = BuildCompressionOptions(level, dictionary, fastBytes, matchFinder, solid);
                ArchiveSession session = new(repository);
                long archiveId = await session.RunAsync(directory, note, tags, compressionOptions, compressHighEntropy);

                AnsiConsole.MarkupLine($"[green]Archive completed / 归档完成，Archive ID: {archiveId}[/]");
                AnsiConsole.MarkupLine($"[cyan]Output directory / 输出目录：{Markup.Escape(repository.ArchiveOutputDirectory)}[/]");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AnsiConsole.MarkupLine($"[red]Archive failed / 归档失败：{Markup.Escape(exception.Message)}[/]");
            }
        });

        return command;
    }

    private static ArchiveCompressionOptions BuildCompressionOptions(
        string level,
        string? dictionary,
        int? fastBytes,
        string? matchFinder,
        string? solid)
    {
        SevenZipCompressionLevel compressionLevel = ParseCompressionLevel(level);
        return new ArchiveCompressionOptions
        {
            Level = compressionLevel,
            DictionarySize = NormalizeOptionalValue(dictionary),
            FastBytes = fastBytes,
            MatchFinder = NormalizeOptionalValue(matchFinder),
            SolidMode = NormalizeOptionalValue(solid)
        }.Validate();
    }

    private static SevenZipCompressionLevel ParseCompressionLevel(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "store" or "0" => SevenZipCompressionLevel.Store,
            "fastest" or "1" => SevenZipCompressionLevel.Fastest,
            "fast" or "3" => SevenZipCompressionLevel.Fast,
            "normal" or "5" => SevenZipCompressionLevel.Normal,
            "maximum" or "max" or "7" => SevenZipCompressionLevel.Maximum,
            "ultra" or "9" => SevenZipCompressionLevel.Ultra,
            _ => throw new InvalidOperationException("无效压缩等级。可用值：store, fastest, fast, normal, maximum, ultra。")
        };
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}


