using Spectre.Console;
using Vault.Models;

namespace Vault.UI;

public sealed class InteractiveViewer
{
    private const string BackKeyword = "/back";

    public async Task ShowArchiveListAsync(
        IReadOnlyList<ArchiveSummary> summaries,
        Func<long, Task<IReadOnlyList<ArchiveFile>>> loadFilesAsync,
        Func<long, IReadOnlyList<ArchiveFile>, string, Task>? exportTreeAsync = null)
    {
        if (summaries.Count == 0)
        {
            WriteWarningLine("No archive records yet.", "当前还没有归档记录。");
            return;
        }

        await ShowPagedArchiveSelectionAsync(summaries, loadFilesAsync, exportTreeAsync, "Archives", "归档列表");
    }

    public async Task ShowSearchResultsAsync(
        string keyword,
        IReadOnlyList<SearchResultItem> results,
        Func<long, Task<IReadOnlyList<ArchiveFile>>> loadFilesAsync,
        Func<long, IReadOnlyList<ArchiveFile>, string, Task>? exportTreeAsync = null)
    {
        if (results.Count == 0)
        {
            WriteWarningLine($"No results for keyword {keyword}.", "没有找到相关结果。");
            return;
        }

        int pageSize = GetPageSize();
        int pageIndex = 0;

        while (true)
        {
            IReadOnlyList<SearchResultItem> page = results.Skip(pageIndex * pageSize).Take(pageSize).ToList();
            Table table = CreateSearchTable(page, pageIndex, pageSize, results.Count, keyword);
            AnsiConsole.Clear();
            AnsiConsole.Write(table);

            List<SearchPromptChoice> archiveChoices = BuildPagedSearchArchiveChoices(page, results);
            List<SearchPromptChoice> pagedChoices = BuildPagedSearchChoices(pageIndex, results.Count, pageSize, archiveChoices);
            SearchPromptChoice selection = AnsiConsole.Prompt(
                new SelectionPrompt<SearchPromptChoice>()
                    .Title(CreatePromptTitle("Select an archive to continue, or change page", "选择归档继续，或切换分页"))
                    .PageSize(Math.Min(Math.Max(pagedChoices.Count, 3), 15))
                    .UseConverter(choice => choice.DisplayText)
                    .AddChoices(pagedChoices));

            if (selection.Kind == SearchChoiceKind.Back)
            {
                return;
            }

            if (selection.Kind == SearchChoiceKind.NextPage)
            {
                pageIndex = Math.Min(pageIndex + 1, GetLastPageIndex(results.Count, pageSize));
                continue;
            }

            if (selection.Kind == SearchChoiceKind.PreviousPage)
            {
                pageIndex = Math.Max(pageIndex - 1, 0);
                continue;
            }

            IReadOnlyList<ArchiveFile> files = await loadFilesAsync(selection.ArchiveId);
            await ShowArchiveActionMenuAsync(selection.ArchiveId, files, exportTreeAsync);
        }
    }

    private async Task ShowPagedArchiveSelectionAsync(
        IReadOnlyList<ArchiveSummary> summaries,
        Func<long, Task<IReadOnlyList<ArchiveFile>>> loadFilesAsync,
        Func<long, IReadOnlyList<ArchiveFile>, string, Task>? exportTreeAsync,
        string englishTitle,
        string chineseTitle)
    {
        int pageSize = GetPageSize();
        int pageIndex = 0;

        while (true)
        {
            IReadOnlyList<ArchiveSummary> page = summaries.Skip(pageIndex * pageSize).Take(pageSize).ToList();
            Table table = CreateArchiveTable(page, pageIndex, pageSize, summaries.Count, englishTitle, chineseTitle);

            AnsiConsole.Clear();
            AnsiConsole.Write(table);

            List<ArchivePromptChoice> pagedChoices = BuildPagedArchiveChoices(page, pageIndex, summaries.Count, pageSize);
            ArchivePromptChoice selection = AnsiConsole.Prompt(
                new SelectionPrompt<ArchivePromptChoice>()
                    .Title(CreatePromptTitle("Select an archive to continue, or change page", "选择归档继续，或切换分页"))
                    .PageSize(Math.Min(Math.Max(pagedChoices.Count, 3), 15))
                    .UseConverter(choice => choice.DisplayText)
                    .AddChoices(pagedChoices));

            if (selection.Kind == ArchiveChoiceKind.Back)
            {
                return;
            }

            if (selection.Kind == ArchiveChoiceKind.NextPage)
            {
                pageIndex = Math.Min(pageIndex + 1, GetLastPageIndex(summaries.Count, pageSize));
                continue;
            }

            if (selection.Kind == ArchiveChoiceKind.PreviousPage)
            {
                pageIndex = Math.Max(pageIndex - 1, 0);
                continue;
            }

            IReadOnlyList<ArchiveFile> files = await loadFilesAsync(selection.ArchiveId);
            await ShowArchiveActionMenuAsync(selection.ArchiveId, files, exportTreeAsync);
        }
    }

