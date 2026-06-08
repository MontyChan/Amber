using Microsoft.Data.Sqlite;

namespace Vault.Data;

public sealed class Database
{
    private const string LocationsFileName = "locations.json";
    private const string DefaultDatabaseFileName = "amber.db";
    private readonly string _defaultDatabasePath;
    private readonly string _defaultArchiveOutputDirectory;

    public Database()
    {
        string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AppDirectory = Path.Combine(homeDirectory, ".amber");
        LocationsFilePath = Path.Combine(AppDirectory, LocationsFileName);
        _defaultDatabasePath = Path.Combine(AppDirectory, DefaultDatabaseFileName);
        _defaultArchiveOutputDirectory = Path.Combine(AppDirectory, "archives");

        Locations = StorageLocations.Load(LocationsFilePath);
        DatabasePath = ResolveDatabasePath(Locations.DatabasePath, _defaultDatabasePath);
        ArchiveOutputDirectory = ResolveDirectoryPath(Locations.ArchiveOutputDirectory, _defaultArchiveOutputDirectory);
    }

    public string AppDirectory { get; }

    public string LocationsFilePath { get; }

    public string DatabasePath { get; private set; }

    public StorageLocations Locations { get; private set; }

    public string ArchiveOutputDirectory { get; private set; }

    public void Initialize()
    {
        Directory.CreateDirectory(AppDirectory);

        string? databaseDirectory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        Directory.CreateDirectory(ArchiveOutputDirectory);
        PersistLocations();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS archives (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    compressed_path TEXT NULL,
    stored_path TEXT NULL,
    created_at TEXT NOT NULL,
    note TEXT NULL,
    tags TEXT NULL
);

CREATE TABLE IF NOT EXISTS files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    archive_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL,
    size INTEGER NOT NULL,
    modified_at TEXT NOT NULL,
    is_stored INTEGER NOT NULL,
    FOREIGN KEY (archive_id) REFERENCES archives(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_files_archive_id ON files(archive_id);
CREATE INDEX IF NOT EXISTS idx_files_relative_path ON files(relative_path);
CREATE INDEX IF NOT EXISTS idx_archives_created_at ON archives(created_at);
";
        command.ExecuteNonQuery();
    }

    public void ReloadSettings()
    {
        Locations = StorageLocations.Load(LocationsFilePath);
        DatabasePath = ResolveDatabasePath(Locations.DatabasePath, _defaultDatabasePath);
        ArchiveOutputDirectory = ResolveDirectoryPath(Locations.ArchiveOutputDirectory, _defaultArchiveOutputDirectory);
        Directory.CreateDirectory(ArchiveOutputDirectory);
    }

    public void SetStorageLocations(string databasePath, string archiveOutputDirectory)
    {
        DatabasePath = ResolveDatabasePath(databasePath, _defaultDatabasePath);
        ArchiveOutputDirectory = ResolveDirectoryPath(archiveOutputDirectory, _defaultArchiveOutputDirectory);
        PersistLocations();
    }

    public SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new($"Data Source={DatabasePath}");
        connection.Open();
        return connection;
    }

    public static string ResolveDatabasePath(string? configured, string fallbackPath)
    {
        return ResolveFilePath(configured, fallbackPath, DefaultDatabaseFileName);
    }

    private void PersistLocations()
    {
        Locations.DatabasePath = DatabasePath;
        Locations.ArchiveOutputDirectory = ArchiveOutputDirectory;
        Locations.Save(LocationsFilePath);
    }

    private static string ResolveFilePath(string? configured, string fallbackPath, string defaultFileName)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallbackPath;
        }

        string trimmed = configured.Trim().Trim('"');
        string expanded = Environment.ExpandEnvironmentVariables(trimmed);
        string fullPath = Path.GetFullPath(expanded);

        if (LooksLikeDirectoryPath(trimmed, fullPath))
        {
            return Path.Combine(fullPath, defaultFileName);
        }

        return fullPath;
    }

    private static bool LooksLikeDirectoryPath(string originalValue, string fullPath)
    {
        if (Directory.Exists(fullPath))
        {
            return true;
        }

        char trailingChar = originalValue.LastOrDefault();
        return trailingChar == Path.DirectorySeparatorChar || trailingChar == Path.AltDirectorySeparatorChar;
    }

    private static string ResolveDirectoryPath(string? configured, string fallbackPath)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallbackPath;
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"')));
    }
}

