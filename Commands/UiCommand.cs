using System.CommandLine;
using Vault.Data;
using Vault.UI;

namespace Vault.Commands;

public static class UiCommand
{
    public static Command Create(ArchiveRepository repository)
    {
        Command command = new("ui", "Launch secondary terminal UI / 启动备用终端界面");

        command.SetHandler(async () =>
        {
            AmberConsoleApp app = new(repository);
            await app.RunMainMenuAsync();
        });

        return command;
    }
}