    private static async Task ShowArchiveActionMenuAsync(
        long archiveId,
        IReadOnlyList<ArchiveFile> files,
        Func<long, IReadOnlyList<ArchiveFile>, string, Task>? exportTreeAsync)
    {
        while (true)
        {
            List<ArchiveActionChoice> choices = new()
            {
                new ArchiveActionChoice(ArchiveActionKind.ViewTree, PlainLabel("View file tree", "查看文件树"))
            };

            if (exportTreeAsync is not null)
            {
                choices.Add(new ArchiveActionChoice(ArchiveActionKind.ExportPlaceholderTree, PlainLabel("Export directory structure", "导出目录结构")));
            }

            choices.Add(new ArchiveActionChoice(ArchiveActionKind.Back, PlainLabel("Back", "返回上一级")));

            AnsiConsole.Clear();
            ArchiveActionChoice action = AnsiConsole.Prompt(
                new SelectionPrompt<ArchiveActionChoice>()
                    .Title(CreatePromptTitle($"Archive #{archiveId}", $"归档 #{archiveId}"))
                    .PageSize(Math.Min(Math.Max(choices.Count, 3), 8))
                    .UseConverter(choice => choice.DisplayText)
                    .AddChoices(choices));

            if (action.Kind == ArchiveActionKind.Back)
            {
                return;
            }

            if (action.Kind == ArchiveActionKind.ViewTree)
            {
                ShowFileTree(archiveId, files);
                continue;
            }

            string? outputDirectory = AskRequiredTextOrBack("Export directory", "导出目录");
            if (outputDirectory is null)
            {
                continue;
            }

            await exportTreeAsync!(archiveId, files, outputDirectory);
            WriteMutedLine("Press any key to continue", "按任意键继续");
            Console.ReadKey(intercept: true);
        }
    }

    private static Table CreateArchiveTable(IReadOnlyList<ArchiveSummary> items, int pageIndex, int pageSize, int totalCount, string englishTitle, string chineseTitle)
    {
        int currentPage = pageIndex + 1;
        int pageCount = GetLastPageIndex(totalCount, pageSize) + 1;

        Table table = new();
        table.Border = TableBorder.Rounded;
        table.Title = new TableTitle($"[green]{Markup.Escape(englishTitle)}[/] [grey](Page {currentPage}/{pageCount}, Total {totalCount})[/]\n[grey]{Markup.Escape(chineseTitle)}（第 {currentPage}/{pageCount} 页，共 {totalCount} 条）[/]");
        table.AddColumn(new TableColumn("[cyan]ID[/]").NoWrap());
        table.AddColumn(new TableColumn(MarkupLabel("Time", "时间")).NoWrap());
        table.AddColumn(new TableColumn(MarkupLabel("Note", "备注")));
        table.AddColumn(new TableColumn(MarkupLabel("Tags", "标签")));
        table.AddColumn(new TableColumn(MarkupLabel("Files", "文件数")).RightAligned().NoWrap());

        foreach (ArchiveSummary item in items)
        {
            table.AddRow(
                item.Id.ToString(),
                item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                TruncateForTable(item.Note ?? string.Empty, 28),
                TruncateForTable(item.Tags ?? string.Empty, 24),
                item.FileCount.ToString());
        }

        return table;
    }

