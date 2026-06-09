using Spectre.Console;
using Spectre.Console.Rendering;
using Vault.Core;
using Vault.Data;
using Vault.Models;

namespace Vault.UI;

public sealed class AmberConsoleApp
{
    private const string BackKeyword = "/back";
    private readonly ArchiveRepository _repository;

    public AmberConsoleApp(ArchiveRepository repository)
    {
        _repository = repository;
    }

            public static void PrepareConsoleForUi()
    {
    }

    public async Task RunMainMenuAsync()

    {
        while (true)
        {
                                    AnsiConsole.Clear();
            AnsiConsole.Write(CreateWelcomeLayout());
            MainMenuChoice selection = PromptMainMenuChoice();


            switch (selection.Action)
            {
                case MainMenuAction.Archive:
                    await RunArchiveFlowAsync();
                    break;
                case MainMenuAction.Browse:
                    await RunListFlowAsync();
                    break;
                case MainMenuAction.Search:
                    await RunSearchFlowAsync();
                    break;
                case MainMenuAction.Extract:
                    await RunExtractFlowAsync();
                    break;
                case MainMenuAction.Help:
                    ShowHelpScreen();
                    break;
                case MainMenuAction.Storage:
                    await RunStorageSettingsFlowAsync();
                    break;
                case MainMenuAction.Exit:
                    return;
            }
        }
    }

    public void ShowHelpScreen()
    {
                AnsiConsole.Clear();


        Grid header = new();
        header.AddColumn();
        header.AddRow(new FigletText("Amber").Color(Color.Orange1));
        header.AddRow(new Markup("[grey]Local cold archive CLI powered by.NET8, 7z, SQLite[/]"));
        header.AddRow(new Markup("[grey]本地冷存档命令行工具，基于.NET8、7z、SQLite[/]"));

        Panel overview = new(header)
        {
            Header = new PanelHeader("Overview"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Aqua)
        };

        Table commands = new();
        commands.Border = TableBorder.Rounded;
        commands.AddColumn(new TableColumn(MarkupLabel("Command", "命令")).NoWrap());
        commands.AddColumn(MarkupLabel("Description", "说明"));
        commands.AddRow("amber ui", MarkupLabel("Launch interactive UI", "启动交互式界面"));
        commands.AddRow("amber archive <dir>", MarkupLabel("Create archive packages", "创建归档压缩包"));
        commands.AddRow("amber list", MarkupLabel("Browse archive records", "浏览归档记录"));
        commands.AddRow("amber search <keyword>", MarkupLabel("Search by file path or note", "按路径或备注搜索"));
        commands.AddRow("amber extract <id> --file <path> --out <dir>", MarkupLabel("Extract one file from an archive", "从归档中解压单个文件"));

        Table options = new();
        options.Border = TableBorder.Rounded;
        options.AddColumn(MarkupLabel("Feature", "功能"));
        options.AddColumn(MarkupLabel("Details", "详情"));
        options.AddRow(MarkupLabel("Compression split", "压缩分组"), MarkupLabel("Pre-compressed media and archives go to store-mode package", "已压缩媒体与压缩包进入 store 包"));
        options.AddRow(MarkupLabel("Compression levels", "压缩等级"), MarkupLabel("Choose from store, fastest, fast, normal, maximum, ultra", "可选仅存储、最快、快速、标准、最大、极限"));
        options.AddRow(MarkupLabel("Advanced mode", "高级模式"), MarkupLabel("Optional dictionary, fast bytes, match finder, solid mode", "可选字典、Fast bytes、匹配器、solid 模式"));
        options.AddRow(MarkupLabel("Sorting", "排序"), MarkupLabel("Grouped by extension, then ordered by greedy cosine similarity", "按扩展名分组，再按余弦相似度贪心排序"));
        options.AddRow(MarkupLabel("Database", "数据库"), $"[cyan]{Markup.Escape(_repository.DatabasePath)}[/]");
        options.AddRow(MarkupLabel("Archive Output", "归档输出"), $"[cyan]{Markup.Escape(_repository.ArchiveOutputDirectory)}[/]");
        options.AddRow(MarkupLabel("Navigation", "返回"), MarkupLabel($"Type [yellow]{BackKeyword}[/] in forms to go back", $"在表单中输入 [yellow]{BackKeyword}[/] 返回上一级"));

        Rows layout = new(
            overview,
            new Panel(commands)
            {
                Header = new PanelHeader("Commands"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Green)
            },
            new Panel(options)
            {
                Header = new PanelHeader("Behavior"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Yellow)
            });

        AnsiConsole.Write(layout);
        WriteMutedLine("Press any key to return", "按任意键返回");
        Console.ReadKey(intercept: true);
    }

