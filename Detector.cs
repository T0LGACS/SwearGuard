namespace GuardText;

// Küfür algılama. Listeler ayar dosyasından gelir; boşsa aşağıdaki varsayılanlar kullanılır.
// ExactWords: kelime TAM eşleşmeli (kısa küfürler; başka kelimenin içinde geçebilecekler buraya).
// ContainsPatterns: mesajın boşluksuz hâlinin içinde aranır; 6+ harflilerde tek harf hatası da yakalanır.
// AllowWords: bu kelimeler hiç taranmaz (masum ama küfre benzeyen kelimeler).
public class SwearDetector
{
    public static readonly string[] DefaultExact =
    {
        // Türkçe kısaltmalar ve kısa küfürler
        "amk", "amq", "amg", "aq", "awk", "mk", "mq", "amina", "aminakoyim", "aminako", "amcik", "amcuk",
        "oc", "occ", "ocuk", "ibne", "ipne", "gavat", "kavat", "kahpe", "kaltak", "pic", "pust", "yavsak", "yarak", "yarag",
        "sikik", "siktir", "sktr", "siktr", "skm", "skim", "skrm", "sikim", "sikeyim", "sikerim", "sokayim", "sokim",
        "dalyarak", "dalama", "godos", "kancik", "fahise", "serefsiz", "orospu", "orosbu", "orspu", "ananisikim", "anan",
        // İngilizce
        "fuck", "fck", "fuk", "fucking", "fuckin", "shit", "bitch", "cunt", "dick", "pussy", "whore", "slut",
        "nigga", "niger", "fagot", "motherfucker", "wtf",
        // 2026-10-25, Discord sohbet kaydından (33 bin mesaj) kaçanlar. "sık-" (ateş etmek) ile karışan hâller bilerek yok
        // (sikiyo, siktim, sikicem...); sadece "e"li, sadece küfürde geçen hâller var.
        "oruspu", "oclar", "ocmusun", "ocmisin", "pici", "picin", "piclik", "picler", "kahbe", "yavsagi", "yavsaklar",
        "aqu", "naq", "aminoglu", "sikerler", "sikeriz", "sikem", "siken", "sikmeye",
        // 2026-11-02, kullanıcı: "1 el al amımı atcam" uyarı almamıştı ("amın" masum "âmin" ile karışır, listede yok)
        "amim", "amimi", "amima", "amimda", "amimdan", "amimin",
        // 2026-11-12, sunucu sohbetinden kaçanlar. "sikin" yok: "sıkın" (ateş edin) ile karışır
        "sikimle", "sikimden"
    };

    public static readonly string[] DefaultContains =
    {
        "orospu", "orosbu", "orspu", "orusbu", "siktir", "sikeyim", "sikerim", "sikiyim", "sikiym", "siktigim", "sikecegim",
        "sikimin", "aminakoy", "aminako", "aminagoy", "aminisik", "amcik", "ananisik", "anasinisik", "bacinisik", "avradini",
        "gotveren", "gotunu", "gotune", "gotos", "pezevenk", "kahpe", "kaltak", "yavsak", "dalyarak", "fahise", "kodumun",
        "ibne", "serefsiz", "yaragim", "yaragi", "fuck", "bitch", "whore", "nigga", "fagot", "motherf",
        "oruspu", "amcig", "aminake", "sktigim"
    };

    public static readonly string[] DefaultAllow =
    {
        "sik", "sikca", "sikinti", "sikintili", "sikayet", "sikayetci", "siki", "sikica", "siklet", "sikke", "siktim", "sikti",
        "sikicam", "sikiyorum", "sikmak", "sikiyor", "sikti", "ama", "amca", "amcam", "ananas", "dick", "got",
        // "yasak" yazım toleransıyla "yavşak" sanılıyordu (sohbet kaydında 44 kez), "mother" da "motherf"
        "yasak", "yasakmi", "yasakli", "yasaklar", "yasaktir", "yasaklandi", "yasakmis", "yasaga", "mother", "mothers"
    };

    private readonly HashSet<string> _exact, _allow;
    private readonly List<string> _contains;

    public SwearDetector(IEnumerable<string>? exact, IEnumerable<string>? contains, IEnumerable<string>? allow)
    {
        // Ayar dosyasındaki listeler varsayılanların ÜSTÜNE eklenir (2026-10-25; eskiden yerine geçiyordu, varsayılana
        // eklenen yeni kelimeler sunucuda hiç kullanılmıyordu)
        _allow = Norm(DefaultAllow.Concat(allow ?? Enumerable.Empty<string>())).ToHashSet();
        _exact = Norm(DefaultExact.Concat(exact ?? Enumerable.Empty<string>())).Where(w => !_allow.Contains(w)).ToHashSet();
        _contains = Norm(DefaultContains.Concat(contains ?? Enumerable.Empty<string>())).Distinct().ToList();
    }
    private static IEnumerable<string> Norm(IEnumerable<string> l) => l.Select(TextMatch.Normalize).Where(w => w.Length > 0);

