using Game.Ai;
using Game.Core;
using Xunit;

namespace Game.Tests.M4;

/// <summary>Бот 12.4 и T-17(б).</summary>
public class BotTests
{
    private static Bot NewBot(Sandbox s) => new(s.Data);

    // ---------- T-17(б) · Детерминизм матча и реплей ----------

    [Fact]
    public void T17b_bot_match_is_deterministic_and_replays()
    {
        var data = TestData.Data;
        foreach (ulong seed in new ulong[] { 1, 2, 3, 42, 2026 })
        {
            var first = MatchRunner.Play(data, data.GetDeck("pack"), data.GetDeck("bones"), seed, PlanningMode.OpponentFirst);
            var second = MatchRunner.Play(data, data.GetDeck("pack"), data.GetDeck("bones"), seed, PlanningMode.OpponentFirst);
            string log1 = StateSerializer.SerializeEvents(first.State.Log);
            Assert.Equal(log1, StateSerializer.SerializeEvents(second.State.Log));
            Assert.NotNull(first.State.Result);

            // Запись матча проходит через JSON и воспроизводит состояние побайтово, включая лог.
            string recordJson = StateSerializer.SerializeRecord(first.Record);
            var replayed = Replay.Run(data, StateSerializer.DeserializeRecord(recordJson));
            Assert.Equal(StateSerializer.Serialize(first.State), StateSerializer.Serialize(replayed));
            Assert.DoesNotContain(first.State.Log, e => e.Type == EventTypes.Warning);
        }
    }

    // ---------- Оценки и выбор ----------

    [Fact]
    public void M4_prefers_creature_and_places_it_rightmost()
    {
        var s = new Sandbox().Planning(mana: 3);
        s.Put("ferret");
        s.AddToHand("blessing_of_might");
        int wolf = s.AddToHand("wolf");
        // Существо (100 + 20) лучше заклинания (50 + 1); встаёт в правый край.
        Assert.Equal(new PlayCard(wolf, Slot: 1), NewBot(s).ChooseAction(s.State, Side.A));
    }

    [Fact]
    public void M4_more_expensive_creature_first_and_ties_follow_legal_order()
    {
        var s = new Sandbox().Planning(mana: 10);
        int ferret1 = s.AddToHand("ferret");
        s.AddToHand("ferret");
        int harpy = s.AddToHand("harpy");
        var bot = NewBot(s);
        Assert.Equal(new PlayCard(harpy, Slot: 0), bot.ChooseAction(s.State, Side.A));
        s.Apply(new PlayCard(harpy, Slot: 0));
        // Две одинаковые оценки — раньше в legalActions (рука слева направо).
        Assert.Equal(new PlayCard(ferret1, Slot: 1), bot.ChooseAction(s.State, Side.A));
    }

    [Fact]
    public void M4_score_table_order()
    {
        // battle_spell/secret (60+) > чары на поле (55+) > прочие заклинания (50+) > draw/mana (40) > способность (30+).
        var s = new Sandbox().Planning(mana: 11);
        var tinkerer = s.Put("tinkerer");
        s.Put("scavenger");
        int hoard = s.AddToHand("hoard");
        int rally = s.AddToHand("rally");
        int forge = s.AddToHand("forge");
        int snare = s.AddToHand("snare");
        var bot = NewBot(s);

        var played = new List<GameAction>();
        for (int i = 0; i < 5; i++)
        {
            var a = bot.ChooseAction(s.State, Side.A);
            played.Add(a);
            s.Apply(a);
        }
        Assert.Equal(new GameAction[]
        {
            new PlayCard(snare),
            new PlayCard(forge, Slot: 0),
            new PlayCard(rally),
            new PlayCard(hoard),
            new UseAbility(tinkerer.EntityId, 0, Target: s.Find("scavenger").EntityId),
        }, played);
    }

