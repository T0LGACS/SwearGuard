using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using GuardText;

namespace SwearGuard;

public class SwearGuardConfig : BasePluginConfig
{
    // Bu yetkisi olan oyuncular (admin, moderatör) kontrol edilmez (kullanıcı 2026-10-25: "admine küfürde serbest olsun").
    // Boş bırakılırsa herkes kontrol edilir.
    [JsonPropertyName("AdminExemptFlag")]
    public string AdminExemptFlag { get; set; } = "@css/generic";

    // Kelimeler harf/aksan farkı olmadan yazılır (ç->c, ğ->g, ı->i, ö->o, ş->s, ü->u), tekrar eden harfler teke iner.
    // Boş bırakılırsa Detector.cs'teki geniş varsayılan listeler kullanılır.
    // ExactWords: mesajdaki bir kelime TAM eşitse yakalanır (kısa küfürler için; yanlış yakalamayı önler).
    [JsonPropertyName("ExactWords")]
    public List<string> ExactWords { get; set; } = new();

    // ContainsPatterns: mesajın boşluksuz hâlinin içinde geçerse yakalanır ("o r o s p u", "orospucocugu").
    // 6+ harfli olanlarda harf yer değişikliği ve tek harf eksik/fazla da yakalanır (orsopu, orspu).
    [JsonPropertyName("ContainsPatterns")]
    public List<string> ContainsPatterns { get; set; } = new();

    // AllowWords: küfre benzeyen ama masum kelimeler, hiç taranmaz (sık, sıktım, şikayet...).
    [JsonPropertyName("AllowWords")]
    public List<string> AllowWords { get; set; } = new();

    [JsonPropertyName("Prefix")]
    public string Prefix { get; set; } = "[Küfür Koruması]";

    // {name} yerine oyuncunun adı gelir. 1. ve 2. küfür sadece uyarıdır; 3. küfürde ceza komutu çalışır.
    [JsonPropertyName("WarnMessage")]
    public string WarnMessage { get; set; } = "Küfür etmek yasak. 3. küfürde 15 gün susturulursun. (1/3)";

    // 2. küfür uyarısı (eski adıyla KickMessage; artık kimse atılmaz)
    [JsonPropertyName("KickMessage")]
    public string KickMessage { get; set; } = "{name}, son uyarı! Bir kez daha küfür edersen 15 gün susturulacaksın. (2/3)";

    [JsonPropertyName("BanMessage")]
    public string BanMessage { get; set; } = "{name} tekrar küfür ettiği için 15 gün susturuldu (sesli ve yazılı).";

    // 3. küfürde ceza süresi (dakika). 21600 = 15 gün.
    [JsonPropertyName("BanMinutes")]
    public int BanMinutes { get; set; } = 21600;

    // 2. küfürde ek komut (boş = sadece uyarı). Yer tutucular: {userid} {steamid} {minutes} {reason}
    [JsonPropertyName("KickCommand")]
    public string KickCommand { get; set; } = "";

    // 3. küfür cezası. Varsayılan: CS2-SimpleAdmin ile susturma (sesli + yazılı).
    [JsonPropertyName("BanCommand")]
    public string BanCommand { get; set; } = "css_silence #{userid} {minutes} \"Kufur (3. uyari)\"";

    // Engellenen küfürlü mesaj sohbette görünmez ama Chat_Logger kaydına (panelin canlı chat'i) ve Chat_Logger'ın
    // Discord webhook'una uyarı sayısıyla yazılır. Webhook adresi Chat_Logger'ın kendi ayarından okunur.
    [JsonPropertyName("LogBlocked")]
    public bool LogBlocked { get; set; } = true;
}

public class SwearGuard : BasePlugin, IPluginConfig<SwearGuardConfig>
{
    public override string ModuleName => "SwearGuard";
    public override string ModuleVersion => "2.8.0";
    public override string ModuleAuthor => "Claude";
    public override string ModuleDescription => "Küfür eden oyunculara iki uyarı, üçüncüde susturma uygular.";

    public SwearGuardConfig Config { get; set; } = new();

    private SwearDetector _detector = new(null, null, null);
    private readonly Dictionary<ulong, int> _strikes = new();

    public void OnConfigParsed(SwearGuardConfig config)
    {
        Config = config;
        _detector = new SwearDetector(config.ExactWords, config.ContainsPatterns, config.AllowWords);
    }

