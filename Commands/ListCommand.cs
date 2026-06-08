using System.CommandLine;
using Spectre.Console;
using Vault.Data;
using Vault.UI;

namespace Vault.Commands;

public static class ListCommand
{
    public static Command Create(ArchiveRepository repository)
    {
        Command command = new("list", "List all archives / 列出所有归档记录");

        command.SetHandler(async () =>
        {
            try
            {
                InteractiveViewer viewer = new();
                IReadOnlyList<Vault.Models.ArchiveSummary> summaries = await repository.GetArchiveSummariesAsync();
                await viewer.ShowArchiveListAsync(summaries, archiveId => repository.GetArchiveFilesAsync(archiveId));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AnsiConsole.MarkupLine($"[red]Failed to read archive list / 读取归档列表失败：{Markup.Escape(exception.Message)}[/]");
            }
        });

        return command;
    }
}
