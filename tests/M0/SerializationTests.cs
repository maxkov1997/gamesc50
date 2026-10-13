using Game.Core;
using Xunit;

namespace Game.Tests.M0;

/// <summary>M0: сериализация состояния, действий и создание матча.</summary>
public class SerializationTests
{
    private static MatchState NewMatch(ulong seed) =>
        Match.CreateMatch(TestData.Data, TestData.Data.GetDeck("pack"), TestData.Data.GetDeck("bones"), seed, PlanningMode.OpponentFirst);

    [Fact]
    public void M0_create_match_sets_up_both_sides()
    {
        var state = NewMatch(1);
        var cfg = TestData.Data.Config;
        foreach (var p in state.Players)
        {
            Assert.Equal(cfg.HeroHp, p.HeroHp);
            Assert.Equal(cfg.StartHand, p.Hand.Count);
            Assert.Equal(15 - cfg.StartHand, p.DrawPile.Count);
            Assert.Equal(cfg.FieldSlots, p.Field.Count);
            Assert.Empty(p.Board);
            Assert.Empty(p.Discard);
            // Каждый экземпляр лежит ровно в одной зоне.
            Assert.Equal(p.Instances.Select(i => i.InstanceId).OrderBy(x => x), p.Hand.Concat(p.DrawPile).OrderBy(x => x));
        }
        // instanceId уникальны на весь матч.
        var ids = state.Players.SelectMany(p => p.Instances).Select(i => i.InstanceId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        Assert.Equal(EventTypes.MatchStarted, state.Log[0].Type);
        Assert.Equal(2 * cfg.StartHand, state.Log.Count(e => e.Type == EventTypes.CardDrawn));
        Assert.DoesNotContain(state.Log, e => e.Type == EventTypes.Warning);
    }

    [Fact]
    public void M0_create_match_is_reproducible_by_seed()
    {
        for (ulong seed = 1; seed <= 50; seed++)
            Assert.Equal(StateSerializer.Serialize(NewMatch(seed)), StateSerializer.Serialize(NewMatch(seed)));

        Assert.NotEqual(StateSerializer.Serialize(NewMatch(1)), StateSerializer.Serialize(NewMatch(2)));
    }

    [Fact]
    public void M0_state_roundtrips_through_json()
    {
        var state = NewMatch(42);
        // Наполним все виды полей, чтобы проверить их сериализацию.
        var a = state.Player(Side.A);
        a.Board.Add(new Creature
        {
            EntityId = 2, InstanceId = a.Hand[0], CardId = "ferret",
            BonusAttack = 2, BonusHealth = 1, GainedKeywords = { "taunt" }, Enchantments = { a.Hand[1] },
        });
        a.Board.Add(new Creature { EntityId = 4, CardId = "tok_spark", UsedAbilities = { 0 } });
        a.Field[1] = a.Hand[2];
        a.BattleZone.Add(a.Hand[3]);
        a.Hand.Clear();
        a.GetInstance(a.Board[0].InstanceId!.Value).UsedAbilities.Add(0);
        a.ManaBonus = 2;
        state.Result = Outcome.Draw;

        string json = StateSerializer.Serialize(state);
        var restored = StateSerializer.Deserialize(json);
        Assert.Equal(json, StateSerializer.Serialize(restored));

        Assert.Null(restored.Player(Side.A).Field[0]);
        Assert.Equal(a.Field[1], restored.Player(Side.A).Field[1]);
        Assert.Null(restored.Player(Side.A).Board[1].InstanceId);
        Assert.Equal(Outcome.Draw, restored.Result);
        Assert.Equal(state.Config.HeroHp, restored.Config.HeroHp);
        Assert.Contains("\"mode\":\"opponent_first\"", json);
    }

    [Fact]
    public void M0_actions_roundtrip_through_json()
    {
        GameAction[] actions =
        {
            new PlayCard(5, Slot: 2, Target: 9),
            new PlayCard(6),
            new UseAbility(3, 0, Target: 4),
            new Move(3, 0),
            new Recall(3),
            new Dismiss(3),
            new Ready(),
        };
        foreach (var action in actions)
        {
            string json = StateSerializer.SerializeAction(action);
            Assert.Equal(action, StateSerializer.DeserializeAction(json));
        }
        Assert.Equal("""{"kind":"play_card","instance_id":5,"slot":2,"target":9}""", StateSerializer.SerializeAction(actions[0]));
    }

    [Fact]
    public void M0_deck_list_extension_points()
    {
        var deck = new DeckList { Id = "d", Name = "Колода", Cards = new List<string>(TestData.Data.GetDeck("pack").Cards) };
        deck.AddCard("ferret");
        Assert.Empty(DeckRules.Validate(deck, TestData.Data.Cards, TestData.Data.Config));
        deck.AddCard("ferret");
        Assert.Contains(DeckRules.Validate(deck, TestData.Data.Cards, TestData.Data.Config), e => e.Code == ErrorCodes.DeckCopies);
        Assert.True(deck.RemoveCard("ferret"));
        Assert.Empty(DeckRules.Validate(deck, TestData.Data.Cards, TestData.Data.Config));
    }
}
