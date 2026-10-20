using System.Text.Json;
using Game.Core;

namespace Game.Content;

/// <summary>
/// Загрузка content/: config.json (раздел 3), cards.json (11.1), decks/*.json (11.7).
/// Любая ошибка данных — GameDataException со списком всех найденных ошибок.
/// </summary>
public static class ContentLoader
{
    public const string ConfigFile = "config.json";
    public const string CardsFile = "cards.json";
    public const string DecksDir = "decks";

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    private static readonly string[] DeckFields = { "id", "name", "cards" };

    public const string StringsFile = "strings.json";

    public static GameData LoadDirectory(string contentDir) => Load(new DirectorySource(contentDir));

    public static GameData Load(IContentSource source)
    {
        var errors = new List<ValidationError>();
        var config = ParseConfig(source.Read(ConfigFile), ConfigFile, errors);
        var cards = ParseCards(source.Read(CardsFile), CardsFile, errors);

        var decks = new List<DeckList>();
        foreach (var file in source.List(DecksDir, ".json"))
        {
            var deck = ParseDeck(source.Read(file), file, cards, config, errors);
            if (deck == null) continue;
            if (decks.Any(d => d.Id == deck.Id))
                errors.Add(new(ErrorCodes.DuplicateId, file, $"колода с id «{deck.Id}» уже есть"));
            else
                decks.Add(deck);
        }

        if (errors.Count > 0) throw new GameDataException(errors);
        return new GameData { Config = config!, Cards = cards!, Decks = decks };
    }

    public static UiStrings LoadStrings(IContentSource source) => LoadStrings(source.Read(StringsFile));

    public static UiStrings LoadStrings(string json) =>
        ThrowIfErrors(errors => ParseStrings(json, StringsFile, errors));

    private static readonly string[] StringSections = { "ui", "keywords", "tags", "card_types", "log" };

