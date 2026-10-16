using Game.Core;
using Xunit;

namespace Game.Tests.M1;

/// <summary>Тест-кейсы этапа M1 (раздел 13): T-18 … T-23, T-26, T-27(б), T-29(б), T-30, T-33.</summary>
public class PlanningTests
{
    private static void AssertNoWarnings(MatchState state) =>
        Assert.DoesNotContain(state.Log, e => e.Type == EventTypes.Warning);

    /// <summary>Прогнать раунды без боя: начало → конец.</summary>
    private static void SkipRounds(Sandbox s, int count)
    {
        for (int i = 0; i < count; i++)
        {
            s.Rules.StartRound(s.State);
            s.Rules.EndRound(s.State);
        }
    }

    // ---------- T-18 · Мана ----------

    [Fact]
    public void T18_mana()
    {
        var s = new Sandbox();
        s.Rules.StartRound(s.State);
        Assert.Equal((1, 1), (s.A.Mana, s.A.ManaMax));
        Assert.Equal(1, s.P(Side.B).Mana);
        s.Rules.EndRound(s.State);
        // Несыгранная мана не переносится.
        Assert.Equal(0, s.A.Mana);

        SkipRounds(s, 3);
        s.Rules.StartRound(s.State);
        Assert.Equal(5, s.State.Round);
        Assert.Equal(5, s.A.Mana);
        s.Rules.EndRound(s.State);

        SkipRounds(s, 6);
        s.Rules.StartRound(s.State);
        Assert.Equal(12, s.State.Round);
        Assert.Equal((10, 10), (s.A.Mana, s.A.ManaMax));
        AssertNoWarnings(s.State);
    }

    [Fact]
    [Covers("hoard")]
    public void T18_hoard_gives_mana_next_round_only()
    {
        var s = new Sandbox();
        SkipRounds(s, 2);
        s.Rules.StartRound(s.State); // раунд 3
        int hoard = s.AddToHand("hoard");
        s.Apply(new PlayCard(hoard));
        Assert.Equal(2, s.A.Mana);
        Assert.Equal(new[] { hoard }, s.A.Discard);
        s.Rules.EndRound(s.State);

        s.Rules.StartRound(s.State); // раунд 4
        Assert.Equal((6, 4), (s.A.Mana, s.A.ManaMax));
        Assert.Equal(4, s.P(Side.B).Mana);
        s.Rules.EndRound(s.State);

        s.Rules.StartRound(s.State); // раунд 5
        Assert.Equal(5, s.A.Mana);
    }

    // ---------- T-19 · Добор и прокрутка ----------

    [Fact]
    public void T19_draw_and_reshuffle()
    {
        var s = new Sandbox();
        for (int i = 0; i < 2; i++) s.AddToDrawPile("ferret");
        for (int i = 0; i < 3; i++) s.AddToDiscard("wolf");
        for (int i = 0; i < 4; i++) s.AddToHand("harpy");

        var events = Zones.Draw(s.State, Side.A, 3);

        Assert.Equal(new[] { EventTypes.CardDrawn, EventTypes.CardDrawn, EventTypes.DeckReshuffled, EventTypes.CardDrawn },
            events.Select(e => e.Type));
        Assert.Equal(3, events.Single(e => e.Type == EventTypes.DeckReshuffled).Count);
        Assert.Equal((7, 2, 0), (s.A.Hand.Count, s.A.DrawPile.Count, s.A.Discard.Count));
        // Две первые карты — из старой колоды, третья — из прокрученного сброса.
        Assert.Equal(new[] { "ferret", "ferret", "wolf" }, s.A.Hand.Skip(4).Select(id => s.CardOf(id)));
    }

    [Fact]
    public void T19_draw_from_empty_deck_and_discard_does_nothing()
    {
        var s = new Sandbox();
        s.AddToHand("harpy");
        var events = Zones.Draw(s.State, Side.A, 3);
        Assert.Empty(events);
        Assert.Single(s.A.Hand);
    }

