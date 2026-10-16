using Game.Core;
using Xunit;
using static Game.Tests.CombatTestKit;

namespace Game.Tests.M3;

/// <summary>
/// Тест-кейсы этапа M3 (раздел 13): T-11, T-12, T-14 (с секретом), T-24, T-25, T-27(а, в), T-28, T-29(а), T-31, T-32.
/// </summary>
public class CombatTriggerTests
{
    // ---------- T-11 · Секрет и порядок посмертных эффектов ----------

    [Fact]
    [Covers("vengeance")]
    public void T11_secret_and_death_order()
    {
        var s = new Sandbox();
        var f1 = s.Put("ferret");
        var f2 = s.Put("ferret");
        var spirit = s.Put("candle_spirit", Side.B);
        int vengeance = s.PutBattle("vengeance", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            // Хорёк и Дух гибнут в обмене.
            var died = r.Of(EventTypes.Died).Select(e => e.EntityId!.Value).ToList();
            Assert.Contains(spirit.EntityId, died);
            int firstFerret = died.First(id => id == f1.EntityId || id == f2.EntityId);
            int secondFerret = firstFerret == f1.EntityId ? f2.EntityId : f1.EntityId;

            // Сначала Дух призывает Искорку, затем Месть убивает второго Хорька.
            int summon = r.Events.FindIndex(e => e.Type == EventTypes.CreatureSummoned && e.CardId == "tok_spark");
            int revealed = r.Events.FindIndex(e => e.Type == EventTypes.SecretRevealed && e.CardId == "vengeance");
            Assert.True(summon >= 0 && summon < revealed);
            var hit = r.Events.Skip(revealed).First(e => e.Type == EventTypes.Damage);
            Assert.Equal((secondFerret, 3), (hit.Target!.Value, hit.Amount!.Value));
            Assert.Null(hit.SourceEntityId);

            AssertWinner(r, Outcome.B, 2);
            Assert.Equal(new[] { vengeance }, r.SpentCards[(int)Side.B]);
        }

        s.RunCombat();
        Assert.Contains(vengeance, s.P(Side.B).Discard);
        Assert.DoesNotContain(vengeance, s.P(Side.B).BattleZone);
    }

    // ---------- T-12 · Засада ----------

    [Fact]
    [Covers("ambush", "tok_ambusher")]
    public void T12_ambush()
    {
        var s = new Sandbox();
        var f1 = s.Put("ferret");
        var f2 = s.Put("ferret");
        s.Put("tok_skeleton", Side.B);
        s.PutBattle("ambush", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            Assert.Single(r.Of(EventTypes.SecretRevealed));
            var ambusher = r.Of(EventTypes.CreatureSummoned).Single();
            Assert.Equal("tok_ambusher", ambusher.CardId);
            // Бой не закончился: Засадник атакует и убивает второго Хорька.
            var attacks = Attacks(r);
            Assert.Equal(2, attacks.Count);
            Assert.Equal(ambusher.EntityId, attacks[1].Attacker);
            Assert.Contains(attacks[1].Target, new[] { f1.EntityId, f2.EntityId });
            AssertWinner(r, Outcome.B, 2);
        }
    }

    // ---------- T-14 · Урон герою с секретом ----------

    [Fact]
    [Covers("riposte")]
    public void T14_hero_damage_with_riposte()
    {
        // Выжившие A стоят 6 → урон 7; Ответный удар уменьшает до 2.
        var s = new Sandbox();
        s.Put("devourer");
        int riposte = s.PutBattle("riposte", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            AssertWinner(r, Outcome.A, 2);
            Assert.Single(r.Of(EventTypes.SecretRevealed));
            Assert.Equal(2, r.Of(EventTypes.HeroDamaged).Single().Amount);
            Assert.Equal(new[] { riposte }, r.SpentCards[(int)Side.B]);
        }

        // Урон 3 → 0, не ниже нуля.
        var s2 = new Sandbox();
        s2.Put("shieldbearer");
        s2.PutBattle("riposte", Side.B);
        foreach (var (_, r) in FightAll(s2)) AssertWinner(r, Outcome.A, 0);
        s2.RunCombat();
        Assert.Equal(s2.Data.Config.HeroHp, s2.P(Side.B).HeroHp);
    }