    // Yakalanırsa sebebi (eşleşen kelime), yakalanmazsa null döner
    public string? Check(string? message)
    {
        var tokens = TextMatch.Tokens(message, _allow);
        foreach (var t in tokens)
        {
            if (_exact.Contains(t)) return t;
            foreach (var p in _contains)
                if (p.Length >= 6 && TextMatch.Near(t, p)) return p;
        }
        var compact = string.Concat(tokens);
        foreach (var p in _contains)
            foreach (var at in TextMatch.FindNear(compact, p))
                if (TextMatch.SpanOk(tokens, at, p.Length)) return p;
        // "yarram": tekrar eden harf teke inince masum "yaram" oluyor; çift r'yi sadeleştirilmemiş metinde ara
        var folded = new System.Text.StringBuilder();
        foreach (var ch in message ?? "") { var c = TextMatch.Fold(ch); if (c != null) folded.Append(c.Value); }
        if (folded.ToString().Contains("yarr")) return "yarr";
        return Patterns(tokens, compact) ?? Masked(message);
    }

    // Yıldızla gizlenmiş küfür (2026-10-31, kullanıcı: "o* seni am*"): yıldız harf yerine geçer ve normalde silinir
    // ("o*" -> "o", "am*" -> "am", ikisi de masum). Yıldızlı her kelime, yıldızlar harf sayılarak küfür listesiyle
    // TAM kelime olarak karşılaştırılır: "o*" -> oç, "am*" -> amk, "s*ktir", "f*ck", "*mk". Yıldız kelimenin içinde
    // olmalı ve kelimede gerçek bir harf bulunmalı: "5*" (puan), "*gg*" (vurgu) yakalanmaz.
    private static readonly char[] MaskChars = { '*', '#' };
    private string? Masked(string? message)
    {
        if (message == null || message.IndexOfAny(MaskChars) < 0) return null;
        foreach (var word in message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.IndexOfAny(MaskChars) < 0 || !word.Any(char.IsLetter)) continue;
            var sb = new System.Text.StringBuilder();
            foreach (var ch in word)
            {
                if (MaskChars.Contains(ch)) { if (sb.Length == 0 || sb[^1] != '?') sb.Append('?'); continue; }
                var c = TextMatch.Fold(ch);
                if (c != null) sb.Append(c.Value);
            }
            var shape = TextMatch.Collapse(sb.ToString());
            // Sadece yıldızların kendisi vurgu sayılır ("*gg*"): yıldız harflerin arasında ya da sonunda olmalı
            var letters = shape.Replace("?", "");
            if (letters.Length == 0 || !shape.Contains('?') || (shape.StartsWith('?') && shape.EndsWith('?'))) continue;
            // Ortadaki yıldız 1-4 harf ("s*ktir"), baştaki/sondaki en fazla 2 ("o*" -> oç, "am*" -> amk); yoksa "ya*" yarak, "gg*" gavat olur
            var rx = new System.Text.RegularExpressions.Regex("^" + string.Concat(shape.Select((c, i) =>
                c != '?' ? c.ToString() : i == 0 || i == shape.Length - 1 ? "[a-z]{1,2}" : "[a-z]{1,4}")) + "$");
            foreach (var w in _exact.Concat(_contains))
                if (rx.IsMatch(w)) return word;
        }
        return null;
    }

    // Yazım hatalı/bitişik küfürler (2026-10-24, sunucu sohbet kaydından): "oananaimaniakoyayim", "ananmaniakaoyuayim",
    // "aminaoaktigim", "orspcununcocu", "mkerosu", "allahini" (arkasından "sev..." gelmiyorsa).
    private static readonly System.Text.RegularExpressions.Regex AnaKoy =
        new("(anan|amin|amcin|amna)[a-z]{0,12}k[aeiou]{0,2}y", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex AminaAk =
        new("amina[a-z]{0,3}(ak|sok|koy)", System.Text.RegularExpressions.RegexOptions.Compiled);
    // "ananın amı", "amın evladı" (2026-10-25)
    private static readonly string[] Owners = { "anan", "anas", "bacin", "bacis", "eben", "avradin", "alahin" };
    private static readonly HashSet<string> Am = new() { "am", "ami", "amini", "amin", "amina", "aminda", "amindan" };
    private static readonly HashSet<string> AminWho = new() { "evladi", "evlat", "oglu", "cocugu", "cocu", "evlatlari" };
    // "siktig" yok: "sıktığın için" (ateş ettiğin) ile karışıyor; "siktigim" ContainsPatterns'te
    private static readonly string[] SikStems = { "sikey", "siker", "sikec", "skey", "sker" };
    // Çift harf teke iner: "annen" -> "anen" (2026-11-12: listede "annen" yazdığı için hiç eşleşmiyordu)
    private static readonly string[] OwnerStems = { "anan", "anay", "anas", "anen", "anes", "bacin", "bacis", "eben", "avrad", "alahin", "sulal", "kizin", "karin" };
    // "anneni yalım", "anasını yediğim" (2026-11-12)
    private static readonly string[] ActStems = { "sik", "sk", "sok", "koy", "cak", "yedig", "yalim", "yalar", "yalay", "yalic", "yalac" };
    private static readonly System.Text.RegularExpressions.Regex AnaYedi =
        new("(anan|anas|anen|anes|bacin|bacis)[a-z]{0,4}yedig", System.Text.RegularExpressions.RegexOptions.Compiled);
    // "seni çaktım", "çakarım sizi" (kullanıcı 2026-11-02: CS'te "çaktım" = "amına çaktım"). Sadece "seni/sizi" ile:
    // "kafasına çaktım", "hs çaktım" (atış) serbest. "çakal", "çakmak" kökleri listede yok.
    private static readonly HashSet<string> Seni = new() { "seni", "sizi", "sen", "sizin" };
    private static readonly string[] CakStems = { "cakt", "cakar", "cakac", "cakca", "cakam", "cakay", "cakim", "cakiyo", "cakic", "cakcam", "cakaca" };
    private static readonly string[] MotherStems = { "anan", "anen", "anesi", "anasi", "anasin", "anayi", "anaci", "validen", "validesi" };
    private static string? Patterns(List<string> tokens, string compact)
    {
        foreach (var rx in new[] { AnaKoy, AminaAk, AnaYedi })
        {
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(compact))
                if (TextMatch.SpanOk(tokens, m.Index, m.Length)) return m.Value;
            // Birleşik metindeki eşleşme reddedilse de tek kelimenin içinde ayrıca olabilir ("erwinoananın amcınakoyrauym")
            foreach (var tok in tokens) { var m = rx.Match(tok); if (m.Success) return m.Value; }
        }
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Length <= 5 && t.StartsWith("amk")) return t;                     // "amkj", "amku", "amkl"
            if (t.Length > 5 && t.EndsWith("amk")) return t;                         // "fırladıamk" (bitişik)
            // Karşıdakinin annesiyle ilgili her şey küfür (kullanıcı 2026-11-12: "anneyle alakalı herşeyi küfür yap").
            // Çift harf teke iner: annen -> anen. Kendi annesi ("annem", "anam") ve "anasayfa", "anayasa" serbest.
            if (MotherStems.Any(m => t.StartsWith(m))) return t;
            if (t.StartsWith("yaram") && i + 1 < tokens.Count && tokens[i + 1] == "ye") return t + " ye"; // "yaramı ye"
            // "e"li sik- kökleri sadece küfürde geçer ("sık-" ateş etmek "a"lı çekimlenir: sıkar, sıkacak):
            // "sikey msenin", "sikeym", "siker", "sikecem" (kullanıcı 2026-10-25)
            if (SikStems.Any(st => t.StartsWith(st))) return t;
            // "anayı sikey", "ananı sokim", "bacını koyim": sahiplik + sik/sok/koy kökü, yazım ne kadar bozuk olursa olsun
            if (i + 1 < tokens.Count && OwnerStems.Any(o => t.StartsWith(o)) && ActStems.Any(a => tokens[i + 1].StartsWith(a)))
                return t + " " + tokens[i + 1];
            if (i + 1 < tokens.Count && Am.Contains(tokens[i + 1]) && Owners.Any(o => t.StartsWith(o))) return t + " " + tokens[i + 1];
            if (i + 1 < tokens.Count && (Seni.Contains(t) && CakStems.Any(c => tokens[i + 1].StartsWith(c))
                || CakStems.Any(c => t.StartsWith(c)) && Seni.Contains(tokens[i + 1]))) return t + " " + tokens[i + 1];
            if ((t == "amin" || t == "ami") && i + 1 < tokens.Count && AminWho.Contains(tokens[i + 1])) return t + " " + tokens[i + 1];
            // "rsopu": "oerrrrsopu" -> "oersopu" iki yazım hatası (2026-11-23)
            if (t.Contains("orsp") || t.Contains("rsopu") || t.Contains("orosp") || t.Contains("orsb") || t.Contains("orosb") || t.Contains("orasb") || t.Contains("orasp")) return t;
            if (t.Length >= 4 && t.StartsWith("mk")) return t;                       // "mkerosu", "mkabotu" (mk + isim)
            if ((t == "alahini" || t == "alahinizi" || t == "alahina") // tekrar eden harf teke iner: allah -> alah
                &&
                !(i + 1 < tokens.Count && tokens[i + 1].StartsWith("sev"))) return t; // "allahını seversen" masum
        }
        return null;
    }
}
