using Game.Core;
using Xunit;
using static Game.Tests.CombatTestKit;

namespace Game.Tests.M2;

/// <summary>Тест-кейсы этапа M2 (раздел 13): T-01 … T-10, T-13, T-14 (без секрета), T-15, T-16, T-17(а).</summary>
public class CombatTests
{
    // ---------- T-01 · Одновременный урон ----------

    [Fact]
    public void T01_simultaneous_damage()
    {
        var s = new Sandbox();
        s.Put("ferret");
        s.Put("gate_guard", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            AssertWinner(r, Outcome.B, 2);
            Assert.Equal(CombatReasons.Wipe, r.Reason);
            AssertNoWarnings(r);
        }
    }

    // ---------- T-02 · Щит ----------

    [Fact]
    public void T02_shield()
    {
        var s = new Sandbox();
        var bearer = s.Put("shieldbearer");
        s.Put("ferret", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            AssertWinner(r, Outcome.A, 3);
            Assert.Single(r.Of(EventTypes.ShieldBroken));
            AssertNoWarnings(r);
        }

        // После боя у Щитоносца на постоянном столе снова есть Щит.
        s.State.Stage = RoundStage.Planning;
        foreach (var p in s.State.Players) p.IsReady = true;
        s.Rules.RunCombat(s.State);
        Assert.Contains("shield", Stats.Keywords(s.Data.Cards, s.A, bearer));
        Assert.Equal(s.Data.Config.HeroHp - 3, s.P(Side.B).HeroHp);
    }

    // ---------- T-03 · Яд против Щита ----------

    [Fact]
    public void T03_poison_vs_shield()
    {
        var s = new Sandbox();
        s.Put("venom_toad");
        s.Put("shieldbearer", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            AssertWinner(r, Outcome.B, 3);
            Assert.Single(r.Of(EventTypes.ShieldBroken));
            // Урон Жабы поглощён — Яд не сработал, Щитоносец не погиб.
            var died = Assert.Single(r.Of(EventTypes.Died));
            Assert.Equal("venom_toad", died.CardId);
        }
    }

    // ---------- T-04 · Яд убивает ----------

    [Fact]
    public void T04_poison_kills()
    {
        var s = new Sandbox();
        s.Put("venom_toad");
        s.Put("iron_boar", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            AssertWinner(r, Outcome.Draw, 0);
            Assert.Equal(2, r.Of(EventTypes.Died).Count());
            Assert.Empty(r.Of(EventTypes.HeroDamaged));
        }
    }

    // ---------- T-05 · Провокация ----------

    [Fact]
    public void T05_taunt()
    {
        var s = new Sandbox();
        for (int i = 0; i < 3; i++) s.Put("ferret");
        var spirit = s.Put("candle_spirit", Side.B);
        var guard = s.Put("gate_guard", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var start = r.Of(EventTypes.CombatStarted).Single();
            Assert.Equal(Side.A, start.Side);

            // Пока Страж жив, все атаки A — в него.
            var guardDied = r.Events.FindIndex(e => e.Type == EventTypes.Died && e.EntityId == guard.EntityId);
            foreach (var e in r.Events.Take(guardDied).Where(e => e.Type == EventTypes.AttackDeclared && e.Side == Side.A))
                Assert.Equal(guard.EntityId, e.Target);

            // Дух при гибели оставляет Искорку на своём месте.
            var spiritDied = r.Events.Single(e => e.Type == EventTypes.Died && e.EntityId == spirit.EntityId);
            var spark = r.Of(EventTypes.CreatureSummoned).Single();
            Assert.Equal(("tok_spark", spiritDied.Position), (spark.CardId, spark.Position));

            AssertWinner(r, Outcome.B, 2);
        }
    }

    // ---------- T-06 · Двойная атака ----------

    [Fact]
    public void T06_double_attack()
    {
        var s = new Sandbox();
        var blade = s.Put("whirlblade");
        s.Put("tok_skeleton");
        s.Put("tok_skeleton");
        s.Put("tok_skeleton", Side.B);
        s.Put("tok_skeleton", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var attacks = Attacks(r);
            Assert.Equal(2, attacks.Count);
            Assert.All(attacks, a => Assert.Equal(blade.EntityId, a.Attacker));
            AssertWinner(r, Outcome.A, 6);
        }
    }

    // ---------- T-07 · Рассечение ----------