    private static Table CreateSearchTable(IReadOnlyList<SearchResultItem> items, int pageIndex, int pageSize, int totalCount, string keyword)
    {
        int currentPage = pageIndex + 1;
        int pageCount = GetLastPageIndex(totalCount, pageSize) + 1;

        Table table = new();
        table.Border = TableBorder.Rounded;
        table.Title = new TableTitle($"[green]Search Results[/] [grey](Keyword: {Markup.Escape(keyword)}, Page {currentPage}/{pageCount}, Total {totalCount})[/]\n[grey]搜索结果（关键词：{Markup.Escape(keyword)}，第 {currentPage}/{pageCount} 页，共 {totalCount} 条）[/]");
        table.AddColumn(new TableColumn(MarkupLabel("Archive", "归档")).NoWrap());
        table.AddColumn(new TableColumn(MarkupLabel("Time", "时间")).NoWrap());
        table.AddColumn(new TableColumn(MarkupLabel("Matched File", "匹配文件")));
        table.AddColumn(new TableColumn(MarkupLabel("Package", "位置")).NoWrap());
        table.AddColumn(new TableColumn(MarkupLabel("Note", "备注")));

        foreach (SearchResultItem item in items)
        {
            table.AddRow(
                item.ArchiveId.ToString(),
                item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                TruncateForTable(item.RelativePath, 42),
                item.IsStored ? MarkupLabel("stored", "仅存储", "yellow") : MarkupLabel("compressed", "压缩", "green"),
                TruncateForTable(item.Note ?? string.Empty, 24));
        }

        return table;
    }

    private static List<ArchivePromptChoice> BuildPagedArchiveChoices(
        IReadOnlyList<ArchiveSummary> page,
        int pageIndex,
        int totalCount,
        int pageSize)
    {
        List<ArchivePromptChoice> choices = page
            .Select(summary => new ArchivePromptChoice(
                ArchiveChoiceKind.View,
                summary.Id,
                PlainLabel($"Open Archive #{summary.Id} ({summary.FileCount} files)", $"打开归档 #{summary.Id}（{summary.FileCount} 个文件）")))
            .ToList();

        if (pageIndex > 0)
        {
            choices.Add(new ArchivePromptChoice(ArchiveChoiceKind.PreviousPage, 0, PlainLabel("Previous page", "上一页")));
        }

        if (pageIndex < GetLastPageIndex(totalCount, pageSize))
        {
            choices.Add(new ArchivePromptChoice(ArchiveChoiceKind.NextPage, 0, PlainLabel("Next page", "下一页")));
        }

        choices.Add(new ArchivePromptChoice(ArchiveChoiceKind.Back, 0, PlainLabel("Back", "返回上一级")));
        return choices;
    }

    private static List<SearchPromptChoice> BuildPagedSearchChoices(
        int pageIndex,
        int totalCount,
        int pageSize,
        IReadOnlyList<SearchPromptChoice> archiveChoices)
    {
        List<SearchPromptChoice> choices = new(archiveChoices);

        if (pageIndex > 0)
        {
            choices.Add(new SearchPromptChoice(SearchChoiceKind.PreviousPage, 0, PlainLabel("Previous page", "上一页")));
        }

        if (pageIndex < GetLastPageIndex(totalCount, pageSize))
        {
            choices.Add(new SearchPromptChoice(SearchChoiceKind.NextPage, 0, PlainLabel("Next page", "下一页")));
        }

        choices.Add(new SearchPromptChoice(SearchChoiceKind.Back, 0, PlainLabel("Back", "返回上一级")));
        return choices;
    }

    private static List<SearchPromptChoice> BuildPagedSearchArchiveChoices(IReadOnlyList<SearchResultItem> page, IReadOnlyList<SearchResultItem> allResults)
    {
        HashSet<long> pageArchiveIds = page.Select(item => item.ArchiveId).ToHashSet();

        return allResults
            .Where(item => pageArchiveIds.Contains(item.ArchiveId))
            .GroupBy(item => item.ArchiveId)
            .Select(group =>
            {
                SearchResultItem first = group.First();
                return new SearchPromptChoice(
                    SearchChoiceKind.ViewArchive,
                    group.Key,
                    PlainLabel($"Open Archive #{group.Key} ({group.Count()} matches)", $"打开归档 #{group.Key}（{group.Count()} 条匹配）"));
            })
            .OrderBy(choice => choice.ArchiveId)
            .ToList();
    }

