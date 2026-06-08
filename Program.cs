using System.CommandLine;
using System.Text;
using Spectre.Console;
using Vault.Commands;
using Vault.Data;
using Vault.UI;


Database database = new Database();
ArchiveRepository repository = new ArchiveRepository(database);
RootCommand rootCommand = new("Amber - local cold archive CLI / 本地冷存档命令行工具");

rootCommand.AddCommand(UiCommand.Create(repository));
rootCommand.AddCommand(ArchiveCommand.Create(repository));
rootCommand.AddCommand(ListCommand.Create(repository));
rootCommand.AddCommand(SearchCommand.Create(repository));
rootCommand.AddCommand(ExtractCommand.Create(repository));

try
{
    Console.InputEncoding = Encoding.UTF8;
    Console.OutputEncoding = Encoding.UTF8;
    database.Initialize();


    if (args.Length == 0)
    {
                AmberConsoleApp.PrepareConsoleForUi();
        AmberConsoleApp app = new(repository);

        await app.RunMainMenuAsync();
        return 0;
    }

    if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h" || args[0] == "-?" || args[0].Equals("help", StringComparison.OrdinalIgnoreCase)))
    {
                AmberConsoleApp.PrepareConsoleForUi();
        AmberConsoleApp app = new(repository);

        app.ShowHelpScreen();
        return 0;
    }

    return await rootCommand.InvokeAsync(args);
}
catch (Exception exception)
{
    AnsiConsole.MarkupLine($"[red]Initialization failed / 初始化失败：{Markup.Escape(exception.Message)}[/]");
    return 1;
}