    [Fact]
    public void T07_cleave()
    {
        var s = new Sandbox();
        var axeman = s.Put("axeman");
        var guardA = s.Put("gate_guard");
        s.Put("tok_skeleton");
        s.Put("tok_skeleton");
        var skelB1 = s.Put("tok_skeleton", Side.B);
        var guardB = s.Put("gate_guard", Side.B);
        var skelB2 = s.Put("tok_skeleton", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var attacks = Attacks(r);
            Assert.Equal((axeman.EntityId, guardB.EntityId), attacks[0]);
            // Урон получают три существа B.
            int firstAttack = r.Events.FindIndex(e => e.Type == EventTypes.AttackDeclared);
            int secondAttack = r.Events.FindIndex(firstAttack + 1, e => e.Type == EventTypes.AttackDeclared);
            var hits = r.Events.Skip(firstAttack).Take(secondAttack - firstAttack).Where(e => e.Type == EventTypes.Damage).ToList();
            Assert.Equal(new[] { guardB.EntityId, skelB1.EntityId, skelB2.EntityId, axeman.EntityId }, hits.Select(e => e.Target!.Value));
            // Оба Скелета B гибнут; Страж B атакует Стража A и погибает.
            Assert.Equal(new[] { skelB1.EntityId, skelB2.EntityId },
                r.Events.Take(secondAttack).Where(e => e.Type == EventTypes.Died).Select(e => e.EntityId!.Value));
            Assert.Equal((guardB.EntityId, guardA.EntityId), attacks[1]);
            Assert.Equal(2, attacks.Count);
            AssertWinner(r, Outcome.A, 7);
        }
    }

    // ---------- T-08 · Позиция призыва и лимит стола ----------

    [Fact]
    public void T08a_summon_at_death_position()
    {
        var s = new Sandbox();
        var left = s.Put("tok_skeleton");
        var keeper = s.Put("gravekeeper");
        var right = s.Put("tok_skeleton");
        s.Put("stone_wall", Side.B);

        var engine = new CombatEngine(s.Data.Cards, Combat.Snapshot(s.State), new Pcg32(1, 1));
        engine.Destroy(keeper.EntityId);

        var board = engine.Side(Side.A).Board;
        Assert.Equal(4, board.Count);
        Assert.All(board, c => Assert.Equal("tok_skeleton", c.CardId));
        Assert.Equal(left.EntityId, board[0].EntityId);
        Assert.Equal(right.EntityId, board[3].EntityId);
        Assert.True(board[1].EntityId < 0 && board[2].EntityId < 0, "новые сущности боя — из отдельного диапазона");
    }

    [Fact]
    public void T08b_summon_respects_board_limit()
    {
        var s = new Sandbox();
        for (int i = 0; i < 3; i++) s.Put("tok_skeleton");
        var keeper = s.Put("gravekeeper");
        for (int i = 0; i < 3; i++) s.Put("tok_skeleton");
        s.Put("stone_wall", Side.B);
        var old = s.A.Board.Select(c => c.EntityId).ToList();

        var engine = new CombatEngine(s.Data.Cards, Combat.Snapshot(s.State), new Pcg32(1, 1));
        engine.Destroy(keeper.EntityId);

        var board = engine.Side(Side.A).Board;
        Assert.Equal(7, board.Count);
        Assert.DoesNotContain(old[3], board.Select(c => c.EntityId));
        Assert.DoesNotContain(board[3].EntityId, old);
        Assert.Single(engine.Events, e => e.Type == EventTypes.SummonFailed);
    }

    // ---------- T-09 · Перерождение ----------

    [Fact]
    public void T09_reborn()
    {
        var s = new Sandbox();
        var servant = s.Put("bone_servant");
        s.Put("gate_guard", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var reborn = Assert.Single(r.Of(EventTypes.Reborn));
            Assert.Equal(servant.EntityId, reborn.SourceEntityId);
            Assert.Equal((2, 1), (reborn.Creature!.Attack, reborn.Creature.Health));
            Assert.DoesNotContain("reborn", reborn.Creature.Keywords);
            AssertWinner(r, Outcome.Draw, 0);
        }
        Assert.Contains("reborn", Stats.Keywords(s.Data.Cards, s.A, servant));
    }

    // ---------- T-10 · Бой кончается в «Начале боя» ----------

    [Fact]
    public void T10_combat_ends_at_start_of_combat()
    {
        var s = new Sandbox();
        s.Put("battle_mage");
        s.Put("gate_guard", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            Assert.Equal(0, r.Attacks);
            Assert.Empty(r.Of(EventTypes.AttackDeclared));
            AssertWinner(r, Outcome.A, 5);
        }
    }

    // ---------- T-13 · Пат и пас ----------

    [Fact]
    public void T13a_stalemate()
    {
        var s = new Sandbox();
        s.Put("stone_wall");
        s.Put("stone_wall", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            AssertWinner(r, Outcome.Draw, 0);
            Assert.Equal(CombatReasons.Stalemate, r.Reason);
            Assert.Equal(0, r.Attacks);
        }
    }