    // ---------- T-24 · Аура ----------

    [Fact]
    [Covers("pack_leader")]
    public void T24_aura()
    {
        var s = new Sandbox();
        var leader = s.Put("pack_leader");
        var ferret = s.Put("ferret");
        Assert.Equal(3, s.Attack(ferret));
        Assert.Equal(3, s.Attack(leader));

        var engine = s.Engine();
        engine.Destroy(leader.EntityId);
        var combatFerret = engine.Side(Side.A).Board.Single();
        Assert.Equal(2, Stats.Attack(s.Data.Cards, engine.Side(Side.A), combatFerret));

        // На постоянном столе база Хорька — 2: аура не записана в его усиления.
        Assert.Equal(0, ferret.BonusAttack);
        Assert.Equal(2, s.Data.Cards.Get(ferret.CardId).Attack);
    }

    // ---------- T-25 · Возмездие и «навсегда» ----------

    [Fact]
    [Covers("watchdog")]
    public void T25_avenge_and_permanent()
    {
        var s = new Sandbox();
        var skeletons = Enumerable.Range(0, 4).Select(_ => s.Put("tok_skeleton").EntityId).ToArray();
        var dog = s.Put("watchdog");
        s.Put("stone_wall", Side.B);

        var engine = s.Engine();
        engine.Destroy(skeletons);

        Assert.Equal(2, engine.Events.Count(e => e.Type == EventTypes.Trigger && e.Trigger == "avenge"));
        var combatDog = engine.Side(Side.A).Board.Single();
        Assert.Equal((4, 5), (Stats.Attack(s.Data.Cards, engine.Side(Side.A), combatDog), Stats.Health(s.Data.Cards, combatDog)));

        // После боя постоянный Пёс 4/5.
        s.Planning();
        s.Rules.ApplyCombatResult(s.State, engine.Run());
        Assert.Equal((4, 5), s.AH(dog));
    }

    // ---------- T-27 · Чары ----------

    [Fact]
    [Covers("seal_of_rebirth")]
    public void T27a_seal_in_combat()
    {
        var s = new Sandbox();
        var ferret = s.Put("ferret");
        int seal = s.Enchant(ferret, "seal_of_rebirth");
        s.Put("stone_wall", Side.B);

        var engine = s.Engine();
        engine.Destroy(ferret.EntityId);
        var spark = Assert.Single(engine.Side(Side.A).Board);
        Assert.Equal("tok_spark", spark.CardId);
        Assert.Equal(0, engine.Events.Single(e => e.Type == EventTypes.CreatureSummoned).Position);

        // После боя Печать по-прежнему на постоянном Хорьке.
        s.Put("tok_skeleton", Side.B);
        s.RunCombat();
        Assert.Equal(new[] { seal }, ferret.Enchantments);
        Assert.DoesNotContain(seal, s.A.Discard);
    }

    [Fact]
    [Covers("sanctuary")]
    public void T27c_sanctuary()
    {
        var s = new Sandbox();
        s.PutField("sanctuary", 0);
        var ferret = s.Put("ferret");
        s.Put("wolf");
        s.Put("stone_wall", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var start = r.Events.FindIndex(e => e.Type == EventTypes.CombatStarted);
            var firstAttack = r.Events.FindIndex(e => e.Type == EventTypes.AttackDeclared);
            var gained = r.Events.Skip(start).Take(firstAttack - start).Single(e => e.Type == EventTypes.KeywordChanged);
            Assert.Equal((ferret.EntityId, "shield", Registry.Durations.Combat), (gained.EntityId!.Value, gained.Keyword, gained.Duration));
        }

        s.RunCombat();
        Assert.DoesNotContain("shield", Stats.Keywords(s.Data.Cards, s.A, ferret));
        Assert.Equal(s.Data.Cards.Get("sanctuary").Id, s.CardOf(s.A.Field[0]!.Value));
    }

