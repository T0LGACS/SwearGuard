using System.Text;

namespace GuardText;

// SwearGuard ve KickGuard'ın ortak metin eşleştirmesi (oyun kütüphanesine bağımlı değil, ayrıca test edilir).
// Metin sadeleştirilir: küçük harf, Türkçe harfler (ç->c, ğ->g, ı->i, ö->o, ş->s, ü->u), rakam/sembolle yazılan
// harfler (@->a, 1/!->i, 0->o, 3->e, 4->a, 5/$->s, 7->t, 9->g, €->e) ve tekrar eden harfler tek harfe iner (amkkk -> amk).
public static class TextMatch
{
    public static char? Fold(char raw)
    {
        var c = raw switch
        {
            'İ' or 'I' or 'ı' or '!' or '|' or '1' => 'i',
            '@' or '4' => 'a',
            '$' or '5' => 's',
            '0' => 'o',
            '3' or '€' => 'e',
            '7' => 't',
            '9' => 'g',
            _ => char.ToLowerInvariant(raw)
        };
        c = c switch { 'ç' => 'c', 'ğ' => 'g', 'ö' => 'o', 'ş' => 's', 'ü' => 'u', 'â' => 'a', 'î' => 'i', 'û' => 'u', _ => c };
        return char.IsLetterOrDigit(c) ? c : null;
    }

    // Tekrar eden harfleri teke indir: "siiiktir" -> "siktir", "yarrak" -> "yarak"
    public static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (sb.Length == 0 || sb[^1] != c) sb.Append(c);
        return sb.ToString();
    }

    // Tek kelime/kalıp sadeleştirme (listeler için)
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var raw in s) { var c = Fold(raw); if (c != null) sb.Append(c.Value); }
        return Collapse(sb.ToString());
    }

    // Mesajı kelimelere böler. Tek harfli kelimeler arka arkaya geliyorsa birleştirilir ("a m k" -> "amk").
    public static List<string> Tokens(string? message, ISet<string>? allow = null)
    {
        var raw = new List<string>();
        var cur = new StringBuilder();
        foreach (var ch in message ?? "")
        {
            var c = Fold(ch);
            if (c != null) cur.Append(c.Value);
            else if (cur.Length > 0) { raw.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) raw.Add(cur.ToString());

        var tokens = new List<string>();
        var letters = new StringBuilder();
        foreach (var t in raw)
        {
            if (t.Length == 1) { letters.Append(t); continue; }
            if (letters.Length > 0) { tokens.Add(letters.ToString()); letters.Clear(); }
            tokens.Add(t);
        }
        if (letters.Length > 0) tokens.Add(letters.ToString());

        return tokens.Select(Collapse).Where(t => t.Length > 0 && (allow == null || !allow.Contains(t))).ToList();
    }

    // Yazım hatası: tek harf yer değişikliği (orsopu), tek harf eksik (orspu) ya da fazla (orosppu teki zaten iner).
    // Harf DEĞİŞİMİ sayılmaz: "sileyim" ile "sikeyim" gibi masum kelimeler karışmasın.
    public static bool Near(string text, string pattern)
    {
        if (text == pattern) return true;
        if (pattern.Length < 6) return false;
        int lt = text.Length, lp = pattern.Length;
        if (lt == lp)
        {
            int i = 0;
            while (i < lt && text[i] == pattern[i]) i++;
            return i < lt - 1 && text[i] == pattern[i + 1] && text[i + 1] == pattern[i] && text[(i + 2)..] == pattern[(i + 2)..];
        }
        if (Math.Abs(lt - lp) != 1) return false;
        var (shorter, longer) = lt < lp ? (text, pattern) : (pattern, text);
        int k = 0;
        while (k < shorter.Length && shorter[k] == longer[k]) k++;
        return k > 0 && shorter[k..] == longer[(k + 1)..]; // ilk harf aynı olmalı
    }

    // Boşluksuz birleşik metinde kalıbın bulunduğu yerler (tam ya da tek harf yer değişikliği).
    public static IEnumerable<int> FindNear(string text, string pattern)
    {
        for (int i = 0; i + pattern.Length <= text.Length; i++)
        {
            if (text[i] != pattern[0]) continue;
            var part = text.Substring(i, pattern.Length);
            if (part == pattern || (pattern.Length >= 6 && Near(part, pattern))) yield return i;
        }
    }

    // Birleşik metindeki bir eşleşme kelime sınırına uyuyor mu (2026-10-25, Discord sohbet kaydından):
    // tek kelimenin içindeyse geçerli; birden çok kelimeye yayılıyorsa bir kelimenin BAŞINDAN başlamalı ve son
    // kelimeye en az 3 harf girmeli. Böylece "kankam çıkar" (amcık), "go to spec" (götoş), "adamın kafasına koy"
    // gibi kelime ortasından başlayan rastlantılar yakalanmaz; "amına koyim", "o r o s p u" yakalanır.
    public static bool SpanOk(IReadOnlyList<string> tokens, int index, int length)
    {
        int start = 0, k = 0;
        while (k < tokens.Count && start + tokens[k].Length <= index) { start += tokens[k].Length; k++; }
        if (k >= tokens.Count) return false;
        int end = index + length;
        if (end <= start + tokens[k].Length) return true;   // tek kelimenin içinde
        // Kelime ortasından başlıyorsa sadece önceki kelimenin SON harfinden başlayabilir: "annenis iekrim" (sikerim'in
        // ilk harfi önceki kelimeye yapışmış) yakalanır, "kankam çıkar" (amcık, 2 harf taşıyor) yakalanmaz (2026-10-25)
        if (index != start && index != start + tokens[k].Length - 1) return false;
        int s2 = start, j = k;
        while (j < tokens.Count && s2 + tokens[j].Length < end) { s2 += tokens[j].Length; j++; }
        return end - s2 >= 3;                               // son kelimeye en az 3 harf
    }

    // Boşluksuz birleşik metinde kalıp ya da tek harf yer değişikliği (orsopu) var mı.
    // Burada eksik/fazla harf sayılmaz: kelimeler birleşince "yarın görüşürüz" gibi masum metinlerde
    // kalıba bir harf uzak parçalar çıkabiliyor. Eksik/fazla harf sadece kelime kelime bakılırken (Near) kabul edilir.
    public static bool ContainsNear(string text, string pattern)
    {
        if (text.Contains(pattern)) return true;
        if (pattern.Length < 6) return false;
        for (int i = 0; i + pattern.Length <= text.Length; i++)
            if (text[i] == pattern[0] && Near(text.Substring(i, pattern.Length), pattern)) return true;
        return false;
    }
}
