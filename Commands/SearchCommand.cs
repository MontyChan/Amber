using System.CommandLine;
using Spectre.Console;
using Vault.Data;
using Vault.UI;

namespace Vault.Commands;

public static class SearchCommand
{
    public static Command Create(ArchiveRepository repository)
    {
        Argument<string> keywordArgument = new("keyword", "Keyword to search / 搜索关键词");
        Command command = new("search", "Search archive file paths and notes / 搜索归档文件路径和备注");
        command.AddArgument(keywordArgument);

        command.SetHandler(async (string keyword) =>
        {
            try
            {
                InteractiveViewer viewer = new();
                IReadOnlyList<Vault.Models.SearchResultItem> results = await repository.SearchAsync(keyword);
                await viewer.ShowSearchResultsAsync(keyword, results, archiveId => repository.GetArchiveFilesAsync(archiveId));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AnsiConsole.MarkupLine($"[red]Search failed / 搜索失败：{Markup.Escape(exception.Message)}[/]");
            }
        }, keywordArgument);

        return command;
    }
}