    // ---------- T-28 · Когда атакует ----------

    [Fact]
    [Covers("wolf")]
    public void T28_on_attack()
    {
        var s = new Sandbox();
        var wolf = s.Put("wolf");
        var wall = s.Put("stone_wall", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var attacks = Attacks(r);
            Assert.Equal(2, attacks.Count);
            Assert.All(attacks, a => Assert.Equal((wolf.EntityId, wall.EntityId), a));
            var hits = r.Of(EventTypes.Damage).Where(e => e.SourceEntityId == wolf.EntityId).Select(e => e.Amount!.Value);
            Assert.Equal(new[] { 3, 4 }, hits);
            AssertWinner(r, Outcome.A, 3);
        }

        s.RunCombat();
        Assert.Equal((2, 2), s.AH(wolf));
    }

    // ---------- T-29(а) · Когда умирает союзник (бой) ----------

    [Fact]
    public void T29a_mourner_in_combat()
    {
        var s = new Sandbox();
        var skeleton = s.Put("tok_skeleton");
        var mourner = s.Put("mourner");
        s.Put("stone_wall", Side.B);

        var engine = s.Engine();
        engine.Destroy(skeleton.EntityId);
        var combatMourner = engine.Side(Side.A).Board.Single();
        Assert.Equal((2, 4), (Stats.Attack(s.Data.Cards, engine.Side(Side.A), combatMourner), Stats.Health(s.Data.Cards, combatMourner)));

        s.Planning();
        s.Rules.ApplyCombatResult(s.State, engine.Run());
        Assert.Equal((1, 3), s.AH(mourner));
    }

    // ---------- T-31 · Капкан и сорвавшаяся атака ----------

    [Fact]
    [Covers("snare")]
    public void T31_snare_cancels_attack()
    {
        var s = new Sandbox();
        var f1 = s.Put("ferret");
        var f2 = s.Put("ferret");
        var guard = s.Put("gate_guard", Side.B);
        int snare = s.PutBattle("snare", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var attacks = Attacks(r);
            Assert.Equal((f1.EntityId, guard.EntityId), attacks[0]);
            // Капкан уничтожает Хорька до урона, Страж урона не получает.
            int second = r.Events.FindIndex(e => e.Type == EventTypes.AttackDeclared && e.SourceEntityId == guard.EntityId);
            var before = r.Events.Take(second).ToList();
            Assert.Contains(before, e => e.Type == EventTypes.SecretRevealed && e.CardId == "snare");
            Assert.Contains(before, e => e.Type == EventTypes.Died && e.EntityId == f1.EntityId);
            Assert.DoesNotContain(before, e => e.Type == EventTypes.Damage);
            // Затем Страж убивает второго Хорька.
            Assert.Equal((guard.EntityId, f2.EntityId), attacks[1]);
            AssertWinner(r, Outcome.B, 2);
        }

        s.RunCombat();
        Assert.Contains(snare, s.P(Side.B).Discard);
    }

    // ---------- T-32 · Секрет срабатывает один раз ----------

    [Fact]
    public void T32_secret_triggers_once()
    {
        var s = new Sandbox();
        var wall = s.Put("stone_wall");
        var sk1 = s.Put("tok_skeleton", Side.B);
        var sk2 = s.Put("tok_skeleton", Side.B);
        s.PutBattle("vengeance", Side.B);

        var engine = s.Engine();
        engine.Destroy(sk1.EntityId, sk2.EntityId);

        Assert.Single(engine.Events, e => e.Type == EventTypes.SecretRevealed);
        var combatWall = engine.Side(Side.A).Board.Single();
        Assert.Equal(wall.EntityId, combatWall.EntityId);
        Assert.Equal(2, Stats.Health(s.Data.Cards, combatWall));
    }
}
