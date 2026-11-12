using System.Text.RegularExpressions;

namespace PTScheduler.Application.Import;

public enum ImportField { Skip, FirstName, LastName, FullName, Email, Phone, Goal, BirthDate }

public sealed record ImportTable(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, string Source, bool HasHeader = true);

public sealed record ImportCandidate(int Row, string FirstName, string LastName, string? Email, string? Phone, string? Goal, DateOnly? BirthDate, string? Problem);

/// <summary>
/// Import klientów z tego, co trener ma pod ręką: CSV (średnik, przecinek, tabulator), wklejka z Excela,
/// kontakty z telefonu (.vcf). Kolumny dopasowane po nazwie i po zawartości (e-mail, telefon, data).
/// </summary>
public static partial class ClientImportParser
{
    public const int MaxRows = 2000;

    public static ImportTable Parse(string text)
    {
        text = text.TrimStart('﻿');
        if (text.Contains("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase)) return ParseVcf(text);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Take(MaxRows + 1).ToList();
        if (lines.Count == 0) return new([], [], "csv");
        var first = lines[0];
        var sep = new[] { '\t', ';', ',' }.OrderByDescending(c => first.Count(ch => ch == c)).First();
        var rows = lines.Select(l => SplitLine(l, sep)).ToList();
        var looksLikeHeader = !rows[0].Any(v => Email().IsMatch(v) || PhoneDigits(v).Length >= 9);
        var headers = looksLikeHeader ? rows[0].Select(h => h.Trim()).ToList() : rows[0].Select((_, i) => $"Kolumna {i + 1}").ToList();
        var data = (looksLikeHeader ? rows.Skip(1) : rows).ToList();
        return new(headers, data, sep == '\t' ? "excel" : "csv", looksLikeHeader);
    }

    private static ImportTable ParseVcf(string text)
    {
        var rows = new List<string[]>();
        foreach (Match card in Regex.Matches(text, @"BEGIN:VCARD(.*?)END:VCARD", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            // Zawinięte linie vCard zaczynają się spacją — sklejamy.
            var body = Regex.Replace(card.Groups[1].Value, @"\r?\n[ \t]", "");
            string Field(string name) => Regex.Match(body, $@"^{name}(;[^:\r\n]*)?:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Groups[2].Value.Trim();
            var n = Field("N").Split(';');
            var last = n.ElementAtOrDefault(0)?.Trim() ?? "";
            var firstName = n.ElementAtOrDefault(1)?.Trim() ?? "";
            var full = Field("FN");
            if (firstName.Length == 0 && last.Length == 0 && full.Length > 0) (firstName, last) = SplitFullName(full);
            rows.Add([Unescape(firstName), Unescape(last), Field("EMAIL"), Field("TEL")]);
            if (rows.Count >= MaxRows) break;
        }
        return new(["Imię", "Nazwisko", "E-mail", "Telefon"], rows, "vcf", HasHeader: false);
    }

    private static string Unescape(string s) => s.Replace("\\,", ",").Replace("\\;", ";");

    private static readonly (ImportField Field, string[] Names)[] Synonyms =
    [
        (ImportField.FullName, ["imię i nazwisko", "imie i nazwisko", "klient", "nazwa", "name", "full name", "osoba", "kontakt"]),
        (ImportField.FirstName, ["imię", "imie", "first name", "firstname", "given name"]),
        (ImportField.LastName, ["nazwisko", "last name", "lastname", "surname", "family name"]),
        (ImportField.Email, ["email", "e-mail", "mail", "adres e-mail", "adres email", "e-mail address"]),
        (ImportField.Phone, ["telefon", "tel", "tel.", "nr telefonu", "numer telefonu", "komórka", "komorka", "phone", "mobile", "gsm"]),
        (ImportField.Goal, ["cel", "cel treningowy", "goal", "notatka", "uwagi"]),
        (ImportField.BirthDate, ["data urodzenia", "urodziny", "birthday", "birth date", "dateofbirth", "ur."]),
    ];

    /// <summary>Kolumna → pole: najpierw po nazwie nagłówka, potem po zawartości.</summary>
    public static Dictionary<int, ImportField> GuessMapping(ImportTable t)
    {
        var map = new Dictionary<int, ImportField>();
        for (var i = 0; i < t.Headers.Count; i++)
        {
            var h = t.Headers[i].Trim().ToLowerInvariant();
            var hit = Synonyms.FirstOrDefault(s => s.Names.Contains(h)).Field;
            if (hit == ImportField.Skip) hit = Synonyms.FirstOrDefault(s => s.Names.Any(n => n.Length > 3 && h.Contains(n))).Field;
            if (hit != ImportField.Skip && !map.ContainsValue(hit)) map[i] = hit;
        }
        // Po zawartości: kolumna z adresami e-mail, numerami, datami.
        var sample = t.Rows.Take(20).ToList();
        for (var i = 0; i < t.Headers.Count; i++)
        {
            if (map.ContainsKey(i)) continue;
            var values = sample.Select(r => r.ElementAtOrDefault(i) ?? "").Where(v => v.Length > 0).ToList();
            if (values.Count == 0) continue;
            ImportField guess =
                values.Count(v => Email().IsMatch(v)) * 2 > values.Count ? ImportField.Email :
                values.Count(v => PhoneDigits(v).Length is >= 9 and <= 13 && !v.Contains('-', StringComparison.Ordinal) || PhoneDigits(v).Length is >= 9 and <= 13 && v.Contains(' ')) * 2 > values.Count ? ImportField.Phone :
                values.Count(v => TryDate(v) is not null) * 2 > values.Count ? ImportField.BirthDate : ImportField.Skip;
            if (guess != ImportField.Skip && !map.ContainsValue(guess)) map[i] = guess;
        }
        // Bez nagłówka z imieniem: kolumny z samymi słowami (litery) — dwie obok siebie to imię i nazwisko, jedna to całe imię i nazwisko.
        if (!HasName(map))
        {
            var wordy = Enumerable.Range(0, t.Headers.Count).Where(i => !map.ContainsKey(i)).Where(i =>
            {
                var values = sample.Select(r => r.ElementAtOrDefault(i) ?? "").Where(v => v.Length > 0).ToList();
                return values.Count > 0 && values.Count(v => v.All(ch => char.IsLetter(ch) || ch is ' ' or '-' or '\'')) * 2 > values.Count;
            }).ToList();
            if (wordy.Count >= 2 && wordy[1] == wordy[0] + 1) { map[wordy[0]] = ImportField.FirstName; map[wordy[1]] = ImportField.LastName; }
            else if (wordy.Count >= 1) map[wordy[0]] = ImportField.FullName;
        }
        return map;
    }

    public static bool HasName(Dictionary<int, ImportField> map) =>
        map.ContainsValue(ImportField.FullName) || map.ContainsValue(ImportField.FirstName);

    public static List<ImportCandidate> Build(ImportTable t, Dictionary<int, ImportField> map)
    {
        string Val(string[] row, ImportField f) => map.Where(m => m.Value == f).Select(m => row.ElementAtOrDefault(m.Key)?.Trim() ?? "").FirstOrDefault() ?? "";
        var list = new List<ImportCandidate>();
        var rowNo = t.HasHeader ? 1 : 0; // numer jak w pliku (z nagłówkiem dane zaczynają się od 2. wiersza)
        foreach (var row in t.Rows)
        {
            rowNo++;
            var first = Val(row, ImportField.FirstName);
            var last = Val(row, ImportField.LastName);
            var full = Val(row, ImportField.FullName);
            if (first.Length == 0 && full.Length > 0) (first, last) = SplitFullName(full);
            if (first.Length == 0 && last.Length == 0 && Val(row, ImportField.Email).Length == 0) continue; // pusty wiersz
            var email = Val(row, ImportField.Email);
            var phone = Val(row, ImportField.Phone);
            string? problem = first.Length == 0 ? "brak imienia"
                : email.Length == 0 ? "brak e-maila (potrzebny do konta klienta)"
                : !Email().IsMatch(email) ? "nieprawidłowy e-mail" : null;
            list.Add(new ImportCandidate(rowNo, Cap(first), Cap(last.Length == 0 ? "" : last), email.Length == 0 ? null : email.ToLowerInvariant(),
                phone.Length == 0 ? null : phone, NullIfEmpty(Val(row, ImportField.Goal)), TryDate(Val(row, ImportField.BirthDate)), problem));
        }
        return list;
    }

    public static (string First, string Last) SplitFullName(string full)
    {
        // „Kowalski, Jan” — nazwisko przed przecinkiem.
        if (full.Contains(','))
        {
            var byComma = full.Split(',', 2, StringSplitOptions.TrimEntries);
            return (byComma[1], byComma[0]);
        }
        var parts = full.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (parts.ElementAtOrDefault(0) ?? "", parts.ElementAtOrDefault(1) ?? "");
    }

    private static string Cap(string s) => s.Length == 0 ? s : s.Length == 1 ? s.ToUpperInvariant()
        : s.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)) || s.All(ch => !char.IsLetter(ch) || char.IsLower(ch))
            ? char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant() : s;

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static DateOnly? TryDate(string s)
    {
        if (s.Length < 6) return null;
        foreach (var f in new[] { "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "dd-MM-yyyy", "yyyy.MM.dd" })
            if (DateOnly.TryParseExact(s, f, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) && d.Year is > 1920 and < 2025)
                return d;
        return null;
    }

    private static string PhoneDigits(string s) => new(s.Where(char.IsDigit).ToArray());

    private static string[] SplitLine(string line, char sep)
    {
        var fields = new List<string>();
        var inQuote = false;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"') { if (inQuote && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else inQuote = !inQuote; continue; }
            if (ch == sep && !inQuote) { fields.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        fields.Add(sb.ToString().Trim());
        return fields.ToArray();
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();
}
