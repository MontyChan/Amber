using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Vault.Core;

public sealed class SevenZipRunner
{
    private static readonly Regex ProgressRegex = new(@"(?<!\d)(100|[1-9]?\d)%", RegexOptions.Compiled);
    private readonly string _sevenZipPath;

    public SevenZipRunner()
    {
        _sevenZipPath = ResolveSevenZipPath();
    }

    public async Task CreateArchiveAsync(
        string archivePath,
        string workingDirectory,
        IReadOnlyCollection<string> relativePaths,
        ArchiveCompressionOptions compressionOptions,
        Action<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (relativePaths.Count == 0)
        {
            return;
        }

        compressionOptions = compressionOptions.Validate();
        string listFilePath = Path.Combine(Path.GetTempPath(), $"vault_{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllLinesAsync(listFilePath, relativePaths, cancellationToken);

            List<string> arguments = new()
            {
                "a",
                "-t7z",
                archivePath,
                $"@{listFilePath}",
                "-y",
                "-bb0",
                "-bsp1"
            };

            AppendCompressionArguments(arguments, compressionOptions);

            ProcessExecutionResult result = await RunProcessAsync(arguments, workingDirectory, progressCallback, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"7z 创建压缩包失败：{Environment.NewLine}{result.StandardError}");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"无法启动 7z。Amber 当前解析到的 7z 路径为：{_sevenZipPath}。请确认环境变量 VAULT_7Z_PATH 指向正确的 7z 可执行文件，或确认启动 Amber 的同一个终端/进程环境里可以找到 7z。{exception.Message}", exception);
        }
        finally
        {
            if (File.Exists(listFilePath))
            {
                File.Delete(listFilePath);
            }
        }
    }

    public async Task ExtractSingleFileAsync(
        string archivePath,
        string archiveEntryPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        try
        {
            List<string> arguments = new()
            {
                "x",
                archivePath,
                $"-o{outputDirectory}",
                archiveEntryPath,
                "-y"
            };

            ProcessExecutionResult result = await RunProcessAsync(arguments, Directory.GetCurrentDirectory(), progressCallback: null, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"7z 解压文件失败：{Environment.NewLine}{result.StandardError}");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"无法启动 7z。Amber 当前解析到的 7z 路径为：{_sevenZipPath}。请确认环境变量 VAULT_7Z_PATH 指向正确的 7z 可执行文件，或确认启动 Amber 的同一个终端/进程环境里可以找到 7z。{exception.Message}", exception);
        }
    }

    public async Task ExtractArchiveAsync(
        string archivePath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        try
        {
            List<string> arguments = new()
            {
                "x",
                archivePath,
                $"-o{outputDirectory}",
                "-y"
            };

            ProcessExecutionResult result = await RunProcessAsync(arguments, Directory.GetCurrentDirectory(), progressCallback: null, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"7z 解压归档失败：{Environment.NewLine}{result.StandardError}");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"无法启动 7z。Amber 当前解析到的 7z 路径为：{_sevenZipPath}。请确认环境变量 VAULT_7Z_PATH 指向正确的 7z 可执行文件，或确认启动 Amber 的同一个终端/进程环境里可以找到 7z。{exception.Message}", exception);
        }
    }

    private static void AppendCompressionArguments(List<string> arguments, ArchiveCompressionOptions compressionOptions)
    {
        if (compressionOptions.IsStore)
        {
            arguments.Add("-mx=0");
            return;
        }

        arguments.Add("-m0=lzma2");
        arguments.Add("-ms=on");
        arguments.Add($"-mx={(int)compressionOptions.Level}");

        if (!string.IsNullOrWhiteSpace(compressionOptions.DictionarySize))
        {
            arguments.Add($"-md={compressionOptions.DictionarySize.Trim()}");
        }

        if (compressionOptions.FastBytes.HasValue)
        {
            arguments.Add($"-mfb={compressionOptions.FastBytes.Value}");
        }

        if (!string.IsNullOrWhiteSpace(compressionOptions.MatchFinder))
        {
            arguments.Add($"-mmf={compressionOptions.MatchFinder.Trim().ToLowerInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(compressionOptions.SolidMode))
        {
            arguments.Add($"-ms={compressionOptions.SolidMode.Trim().ToLowerInvariant()}");
        }
    }

    private async Task<ProcessExecutionResult> RunProcessAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<int>? progressCallback,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _sevenZipPath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("启动 7z 进程失败，系统没有返回有效的进程句柄。");
        }

        Task<string> stdoutTask = ReadStreamAsync(process.StandardOutput, progressCallback, cancellationToken);
        Task<string> stderrTask = ReadStreamAsync(process.StandardError, progressCallback: null, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stderr))
        {
            stderr = string.IsNullOrWhiteSpace(stdout) ? "7z 未输出错误详情。" : stdout;
        }

        return new ProcessExecutionResult(process.ExitCode, stdout, stderr);
    }

    private static string ResolveSevenZipPath()
    {
        string? configured = Environment.GetEnvironmentVariable("VAULT_7Z_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string expanded = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
            if (File.Exists(expanded))
            {
                return expanded;
            }

            return configured;
        }

        string[] executableNames = OperatingSystem.IsWindows()
            ? ["7z.exe", "7zz.exe", "7za.exe"]
            : ["7z", "7zz", "7za"];

        foreach (string executableName in executableNames)
        {
            string? resolved = FindExecutableOnPath(executableName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            string[] commonWindowsPaths =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "7-Zip", "7z.exe")
            ];

            foreach (string candidate in commonWindowsPaths)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return executableNames[0];
    }

    private static async Task<string> ReadStreamAsync(
        StreamReader reader,
        Action<int>? progressCallback,
        CancellationToken cancellationToken)
    {
        StringBuilder output = new();
        char[] buffer = new char[256];
        int lastReportedProgress = -1;
        string progressWindow = string.Empty;

        while (true)
        {
            int charsRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (charsRead == 0)
            {
                break;
            }

            output.Append(buffer, 0, charsRead);

            if (progressCallback is null)
            {
                continue;
            }

            progressWindow = AppendProgressWindow(progressWindow, buffer, charsRead);
            lastReportedProgress = ReportLatestProgress(progressWindow, lastReportedProgress, progressCallback);
        }

        return output.ToString();
    }

    private static string AppendProgressWindow(string currentWindow, char[] buffer, int charsRead)
    {
        string nextWindow = currentWindow + new string(buffer, 0, charsRead);
        return nextWindow.Length <= 64 ? nextWindow : nextWindow[^64..];
    }

    private static int ReportLatestProgress(string progressWindow, int lastReportedProgress, Action<int> progressCallback)
    {
        MatchCollection matches = ProgressRegex.Matches(progressWindow);
        int latestProgress = lastReportedProgress;

        foreach (Match match in matches)
        {
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int parsedProgress))
            {
                continue;
            }

            if (parsedProgress <= latestProgress)
            {
                continue;
            }

            latestProgress = parsedProgress;
        }

        if (latestProgress > lastReportedProgress)
        {
            progressCallback(latestProgress);
        }

        return latestProgress;
    }

    private static string? FindExecutableOnPath(string executableName)
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (string rawDirectory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string directory = Environment.ExpandEnvironmentVariables(rawDirectory.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.Combine(directory, executableName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

public sealed class ProcessExecutionResult
{
    public ProcessExecutionResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }
}


