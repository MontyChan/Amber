using Dapper;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using Vault.Models;

namespace Vault.Data;

public sealed class ArchiveStorageMigrator
{
    private readonly Database _database;

    public ArchiveStorageMigrator(Database database)
    {
        _database = database;
    }

    public async Task MigrateAsync(StorageMigrationPlan plan, CancellationToken cancellationToken = default)
    {
        string sourceDatabasePath = _database.DatabasePath;
        string targetDatabasePath = Database.ResolveDatabasePath(plan.DatabasePath, sourceDatabasePath);
        string targetArchiveOutputDirectory = NormalizeDirectoryPath(plan.ArchiveOutputDirectory, _database.ArchiveOutputDirectory);

        IReadOnlyList<ArchivePackageMove> packageMoves = await LoadPackageMovesAsync(targetArchiveOutputDirectory, cancellationToken);
        bool moveDatabaseFile = !string.Equals(sourceDatabasePath, targetDatabasePath, StringComparison.OrdinalIgnoreCase);
        int totalSteps = CountPackageSteps(packageMoves) + CountFileSteps(moveDatabaseFile) + 2;

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async context =>
            {
                ProgressTask task = context.AddTask("Migrating storage locations / 正在迁移存储位置", maxValue: totalSteps);

                await CopyArchivePackagesAsync(packageMoves, task, cancellationToken);
                await UpdateDatabaseArchivePathsAsync(packageMoves, cancellationToken);
                task.Increment(1);

                CopyFileIfNeeded(sourceDatabasePath, targetDatabasePath, task, cancellationToken);
                _database.SetStorageLocations(targetDatabasePath, targetArchiveOutputDirectory);
                task.Increment(1);

                SqliteConnection.ClearAllPools();
                DeleteOldArchivePackages(packageMoves, task, cancellationToken);
                DeleteOldDatabaseFileIfNeeded(sourceDatabasePath, targetDatabasePath, task, cancellationToken);
                _database.ReloadSettings();
            });
    }

    private async Task<IReadOnlyList<ArchivePackageMove>> LoadPackageMovesAsync(string targetDirectory, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT id,
       compressed_path AS CompressedPath,
       stored_path AS StoredPath
FROM archives
ORDER BY id ASC;";

        await using SqliteConnection connection = _database.OpenConnection();
        IEnumerable<Archive> archives = await connection.QueryAsync<Archive>(new CommandDefinition(sql, cancellationToken: cancellationToken));

        List<ArchivePackageMove> moves = new();
        foreach (Archive archive in archives)
        {
            AddMove(moves, archive.Id, ArchivePackageKind.Compressed, archive.CompressedPath, targetDirectory);
            AddMove(moves, archive.Id, ArchivePackageKind.Stored, archive.StoredPath, targetDirectory);
        }

        return moves;
    }

    private static void AddMove(List<ArchivePackageMove> moves, long archiveId, ArchivePackageKind kind, string? sourcePath, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        string fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        string targetPath = Path.Combine(targetDirectory, fileName);
        moves.Add(new ArchivePackageMove(archiveId, kind, Path.GetFullPath(sourcePath), targetPath));
    }

    private async Task CopyArchivePackagesAsync(IReadOnlyList<ArchivePackageMove> moves, ProgressTask task, CancellationToken cancellationToken)
    {
        foreach (ArchivePackageMove move in moves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyFileIfNeeded(move.SourcePath, move.TargetPath, task, cancellationToken);
        }

        await Task.CompletedTask;
    }

    private async Task UpdateDatabaseArchivePathsAsync(IReadOnlyList<ArchivePackageMove> moves, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (ArchivePackageMove move in moves)
        {
            string columnName = move.Kind == ArchivePackageKind.Compressed ? "compressed_path" : "stored_path";
            string sql = $"UPDATE archives SET {columnName} = @TargetPath WHERE id = @ArchiveId;";
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { move.TargetPath, move.ArchiveId },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void DeleteOldArchivePackages(IReadOnlyList<ArchivePackageMove> moves, ProgressTask task, CancellationToken cancellationToken)
    {
        foreach (ArchivePackageMove move in moves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteFileIfNeeded(move.SourcePath, move.TargetPath, task, cancellationToken);
        }
    }

    private static void CopyFileIfNeeded(string sourcePath, string targetPath, ProgressTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException($"Source file does not exist / 源文件不存在：{sourcePath}");
        }

        string? directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(targetPath))
        {
            throw new InvalidOperationException($"Target file already exists / 目标文件已存在：{targetPath}");
        }

        File.Copy(sourcePath, targetPath, overwrite: false);

        FileInfo sourceInfo = new(sourcePath);
        FileInfo targetInfo = new(targetPath);
        if (sourceInfo.Length != targetInfo.Length)
        {
            throw new InvalidOperationException($"Copied file verification failed / 复制后的文件校验失败：{targetPath}");
        }

        task.Increment(1);
    }

    private static void DeleteFileIfNeeded(string sourcePath, string targetPath, ProgressTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
            task.Increment(1);
        }
    }

    private static void DeleteOldDatabaseFileIfNeeded(string sourcePath, string targetPath, ProgressTask task, CancellationToken cancellationToken)
    {
        try
        {
            DeleteFileIfNeeded(sourcePath, targetPath, task, cancellationToken);
        }
        catch (IOException exception)
        {
            AnsiConsole.MarkupLine($"[yellow]Database migration was completed, but the old database file could not be deleted because it is still in use.[/]");
            AnsiConsole.MarkupLine($"[grey]数据库迁移已经完成，但旧数据库文件仍被占用，暂时无法删除：{Markup.Escape(sourcePath)}[/]");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(exception.Message)}[/]");
            task.Increment(1);
        }
        catch (UnauthorizedAccessException exception)
        {
            AnsiConsole.MarkupLine($"[yellow]Database migration was completed, but the old database file could not be deleted due to access permissions.[/]");
            AnsiConsole.MarkupLine($"[grey]数据库迁移已经完成，但旧数据库文件因权限问题暂时无法删除：{Markup.Escape(sourcePath)}[/]");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(exception.Message)}[/]");
            task.Increment(1);
        }
    }

    private static int CountPackageSteps(IReadOnlyList<ArchivePackageMove> moves)
    {
        return moves.Count(move => !string.Equals(move.SourcePath, move.TargetPath, StringComparison.OrdinalIgnoreCase)) * 2;
    }

    private static int CountFileSteps(bool moved)
    {
        return moved ? 2 : 0;
    }

    private static string NormalizeDirectoryPath(string? value, string fallback)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables((string.IsNullOrWhiteSpace(value) ? fallback : value).Trim().Trim('"')));
    }
}

public enum ArchivePackageKind
{
    Compressed,
    Stored
}

public sealed record ArchivePackageMove(long ArchiveId, ArchivePackageKind Kind, string SourcePath, string TargetPath);

public sealed record StorageMigrationPlan(string DatabasePath, string ArchiveOutputDirectory);
