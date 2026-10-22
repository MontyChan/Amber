using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Vault.Models;


namespace Vault.Data;

public sealed class ArchiveRepository
{
    private const int ProgressReportInterval = 256;
    private readonly Database _database;


    public ArchiveRepository(Database database)
    {
        _database = database;
    }

    public string ArchiveOutputDirectory => _database.ArchiveOutputDirectory;

    public string DatabasePath => _database.DatabasePath;

    public async Task MigrateArchiveOutputDirectoryAsync(string archiveOutputDirectory, CancellationToken cancellationToken = default)
    {
        await MigrateStorageLocationsAsync(DatabasePath, archiveOutputDirectory, cancellationToken);
    }

    public async Task MigrateDatabasePathAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await MigrateStorageLocationsAsync(databasePath, ArchiveOutputDirectory, cancellationToken);
    }

    public async Task MigrateStorageLocationsAsync(string databasePath, string archiveOutputDirectory, CancellationToken cancellationToken = default)
    {
        ArchiveStorageMigrator migrator = new(_database);
        await migrator.MigrateAsync(new StorageMigrationPlan(databasePath, archiveOutputDirectory), cancellationToken);
    }

        public async Task<long> InsertArchiveAsync(
            Archive archive,
            IReadOnlyCollection<ArchiveFile> files,
            Action<int>? progressCallback = null,
            CancellationToken cancellationToken = default)

    {
        await using SqliteConnection connection = _database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string archiveSql = @"
INSERT INTO archives (compressed_path, stored_path, created_at, note, tags)
VALUES (@CompressedPath, @StoredPath, @CreatedAt, @Note, @Tags);
SELECT last_insert_rowid();";

        long archiveId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            archiveSql,
            archive,
            transaction,
            cancellationToken: cancellationToken));

        if (files.Count > 0)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = @"