    public override void Load(bool hotReload)
    {
        AddCommandListener("say", OnSay);
        AddCommandListener("say_team", OnSay);
        // Chat_Logger her saatin ilk mesajında yeni dosyayı açarken yazamıyor ("Cant Write") ve o mesajı kaybediyor
        // (2026-10-24, saatin ilk mesajı hep kayboluyordu). Bu saatin ve sonraki saatin dosyası önceden boş açılır.
        PrepareChatLogFiles();
        AddTimer(60f, PrepareChatLogFiles, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
    }

    private void PrepareChatLogFiles()
    {
        try
        {
            var logs = Path.Combine(ChatLoggerDir, "logs");
            if (!Directory.Exists(logs)) return;
            foreach (var t in new[] { DateTime.Now, DateTime.Now.AddHours(1) })
            {
                var f = Path.Combine(logs, t.ToString("HH00_MM_dd_yyyy") + ".txt");
                if (!File.Exists(f)) File.WriteAllText(f, "");
            }
        }
        catch (Exception e) { Console.WriteLine("[SwearGuard] sohbet kayıt dosyası açılamadı: " + e.Message); }
    }

    private HookResult OnSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;
        if (!string.IsNullOrWhiteSpace(Config.AdminExemptFlag) && AdminManager.PlayerHasPermissions(player, Config.AdminExemptFlag))
            return HookResult.Continue;

        var hit = _detector.Check(info.GetArg(1));
        if (hit == null)
            return HookResult.Continue;

        var steamId = player.SteamID;
        _strikes.TryGetValue(steamId, out var count);
        count++;
        _strikes[steamId] = count;

        var name = player.PlayerName;
        Console.WriteLine($"[SwearGuard] {name} ({steamId}) ihlal {count}: '{hit}'");
        if (Config.LogBlocked)
        {
            var note = count == 1 ? "1. kez uyarıldı" : count == 2 ? "2. kez uyarıldı, son uyarı"
                : $"{count}. küfür: {Config.BanMinutes / 1440} gün susturuldu";
            ChatNote.Write(ModuleDirectory, "Küfür", name, steamId, info.GetArg(0) == "say_team", info.GetArg(1), note);
        }

        if (count == 1)
            player.PrintToChat(Format(Config.WarnMessage, name));
        else if (count == 2)
        {
            Server.PrintToChatAll(Format(Config.KickMessage, name));
            if (!string.IsNullOrWhiteSpace(Config.KickCommand)) Run(Config.KickCommand, player, "Kufur");
        }
        else
        {
            Server.PrintToChatAll(Format(Config.BanMessage, name));
            Run(Config.BanCommand, player, "Kufur");
            _strikes[steamId] = 0; // ceza sonrası sayaç sıfırlanır
        }

        // Küfürlü mesaj sohbette görünmesin.
        return HookResult.Handled;
    }

    // Chat_Logger'ın yerleri (bu eklentinin klasörüne göre: plugins/SwearGuard -> plugins/Chat_Logger, configs/plugins/Chat_Logger)
    private string ChatLoggerDir => Path.Combine(Path.GetDirectoryName(ModuleDirectory)!, "Chat_Logger");
    private string ChatLoggerConfig => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ModuleDirectory)!)!,
        "configs", "plugins", "Chat_Logger", "Chat_Logger.json");

    [ConsoleCommand("css_kufurcezasifirla", "Tüm küfür ihlal sayaçlarını sıfırlar (sadece sunucu konsolundan)")]
    public void OnResetStrikes(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller != null) return; // oyuncular kendi sayaçlarını sıfırlayamasın
        _strikes.Clear();
        command.ReplyToCommand("[SwearGuard] Tüm ihlal sayaçları sıfırlandı.");
    }

    private string Format(string message, string name) =>
        $" {ChatColors.Red}{Config.Prefix}{ChatColors.Default} " + message.Replace("{name}", name);

    private void Run(string template, CCSPlayerController player, string reason)
    {
        if (player.UserId is not int userId) return;
        Server.ExecuteCommand(template
            .Replace("{userid}", userId.ToString())
            .Replace("{steamid}", player.SteamID.ToString())
            .Replace("{minutes}", Config.BanMinutes.ToString())
            .Replace("{reason}", reason));
    }
}