    // ---------- T-20 · Переполнение руки ----------

    [Fact]
    public void T20_hand_overflow_burns_card()
    {
        var s = new Sandbox();
        for (int i = 0; i < 10; i++) s.AddToHand("harpy");
        int top = s.AddToDrawPile("ferret");

        var events = Zones.Draw(s.State, Side.A, 1);

        var e = Assert.Single(events);
        Assert.Equal((EventTypes.CardBurned, top), (e.Type, e.InstanceId));
        Assert.Equal(10, s.A.Hand.Count);
        Assert.Equal(new[] { top }, s.A.Discard);
    }

    // ---------- T-21 · Отзыв ----------

    [Fact]
    [Covers("apprentice", "blessing_of_might", "seal_of_rebirth")]
    public void T21_recall()
    {
        var s = new Sandbox().Planning();
        var ally = s.Put("ferret");
        var app = s.Put("apprentice");
        int appInstance = app.InstanceId!.Value;

        int blessing = s.AddToHand("blessing_of_might");
        s.Apply(new PlayCard(blessing, Target: app.EntityId));
        int seal = s.AddToHand("seal_of_rebirth");
        s.Apply(new PlayCard(seal, Target: app.EntityId));
        Assert.Equal((3, 3), s.AH(app));
        Assert.Equal(new[] { seal }, app.Enchantments);

        int manaBefore = s.A.Mana;
        s.Apply(new Recall(app.EntityId));

        Assert.Equal(manaBefore, s.A.Mana);
        Assert.Equal(new[] { "ferret" }, s.BoardIds());
        Assert.Contains(appInstance, s.A.Hand);
        Assert.Contains(seal, s.A.Discard);
        // В руке — исходная карта: базовые 1/2 без усилений.
        var def = s.Data.Cards.Get(s.CardOf(appInstance));
        Assert.Equal((1, 2), (def.Attack, def.Health));

        // Повторный розыгрыш: стоит 1, новая сущность, снова +1/+1 союзнику.
        int mana = s.A.Mana;
        var allyBefore = s.AH(ally);
        s.Apply(new PlayCard(appInstance, Slot: 1, Target: ally.EntityId));
        var replayed = s.P(Side.A).Board[1];
        Assert.Equal(mana - 1, s.A.Mana);
        Assert.NotEqual(app.EntityId, replayed.EntityId);
        Assert.Equal(appInstance, replayed.InstanceId);
        Assert.Equal((1, 2), s.AH(replayed));
        Assert.Equal((allyBefore.Item1 + 1, allyBefore.Item2 + 1), s.AH(ally));
        AssertNoWarnings(s.State);
    }

    [Fact]
    public void T21_no_recall_with_full_hand_or_for_token()
    {
        var s = new Sandbox().Planning();
        var ferret = s.Put("ferret");
        var spark = s.Put("tok_spark");
        Assert.Contains(new Recall(ferret.EntityId), s.Legal());
        Assert.DoesNotContain(new Recall(spark.EntityId), s.Legal());

        for (int i = 0; i < 10; i++) s.AddToHand("harpy");
        Assert.DoesNotContain(s.Legal(), a => a is Recall);
        Assert.Throws<InvalidActionException>(() => s.Apply(new Recall(ferret.EntityId)));
    }

    // ---------- T-22 · Цели в подготовке ----------

    [Fact]
    public void T22_blessing_targets_only_own_creatures()
    {
        var s = new Sandbox().Planning();
        int blessing = s.AddToHand("blessing_of_might");
        s.Put("ferret", Side.B);
        Assert.DoesNotContain(s.Legal(), a => a is PlayCard pc && pc.InstanceId == blessing);

        var mine = s.Put("ferret");
        var plays = s.Legal().OfType<PlayCard>().Where(pc => pc.InstanceId == blessing).ToList();
        Assert.Equal(new[] { new PlayCard(blessing, Target: mine.EntityId) }, plays);

        var enemy = s.P(Side.B).Board[0];
        Assert.Throws<InvalidActionException>(() => s.Apply(new PlayCard(blessing, Target: enemy.EntityId)));
    }

