using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;

namespace GuardText;

// SwearGuard ve KickGuard ortak: yakalanan mesaja uyarı notunu panelin canlı chat'ine (Chat_Logger kaydı) ve Discord'a yazar.
// Chat_Logger "say" mesajını eklentimiz engellese de kaydediyor; hangi eklentinin önce çalıştığı yükleme sırasına bağlı.
// Bu yüzden not bir sonraki karede yazılır: Chat_Logger mesajı yazdıysa altına sadece not satırı eklenir, yazmadıysa
// (yazamadıysa) mesaj notla birlikte yazılır. Böylece mesaj iki kez görünmez (2026-10-25).
public static class ChatNote
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // pluginDir: eklentinin ModuleDirectory'si (plugins/<ad>); Chat_Logger yanında (plugins/Chat_Logger) aranır
    public static void Write(string pluginDir, string tag, string name, ulong steamId, bool team, string message, string note)
    {
        var now = DateTime.Now;
        Server.NextFrame(() => WriteNow(pluginDir, tag, name, steamId, team, message, note, now));
    }

    private static void WriteNow(string pluginDir, string tag, string name, ulong steamId, bool team, string message, string note, DateTime now)
    {
        var plugins = Path.GetDirectoryName(pluginDir)!;
        var file = Path.Combine(plugins, "Chat_Logger", "logs", now.ToString("HH00_MM_dd_yyyy") + ".txt");
        var config = Path.Combine(Path.GetDirectoryName(plugins)!, "configs", "plugins", "Chat_Logger", "Chat_Logger.json");
        var teamTag = team ? "[TEAM]" : "[ALL]";

        var logged = false;
        try
        {
            if (File.Exists(file))
                logged = File.ReadLines(file).Reverse().Take(30)
                    .Any(l => l.Contains($"[{steamId}]") && !l.Contains("⚠️") && l.TrimEnd().EndsWith(message.Trim()));
        }
        catch { }
        var text = logged ? $"⚠️ {tag}: {note}" : $"{message} ⚠️ ({tag}: {note})";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, $"[{now:HH:mm:ss}] [{steamId}] - {teamTag} ({name}) : {text}" + Environment.NewLine);
        }
        catch (Exception e) { Console.WriteLine($"[{tag}] sohbet kaydına yazılamadı: " + e.Message); }

        string? hook = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(config),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.TryGetProperty("WebHookURL", out var u)) hook = u.GetString();
        }
        catch { }
        if (string.IsNullOrWhiteSpace(hook) || !hook.StartsWith("https://")) return;

        var body = JsonSerializer.Serialize(new
        {
            embeds = new[] { new {
                author = new { name, url = $"https://steamcommunity.com/profiles/{steamId}" },
                description = $"[VDS] [{now:HH:mm:ss}] {teamTag} : {text}",
                color = 0xDC2626,
            } },
        });
        // Chat_Logger'ın kendi mesajı Discord'a önce düşsün diye biraz beklenir
        Task.Run(async () =>
        {
            try { await Task.Delay(1500); await Http.PostAsync(hook, new StringContent(body, Encoding.UTF8, "application/json")); }
            catch (Exception e) { Console.WriteLine($"[{tag}] Discord'a yazılamadı: " + e.Message); }
        });
    }
}
