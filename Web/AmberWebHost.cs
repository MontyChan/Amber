using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Vault.Core;
using Vault.Data;
using Vault.Models;

namespace Vault.Web;

public static class AmberWebHost
{
    public static async Task<int> RunAsync()
    {
        string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRoot
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

        WebApplication app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        WebSession session = new();
        MapEndpoints(app, session);

        await app.StartAsync();
        string url = app.Urls.First();
        OpenBrowser(url);
        Console.WriteLine($"Amber web UI: {url}");
        await app.WaitForShutdownAsync();
        return 0;
    }

    private static void MapEndpoints(WebApplication app, WebSession session)
    {
        app.MapGet("/api/settings", () => Results.Ok(new { databasePath = session.DatabasePath, archiveOutputDirectory = session.ArchiveOutputDirectory, firstRun = session.FirstRun }));

        app.MapPost("/api/settings/open", (OpenSettingsRequest request) =>
        {
            try
            {
                session.Open(request.DatabasePath, request.ArchiveOutputDirectory);
                return Results.Ok(new { databasePath = session.DatabasePath, archiveOutputDirectory = session.ArchiveOutputDirectory });
            }
            catch (Exception exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        app.MapGet("/api/archives", async (CancellationToken cancellationToken) => Results.Ok(await session.Repository.GetArchiveSummariesAsync(cancellationToken)));

        app.MapPost("/api/archives", async (CreateArchiveRequest request, CancellationToken cancellationToken) =>
        {
            try
            {
                ArchiveJobSnapshot job = session.StartArchive(request);
                return Results.Accepted($"/api/archive-jobs/{job.Id}", job);
            }
            catch (Exception exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        app.MapGet("/api/archive-jobs/{jobId:guid}", (Guid jobId) =>
        {
            ArchiveJobSnapshot? job = session.GetArchiveJob(jobId);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });

        app.MapDelete("/api/archives/{archiveId:long}", async (long archiveId, CancellationToken cancellationToken) =>
        {
            try
            {
                await session.Repository.DeleteArchiveAsync(archiveId, cancellationToken);
                return Results.Ok(new { message = "Archive deleted" });
            }
            catch (Exception exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        app.MapPatch("/api/archives/{archiveId:long}", async (long archiveId, UpdateArchiveMetadataRequest request, CancellationToken cancellationToken) =>
        {
            try
            {
                await session.Repository.UpdateArchiveMetadataAsync(archiveId, request.Note, request.Tags, cancellationToken);
                return Results.Ok(new { message = "Archive details updated" });
            }
            catch (Exception exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        app.MapGet("/api/archives/{archiveId:long}", async (long archiveId, CancellationToken cancellationToken) =>
        {
            Archive? archive = await session.Repository.GetArchiveByIdAsync(archiveId, cancellationToken);
            return archive is null ? Results.NotFound() : Results.Ok(archive);
        });

        app.MapGet("/api/archives/{archiveId:long}/files", async (long archiveId, string? prefix, string? query, int? page, int? pageSize, CancellationToken cancellationToken) =>
            Results.Ok(await session.Repository.GetArchiveFilePageAsync(archiveId, prefix ?? string.Empty, query ?? string.Empty, page ?? 1, pageSize ?? 200, cancellationToken)));

        app.MapGet("/api/archives/{archiveId:long}/tree", async (long archiveId, string? path, CancellationToken cancellationToken) =>
            Results.Ok(await session.Repository.GetArchiveDirectoryEntriesAsync(archiveId, path ?? string.Empty, cancellationToken)));

        app.MapPost("/api/archives/{archiveId:long}/extract", async (long archiveId, ExtractRequest request, CancellationToken cancellationToken) =>
        {
            try
            {
                await session.ExtractAsync(archiveId, request.RelativePath, request.OutputDirectory, cancellationToken);
                return Results.Ok(new { message = "Extraction completed" });
            }
            catch (Exception exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        app.MapGet("/api/archives/{archiveId:long}/download", async (long archiveId, string path, CancellationToken cancellationToken) =>
        {
            try
            {
                DownloadFile download = await session.PrepareDownloadAsync(archiveId, path, cancellationToken);
                return Results.File(download.FilePath, download.ContentType, download.FileName, enableRangeProcessing: true);
            }
            catch (Exception exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        app.MapFallbackToFile("index.html");
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // The URL is still printed when the platform blocks automatic browser launch.
        }
    }
}

public sealed record OpenSettingsRequest(string DatabasePath, string ArchiveOutputDirectory);
public sealed record ExtractRequest(string RelativePath, string OutputDirectory);
public sealed record UpdateArchiveMetadataRequest(string? Note, string? Tags);
public sealed record CreateArchiveRequest(
    string Directory,
    string? Note,
    string? Tags,
    bool IncludeHighEntropyFiles,
    string Level = "ultra",
    string? DictionarySize = null,
    int? FastBytes = null,
    string? MatchFinder = null,
    string? SolidMode = null);

public sealed class WebSession
{
    private Database _database;
    private ArchiveRepository _repository;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ArchiveJob> _archiveJobs = new();

    public WebSession()
    {
        _database = new Database();
        FirstRun = !File.Exists(_database.LocationsFilePath);
        _database.Initialize();
        _repository = new ArchiveRepository(_database);
    }

    public bool FirstRun { get; }
    public ArchiveRepository Repository => _repository;
    public string DatabasePath => _database.DatabasePath;
    public string ArchiveOutputDirectory => _database.ArchiveOutputDirectory;

    public void Open(string databasePath, string archiveOutputDirectory)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("Database file does not exist / 数据库文件不存在", databasePath);
        }

        if (!Directory.Exists(archiveOutputDirectory))
        {
            throw new DirectoryNotFoundException($"Archive directory does not exist / 归档目录不存在：{archiveOutputDirectory}");
        }

        Database database = new(databasePath, archiveOutputDirectory);
        using Microsoft.Data.Sqlite.SqliteConnection connection = database.OpenConnection();
        using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('archives', 'files');";
        if (Convert.ToInt32(command.ExecuteScalar()) != 2)
        {
            throw new InvalidOperationException("The selected database is not an Amber database / 选择的数据库不是 Amber 数据库");
        }

        _database = database;
        _repository = new ArchiveRepository(database);
    }

    public async Task ExtractAsync(long archiveId, string relativePath, string outputDirectory, CancellationToken cancellationToken)
    {
        Archive? archive = await _repository.GetArchiveByIdAsync(archiveId, cancellationToken) ?? throw new InvalidOperationException("Archive not found / 未找到归档");
        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        SevenZipRunner runner = new();
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            bool extracted = false;
            foreach ((bool isStored, string? recordedPath) in new[] { (false, archive.CompressedPath), (true, archive.StoredPath) })
            {
                if (string.IsNullOrWhiteSpace(recordedPath)) continue;
                string? archivePackagePath = await _repository.ResolveArchivePackagePathAsync(archive, isStored, cancellationToken);
                if (string.IsNullOrWhiteSpace(archivePackagePath) || !File.Exists(archivePackagePath))
                    throw new InvalidOperationException($"Archive package is missing / 归档包不存在：{archivePackagePath ?? recordedPath}");
                await runner.ExtractArchiveAsync(archivePackagePath, fullOutputDirectory, cancellationToken);
                extracted = true;
            }
            if (!extracted) throw new InvalidOperationException("Archive has no package files / 该归档没有压缩包");
            return;
        }

        string normalizedPath = relativePath.Replace('\\', '/');
        ArchiveFile? file = await _repository.GetArchiveFileAsync(archiveId, normalizedPath, cancellationToken) ?? throw new InvalidOperationException("File not found / 未找到文件");
        string packagePath = await _repository.ResolveArchivePackagePathAsync(archive, file.IsStored, cancellationToken) ?? throw new InvalidOperationException("Archive package is missing / 归档包不存在");
        await runner.ExtractSingleFileAsync(packagePath, FileSorter.ToArchiveEntryPath(normalizedPath), fullOutputDirectory, cancellationToken);
    }

    public ArchiveJobSnapshot StartArchive(CreateArchiveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Directory))
        {
            throw new InvalidOperationException("Archive source directory is required / 归档源目录不能为空");
        }

        ArchiveCompressionOptions compressionOptions = BuildCompressionOptions(request);
        Guid jobId = Guid.NewGuid();
        ArchiveJob job = new(jobId);
        _archiveJobs[jobId] = job;

        _ = Task.Run(async () =>
        {
            try
            {
                ArchiveSession session = new(_repository);
                await session.RunAsync(
                    request.Directory,
                    request.Note,
                    request.Tags,
                    compressionOptions,
                    request.IncludeHighEntropyFiles,
                    CancellationToken.None,
                    job.Report);
                job.Complete(session.CreatedArchiveId);
            }
            catch (Exception exception)
            {
                job.Fail(exception.Message);
            }
        });

        return job.Snapshot();
    }

    public ArchiveJobSnapshot? GetArchiveJob(Guid jobId)
    {
        return _archiveJobs.TryGetValue(jobId, out ArchiveJob? job) ? job.Snapshot() : null;
    }

    private static ArchiveCompressionOptions BuildCompressionOptions(CreateArchiveRequest request)
    {
        SevenZipCompressionLevel level = request.Level.Trim().ToLowerInvariant() switch
        {
            "store" or "0" => SevenZipCompressionLevel.Store,
            "fastest" or "1" => SevenZipCompressionLevel.Fastest,
            "fast" or "3" => SevenZipCompressionLevel.Fast,
            "normal" or "5" => SevenZipCompressionLevel.Normal,
            "maximum" or "max" or "7" => SevenZipCompressionLevel.Maximum,
            "ultra" or "9" => SevenZipCompressionLevel.Ultra,
            _ => throw new InvalidOperationException("Invalid compression level / 无效的压缩等级")
        };

        return new ArchiveCompressionOptions
        {
            Level = level,
            DictionarySize = request.DictionarySize,
            FastBytes = request.FastBytes,
            MatchFinder = request.MatchFinder,
            SolidMode = request.SolidMode
        }.Validate();
    }

    public async Task<DownloadFile> PrepareDownloadAsync(long archiveId, string relativePath, CancellationToken cancellationToken)
    {
        Archive? archive = await _repository.GetArchiveByIdAsync(archiveId, cancellationToken) ?? throw new InvalidOperationException("Archive not found / 未找到归档");
        string normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        ArchiveFile? file = await _repository.GetArchiveFileAsync(archiveId, normalizedPath, cancellationToken) ?? throw new InvalidOperationException("File not found / 未找到文件");
        string packagePath = await _repository.ResolveArchivePackagePathAsync(archive, file.IsStored, cancellationToken) ?? throw new InvalidOperationException("Archive package is missing / 归档包不存在");

        string tempDirectory = Path.Combine(Path.GetTempPath(), "amber-downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string extractedPath = Path.Combine(tempDirectory, FileSorter.ToArchiveEntryPath(normalizedPath));
        try
        {
            await new SevenZipRunner().ExtractSingleFileAsync(packagePath, FileSorter.ToArchiveEntryPath(normalizedPath), tempDirectory, cancellationToken);
            if (!File.Exists(extractedPath))
            {
                throw new InvalidOperationException("The extracted file was not found / 解压后的文件不存在");
            }

            FileExtensionContentTypeProvider provider = new();
            string contentType = provider.TryGetContentType(extractedPath, out string? detectedType) ? detectedType : "application/octet-stream";
            _ = DeleteLaterAsync(tempDirectory);
            return new DownloadFile(extractedPath, Path.GetFileName(extractedPath), contentType);
        }
        catch
        {
            TryDeleteDirectory(tempDirectory);
            throw;
        }
    }

    private static async Task DeleteLaterAsync(string directory)
    {
        await Task.Delay(TimeSpan.FromMinutes(10));
        TryDeleteDirectory(directory);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // A download may still have the file open; the next process cleanup can remove it.
        }
    }
}

public sealed record ArchiveJobSnapshot(Guid Id, string Stage, int Percent, long Completed, long Total, string Message, string Status, long? ArchiveId, string? Error);

public sealed class ArchiveJob
{
    private readonly object _sync = new();
    private string _stage = "queued";
    private int _percent;
    private long _completed;
    private long _total;
    private string _message = "Queued";
    private string _status = "running";
    private long? _archiveId;
    private string? _error;

    public ArchiveJob(Guid id) => Id = id;
    public Guid Id { get; }

    public void Report(ArchiveProgressUpdate progress)
    {
        lock (_sync)
        {
            _stage = progress.Stage;
            _percent = Math.Clamp(progress.Percent, 0, 100);
            _completed = progress.Completed;
            _total = progress.Total;
            _message = progress.Message;
        }
    }

    public void Complete(long archiveId)
    {
        lock (_sync)
        {
            _stage = "completed"; _percent = 100; _message = "Archive complete"; _status = "completed"; _archiveId = archiveId;
        }
    }

    public void Fail(string error)
    {
        lock (_sync)
        {
            _status = "failed"; _error = error; _message = "Archive failed";
        }
    }

    public ArchiveJobSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new ArchiveJobSnapshot(Id, _stage, _percent, _completed, _total, _message, _status, _archiveId, _error);
        }
    }
}

public sealed record DownloadFile(string FilePath, string FileName, string ContentType);
