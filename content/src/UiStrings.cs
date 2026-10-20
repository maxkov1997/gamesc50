using System.Globalization;
using System.Text;
using System.Text.Json;
using Game.Core;

namespace Game.Content;

public sealed record KeywordText(string Name, string Text);

/// <summary>
/// Тексты для игрока (content/strings.json): интерфейс, ключевые слова, племена, типы карт, строки лога.
/// Строк в коде нет — точка расширения для локализации (раздел 2).
/// </summary>
public sealed class UiStrings
{
    public IReadOnlyDictionary<string, string> Ui { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, KeywordText> Keywords { get; init; } = new Dictionary<string, KeywordText>();
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> CardTypes { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Log { get; init; } = new Dictionary<string, string>();

    /// <summary>Строка интерфейса с подстановкой {имя}. Нет ключа — видно сразу: [ключ].</summary>
    public string T(string key, params (string Name, object Value)[] args) =>
        Format(Ui.TryGetValue(key, out var s) ? s : $"[{key}]", args);

    public string LogLine(string key, params (string Name, object Value)[] args) =>
        Format(Log.TryGetValue(key, out var s) ? s : $"[{key}]", args);

    public string KeywordName(string id) => Keywords.TryGetValue(id, out var k) ? k.Name : id;

    public string KeywordText(string id, GameConfig config) =>
        Keywords.TryGetValue(id, out var k) ? Format(k.Text, ("dodge_chance", config.DodgeChance)) : id;

    public string TagName(string id) => Tags.TryGetValue(id, out var t) ? t : id;

    public string CardTypeName(CardType type) =>
        CardTypes.TryGetValue(JsonNamingPolicy.SnakeCaseLower.ConvertName(type.ToString()), out var t) ? t : type.ToString();

    public static string Format(string template, params (string Name, object Value)[] args)
    {
        var sb = new StringBuilder(template);
        foreach (var (name, value) in args)
            sb.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