    [Fact]
    public void T22_apprentice_on_empty_board_skips_effect()
    {
        var s = new Sandbox().Planning();
        int app = s.AddToHand("apprentice");
        Assert.Contains(new PlayCard(app, Slot: 0), s.Legal());

        s.Apply(new PlayCard(app, Slot: 0));

        var c = Assert.Single(s.A.Board);
        Assert.Equal((1, 2), s.AH(c));
        Assert.DoesNotContain(s.State.Log, e => e.Type == EventTypes.StatsChanged);
        AssertNoWarnings(s.State);
    }

    // ---------- T-23 · Изоляция ----------

    [Fact]
    public void T23_isolation()
    {
        var gen = new Pcg32(2026, 23);
        string? referenceB = null;
        for (int run = 0; run < 1000; run++)
        {
            var s = new Sandbox(seed: 5);
            s.State.Player(Side.A).Instances.Clear();
            // Насыщенная подготовка A: полная рука из колоды «Механизмы» и «Кости», мана 10, существа на столе.
            foreach (var id in s.Data.GetDeck("mechs").Cards.Take(5).Concat(s.Data.GetDeck("bones").Cards.Take(5)))
                s.AddToHand(id);
            s.AddToDrawPile("insight");
            s.AddToDiscard("ferret");
            s.Put("tinkerer");
            s.Put("candle_spirit");
            s.AddToHand("ferret", Side.B);
            s.Put("mourner", Side.B);
            s.Planning(round: 5);
            s.PutField("forge", 0);

            string HashB() => StateSerializer.Serialize(new MatchState { Players = { s.P(Side.B) }, PlanningRng = { s.State.Rng(Side.B) } });
            string before = HashB();
            referenceB ??= before;
            Assert.Equal(referenceB, before);

            int steps = 1 + gen.Int(30);
            for (int i = 0; i < steps; i++)
            {
                var legal = s.Legal(Side.A);
                if (legal.Count == 0) break;
                s.Apply(legal[gen.Int(legal.Count)], Side.A);
                Assert.Equal(before, HashB());
            }
            s.Rules.EndRound(s.State);
            AssertNoWarnings(s.State);
        }
    }

    // ---------- T-26 · Поглощение в подготовке ----------

    [Fact]
    [Covers("devourer")]
    public void T26_devourer_consumes_in_planning()
    {
        var s = new Sandbox().Planning();
        var spirit = s.Put("candle_spirit");
        int devourer = s.AddToHand("devourer");

        s.Apply(new PlayCard(devourer, Slot: 1, Target: spirit.EntityId));

        Assert.Equal(new[] { "tok_spark", "devourer" }, s.BoardIds());
        Assert.Equal((5, 5), s.AH(s.Find("devourer")));
        Assert.Equal((1, 1), (s.Find("devourer").BonusAttack, s.Find("devourer").BonusHealth));
        Assert.Contains(spirit.InstanceId!.Value, s.A.Discard);
        AssertNoWarnings(s.State);
    }

    // ---------- T-27(б) · Кузница ----------

    [Fact]
    [Covers("forge")]
    public void T27b_forge_buffs_mech_at_round_end()
    {
        var s = new Sandbox().Planning();
        s.PutField("forge", 0);
        var tinkerer = s.Put("tinkerer");
        s.Put("ferret");

        s.Rules.EndRound(s.State);

        Assert.Equal((3, 3), s.AH(tinkerer));
        Assert.Equal((2, 1), s.AH(s.Find("ferret")));
        // Навсегда: остаётся и в следующем раунде.
        s.Rules.StartRound(s.State);
        Assert.Equal((3, 3), s.AH(tinkerer));
        AssertNoWarnings(s.State);
    }

