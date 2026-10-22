using System.CommandLine;
using Vault.Web;

namespace Vault.Commands;

public static class WebCommand
{
    public static Command Create()
    {
        Command command = new("web", "Launch local browser UI / 启动浏览器界面");
        command.SetHandler(async () => await AmberWebHost.RunAsync());
        return command;
    }
}
