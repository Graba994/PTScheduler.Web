using System.Text.Json;

namespace PTScheduler.Application.Wearables;

/// <summary>
/// Dane z Garmin Connect (Health API, dostarczane przez Portal) → nasze wiersze. Garmin wysyła tablice pod kluczami
/// „dailies”, „sleeps”, „hrv”, „activities”; pola, których nie ma albo mają inny typ, po prostu pomijamy —
/// ten sam rekord może przyjść ponownie (poprawiony) i wtedy nadpisuje poprzedni.
/// </summary>
public static class GarminParser
{
    /// <summary>Rodzaje danych, które zapisujemy. Pozostałe klucze z Garmina ignorujemy.</summary>
    public static readonly string[] Kinds = ["dailies", "sleeps", "hrv", "activities"];

    public sealed record DayPart(DateOnly Date, int? Steps = null, int? RestingHeartRate = null, int? AverageStress = null,
        int? BodyBatteryHigh = null, int? BodyBatteryLow = null, int? ActiveKilocalories = null,
        int? SleepMinutes = null, int? DeepSleepMinutes = null, int? LightSleepMinutes = null, int? RemSleepMinutes = null,
        int? AwakeMinutes = null, int? SleepScore = null, int? HrvLastNight = null);

    public sealed record Activity(string ExternalId, string Name, string Type, DateTime StartUtc, int DurationSeconds,
        int? AverageHeartRate, int? MaxHeartRate, int? Kilocalories, int? DistanceMeters, string? DeviceName);

    /// <summary>Identyfikator użytkownika Garmina w rekordzie (do przypisania klienta w Portalu).</summary>
    public static string? UserId(JsonElement record) => Str(record, "userId");

    public static DayPart? Day(string kind, JsonElement r)
    {
        if (DateOf(r) is not { } date) return null;
        return kind switch
        {
            "dailies" => new DayPart(date,
                Steps: Int(r, "steps"),
                RestingHeartRate: Int(r, "restingHeartRateInBeatsPerMinute"),
                AverageStress: Int(r, "averageStressLevel") is >= 0 and var s ? s : null,
                BodyBatteryHigh: Int(r, "bodyBatteryHighestValue") ?? Int(r, "bodyBatteryChargedValue"),
                BodyBatteryLow: Int(r, "bodyBatteryLowestValue"),
                ActiveKilocalories: Int(r, "activeKilocalories")),
            "sleeps" => new DayPart(date,
                SleepMinutes: Minutes(r, "durationInSeconds"),
                DeepSleepMinutes: Minutes(r, "deepSleepDurationInSeconds"),
                LightSleepMinutes: Minutes(r, "lightSleepDurationInSeconds"),
                RemSleepMinutes: Minutes(r, "remSleepInSeconds") ?? Minutes(r, "remSleepDurationInSeconds"),
                AwakeMinutes: Minutes(r, "awakeDurationInSeconds"),
                SleepScore: r.TryGetProperty("overallSleepScore", out var os) && os.ValueKind == JsonValueKind.Object ? Int(os, "value") : Int(r, "sleepScore")),
            "hrv" => new DayPart(date, HrvLastNight: Int(r, "lastNightAvg")),
            _ => null
        };
    }

    /// <summary>
    /// Dzień rekordu: „calendarDate”, a gdy go brak — czas lokalny końca (start + przesunięcie strefy + czas trwania),
    /// bo noc należy do dnia, w którym się budzimy.
    /// </summary>
    public static DateOnly? DateOf(JsonElement r)
    {
        if (Str(r, "calendarDate") is { } ds && DateOnly.TryParse(ds, System.Globalization.CultureInfo.InvariantCulture, out var date)) return date;
        if (Long(r, "startTimeInSeconds") is not long start) return null;
        var local = start + (Long(r, "startTimeOffsetInSeconds") ?? 0) + (Long(r, "durationInSeconds") ?? 0);
        return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(local).UtcDateTime);
    }

    public static Activity? ActivityOf(JsonElement r)
    {
        var id = Str(r, "summaryId") ?? Str(r, "activityId");
        if (id is null || Long(r, "startTimeInSeconds") is not long start) return null;
        var type = Str(r, "activityType") ?? "";
        return new Activity(id, Str(r, "activityName") ?? TypeName(type), type,
            DateTimeOffset.FromUnixTimeSeconds(start).UtcDateTime, Int(r, "durationInSeconds") ?? 0,
            Int(r, "averageHeartRateInBeatsPerMinute"), Int(r, "maxHeartRateInBeatsPerMinute"),
            Int(r, "activeKilocalories"), Int(r, "distanceInMeters"), Str(r, "deviceName"));
    }

    /// <summary>Typ aktywności Garmina po polsku (najczęstsze; reszta — z dużej litery, bez podkreśleń).</summary>
    public static string TypeName(string type) => type.ToUpperInvariant() switch
    {
        "RUNNING" or "TREADMILL_RUNNING" or "TRAIL_RUNNING" => "Bieg",
        "CYCLING" or "INDOOR_CYCLING" or "ROAD_BIKING" or "MOUNTAIN_BIKING" => "Rower",
        "STRENGTH_TRAINING" => "Trening siłowy",
        "WALKING" => "Spacer",
        "HIKING" => "Wędrówka",
        "LAP_SWIMMING" or "OPEN_WATER_SWIMMING" or "SWIMMING" => "Pływanie",
        "YOGA" => "Joga",
        "PILATES" => "Pilates",
        "HIIT" => "HIIT",
        "CARDIO" or "INDOOR_CARDIO" => "Cardio",
        "ELLIPTICAL" => "Orbitrek",
        "INDOOR_ROWING" or "ROWING" => "Wiosłowanie",
        "" => "Aktywność",
        _ => char.ToUpperInvariant(type[0]) + type[1..].ToLowerInvariant().Replace('_', ' ')
    };

    private static string? Str(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() is { Length: > 0 } s ? s : null,
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        } : null;

    private static long? Long(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static int? Int(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d is > int.MinValue and < int.MaxValue
            ? (int)Math.Round(d, MidpointRounding.AwayFromZero) : null;

    private static int? Minutes(JsonElement r, string name) => Int(r, name) is int s ? (int)Math.Round(s / 60.0) : null;
}
