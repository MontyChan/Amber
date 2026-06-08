using Dapper;
using Microsoft.Data.Sqlite;
using Vault.Models;

namespace Vault.Data;

public sealed class ArchiveRepository
{
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

    public async Task<long> InsertArchiveAsync(Archive archive, IReadOnlyCollection<ArchiveFile> files, CancellationToken cancellationToken = default)
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

        const string fileSql = @"
INSERT INTO files (archive_id, relative_path, size, modified_at, is_stored)
VALUES (@ArchiveId, @RelativePath, @Size, @ModifiedAt, @IsStored);";

        foreach (ArchiveFile file in files)
        {
            file.ArchiveId = archiveId;
            await connection.ExecuteAsync(new CommandDefinition(
                fileSql,
                file,
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return archiveId;
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
