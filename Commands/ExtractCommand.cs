using System.CommandLine;
using Spectre.Console;
using Vault.Core;
using Vault.Data;

namespace Vault.Commands;

public static class ExtractCommand
{
    public static Command Create(ArchiveRepository repository)
    {
        Argument<long> archiveIdArgument = new("archive_id", "Archive ID to extract from / 要解压的归档 ID");
        Option<string> fileOption = new("--file", "Relative path to extract / 要解压的相对路径") { IsRequired = true };
        Option<string> outOption = new("--out", "Output directory / 输出目录") { IsRequired = true };

        Command command = new("extract", "Extract one file from an archive / 从归档中单独解压一个文件");
        command.AddArgument(archiveIdArgument);
        command.AddOption(fileOption);
        command.AddOption(outOption);

        command.SetHandler(async (long archiveId, string file, string outputDirectory) =>
        {
            try
            {
                Vault.Models.Archive? archive = await repository.GetArchiveByIdAsync(archiveId);
                if (archive is null)
                {
                    throw new InvalidOperationException($"Archive #{archiveId} was not found / 未找到 ID 为 {archiveId} 的归档记录。");
                }

                string normalizedRelativePath = file.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                Vault.Models.ArchiveFile? archiveFile = await repository.GetArchiveFileAsync(archiveId, normalizedRelativePath);
                if (archiveFile is null)
                {
                    throw new InvalidOperationException($"File does not exist in archive / 归档 #{archiveId} 中不存在文件：{normalizedRelativePath}");
                }

                string? packagePath = archiveFile.IsStored ? archive.StoredPath : archive.CompressedPath;
                if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
                {
                    throw new InvalidOperationException($"Archive package is missing / 归档包不存在：{packagePath ?? "<空路径>"}。可能已被移动或删除。");
                }

                SevenZipRunner runner = new();
                await runner.ExtractSingleFileAsync(packagePath, FileSorter.ToArchiveEntryPath(normalizedRelativePath), Path.GetFullPath(outputDirectory));
                AnsiConsole.MarkupLine($"[green]Extraction completed / 解压完成：[/][cyan]{Markup.Escape(normalizedRelativePath)}[/]");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AnsiConsole.MarkupLine($"[red]Extraction failed / 解压失败：{Markup.Escape(exception.Message)}[/]");
            }
        }, archiveIdArgument, fileOption, outOption);

        return command;
    }
}
