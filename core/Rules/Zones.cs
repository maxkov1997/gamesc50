namespace Game.Core;

/// <summary>Перемещения карт между зонами (5.2).</summary>
public static class Zones
{
    /// <summary>
    /// Взять count карт. Пустая колода — прокрутка сброса; пусты обе — карта не берётся.
    /// Полная рука — взятая карта сразу в сброс. Возвращает новые события (они же в state.Log).
    /// </summary>
    public static List<GameEvent> Draw(MatchState state, Side side, int count)
    {
        var p = state.Player(side);
        var events = new List<GameEvent>();
        for (int i = 0; i < count; i++)
        {
            if (p.DrawPile.Count == 0)
            {
                if (p.Discard.Count == 0) break;
                Reshuffle(state, side, events);
            }

            int top = p.DrawPile[^1];
            p.DrawPile.RemoveAt(p.DrawPile.Count - 1);
            if (p.Hand.Count >= state.Config.HandMax)
            {
                p.Discard.Add(top);
                events.Add(new GameEvent { Type = EventTypes.CardBurned, Side = side, InstanceId = top });
            }
            else
            {
                p.Hand.Add(top);
                events.Add(new GameEvent { Type = EventTypes.CardDrawn, Side = side, InstanceId = top });
            }
        }
        state.Log.AddRange(events);
        return events;
    }

    private static void Reshuffle(MatchState state, Side side, List<GameEvent> events)
    {
        var p = state.Player(side);
        p.DrawPile.AddRange(p.Discard);
        p.Discard.Clear();
        state.Rng(side).Shuffle(p.DrawPile);
        // Точка расширения (раздел 2): к deck_reshuffled позже привяжется повышение уровня.
        events.Add(new GameEvent { Type = EventTypes.DeckReshuffled, Side = side, Count = p.DrawPile.Count });
    }
}
