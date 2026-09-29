using System.Text.Json;
using Portway.Core;

namespace Portway.Desktop;

public sealed record TransferCheckpoint(string Id, TransferRequest Request, Site Site, string Status, string? Error, DateTimeOffset Created, Dictionary<string, Entry> Sources, Dictionary<string, Entry> FileSnapshots, HashSet<string> Completed, HashSet<string> CompletedFiles, int Attempts = 0, long Bytes = 0, long Total = 0, string Current = "");
public sealed class QueueJournal(ProfileStore profiles)
{
    readonly string directory = Path.Combine(profiles.DataPath, "queue");
    readonly object gate = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public TransferCheckpoint[] Load()
    {
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            var result = new List<TransferCheckpoint>();
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").Take(1000))
            {
                try
                {
                    var item = JsonSerializer.Deserialize<TransferCheckpoint>(File.ReadAllText(file), Json);
                    if (item != null && ValidId(item.Id))
                        result.Add(item);
                }
                catch (JsonException)
                {
                    File.Move(file, file + ".corrupt-" + Guid.NewGuid().ToString("N"));
                }
            }

            return result.ToArray();
        }
    }

    static bool ValidId(string id) => id.Length == 32 && id.All(Uri.IsHexDigit);
    public void Save(TransferCheckpoint checkpoint)
    {
        if (!ValidId(checkpoint.Id))
            throw new ArgumentException("잘못된 작업 ID입니다.");
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, checkpoint.Id + ".json");
            var temp = target + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, checkpoint with { Site = SiteSecrets.Public(checkpoint.Site) }, Json);
                stream.Flush(true);
            }

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, target, true);
        }
    }

    public void Delete(string id)
    {
        if (!ValidId(id))
            throw new ArgumentException("잘못된 작업 ID입니다.");
        lock (gate)
            File.Delete(Path.Combine(directory, id + ".json"));
    }
}
