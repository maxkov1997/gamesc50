namespace Game.Core;

/// <summary>Список карт колоды вне матча (content/decks/*.json).</summary>
public sealed class DeckList
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<string> Cards { get; init; } = new();

    // Точка расширения для будущего драфта (раздел 2): изменение колоды вне матча.
    // Проверка правил колоды — DeckRules.Validate.
    public void AddCard(string cardId) => Cards.Add(cardId);

    public bool RemoveCard(string cardId) => Cards.Remove(cardId);
}

/// <summary>Всё, что загрузчик content отдаёт ядру.</summary>
public sealed class GameData
{
    public required GameConfig Config { get; init; }
    public required CardDatabase Cards { get; init; }
    public IReadOnlyList<DeckList> Decks { get; init; } = Array.Empty<DeckList>();

    public DeckList GetDeck(string id) =>
        Decks.FirstOrDefault(d => d.Id == id) ?? throw new KeyNotFoundException($"Нет колоды с id «{id}»");
}