    [Fact]
    public void M4_field_enchantment_only_into_free_slot()
    {
        var s = new Sandbox().Planning(mana: 10);
        for (int i = 0; i < s.A.Field.Count; i++) s.PutField("forge", i);
        s.AddToHand("sanctuary");
        // Свободных слотов нет — чары на поле бот не рассматривает, остаётся «Готов».
        Assert.Equal(new Ready(), NewBot(s).ChooseAction(s.State, Side.A));
    }

    [Fact]
    public void M4_targets_buffs_highest_attack_and_consume_lowest_cost()
    {
        var s = new Sandbox().Planning(mana: 10);
        s.Put("ferret");          // 2/1, стоимость 1
        var harpy = s.Put("harpy"); // 3/2, стоимость 3
        var guard = s.Put("gate_guard"); // 1/3, стоимость 1
        var bot = NewBot(s);

        int blessing = s.AddToHand("blessing_of_might");
        Assert.Equal(new PlayCard(blessing, Target: harpy.EntityId), bot.ChooseAction(s.State, Side.A));
        s.A.Hand.Clear();

        // Поглощение — самый дешёвый союзник; при равенстве — левый (Хорёк левее Стража).
        int devourer = s.AddToHand("devourer");
        var choice = (PlayCard)bot.ChooseAction(s.State, Side.A);
        Assert.Equal(devourer, choice.InstanceId);
        Assert.Equal(s.A.Board[0].EntityId, choice.Target);
        Assert.NotEqual(guard.EntityId, choice.Target);
    }

    [Fact]
    public void M4_dismisses_cheapest_when_board_full()
    {
        var s = new Sandbox().Planning(mana: 10);
        s.Put("wolf");
        var spark = s.Put("tok_spark");
        for (int i = 0; i < 5; i++) s.Put("wolf");
        s.AddToHand("harpy"); // 3 ≥ 0 + 2
        Assert.Equal(new Dismiss(spark.EntityId), NewBot(s).ChooseAction(s.State, Side.A));
    }

    [Fact]
    public void M4_does_not_dismiss_without_expensive_enough_creature()
    {
        var s = new Sandbox().Planning(mana: 10);
        for (int i = 0; i < 7; i++) s.Put("wolf"); // все стоят 2
        s.AddToHand("harpy"); // 3 < 2 + 2
        var action = NewBot(s).ChooseAction(s.State, Side.A);
        Assert.IsNotType<Dismiss>(action);
    }

    [Fact]
    public void M4_arranges_board_then_ready()
    {
        var s = new Sandbox().Planning(mana: 0);
        var guard = s.Put("gate_guard");     // Провокация → 2
        var ferret = s.Put("ferret");        // остальные → 1
        var spirit = s.Put("candle_spirit"); // «Посмертно» → 0
        var wolf = s.Put("wolf");            // 1
        var spark = s.Put("tok_spark");      // токен → 0

        var actions = NewBot(s).PlayPlanning(s.Rules, s.State, Side.A);

        Assert.Equal(new[] { spirit.EntityId, spark.EntityId, ferret.EntityId, wolf.EntityId, guard.EntityId },
            s.A.Board.Select(c => c.EntityId));
        Assert.All(actions.Take(actions.Count - 1), a => Assert.IsType<Move>(a));
        Assert.Equal(new Ready(), actions[^1]);
        Assert.True(s.A.IsReady);
    }

    [Fact]
    public void M4_bot_plays_whole_turn_within_rules()
    {
        var s = new Sandbox();
        foreach (var id in s.Data.GetDeck("mechs").Cards.Take(8)) s.AddToHand(id);
        s.Planning(round: 6, mana: 6);
        var actions = NewBot(s).PlayPlanning(s.Rules, s.State, Side.A);
        Assert.Equal(new Ready(), actions[^1]);
        Assert.True(s.A.Mana >= 0);
        Assert.NotEmpty(s.A.Board);
    }
}