    private async Task RunArchiveFlowAsync()
    {
                AnsiConsole.Clear();

        ShowFlowHeader("Archive Directory", "归档目录");

        string? directory = AskRequiredTextOrBack("Directory to archive", "要归档的目录");
        if (directory is null)
        {
            return;
        }

        string? note = AskOptionalTextOrBack("Note (optional)", "备注（可选）");
        if (note == BackKeyword)
        {
            return;
        }

        string? tags = AskOptionalTextOrBack("Tags, comma separated (optional)", "标签，逗号分隔（可选）");
        if (tags == BackKeyword)
        {
            return;
        }

        ArchiveCompressionOptions? compressionOptions = AskCompressionOptionsOrBack();
        if (compressionOptions is null)
        {
            return;
        }

                bool includeHighEntropyFilesInCompression = AskYesNo("Compress high-entropy files too?", "压缩是否包括高熵文件？");

                try
        {
            ArchiveSession session = new(_repository);
            long archiveId = await session.RunAsync(directory, note, tags, compressionOptions, includeHighEntropyFilesInCompression);

            AnsiConsole.WriteLine();
            WriteSuccessLine($"Archive created successfully: #{archiveId}", $"归档成功：#{archiveId}");
            AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(_repository.ArchiveOutputDirectory)}[/]");
        }

        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteErrorLine("Archive failed", $"归档失败：{exception.Message}");
        }

        WaitForReturn();
    }

        private async Task RunListFlowAsync()
    {
                try
        {
            InteractiveViewer viewer = new();
            IReadOnlyList<ArchiveSummary> summaries = await _repository.GetArchiveSummariesAsync();
            if (summaries.Count == 0)
            {
                WriteWarningLine("No archive records yet.", "当前还没有归档记录。", waitForInput: true);
                return;
            }

            await viewer.ShowArchiveListAsync(
                summaries,
                archiveId => _repository.GetArchiveFilesAsync(archiveId),
                ExportPlaceholderTreeAsync);
        }

        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteErrorLine("Failed to load archives", $"读取归档列表失败：{exception.Message}");
            WaitForReturn();
        }
    }


        private async Task RunSearchFlowAsync()
    {
                AnsiConsole.Clear();

        ShowFlowHeader("Search Archives", "搜索归档");

        string? keyword = AskRequiredTextOrBack("Keyword", "关键词");
        if (keyword is null)
        {
            return;
        }

                try
        {
            InteractiveViewer viewer = new();
            IReadOnlyList<SearchResultItem> results = await _repository.SearchAsync(keyword);
            if (results.Count == 0)
            {
                WriteWarningLine($"No results for keyword {keyword}.", "没有找到相关结果。", waitForInput: true);
                return;
            }

            await viewer.ShowSearchResultsAsync(
                keyword,
                results,
                archiveId => _repository.GetArchiveFilesAsync(archiveId),
                ExportPlaceholderTreeAsync);
        }

        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteErrorLine("Search failed", $"搜索失败：{exception.Message}");
            WaitForReturn();
        }
    }


            private async Task RunExtractFlowAsync()
            {
                AnsiConsole.Clear();
                ShowFlowHeader("Extract Files", "解压文件");

                try
                {
                    IReadOnlyList<ArchiveSummary> summaries = await _repository.GetArchiveSummariesAsync();
                    if (summaries.Count == 0)
                    {
                        WriteWarningLine("No archive records yet.", "当前还没有归档记录。", waitForInput: true);
                        return;
                    }

                    while (true)
                    {
                        ArchiveSummary? selectedArchive = SelectArchiveSummaryOrBack(summaries);
                        if (selectedArchive is null)
                        {
                            return;
                        }

                        Archive? archive = await _repository.GetArchiveByIdAsync(selectedArchive.Id);
                        if (archive is null)
                        {
                            throw new InvalidOperationException($"Archive #{selectedArchive.Id} was not found / 未找到归档 #{selectedArchive.Id}。");
                        }

                        while (true)
                        {
                            ExtractModeChoice mode = AnsiConsole.Prompt(
                                new SelectionPrompt<ExtractModeChoice>()
                                    .Title(CreatePromptTitle("Choose extract mode", "选择解压方式"))
                                    .PageSize(5)
                                    .MoreChoicesText(CreateMoreChoicesText())
                                    .UseConverter(choice => choice.DisplayText)
                                    .AddChoices(new[]
                                    {
                                        new ExtractModeChoice(ExtractMode.AllFiles, PlainLabel("Extract all files", "全部解压")),
                                        new ExtractModeChoice(ExtractMode.SingleFile, PlainLabel("Choose one file", "选择单个文件")),
                                        new ExtractModeChoice(ExtractMode.Back, PlainLabel("Back", "返回上一级"))
                                    }));

                            if (mode.Mode == ExtractMode.Back)
                            {
                                break;
                            }

                            if (mode.Mode == ExtractMode.AllFiles)
                            {
                                string? outputDirectory = AskRequiredTextOrBack("Output directory", "输出目录");
                                if (outputDirectory is null)
                                {
                                    continue;
                                }

                                await ExtractAllArchivePackagesAsync(archive, Path.GetFullPath(outputDirectory));
                                WriteSuccessLine("All files extracted", $"全部解压完成：Archive #{selectedArchive.Id}");
                                WaitForReturn();
                                return;
                            }

                            IReadOnlyList<ArchiveFile> files = await _repository.GetArchiveFilesAsync(selectedArchive.Id);
                            if (files.Count == 0)
                            {
                                WriteWarningLine("This archive has no files.", "该归档没有文件。", waitForInput: true);
                                break;
                            }

                            while (true)
                            {
                                string? filterKeyword = AskOptionalTextOrBack("Filter keyword (optional)", "筛选关键词（可选）");
                                if (filterKeyword == BackKeyword)
                                {
                                    break;
                                }

                                IReadOnlyList<ArchiveFile> filteredFiles = string.IsNullOrWhiteSpace(filterKeyword)
                                    ? files
                                    : files.Where(file => file.RelativePath.Contains(filterKeyword, StringComparison.OrdinalIgnoreCase)).ToList();

                                if (filteredFiles.Count == 0)
                                {
                                    WriteWarningLine("No files matched the filter.", "没有匹配筛选条件的文件。", waitForInput: true);
                                    continue;
                                }

                                ArchiveFile? selectedFile = SelectArchiveFileOrBack(selectedArchive.Id, filteredFiles);
                                if (selectedFile is null)
                                {
                                    continue;
                                }

                                string? singleOutputDirectory = AskRequiredTextOrBack("Output directory", "输出目录");
                                if (singleOutputDirectory is null)
                                {
                                    continue;
                                }

                                string normalizedRelativePath = selectedFile.RelativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                                string? packagePath = selectedFile.IsStored ? archive.StoredPath : archive.CompressedPath;
                                if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
                                {
                                    throw new InvalidOperationException($"Archive package is missing / 归档包不存在：{packagePath ?? "<empty>"}");
                                }

                                SevenZipRunner runner = new();
                                await runner.ExtractSingleFileAsync(packagePath, FileSorter.ToArchiveEntryPath(normalizedRelativePath), Path.GetFullPath(singleOutputDirectory));
                                WriteSuccessLine("Extraction completed", $"解压完成：{normalizedRelativePath}");
                                WaitForReturn();
                                return;
                            }
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    WriteErrorLine("Extraction failed", $"解压失败：{exception.Message}");
                }

                WaitForReturn();
            }


    private static async Task ExtractAllArchivePackagesAsync(Archive archive, string outputDirectory)
    {
        SevenZipRunner runner = new();
        bool extractedAnyPackage = false;

        if (!string.IsNullOrWhiteSpace(archive.CompressedPath))
        {
            if (!File.Exists(archive.CompressedPath))
            {
                throw new InvalidOperationException($"Compressed archive package is missing / 压缩归档包不存在：{archive.CompressedPath}");
            }

            await runner.ExtractArchiveAsync(archive.CompressedPath, outputDirectory);
            extractedAnyPackage = true;
        }

        if (!string.IsNullOrWhiteSpace(archive.StoredPath))
        {
            if (!File.Exists(archive.StoredPath))
            {
                throw new InvalidOperationException($"Stored archive package is missing / 存储归档包不存在：{archive.StoredPath}");
            }

            await runner.ExtractArchiveAsync(archive.StoredPath, outputDirectory);
            extractedAnyPackage = true;
        }

        if (!extractedAnyPackage)
        {
            throw new InvalidOperationException("Archive has no package files to extract / 该归档没有可解压的包文件。");
        }
    }



        private async Task ExportPlaceholderTreeAsync(long archiveId, IReadOnlyList<ArchiveFile> files, string outputDirectory)
    {
        Archive? archive = await _repository.GetArchiveByIdAsync(archiveId);
        if (archive is null)
        {
            throw new InvalidOperationException($"Archive #{archiveId} was not found / 未找到归档 #{archiveId}。");
        }

        ArchiveTreeExporter exporter = new();
        string rootDirectory = await exporter.ExportPlaceholderTreeAsync(archive, files, outputDirectory);
        WriteSuccessLine("Directory structure exported.", $"目录结构已导出：{rootDirectory}");
    }

    private static ArchiveSummary? SelectArchiveSummaryOrBack(IReadOnlyList<ArchiveSummary> summaries)
    {
                SelectionPrompt<ArchiveSummaryChoice> prompt = new SelectionPrompt<ArchiveSummaryChoice>()
            .Title(CreatePromptTitle("Choose an archive", "选择一个归档"))
            .PageSize(Math.Min(Math.Max(summaries.Count + 1, 3), 15))
            .MoreChoicesText(CreateMoreChoicesText())
            .UseConverter(choice => choice.DisplayText);


                foreach (ArchiveSummary summary in summaries)

        {
            prompt.AddChoice(new ArchiveSummaryChoice(
                summary,
                PlainLabel($"Archive #{summary.Id} · {summary.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {summary.FileCount} files", $"归档 #{summary.Id} · {summary.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {summary.FileCount} 个文件")));
        }

        prompt.AddChoice(new ArchiveSummaryChoice(null, PlainLabel("Back", "返回上一级")));
        ArchiveSummaryChoice selection = AnsiConsole.Prompt(prompt);
        return selection.Summary;
    }

    private static ArchiveFile? SelectArchiveFileOrBack(long archiveId, IReadOnlyList<ArchiveFile> files)
    {
                SelectionPrompt<ArchiveFileChoice> prompt = new SelectionPrompt<ArchiveFileChoice>()
            .Title(CreatePromptTitle($"Choose a file from Archive #{archiveId}", $"从归档 #{archiveId} 选择文件"))
            .PageSize(15)
            .MoreChoicesText(CreateMoreChoicesText())
            .UseConverter(choice => choice.DisplayText);


                foreach (ArchiveFile file in files)

        {
            string packageLabel = file.IsStored ? "stored" : "compressed";
            string packageChinese = file.IsStored ? "仅存储" : "压缩";
            prompt.AddChoice(new ArchiveFileChoice(
                file,
                PlainLabel($"{file.RelativePath} ({FormatFileSize(file.Size)}, {packageLabel})", $"{file.RelativePath}（{FormatFileSize(file.Size)}，{packageChinese}）")));
        }

        prompt.AddChoice(new ArchiveFileChoice(null, PlainLabel("Back", "返回上一级")));
        ArchiveFileChoice selection = AnsiConsole.Prompt(prompt);
        return selection.File;
    }



        private async Task RunStorageSettingsFlowAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();

            ShowFlowHeader("Storage Settings", "存储设置");

            Panel currentPaths = new(new Rows(
                new Markup($"[cyan]Current archive output[/]\n[grey]{Markup.Escape(_repository.ArchiveOutputDirectory)}[/]"),
                new Markup($"[cyan]Database[/]\n[grey]{Markup.Escape(_repository.DatabasePath)}[/]")))
            {
                Header = new PanelHeader("Current Paths"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.SpringGreen2)
            };

            AnsiConsole.Write(currentPaths);

                        StorageSettingChoice selection = AnsiConsole.Prompt(
                new SelectionPrompt<StorageSettingChoice>()
                    .Title(CreatePromptTitle("Choose one item to change", "选择一项进行修改"))
                    .PageSize(5)
                    .MoreChoicesText(CreateMoreChoicesText())
                    .UseConverter(choice => choice.DisplayText)
                    .AddChoices(new[]

                    {
                        new StorageSettingChoice(StorageSettingAction.ArchiveOutput, PlainLabel("Change archive output directory", "修改归档输出目录")),
                        new StorageSettingChoice(StorageSettingAction.DatabasePath, PlainLabel("Change database file path", "修改数据库文件路径")),
                        new StorageSettingChoice(StorageSettingAction.Back, PlainLabel("Back", "返回上一级"))
                    }));

            switch (selection.Action)
            {
                case StorageSettingAction.ArchiveOutput:
                    await RunArchiveOutputMigrationFlowAsync();
                    break;
                case StorageSettingAction.DatabasePath:
                    await RunDatabasePathMigrationFlowAsync();
                    break;
                case StorageSettingAction.Back:
                    return;
            }
        }
    }

    private async Task RunArchiveOutputMigrationFlowAsync()

    {
        string? newPath = AskRequiredTextOrBack("New archive output directory", "新的归档输出目录");
        if (newPath is null)
        {
            return;
        }

        string resolvedPath = ResolveUserPath(newPath);
        if (string.Equals(resolvedPath, _repository.ArchiveOutputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            WriteWarningLine("The new path is the same as the current archive output directory.", "新路径与当前归档输出目录相同。", waitForInput: true);
            return;
        }

        if (!ConfirmStorageMigration("Confirm archive output migration", "确认迁移归档输出目录"))
        {
            return;
        }

        try
        {
            await _repository.MigrateArchiveOutputDirectoryAsync(resolvedPath);
            WriteSuccessLine("Archive output directory updated.", "归档输出目录已更新。", waitForInput: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteErrorLine("Archive output migration failed.", $"归档输出目录迁移失败：{exception.Message}");
            WaitForReturn();
        }
    }

    private async Task RunDatabasePathMigrationFlowAsync()
    {
        string? newPath = AskRequiredTextOrBack("New database file path (or directory)", "新的数据库文件路径（或目录）");
        if (newPath is null)
        {
            return;
        }

        string resolvedPath = ResolveUserPath(newPath);
        if (string.Equals(Database.ResolveDatabasePath(resolvedPath, _repository.DatabasePath), _repository.DatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            WriteWarningLine("The new path is the same as the current database file path.", "新路径与当前数据库文件路径相同。", waitForInput: true);
            return;
        }

        if (!ConfirmStorageMigration("Confirm database migration", "确认迁移数据库文件"))
        {
            return;
        }

        try
        {
            await _repository.MigrateDatabasePathAsync(resolvedPath);
            WriteSuccessLine("Database file path updated.", "数据库文件路径已更新。", waitForInput: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteErrorLine("Database migration failed.", $"数据库文件迁移失败：{exception.Message}");
            WaitForReturn();
        }
    }

                private IRenderable CreateWelcomeLayout()
    {
        Table features = new();
        features.Border = TableBorder.Simple;
        features.AddColumn(new TableColumn(MarkupLabel("Archive", "归档")).NoWrap());
        features.AddColumn(new TableColumn(MarkupLabel("Browse", "浏览")).NoWrap());
        features.AddColumn(new TableColumn(MarkupLabel("Extract", "解压")).NoWrap());
        features.AddRow(
            MarkupLabel("Compression profiles", "压缩等级与高级参数"),
            MarkupLabel("0KB tree export", "导出0KB目录结构"),
            MarkupLabel("Filter then pick", "筛选后选择文件"));
        features.AddRow(
            MarkupLabel("High-entropy switch", "高熵文件可选压缩"),
            MarkupLabel("Search notes/files", "搜索备注与文件"),
            MarkupLabel("Single-file restore", "单文件恢复"));

        Table storage = new();
        storage.Border = TableBorder.Simple;
        storage.AddColumn(new TableColumn(MarkupLabel("Storage", "存储")).NoWrap());
        storage.AddColumn(MarkupLabel("Path", "路径"));
        storage.AddRow(MarkupLabel("Database", "数据库"), $"[green]{Markup.Escape(ShortenPath(_repository.DatabasePath, 58))}[/]");
        storage.AddRow(MarkupLabel("Archives", "归档输出"), $"[green]{Markup.Escape(ShortenPath(_repository.ArchiveOutputDirectory, 58))}[/]");

        Rows rows = new(
            new Markup("[bold orange1]Amber[/] [grey]Local cold archive workbench[/]\n[grey]本地冷存档工作台[/]"),
            features,
            storage);

        return new Panel(rows)
        {
            Header = new PanelHeader("Main Menu"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Orange1),
            Padding = new Padding(1, 0, 1, 0)
        };
    }

    private static MainMenuChoice PromptMainMenuChoice()
    {
        MainMenuChoice[] choices =
        {
            new(MainMenuAction.Archive, "Archive / 归档"),
            new(MainMenuAction.Browse, "Browse / 浏览"),
            new(MainMenuAction.Search, "Search / 搜索"),
            new(MainMenuAction.Extract, "Extract / 解压"),
            new(MainMenuAction.Storage, "Storage / 存储"),
            new(MainMenuAction.Help, "Help / 帮助"),
            new(MainMenuAction.Exit, "Exit / 退出")
        };

        Grid menu = new();
        for (int index = 0; index < choices.Length; index++)
        {
            menu.AddColumn(new GridColumn().NoWrap());
        }

        menu.AddRow(choices.Select((choice, index) => new Markup($"[bold cyan]{index + 1}[/] [white]{Markup.Escape(choice.DisplayText)}[/]")).ToArray());
        AnsiConsole.Write(new Panel(menu)
        {
            Header = new PanelHeader("Select / 请选择"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.SpringGreen2),
            Padding = new Padding(1, 0, 1, 0)
        });

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            int index = key.KeyChar - '1';
            if (index >= 0 && index < choices.Length)
            {
                return choices[index];
            }
        }
    }




    private static ArchiveCompressionOptions? AskCompressionOptionsOrBack()
    {
                CompressionLevelChoice levelChoice = AnsiConsole.Prompt(
            new SelectionPrompt<CompressionLevelChoice>()
                .Title(CreatePromptTitle("Choose compression level", "选择压缩等级"))
                .PageSize(8)
                .MoreChoicesText(CreateMoreChoicesText())
                .UseConverter(choice => choice.DisplayText)
                .AddChoices(GetCompressionLevelChoices()));


        if (levelChoice.Level is null)
        {
            return null;
        }

        if (!AskYesNo("Enter advanced mode?", "进入高级模式？"))
        {
            return new ArchiveCompressionOptions { Level = levelChoice.Level.Value };
        }

        string? dictionarySize = AskOptionalTextOrBack("Dictionary size, e.g. 32m (optional)", "字典大小，例如 32m（可选）");
        if (dictionarySize == BackKeyword)
        {
            return null;
        }

        int? fastBytes = AskOptionalIntOrBack("Fast bytes 5-273 (optional)", "Fast bytes 5-273（可选）");
        if (fastBytes == int.MinValue)
        {
            return null;
        }

                MatchFinderChoice matchFinderChoice = AnsiConsole.Prompt(
            new SelectionPrompt<MatchFinderChoice>()
                .Title(CreatePromptTitle("Choose match finder", "选择匹配器"))
                .PageSize(6)
                .MoreChoicesText(CreateMoreChoicesText())
                .UseConverter(choice => choice.DisplayText)
                .AddChoices(new[]

                {
                    new MatchFinderChoice(null, PlainLabel("Keep default", "保持默认")),
                    new MatchFinderChoice("bt2", PlainLabel("bt2", "bt2")),
                    new MatchFinderChoice("bt3", PlainLabel("bt3", "bt3")),
                    new MatchFinderChoice("bt4", PlainLabel("bt4", "bt4")),
                    new MatchFinderChoice("hc4", PlainLabel("hc4", "hc4")),
                    new MatchFinderChoice(BackKeyword, PlainLabel("Back", "返回上一级"))
                }));

        if (matchFinderChoice.Value == BackKeyword)
        {
            return null;
        }

        string? solidMode = AskOptionalTextOrBack("Solid mode, e.g. on, off, 512m (optional)", "solid 模式，例如 on、off、512m（可选）");
        if (solidMode == BackKeyword)
        {
            return null;
        }

        return new ArchiveCompressionOptions
        {
            Level = levelChoice.Level.Value,
            DictionarySize = NormalizeOptionalPromptValue(dictionarySize),
            FastBytes = fastBytes == null ? null : fastBytes.Value,
            MatchFinder = NormalizeOptionalPromptValue(matchFinderChoice.Value),
            SolidMode = NormalizeOptionalPromptValue(solidMode)
        }.Validate();
    }

    private static IReadOnlyList<CompressionLevelChoice> GetCompressionLevelChoices()
    {
        return new[]
        {
            new CompressionLevelChoice(SevenZipCompressionLevel.Store, PlainLabel("Store only (mx=0)", "仅存储（mx=0）")),
            new CompressionLevelChoice(SevenZipCompressionLevel.Fastest, PlainLabel("Fastest (mx=1)", "最快（mx=1）")),
            new CompressionLevelChoice(SevenZipCompressionLevel.Fast, PlainLabel("Fast (mx=3)", "快速（mx=3）")),
            new CompressionLevelChoice(SevenZipCompressionLevel.Normal, PlainLabel("Normal (mx=5)", "标准（mx=5）")),
            new CompressionLevelChoice(SevenZipCompressionLevel.Maximum, PlainLabel("Maximum (mx=7)", "最大（mx=7）")),
            new CompressionLevelChoice(SevenZipCompressionLevel.Ultra, PlainLabel("Ultra (mx=9)", "极限（mx=9）")),
            new CompressionLevelChoice(null, PlainLabel("Back", "返回上一级"))
        };
    }

                                private static string? AskOptionalTextOrBack(string englishPrompt, string chinesePrompt)
        {
            AnsiConsole.MarkupLine(CreatePromptTitle(englishPrompt, chinesePrompt));

            string value = ReadEditableInput();
            if (value.Equals(BackKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return BackKeyword;
            }

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? AskRequiredTextOrBack(string englishPrompt, string chinesePrompt)
        {
            while (true)
            {
                AnsiConsole.MarkupLine(CreatePromptTitle(englishPrompt, chinesePrompt));

                string value = ReadEditableInput();
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


    private static long? AskRequiredLongOrBack(string englishPrompt, string chinesePrompt)
    {
        while (true)
        {
            string? value = AskRequiredTextOrBack(englishPrompt, chinesePrompt);
            if (value is null)
            {
                return null;
            }

            if (long.TryParse(value, out long parsed))
            {
                return parsed;
            }

            WriteWarningLine("Please enter a valid integer archive ID.", "请输入有效的整数归档 ID。");
        }
    }

    private static int? AskOptionalIntOrBack(string englishPrompt, string chinesePrompt)
    {
        while (true)
        {
            string? value = AskOptionalTextOrBack(englishPrompt, chinesePrompt);
            if (value == BackKeyword)
            {
                return int.MinValue;
            }

            if (value is null)
            {
                return null;
            }

            if (int.TryParse(value, out int parsed))
            {
                return parsed;
            }

            WriteWarningLine("Please enter a valid integer.", "请输入有效整数。");
        }
    }

        private static bool AskYesNo(string englishPrompt, string chinesePrompt)
        {
                        ConfirmationChoice action = AnsiConsole.Prompt(
                new SelectionPrompt<ConfirmationChoice>()
                    .Title(CreatePromptTitle(englishPrompt, chinesePrompt))
                    .PageSize(4)
                    .MoreChoicesText(CreateMoreChoicesText())
                    .UseConverter(choice => choice.DisplayText)
                    .AddChoices(new[]

                    {
                        new ConfirmationChoice(ConfirmationAction.Migrate, PlainLabel("Yes", "是")),
                        new ConfirmationChoice(ConfirmationAction.Back, PlainLabel("No", "否"))
                    }));

            return action.Action == ConfirmationAction.Migrate;
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



    private static void ShowFlowHeader(string englishTitle, string chineseTitle)
    {
        Panel panel = new(new Markup($"[bold white]{Markup.Escape(englishTitle)}[/]\n[grey]{Markup.Escape(chineseTitle)}[/]\n\n[grey]Type [yellow]{BackKeyword}[/] to return to the previous menu.[/]\n[grey]输入 [yellow]{BackKeyword}[/] 返回上一级菜单。[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.DeepSkyBlue1)
        };

        AnsiConsole.Write(panel);
    }

    private static void WaitForReturn()
    {
                AnsiConsole.WriteLine();
        WriteMutedLine("Press any key to continue", "按任意键继续");
        Console.ReadKey(intercept: true);

    }

    private static bool ConfirmStorageMigration(string englishTitle, string chineseTitle)
    {
                ConfirmationChoice action = AnsiConsole.Prompt(
            new SelectionPrompt<ConfirmationChoice>()
                .Title(CreatePromptTitle(englishTitle, chineseTitle))
                .PageSize(4)
                .MoreChoicesText(CreateMoreChoicesText())
                .UseConverter(choice => choice.DisplayText)
                .AddChoices(new[]

                {
                    new ConfirmationChoice(ConfirmationAction.Migrate, PlainLabel("Copy to the new path, then remove the old file when safe", "先复制到新路径，安全后再删除旧文件")),
                    new ConfirmationChoice(ConfirmationAction.Back, PlainLabel("Back", "返回上一级"))
                }));

        return action.Action == ConfirmationAction.Migrate;
    }

    private static string ResolveUserPath(string value)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
    }

        private static string? NormalizeOptionalPromptValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, BackKeyword, StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
    }

        private static string ReadEditableInput()
        {
            AnsiConsole.Markup("[deepskyblue1]>[/] ");

            List<char> buffer = new();

        int cursorIndex = 0;
        int promptLeft = Console.CursorLeft;
        int promptTop = Console.CursorTop;
        int renderedWidth = 0;

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return new string(buffer.ToArray());
                case ConsoleKey.LeftArrow:
                    if (cursorIndex > 0)
                    {
                        cursorIndex--;
                        SetEditableCursorPosition(buffer, cursorIndex, promptLeft, promptTop);
                    }
                    break;
                case ConsoleKey.RightArrow:
                    if (cursorIndex < buffer.Count)
                    {
                        cursorIndex++;
                        SetEditableCursorPosition(buffer, cursorIndex, promptLeft, promptTop);
                    }
                    break;
                case ConsoleKey.Home:
                    cursorIndex = 0;
                    SetEditableCursorPosition(buffer, cursorIndex, promptLeft, promptTop);
                    break;
                case ConsoleKey.End:
                    cursorIndex = buffer.Count;
                    SetEditableCursorPosition(buffer, cursorIndex, promptLeft, promptTop);
                    break;
                case ConsoleKey.Backspace:
                    if (cursorIndex > 0)
                    {
                        buffer.RemoveAt(cursorIndex - 1);
                        cursorIndex--;
                        RedrawEditableInput(buffer, cursorIndex, promptLeft, promptTop, ref renderedWidth);
                    }
                    break;
                case ConsoleKey.Delete:
                    if (cursorIndex < buffer.Count)
                    {
                        buffer.RemoveAt(cursorIndex);
                        RedrawEditableInput(buffer, cursorIndex, promptLeft, promptTop, ref renderedWidth);
                    }
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(cursorIndex, key.KeyChar);
                        cursorIndex++;
                        RedrawEditableInput(buffer, cursorIndex, promptLeft, promptTop, ref renderedWidth);
                    }
                    break;
            }
        }
    }

    private static void RedrawEditableInput(List<char> buffer, int cursorIndex, int promptLeft, int promptTop, ref int renderedWidth)
    {
        string text = new(buffer.ToArray());
        int currentWidth = GetDisplayWidth(text);
        int clearWidth = Math.Max(renderedWidth, currentWidth) + 1;

        Console.SetCursorPosition(promptLeft, promptTop);
        Console.Write(text);

        if (clearWidth > currentWidth)
        {
            Console.Write(new string(' ', clearWidth - currentWidth));
        }

        renderedWidth = currentWidth;
        SetEditableCursorPosition(buffer, cursorIndex, promptLeft, promptTop);
    }

    private static void SetEditableCursorPosition(List<char> buffer, int cursorIndex, int promptLeft, int promptTop)
    {
        int displayOffset = GetDisplayWidthUpTo(buffer, cursorIndex);
        Console.SetCursorPosition(promptLeft + displayOffset, promptTop);
    }

    private static int GetDisplayWidthUpTo(List<char> buffer, int length)
    {
        return GetDisplayWidth(new string(buffer.Take(length).ToArray()));
    }

    private static int GetDisplayWidth(string value)
    {
        int width = 0;

        foreach (char character in value)
        {
            width += GetCharDisplayWidth(character);
        }

        return width;
    }

    private static int GetCharDisplayWidth(char character)
    {
        if (char.IsControl(character))
        {
            return 0;
        }

        if (IsWideCharacter(character))
        {
            return 2;
        }

        return 1;
    }

    private static bool IsWideCharacter(char character)
    {
        return character is >= '\u1100' and <= '\u115F'
            or >= '\u2E80' and <= '\uA4CF'
            or >= '\uAC00' and <= '\uD7A3'
            or >= '\uF900' and <= '\uFAFF'
            or >= '\uFE10' and <= '\uFE19'
            or >= '\uFE30' and <= '\uFE6F'
            or >= '\uFF00' and <= '\uFF60'
            or >= '\uFFE0' and <= '\uFFE6';
    }


            private static void ResetScreen()

    {
        try
        {
            Console.Clear();
            Console.SetCursorPosition(0, 0);
        }
        catch (IOException)
        {
            AnsiConsole.Clear();
        }
        catch (ArgumentOutOfRangeException)
        {
            AnsiConsole.Clear();
        }
    }

        private static void TryResizeConsole(int preferredWidth, int preferredHeight)
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            int width = Math.Min(preferredWidth, Console.LargestWindowWidth);
            int height = Math.Min(preferredHeight, Console.LargestWindowHeight);

            if (width < 80 || height < 24)
            {
                return;
            }

            Console.SetBufferSize(Math.Max(width, Console.BufferWidth), Math.Max(height, Console.BufferHeight));
            Console.SetWindowSize(width, height);
            Console.SetBufferSize(width, height);
        }
        catch (IOException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }


    private static string ShortenPath(string path, int maxLength)

    {
        if (path.Length <= maxLength)
        {
            return path;
        }

        int keepLength = Math.Max(maxLength - 3, 8);
        return "..." + path[^keepLength..];
    }

    private static string PlainLabel(string english, string chinese)

    {
        return $"{english}\n{chinese}";
    }

    private static string CreateMoreChoicesText()
    {
        return "[grey](Use ↑/↓/Home/End to navigate; press End for Back / 可用 ↑/↓/Home/End 导航，按 End 可快速到底部返回)[/]";
    }


    private static string MarkupLabel(string english, string chinese)

    {
        if (string.IsNullOrEmpty(chinese))
        {
            return english;
        }

        return $"[cyan]{english}[/]\n[grey]{chinese}[/]";
    }

    private static string CreatePromptTitle(string english, string chinese)
    {
        return $"[bold springgreen1]{Markup.Escape(english)}[/]\n[grey]{Markup.Escape(chinese)}[/]";
    }

    private static void WriteSuccessLine(string english, string chinese, bool waitForInput = false)
    {
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(english)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(chinese)}[/]");

        if (waitForInput)
        {
            WaitForReturn();
        }
    }

    private static void WriteWarningLine(string english, string chinese, bool waitForInput = false)
    {
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(english)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(chinese)}[/]");

        if (waitForInput)
        {
            WaitForReturn();
        }
    }

    private static void WriteErrorLine(string english, string chinese)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(english)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(chinese)}[/]");
    }

    private static void WriteMutedLine(string english, string chinese)
    {
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(english)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(chinese)}[/]");
    }
}

public enum MainMenuAction
{
    Archive,
    Browse,
    Search,
    Extract,
    Storage,
    Help,
    Exit
}

public enum StorageSettingAction
{
    ArchiveOutput,
    DatabasePath,
    Back
}

public sealed record StorageSettingChoice(StorageSettingAction Action, string DisplayText);

public enum ConfirmationAction
{
    Migrate,
    Back
}

public sealed record ConfirmationChoice(ConfirmationAction Action, string DisplayText);

public sealed record MainMenuChoice(MainMenuAction Action, string DisplayText);

public sealed record CompressionLevelChoice(SevenZipCompressionLevel? Level, string DisplayText);

public sealed record MatchFinderChoice(string? Value, string DisplayText);

public sealed record ArchiveSummaryChoice(ArchiveSummary? Summary, string DisplayText);

public sealed record ArchiveFileChoice(ArchiveFile? File, string DisplayText);

public enum ExtractMode
{
    AllFiles,
    SingleFile,
    Back
}

public sealed record ExtractModeChoice(ExtractMode Mode, string DisplayText);




