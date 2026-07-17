using System.Text.Json;
using TurboRama.ArcadeTimer.Models;

namespace TurboRama.ArcadeTimer.Services;

public sealed class CreditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _mainPath;
    private readonly string _backupPath;
    private readonly string _tempPath;
    private readonly string _lockPath;

    public CreditStore(string baseDirectory)
    {
        _mainPath = Path.Combine(baseDirectory, "credit.json");
        _backupPath = Path.Combine(baseDirectory, "credit.backup.json");
        _tempPath = Path.Combine(baseDirectory, "credit.tmp");
        _lockPath = Path.Combine(baseDirectory, "credit.lock");
    }

    public CreditData Load()
    {
        CreditData? data = TryRead(_mainPath) ?? TryRead(_backupPath);
        if (data is null)
        {
            return new CreditData
            {
                RemainingSeconds = 0,
                TotalCoinsAccepted = 0,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        if (data.RemainingSeconds < 0)
            data.RemainingSeconds = 0;
        if (data.TotalCoinsAccepted < 0)
            data.TotalCoinsAccepted = 0;

        return data;
    }

    public void Save(CreditData data)
    {
        try
        {
            using var fileLock = TryAcquireLock(TimeSpan.FromSeconds(2));
            string json = JsonSerializer.Serialize(data, JsonOptions);

            File.WriteAllText(_tempPath, json);

            _ = JsonSerializer.Deserialize<CreditData>(
                    File.ReadAllText(_tempPath),
                    JsonOptions)
                ?? throw new InvalidDataException("Crédito temporário inválido.");

            if (File.Exists(_mainPath))
                File.Copy(_mainPath, _backupPath, true);

            File.Move(_tempPath, _mainPath, true);
        }
        catch (Exception ex)
        {
            LogService.Write("Falha ao salvar crédito", ex);
        }
    }

    private static CreditData? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<CreditData>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private FileStream? TryAcquireLock(TimeSpan timeout)
    {
        DateTime limit = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < limit)
        {
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                Thread.Sleep(20);
            }
            catch
            {
                break;
            }
        }

        return null;
    }
}