    // ---------- T-29(б) · Когда умирает союзник (подготовка) ----------

    [Fact]
    [Covers("mourner")]
    public void T29b_mourner_in_planning()
    {
        var s = new Sandbox().Planning();
        var spirit = s.Put("candle_spirit");
        var mourner = s.Put("mourner");
        int devourer = s.AddToHand("devourer");

        s.Apply(new PlayCard(devourer, Slot: 2, Target: spirit.EntityId));

        Assert.Equal(new[] { "tok_spark", "mourner", "devourer" }, s.BoardIds());
        Assert.Equal((5, 5), s.AH(s.Find("devourer")));
        Assert.Equal((2, 4), s.AH(mourner));
        Assert.Equal((1, 1), (mourner.BonusAttack, mourner.BonusHealth));
        AssertNoWarnings(s.State);
    }

    // ---------- T-30 · В начале раунда ----------

    [Fact]
    [Covers("wind_up_soldier")]
    public void T30_wind_up_soldier_grows_each_round()
    {
        var s = new Sandbox();
        s.Rules.StartRound(s.State);
        var soldier = s.Put("wind_up_soldier");
        s.Rules.EndRound(s.State);

        s.Rules.StartRound(s.State);
        Assert.Equal((3, 3), s.AH(soldier));
        s.Rules.EndRound(s.State);
    }

    [Fact]
    public void T30_soldier_played_in_round_n_grows_from_n_plus_1()
    {
        var s = new Sandbox();
        SkipRounds(s, 2);
        s.Rules.StartRound(s.State); // раунд 3
        int card = s.AddToHand("wind_up_soldier");
        s.Apply(new PlayCard(card, Slot: 0));
        var soldier = s.A.Board[0];
        Assert.Equal((2, 2), s.AH(soldier));
        s.Rules.EndRound(s.State);

        s.Rules.StartRound(s.State); // раунд 4
        Assert.Equal((3, 3), s.AH(soldier));
        AssertNoWarnings(s.State);
    }

    // ---------- T-33 · Отзыв и «Убрать» — не смерть ----------

    [Fact]
    public void T33a_dismiss_is_not_death()
    {
        var s = new Sandbox().Planning();
        var spirit = s.Put("candle_spirit");
        var mourner = s.Put("mourner");

        s.Apply(new Dismiss(spirit.EntityId));

        Assert.Equal(new[] { "mourner" }, s.BoardIds());
        Assert.Equal((1, 3), s.AH(mourner));
        Assert.Contains(spirit.InstanceId!.Value, s.A.Discard);
        Assert.DoesNotContain(s.State.Log, e => e.Type == EventTypes.Died);
    }

    [Fact]
    [Covers("tinkerer")]
    public void T33b_ability_once_per_round_even_after_recall()
    {
        var s = new Sandbox().Planning();
        var tinkerer = s.Put("tinkerer");
        var scavenger = s.Put("scavenger");
        int tinkInstance = tinkerer.InstanceId!.Value;

        s.Apply(new UseAbility(tinkerer.EntityId, 0, Target: scavenger.EntityId));
        Assert.Equal((4, 4), s.AH(scavenger));
        Assert.DoesNotContain(s.Legal(), a => a is UseAbility);

        s.Apply(new Recall(tinkerer.EntityId));
        s.Apply(new PlayCard(tinkInstance, Slot: 0));
        var again = s.A.Board[0];
        Assert.Equal("tinkerer", again.CardId);
        Assert.DoesNotContain(s.Legal(), a => a is UseAbility);

        s.Rules.EndRound(s.State);
        s.Rules.StartRound(s.State);
        s.A.Mana = 10;
        Assert.Contains(new UseAbility(again.EntityId, 0, Target: scavenger.EntityId), s.Legal());
    }
}