    private static UiStrings? ParseStrings(string json, string source, List<ValidationError> errors)
    {
        if (Parse(json, source, errors) is not { } root) return null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new(ErrorCodes.InvalidType, source, "файл строк должен быть объектом"));
            return null;
        }
        var r = new ObjectReader(root, source, errors);
        r.CheckFields(StringSections, StringSections);

        Dictionary<string, string> Section(string name)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!root.TryGetProperty(name, out var el)) { r.Error(ErrorCodes.MissingField, $"нет раздела «{name}»", name); return result; }
            if (el.ValueKind != JsonValueKind.Object) { r.Error(ErrorCodes.InvalidType, $"раздел «{name}» должен быть объектом", name); return result; }
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String) result[prop.Name] = prop.Value.GetString()!;
                else r.Error(ErrorCodes.InvalidType, "ожидалась строка", $"{name}.{prop.Name}");
            }
            return result;
        }

        var keywords = new Dictionary<string, KeywordText>(StringComparer.Ordinal);
        if (r.Object("keywords", true) is { } kr && root.GetProperty("keywords") is var kel)
        {
            foreach (var prop in kel.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    kr.Error(ErrorCodes.InvalidType, "ожидался объект { name, text }", prop.Name);
                    continue;
                }
                var k = new ObjectReader(prop.Value, $"{kr.Path}.{prop.Name}", errors);
                k.CheckFields(new[] { "name", "text" }, new[] { "name", "text" });
                if (Registry.FindKeyword(prop.Name) == null)
                    kr.Error(ErrorCodes.UnknownKeyword, $"неизвестное ключевое слово «{prop.Name}»", prop.Name);
                keywords[prop.Name] = new KeywordText(k.String("name", true) ?? "", k.String("text", true) ?? "");
            }
        }

        var strings = new UiStrings
        {
            Ui = Section("ui"),
            Keywords = keywords,
            Tags = Section("tags"),
            CardTypes = Section("card_types"),
            Log = Section("log"),
        };

        // Тексты нужны для всего, что видит игрок.
        foreach (var k in Registry.KeywordList)
            if (!strings.Keywords.ContainsKey(k.Id))
                errors.Add(new(ErrorCodes.MissingField, $"{source}.keywords", $"нет текста для ключевого слова «{k.Id}»"));
        foreach (var tag in Registry.TagList)
            if (!strings.Tags.ContainsKey(tag))
                errors.Add(new(ErrorCodes.MissingField, $"{source}.tags", $"нет названия племени «{tag}»"));
        foreach (CardType type in Enum.GetValues<CardType>())
        {
            string id = JsonNamingPolicy.SnakeCaseLower.ConvertName(type.ToString());
            if (!strings.CardTypes.ContainsKey(id))
                errors.Add(new(ErrorCodes.MissingField, $"{source}.card_types", $"нет названия типа карты «{id}»"));
        }
        return strings;
    }

    public static GameConfig LoadConfig(string json) =>
        ThrowIfErrors(errors => ParseConfig(json, ConfigFile, errors));

    public static CardDatabase LoadCards(string json) =>
        ThrowIfErrors(errors => ParseCards(json, CardsFile, errors));

    public static DeckList LoadDeck(string json, CardDatabase cards, GameConfig config, string source = "deck.json") =>
        ThrowIfErrors(errors => ParseDeck(json, source, cards, config, errors));

    private static T ThrowIfErrors<T>(Func<List<ValidationError>, T?> parse) where T : class
    {
        var errors = new List<ValidationError>();
        var result = parse(errors);
        if (errors.Count > 0 || result == null) throw new GameDataException(errors);
        return result;
    }

    private static JsonElement? Parse(string json, string source, List<ValidationError> errors)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, DocOptions);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            errors.Add(new(ErrorCodes.InvalidJson, source, $"некорректный JSON: {ex.Message}"));
            return null;
        }
    }

    private static GameConfig? ParseConfig(string json, string source, List<ValidationError> errors)
    {
        if (Parse(json, source, errors) is not { } root) return null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new(ErrorCodes.InvalidType, source, "конфиг должен быть объектом"));
            return null;
        }
        var r = new ObjectReader(root, source, errors);
        var keys = GameConfig.Fields.Select(f => f.Key).ToArray();
        r.CheckFields(keys, keys);
        var config = new GameConfig();
        foreach (var f in GameConfig.Fields)
        {
            if (r.Int(f.Key, true, f.Min) is { } v) f.Set(config, v);
        }
        if (config.DeckMin > config.DeckMax)
            errors.Add(new(ErrorCodes.InvalidValue, source, "DECK_MIN больше DECK_MAX"));
        if (config.DodgeChance > 100)
            errors.Add(new(ErrorCodes.InvalidValue, $"{source}.DODGE_CHANCE", "шанс в процентах не может быть больше 100"));
        return config;
    }

    private static CardDatabase? ParseCards(string json, string source, List<ValidationError> errors)
    {
        if (Parse(json, source, errors) is not { } root) return null;
        var parsed = new CardParser(errors).ParseAll(root, source);
        var unique = new CardValidator(parsed, errors).Validate(source);
        return new CardDatabase(unique);
    }

    private static DeckList? ParseDeck(string json, string source, CardDatabase? cards, GameConfig? config, List<ValidationError> errors)
    {
        if (Parse(json, source, errors) is not { } root) return null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new(ErrorCodes.InvalidType, source, "колода должна быть объектом"));
            return null;
        }
        var r = new ObjectReader(root, source, errors);
        r.CheckFields(DeckFields, DeckFields);
        string? id = r.String("id", true);
        string? name = r.String("name", true);
        var list = r.StringArray("cards", required: true);
        if (id == null || name == null) return null;

        var deck = new DeckList { Id = id, Name = name, Cards = list };
        if (cards != null && config != null)
            errors.AddRange(DeckRules.Validate(deck, cards, config).Select(e => e with { Path = $"{source}: {e.Path}" }));
        return deck;
    }
}
