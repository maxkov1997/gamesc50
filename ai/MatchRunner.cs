using Game.Core;

namespace Game.Ai;

/// <summary>Матч целиком и его запись для реплея.</summary>
public sealed record MatchRun(MatchState State, MatchRecord Record);

/// <summary>Матч бот против бота по раундам 4.3.</summary>
public static class MatchRunner
{
    public static MatchRun Play(GameData data, DeckList deckA, DeckList deckB, ulong seed, PlanningMode mode)
    {
        var rules = new GameRules(data);
        var bot = new Bot(data);
        var state = Match.CreateMatch(data, deckA, deckB, seed, mode);
        var record = new MatchRecord
        {
            Seed = seed,
            Mode = mode,
            DeckA = new DeckList { Id = deckA.Id, Name = deckA.Name, Cards = new List<string>(deckA.Cards) },
            DeckB = new DeckList { Id = deckB.Id, Name = deckB.Name, Cards = new List<string>(deckB.Cards) },
        };

        // 5.7: в режиме opponent_first противник (B) готовится первым. Правила от порядка не зависят (5.6).
        var order = mode == PlanningMode.OpponentFirst ? new[] { Side.B, Side.A } : new[] { Side.A, Side.B };
        while (state.Result == null)
        {
            rules.StartRound(state);
            var round = new List<RecordedAction>();
            record.Rounds.Add(round);
            foreach (var side in order)
                foreach (var action in bot.PlayPlanning(rules, state, side))
                    round.Add(new RecordedAction(side, action));

            rules.RunCombat(state);
            if (state.Result != null) break;
            rules.EndRound(state);
        }
        return new MatchRun(state, record);
    }
}