INSERT INTO files (archive_id, relative_path, size, modified_at, is_stored)
VALUES ($archiveId, $relativePath, $size, $modifiedAt, $isStored);";

            SqliteParameter archiveIdParameter = command.Parameters.Add("$archiveId", SqliteType.Integer);
            SqliteParameter relativePathParameter = command.Parameters.Add("$relativePath", SqliteType.Text);
            SqliteParameter sizeParameter = command.Parameters.Add("$size", SqliteType.Integer);
            SqliteParameter modifiedAtParameter = command.Parameters.Add("$modifiedAt", SqliteType.Text);
            SqliteParameter isStoredParameter = command.Parameters.Add("$isStored", SqliteType.Integer);

            archiveIdParameter.Value = archiveId;
            command.Prepare();

                        int persistedCount = 0;

            foreach (ArchiveFile file in files)
            {
                file.ArchiveId = archiveId;
                relativePathParameter.Value = file.RelativePath;
                sizeParameter.Value = file.Size;
                modifiedAtParameter.Value = file.ModifiedAt;
                isStoredParameter.Value = file.IsStored ? 1 : 0;
                await command.ExecuteNonQueryAsync(cancellationToken);
                persistedCount++;
                ReportProgress(persistedCount, files.Count, progressCallback);
            }

        }

                await transaction.CommitAsync(cancellationToken);
        return archiveId;
    }

    private static void ReportProgress(int processedCount, int totalCount, Action<int>? progressCallback)
    {
        if (progressCallback is null)
        {
            return;
        }

        if (processedCount < totalCount && processedCount % ProgressReportInterval != 0)
        {
            return;
        }

        progressCallback(processedCount);
    }


    public async Task<IReadOnlyList<ArchiveSummary>> GetArchiveSummariesAsync(CancellationToken cancellationToken = default)

    {
        const string sql = @"
SELECT a.id,
       a.created_at AS CreatedAt,
       a.note AS Note,
       a.tags AS Tags,
       COUNT(f.id) AS FileCount
FROM archives a
LEFT JOIN files f ON f.archive_id = a.id
GROUP BY a.id, a.created_at, a.note, a.tags
ORDER BY a.created_at DESC;";

        await using SqliteConnection connection = _database.OpenConnection();
        IEnumerable<ArchiveSummary> items = await connection.QueryAsync<ArchiveSummary>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return items.ToList();
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT a.id AS ArchiveId,
       a.created_at AS CreatedAt,
       a.note AS Note,
       a.tags AS Tags,
       f.relative_path AS RelativePath,
       f.is_stored AS IsStored
FROM files f
INNER JOIN archives a ON a.id = f.archive_id
WHERE f.relative_path LIKE @Pattern COLLATE NOCASE
   OR IFNULL(a.note, '') LIKE @Pattern COLLATE NOCASE
ORDER BY a.created_at DESC, f.relative_path ASC;";

        await using SqliteConnection connection = _database.OpenConnection();
        IEnumerable<SearchResultItem> items = await connection.QueryAsync<SearchResultItem>(new CommandDefinition(
            sql,
            new { Pattern = $"%{keyword}%" },
            cancellationToken: cancellationToken));

        return items.ToList();
    }

    public async Task<Archive?> GetArchiveByIdAsync(long archiveId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id,
       compressed_path AS CompressedPath,
       stored_path AS StoredPath,
       created_at AS CreatedAt,
       note AS Note,
       tags AS Tags
FROM archives
WHERE id = @ArchiveId;";

        await using SqliteConnection connection = _database.OpenConnection();
        return await connection.QuerySingleOrDefaultAsync<Archive>(new CommandDefinition(
            sql,
            new { ArchiveId = archiveId },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> ResolveArchivePackagePathAsync(Archive archive, bool isStored, CancellationToken cancellationToken = default)
    {
        string? recordedPath = isStored ? archive.StoredPath : archive.CompressedPath;
        if (string.IsNullOrWhiteSpace(recordedPath))
        {
            return null;
        }

        string fullRecordedPath = Path.GetFullPath(recordedPath);
        if (File.Exists(fullRecordedPath))
        {
            return fullRecordedPath;
        }

        string fileName = Path.GetFileName(recordedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fullRecordedPath;
        }

        string? relocatedPath = FindRelocatedPackagePath(recordedPath, fileName);
        if (relocatedPath is null)
        {
            return fullRecordedPath;
        }

        await UpdateArchivePackagePathAsync(archive.Id, isStored, relocatedPath, cancellationToken);
        if (isStored)
        {
            archive.StoredPath = relocatedPath;
        }
        else
        {
            archive.CompressedPath = relocatedPath;
        }

        return relocatedPath;
    }

    private string? FindRelocatedPackagePath(string recordedPath, string fileName)
    {
        string relocatedPath = Path.Combine(ArchiveOutputDirectory, fileName);
        if (!File.Exists(relocatedPath))
        {
            string? oldParentName = Path.GetFileName(Path.GetDirectoryName(recordedPath));
            if (string.IsNullOrWhiteSpace(oldParentName))
            {
                return null;
            }

            relocatedPath = Path.Combine(ArchiveOutputDirectory, oldParentName, fileName);
            if (!File.Exists(relocatedPath))
            {
                return null;
            }
        }

        return relocatedPath;
    }

    private async Task UpdateArchivePackagePathAsync(long archiveId, bool isStored, string packagePath, CancellationToken cancellationToken)
    {
        string columnName = isStored ? "stored_path" : "compressed_path";
        string sql = $"UPDATE archives SET {columnName} = @PackagePath WHERE id = @ArchiveId;";

        await using SqliteConnection connection = _database.OpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { ArchiveId = archiveId, PackagePath = packagePath },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ArchiveFile>> GetArchiveFilesAsync(long archiveId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id,
       archive_id AS ArchiveId,
       relative_path AS RelativePath,
       size,
       modified_at AS ModifiedAt,
       is_stored AS IsStored
FROM files
WHERE archive_id = @ArchiveId
ORDER BY relative_path ASC;";

        await using SqliteConnection connection = _database.OpenConnection();
        IEnumerable<ArchiveFile> items = await connection.QueryAsync<ArchiveFile>(new CommandDefinition(
            sql,
            new { ArchiveId = archiveId },
            cancellationToken: cancellationToken));

        return items.ToList();
    }

    public async Task<object> GetArchiveFilePageAsync(long archiveId, string prefix, string query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);
        prefix = prefix.Replace('\\', '/').Trim('/');
        string prefixPattern = string.IsNullOrEmpty(prefix) ? "%" : $"{EscapeLikePattern(prefix)}/%";
        string queryPattern = string.IsNullOrWhiteSpace(query) ? "%" : $"%{EscapeLikePattern(query.Trim())}%";
        const string countSql = @"
SELECT COUNT(*) FROM files
WHERE archive_id = @ArchiveId
  AND relative_path LIKE @PrefixPattern
  AND relative_path LIKE @QueryPattern COLLATE NOCASE;";
        const string filesSql = @"
SELECT id, archive_id AS ArchiveId, relative_path AS RelativePath, size, modified_at AS ModifiedAt, is_stored AS IsStored
FROM files
WHERE archive_id = @ArchiveId
  AND relative_path LIKE @PrefixPattern
  AND relative_path LIKE @QueryPattern COLLATE NOCASE
ORDER BY relative_path ASC
LIMIT @PageSize OFFSET @Offset;";

        await using Microsoft.Data.Sqlite.SqliteConnection connection = _database.OpenConnection();
        object parameters = new { ArchiveId = archiveId, PrefixPattern = prefixPattern, QueryPattern = queryPattern, PageSize = pageSize, Offset = (page - 1) * pageSize };
        int total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));
        IEnumerable<ArchiveFile> files = await connection.QueryAsync<ArchiveFile>(new CommandDefinition(filesSql, parameters, cancellationToken: cancellationToken));
        return new { files = files.ToList(), total, page, pageSize };
    }

    public async Task<IReadOnlyList<ArchiveBrowserEntry>> GetArchiveDirectoryEntriesAsync(long archiveId, string directoryPath, CancellationToken cancellationToken = default)
    {
        directoryPath = directoryPath.Replace('\\', '/').Trim('/');
        if (directoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Invalid directory path / 无效的目录路径", nameof(directoryPath));
        }

        string prefix = string.IsNullOrEmpty(directoryPath) ? string.Empty : directoryPath + "/";
        string prefixPattern = EscapeLikePattern(prefix) + "%";
        const string sql = @"
WITH scoped AS (
    SELECT relative_path,
           substr(relative_path, length(@Prefix) + 1) AS remainder,
           size,
           modified_at,
           is_stored
    FROM files
    WHERE archive_id = @ArchiveId
      AND relative_path LIKE @PrefixPattern ESCAPE '\'
), segments AS (
    SELECT CASE WHEN instr(remainder, '/') = 0 THEN remainder ELSE substr(remainder, 1, instr(remainder, '/') - 1) END AS Name,
           CASE WHEN instr(remainder, '/') = 0 THEN 0 ELSE 1 END AS IsDirectory,
           relative_path,
           size,
           modified_at,
           is_stored
    FROM scoped
)
SELECT Name,
       IsDirectory,
       CASE WHEN IsDirectory = 1 THEN CASE WHEN @DirectoryPath = '' THEN Name ELSE @DirectoryPath || '/' || Name END ELSE relative_path END AS RelativePath,
       CASE WHEN IsDirectory = 1 THEN 0 ELSE size END AS Size,
       modified_at AS ModifiedAt,
       is_stored AS IsStored
FROM segments
GROUP BY Name, IsDirectory, RelativePath
ORDER BY IsDirectory DESC, Name COLLATE NOCASE ASC;";

        await using Microsoft.Data.Sqlite.SqliteConnection connection = _database.OpenConnection();
        IEnumerable<ArchiveBrowserEntry> entries = await connection.QueryAsync<ArchiveBrowserEntry>(new CommandDefinition(
            sql,
            new { ArchiveId = archiveId, Prefix = prefix, PrefixPattern = prefixPattern, DirectoryPath = directoryPath },
            cancellationToken: cancellationToken));
        return entries.ToList();
    }

    private static string EscapeLikePattern(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    public async Task<ArchiveFile?> GetArchiveFileAsync(long archiveId, string relativePath, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT id,
       archive_id AS ArchiveId,
       relative_path AS RelativePath,
       size,
       modified_at AS ModifiedAt,
       is_stored AS IsStored
FROM files
WHERE archive_id = @ArchiveId
  AND relative_path = @RelativePath;";

        await using SqliteConnection connection = _database.OpenConnection();
        return await connection.QuerySingleOrDefaultAsync<ArchiveFile>(new CommandDefinition(
            sql,
            new { ArchiveId = archiveId, RelativePath = relativePath },
            cancellationToken: cancellationToken));
    }
}
