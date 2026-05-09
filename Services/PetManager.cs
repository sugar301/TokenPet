using System.IO;
using System.IO.Compression;
using System.Text.Json;
using TokenPet.Models;

namespace TokenPet.Services;

public class PetManager
{
    private readonly string _petsDir;
    private readonly List<PetInfo> _pets = new();
    private string _activePetId = "";

    public string PetsDir => _petsDir;
    public IReadOnlyList<PetInfo> Pets => _pets;
    public string ActivePetId => _activePetId;

    public event Action? PetListChanged;
    public event Action<string>? PetChanged;

    public PetManager()
    {
        _petsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet_data", "pets");
    }

    public void Setup()
    {
        Directory.CreateDirectory(_petsDir);
        ScanPets();
    }

    public string? GetActiveSpritePath()
    {
        var pet = _pets.FirstOrDefault(p => p.Id == _activePetId);
        if (pet == null) return null;
        var path = Path.Combine(pet.Directory, pet.SpritesheetPath);
        return File.Exists(path) ? path : null;
    }

    public string? GetActiveDisplayName()
    {
        return _pets.FirstOrDefault(p => p.Id == _activePetId)?.DisplayName;
    }

    public void SetActivePet(string petId)
    {
        if (_activePetId != petId && _pets.Any(p => p.Id == petId))
        {
            _activePetId = petId;
            PetChanged?.Invoke(petId);
        }
    }

    public string? ImportPet(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            var jsonEntry = archive.Entries.FirstOrDefault(e =>
                e.Name.Equals("pet.json", StringComparison.OrdinalIgnoreCase));
            if (jsonEntry == null) return "缺少 pet.json";

            PetInfo? info;
            using (var stream = jsonEntry.Open())
            {
                info = JsonSerializer.Deserialize<PetInfo>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            if (info == null || string.IsNullOrWhiteSpace(info.Id)) return "pet.json 无效";

            var spriteEntry = archive.Entries.FirstOrDefault(e =>
            {
                var ext = Path.GetExtension(e.Name).ToLower();
                return ext is ".png" or ".webp";
            });

            var petDir = Path.Combine(_petsDir, info.Id);
            if (Directory.Exists(petDir)) Directory.Delete(petDir, true);
            Directory.CreateDirectory(petDir);

            foreach (var entry in archive.Entries)
            {
                var destPath = Path.Combine(petDir, entry.Name);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                if (!string.IsNullOrEmpty(entry.Name))
                {
                    entry.ExtractToFile(destPath, true);
                }
            }

            var newJson = new
            {
                id = info.Id,
                displayName = info.DisplayName,
                description = info.Description,
                spritesheetPath = info.SpritesheetPath
            };
            var jsonText = JsonSerializer.Serialize(newJson, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(petDir, "pet.json"), jsonText);

            ScanPets();
            SetActivePet(info.Id);
            PetListChanged?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            return $"导入失败: {ex.Message}";
        }
    }

    public string? ExportPet(string petId, string outputPath)
    {
        try
        {
            var pet = _pets.FirstOrDefault(p => p.Id == petId);
            if (pet == null) return "宠物不存在";

            using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            foreach (var file in Directory.GetFiles(pet.Directory))
            {
                archive.CreateEntryFromFile(file, Path.GetFileName(file));
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"导出失败: {ex.Message}";
        }
    }

    public string? DeletePet(string petId)
    {
        var pet = _pets.FirstOrDefault(p => p.Id == petId);
        if (pet == null) return "宠物不存在";
        try
        {
            if (Directory.Exists(pet.Directory))
                Directory.Delete(pet.Directory, true);
            if (_activePetId == petId)
                _activePetId = "";
            ScanPets();
            PetListChanged?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            return $"删除失败: {ex.Message}";
        }
    }

    private void ScanPets()
    {
        _pets.Clear();
        if (!Directory.Exists(_petsDir)) return;

        foreach (var dir in Directory.GetDirectories(_petsDir))
        {
            var info = LoadPetInfo(dir);
            if (info != null) _pets.Add(info);
        }
    }

    private static PetInfo? LoadPetInfo(string dir)
    {
        var jsonPath = Path.Combine(dir, "pet.json");
        if (!File.Exists(jsonPath)) return null;
        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new PetInfo
            {
                Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                DisplayName = root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "",
                Description = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                SpritesheetPath = root.TryGetProperty("spritesheetPath", out var sp) ? sp.GetString() ?? "" : "",
                Directory = dir
            };
        }
        catch { return null; }
    }
}
