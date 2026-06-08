using System.Text.Json;

namespace Vault.Data;

public sealed class StorageLocations
{
    public string? DatabasePath { get; set; }

    public string? ArchiveOutputDirectory { get; set; }

    public static StorageLocations Load(string locationsFilePath)
    {
        if (!File.Exists(locationsFilePath))
        {
            return new StorageLocations();
        }

        try
        {
            string json = File.ReadAllText(locationsFilePath);
            return JsonSerializer.Deserialize<StorageLocations>(json) ?? new StorageLocations();
        }
        catch
        {
            return new StorageLocations();
        }
    }

    public void Save(string locationsFilePath)
    {
        string? directory = Path.GetDirectoryName(locationsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(locationsFilePath, json);
    }
}
