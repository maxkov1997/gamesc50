namespace Game.Core;

/// <summary>Перемещения карт между зонами (5.2).</summary>
public static class Zones
{
    /// <summary>Взять count карт; события пишутся в state.Log и возвращаются.</summary>
    public static List<GameEvent> Draw(MatchState state, Side side, int count)
    {
        var events = new List<GameEvent>();
        Draw(state.Player(side), state.Rng(side), state.Config, count, events);
        state.Log.AddRange(events);
        return events;
    }

    /// <summary>
    /// Пустая колода — прокрутка сброса; пусты обе — карта не берётся, без штрафа.
    /// Полная рука — взятая карта сразу в сброс. Верх колоды — последний элемент.
    /// </summary>
    public static void Draw(PlayerState p, IRandom rng, GameConfig config, int count, List<GameEvent> events)
    {
        for (int i = 0; i < count; i++)
        {
            if (p.DrawPile.Count == 0)
            {
                if (p.Discard.Count == 0) break;
                Reshuffle(p, rng, events);
            }

            int top = p.DrawPile[^1];
            p.DrawPile.RemoveAt(p.DrawPile.Count - 1);
            if (p.Hand.Count >= config.HandMax)
            {
                p.Discard.Add(top);
                events.Add(new GameEvent { Type = EventTypes.CardBurned, Side = p.Side, InstanceId = top });
            }
            else
            {
                p.Hand.Add(top);
                events.Add(new GameEvent { Type = EventTypes.CardDrawn, Side = p.Side, InstanceId = top });
            }
        }
    }

    private static void Reshuffle(PlayerState p, IRandom rng, List<GameEvent> events)
    {
        p.DrawPile.AddRange(p.Discard);
        p.Discard.Clear();
        rng.Shuffle(p.DrawPile);
        // Точка расширения (раздел 2): к deck_reshuffled позже привяжется повышение уровня.
        events.Add(new GameEvent { Type = EventTypes.DeckReshuffled, Side = p.Side, Count = p.DrawPile.Count });
    }

    /// <summary>Разыгранное заклинание: в сброс, с «Разовой» — в изгнание (5.2).</summary>
    public static void SpendSpell(CardDatabase db, PlayerState p, int instanceId)
    {
        if (db.Get(p.GetInstance(instanceId).CardId).Keywords.Contains(Registry.Keywords.Exhaust)) p.Exile.Add(instanceId);
        else p.Discard.Add(instanceId);
    }

    /// <summary>Существо ушло со стола не в руку: карта в сброс (токен исчезает), чары с него — в сброс.</summary>
    public static void DiscardCreature(PlayerState p, Creature c)
    {
        if (c.InstanceId is int id) p.Discard.Add(id);
        p.Discard.AddRange(c.Enchantments);
        c.Enchantments.Clear();
    }
}
