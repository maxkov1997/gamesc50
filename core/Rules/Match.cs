namespace Game.Core;

/// <summary>Создание матча (4.2).</summary>
public static class Match
{
    /// <summary>createMatch(config, deckA, deckB, seed, mode) → MatchState.</summary>
    /// <exception cref="GameDataException">Колода не прошла проверку 4.2.1.</exception>
    public static MatchState CreateMatch(GameData data, DeckList deckA, DeckList deckB, ulong seed, PlanningMode mode)
    {
        var config = data.Config;

        // 1. Проверка колод.
        var errors = new List<ValidationError>();
        errors.AddRange(DeckRules.Validate(deckA, data.Cards, config).Select(e => e with { Path = $"сторона A, {e.Path}" }));
        errors.AddRange(DeckRules.Validate(deckB, data.Cards, config).Select(e => e with { Path = $"сторона B, {e.Path}" }));
        if (errors.Count > 0) throw new GameDataException(errors);

        var state = new MatchState { Seed = seed, Mode = mode, Config = config };
        var decks = new[] { deckA, deckB };
        foreach (Side side in new[] { Side.A, Side.B })
        {
            var p = new PlayerState { Side = side, HeroHp = config.HeroHp };
            for (int i = 0; i < config.FieldSlots; i++) p.Field.Add(null);
            foreach (var cardId in decks[(int)side].Cards)
            {
                var inst = new CardInstance { InstanceId = state.NextInstanceId++, CardId = cardId };
                p.Instances.Add(inst);
                p.DrawPile.Add(inst.InstanceId);
            }
            state.Players.Add(p);
            state.PlanningRng.Add(RngStreams.PlanningStream(seed, side));
        }

        state.Log.Add(new GameEvent
        {
            Type = EventTypes.MatchStarted,
            Seed = seed,
            Mode = mode,
            Decks = new List<List<string>> { new(deckA.Cards), new(deckB.Cards) },
        });

        // 2. Тасовка колод — в потоке planning[side].
        foreach (var p in state.Players) state.Rng(p.Side).Shuffle(p.DrawPile);

        // 3. Стартовая рука.
        foreach (var p in state.Players) Zones.Draw(state, p.Side, config.StartHand);

        return state;
    }
}