    [Fact]
    public void T13b_pass()
    {
        var s = new Sandbox();
        var wall = s.Put("stone_wall");
        var skeleton = s.Put("tok_skeleton", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var attacks = Attacks(r);
            Assert.Equal(5, attacks.Count);
            Assert.All(attacks, a => Assert.Equal((skeleton.EntityId, wall.EntityId), a));
            AssertWinner(r, Outcome.B, 2);
        }
    }

    // ---------- T-14 · Урон герою (без секрета) ----------

    [Fact]
    public void T14_hero_damage()
    {
        var db = TestData.Data.Cards;
        var cfg = TestData.Data.Config;
        int Damage(params string[] survivors) =>
            Combat.HeroDamage(db, cfg, survivors.Select(id => new Creature { CardId = id }));

        Assert.Equal(5, Damage("ferret", "harpy"));                    // стоимости [1, 3]
        Assert.Equal(2, Damage("tok_spark"));                          // [токен]
        Assert.Equal(15, Damage("devourer", "dawn_knight", "scrap_golem")); // [6, 5, 5] → 17, предел 15
    }

    // ---------- T-15 · Полёт ----------

    [Fact]
    public void T15_flying_dodge()
    {
        var s = new Sandbox();
        var ferret = s.Put("ferret");
        var harpy = s.Put("harpy", Side.B);
        var snapshot = Combat.Snapshot(s.State);

        // Инициатива A (0), выбор цели (0), бросок уклонения задаётся.
        CombatResult Fight(int dodgeRoll) =>
            new CombatEngine(s.Data.Cards, snapshot, new ScriptedRandom(n => n == 100 ? dodgeRoll : 0)).Run();

        var dodged = Fight(24);
        var first = dodged.Events.FindIndex(e => e.Type == EventTypes.AttackDeclared);
        Assert.Equal(ferret.EntityId, dodged.Events[first].SourceEntityId);
        Assert.Equal(harpy.EntityId, dodged.Events[first].Target);
        Assert.Equal(EventTypes.Dodge, dodged.Events[first + 1].Type);
        // Урона нет ни у кого: следующее событие боя — уже атака Гарпии.
        Assert.Equal(EventTypes.AttackDeclared, dodged.Events[first + 2].Type);
        Assert.Equal(harpy.EntityId, dodged.Events[first + 2].SourceEntityId);

        var hit = Fight(25);
        first = hit.Events.FindIndex(e => e.Type == EventTypes.AttackDeclared);
        Assert.DoesNotContain(hit.Events, e => e.Type == EventTypes.Dodge);
        Assert.Equal(EventTypes.Damage, hit.Events[first + 1].Type);
        Assert.Equal((harpy.EntityId, 2), (hit.Events[first + 1].Target!.Value, hit.Events[first + 1].Amount!.Value));
        Assert.Equal((ferret.EntityId, 3), (hit.Events[first + 2].Target!.Value, hit.Events[first + 2].Amount!.Value));
        // Атака засчитана в обоих случаях.
        Assert.True(dodged.Attacks >= 1 && hit.Attacks >= 1);
    }

    // ---------- T-16 · Защита от зацикливания ----------

    [Fact]
    public void T16_loop_protection()
    {
        var data = TestData.WithExtraCards("""
            { "id": "test_loop", "name": "Петля", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
              "abilities": [{ "trigger": "on_death", "effects": [{ "op": "summon", "card": "test_loop", "at": "death_position" }] }] }
            """);
        var s = new Sandbox(data: data);
        s.Put("test_loop");
        s.Put("test_loop", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            AssertWinner(r, Outcome.Draw, 0);
            Assert.Equal(CombatReasons.Limit, r.Reason);
            Assert.Single(r.Of(EventTypes.Warning));
        }
    }

    // ---------- T-17(а) · Детерминизм боя ----------

    [Fact]
    public void T17a_combat_is_deterministic()
    {
        var s = new Sandbox();
        foreach (var id in new[] { "harpy", "venom_toad", "candle_spirit", "bone_servant", "whirlblade", "axeman" }) s.Put(id);
        foreach (var id in new[] { "gravekeeper", "dawn_knight", "battle_mage", "tok_gear", "wolf" }) s.Put(id, Side.B);
        var snapshot = Combat.Snapshot(s.State);
        string snapshotJson = StateSerializer.SerializeSnapshot(snapshot);

        var logs = new HashSet<string>();
        for (ulong seed = 1; seed <= 100; seed++)
        {
            string first = StateSerializer.SerializeEvents(s.Rules.SimulateCombat(snapshot, seed).Events);
            string second = StateSerializer.SerializeEvents(s.Rules.SimulateCombat(snapshot, seed).Events);
            Assert.Equal(first, second);
            logs.Add(first);
        }
        // Снимок не изменился — бой работает на копиях.
        Assert.Equal(snapshotJson, StateSerializer.SerializeSnapshot(snapshot));
        Assert.True(logs.Count > 1, "разные seed должны давать разные бои");
    }
}