    private static void ShowFileTree(long archiveId, IReadOnlyList<ArchiveFile> files)
    {
        AnsiConsole.Clear();
        Tree tree = new($"[green]Archive #{archiveId}[/]\n[grey]文件树[/]");

        Dictionary<string, TreeNode> nodes = new(StringComparer.OrdinalIgnoreCase);

        foreach (ArchiveFile file in files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            string[] parts = file.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string currentPath = string.Empty;
            TreeNode? parentNode = null;

            for (int index = 0; index < parts.Length; index++)
            {
                string part = parts[index];
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

                if (!nodes.TryGetValue(currentPath, out TreeNode? node))
                {
                    string label = index == parts.Length - 1
                        ? $"[cyan]{Markup.Escape(part)}[/] [grey]({FormatFileSize(file.Size)}, {(file.IsStored ? PlainLabel("stored", "仅存储") : PlainLabel("compressed", "压缩"))})[/]"
                        : $"[yellow]{Markup.Escape(part)}[/]";

                    node = parentNode is null ? tree.AddNode(label) : parentNode.AddNode(label);
                    nodes[currentPath] = node;
                }

                parentNode = node;
            }
        }

        AnsiConsole.Write(tree);
        WriteMutedLine("Press any key to return", "按任意键返回列表...");
        Console.ReadKey(intercept: true);
    }

    private static string? AskRequiredTextOrBack(string englishPrompt, string chinesePrompt)
    {
        while (true)
        {
            string value = AnsiConsole.Prompt(
                new TextPrompt<string>($"{CreatePromptTitle(englishPrompt, chinesePrompt)}\n[grey]Type [yellow]{BackKeyword}[/] to return.[/]\n[grey]输入 [yellow]{BackKeyword}[/] 返回。[/]")
                    .AllowEmpty());

            if (value.Equals(BackKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            WriteWarningLine("This field cannot be empty.", "该字段不能为空。");
        }
    }

    private static int GetPageSize()
    {
        int availableHeight = Math.Max(Console.WindowHeight - 12, 5);
        return Math.Min(availableHeight, 12);
    }

    private static int GetLastPageIndex(int totalCount, int pageSize)
    {
        return Math.Max((int)Math.Ceiling(totalCount / (double)pageSize) - 1, 0);
    }

    private static string TruncateForTable(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return Markup.Escape(value);
        }

        return Markup.Escape(string.Concat(value.AsSpan(0, maxLength - 1), "…"));
    }

    private static string FormatFileSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double current = size;
        int unitIndex = 0;

        while (current >= 1024 && unitIndex < units.Length - 1)
        {
            current /= 1024;
            unitIndex++;
        }

        return $"{current:0.##} {units[unitIndex]}";
    }

    private static string PlainLabel(string english, string chinese)
    {
        return $"{english}\n{chinese}";
    }

    private static string MarkupLabel(string english, string chinese, string englishColor = "cyan")
    {
        return $"[{englishColor}]{Markup.Escape(english)}[/]\n[grey]{Markup.Escape(chinese)}[/]";
    }

    private static string CreatePromptTitle(string english, string chinese)
    {
        return $"[bold springgreen1]{Markup.Escape(english)}[/]\n[grey]{Markup.Escape(chinese)}[/]";
    }

    private static void WriteWarningLine(string english, string chinese)
    {
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(english)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(chinese)}[/]");
    }

    private static void WriteMutedLine(string english, string chinese)
    {
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(english)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(chinese)}[/]");
    }
}

public enum ArchiveChoiceKind
{
    View,
    PreviousPage,
    NextPage,
    Back
}

public sealed record ArchivePromptChoice(ArchiveChoiceKind Kind, long ArchiveId, string DisplayText);

public enum SearchChoiceKind
{
    ViewArchive,
    PreviousPage,
    NextPage,
    Back
}

public sealed record SearchPromptChoice(SearchChoiceKind Kind, long ArchiveId, string DisplayText);

public enum ArchiveActionKind
{
    ViewTree,
    ExportPlaceholderTree,
    Back
}

public sealed record ArchiveActionChoice(ArchiveActionKind Kind, string DisplayText);

