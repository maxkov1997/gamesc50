namespace Game.Core;

/// <summary>Проверка колоды (4.2, шаг 1).</summary>
public static class DeckRules
{
    public static List<ValidationError> Validate(DeckList deck, CardDatabase cards, GameConfig config)
    {
        var errors = new List<ValidationError>();
        string path = $"колода «{deck.Id}»";

        int n = deck.Cards.Count;
        if (n < config.DeckMin || n > config.DeckMax)
        {
            errors.Add(new(ErrorCodes.DeckSize, path,
                $"в колоде {n} карт, допустимо от {config.DeckMin} до {config.DeckMax}"));
        }

        // Счётчик копий в порядке первого появления карты — сообщения детерминированы.
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in deck.Cards)
        {
            if (counts.TryGetValue(id, out int c)) counts[id] = c + 1;
            else { counts[id] = 1; order.Add(id); }
        }

        foreach (var id in order)
        {
            if (!cards.TryGet(id, out var card))
            {
                errors.Add(new(ErrorCodes.UnknownCard, path, $"карта «{id}» не существует"));
                continue;
            }
            if (card.Token)
                errors.Add(new(ErrorCodes.DeckToken, path, $"токен «{id}» нельзя класть в колоду"));
            if (counts[id] > config.MaxCopies)
                errors.Add(new(ErrorCodes.DeckCopies, path,
                    $"карта «{id}» — {counts[id]} копий, допустимо не больше {config.MaxCopies}"));
        }
        return errors;
    }
}
