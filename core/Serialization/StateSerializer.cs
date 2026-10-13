using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Core;

/// <summary>serialize(state) → JSON; deserialize(JSON) → MatchState (12.2). Вывод детерминирован.</summary>
public static class StateSerializer
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return o;
    }

    public static string Serialize(MatchState state) => JsonSerializer.Serialize(state, Options);

    public static MatchState Deserialize(string json) =>
        JsonSerializer.Deserialize<MatchState>(json, Options)
        ?? throw new JsonException("Пустое состояние матча");

    public static string SerializeAction(GameAction action) => JsonSerializer.Serialize(action, Options);

    public static GameAction DeserializeAction(string json) =>
        JsonSerializer.Deserialize<GameAction>(json, Options)
        ?? throw new JsonException("Пустое действие");

    public static string SerializeEvents(IEnumerable<GameEvent> events) =>
        JsonSerializer.Serialize(events, Options);

    public static string SerializeSnapshot(CombatSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    /// <summary>Глубокая копия через JSON — для снимка боя (8.1).</summary>
    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)
        ?? throw new JsonException("Пустая копия");
}
