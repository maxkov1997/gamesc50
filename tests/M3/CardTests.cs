using System.Reflection;
using Game.Core;
using Xunit;
using static Game.Tests.CombatTestKit;

namespace Game.Tests.M3;

/// <summary>
/// Тесты карт стартового набора, которые не покрыты тест-кейсами раздела 13,
/// и проверка критерия M3: у каждой карты есть хотя бы один тест.
/// </summary>
public class CardTests
{
    [Fact]
    public void M3_every_starter_card_has_a_test()
    {
        var covered = typeof(CardTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttribute<FactAttribute>() != null)
            .SelectMany(m => m.GetCustomAttributes<CoversAttribute>())
            .SelectMany(a => a.CardIds)
            .ToHashSet();
        var unknown = covered.Where(id => !TestData.Data.Cards.TryGet(id, out _)).ToList();
        Assert.Empty(unknown);
        var missing = TestData.Data.Cards.Cards.Select(c => c.Id).Where(id => !covered.Contains(id)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    [Covers("scavenger")]
    public void Card_scavenger_draws_on_play()
    {
        var s = new Sandbox().Planning();
        int top = s.AddToDrawPile("wolf");
        s.Apply(new PlayCard(s.AddToHand("scavenger"), Slot: 0));
        Assert.Equal(new[] { top }, s.A.Hand);
        Assert.Empty(s.A.DrawPile);
    }

    [Fact]
    [Covers("berserker")]
    public void Card_berserker_grows_after_damage()
    {
        var s = new Sandbox();
        var berserker = s.Put("berserker");
        s.Put("gate_guard", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            int hit = r.Events.FindIndex(e => e.Type == EventTypes.Damage && e.Target == berserker.EntityId);
            var grow = r.Events.Skip(hit).First(e => e.Type == EventTypes.StatsChanged && e.EntityId == berserker.EntityId);
            Assert.Equal((5, Registry.Durations.Combat), (grow.Attack!.Value, grow.Duration));
        }
        s.RunCombat();
        Assert.Equal((3, 5), s.AH(berserker));
    }

    [Fact]
    [Covers("dawn_knight")]
    public void Card_dawn_knight_taunt_and_shield()
    {
        var s = new Sandbox();
        s.Put("ferret");
        var knight = s.Put("dawn_knight", Side.B);
        s.Put("ferret", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            Assert.All(r.Of(EventTypes.AttackDeclared).Where(e => e.Side == Side.A), e => Assert.Equal(knight.EntityId, e.Target));
            Assert.Contains(r.Events, e => e.Type == EventTypes.ShieldBroken && e.EntityId == knight.EntityId);
            Assert.DoesNotContain(r.Events, e => e.Type == EventTypes.Damage && e.Target == knight.EntityId);
        }
    }

    [Fact]
    [Covers("scrap_golem", "tok_gear")]
    public void Card_scrap_golem_leaves_gear()
    {
        var s = new Sandbox();
        s.Put("ferret");
        var golem = s.Put("scrap_golem");
        s.Put("wolf");
        s.Put("stone_wall", Side.B);

        var engine = s.Engine();
        engine.Destroy(golem.EntityId);
        var p = engine.Side(Side.A);
        Assert.Equal(new[] { "ferret", "tok_gear", "wolf" }, p.Board.Select(c => c.CardId));
        var gear = p.Board[1];
        Assert.Equal((2, 2), (Stats.Attack(s.Data.Cards, p, gear), Stats.Health(s.Data.Cards, gear)));
        Assert.Contains("shield", Stats.Keywords(s.Data.Cards, p, gear));
        Assert.Contains("mech", s.Data.Cards.Get(gear.CardId).Tags);
    }

    [Fact]
    [Covers("tempering")]
    public void Card_tempering()
    {
        var s = new Sandbox().Planning();
        var ferret = s.Put("ferret");
        s.Apply(new PlayCard(s.AddToHand("tempering"), Target: ferret.EntityId));
        Assert.Equal((3, 4), s.AH(ferret));
        Assert.Contains("taunt", Stats.Keywords(s.Data.Cards, s.A, ferret));
    }

    [Fact]
    [Covers("holy_veil")]
    public void Card_holy_veil()
    {
        var s = new Sandbox().Planning();
        var ferret = s.Put("ferret");
        s.Apply(new PlayCard(s.AddToHand("holy_veil"), Target: ferret.EntityId));
        Assert.Equal(new[] { "shield" }, ferret.GainedKeywords);

        s.Put("tok_skeleton", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            Assert.Contains(r.Events, e => e.Type == EventTypes.ShieldBroken && e.EntityId == ferret.EntityId);
            AssertWinner(r, Outcome.A, 2);
        }
        // Потраченный в бою Щит возвращается после боя.
        s.RunCombat();
        Assert.Contains("shield", Stats.Keywords(s.Data.Cards, s.A, ferret));
    }

    [Fact]
    [Covers("rally")]
    public void Card_rally()
    {
        var s = new Sandbox().Planning();
        var ferret = s.Put("ferret");
        var wolf = s.Put("wolf");
        s.Apply(new PlayCard(s.AddToHand("rally")));
        Assert.Equal((3, 2), s.AH(ferret));
        Assert.Equal((3, 3), s.AH(wolf));
    }

    [Fact]
    [Covers("fire_volley")]
    public void Card_fire_volley()
    {
        var s = new Sandbox();
        s.Put("stone_wall");
        int volley = s.PutBattle("fire_volley");
        var guards = Enumerable.Range(0, 3).Select(_ => s.Put("gate_guard", Side.B).EntityId).ToList();
        foreach (var (_, r) in FightAll(s))
        {
            Assert.Single(r.Events, e => e.Type == EventTypes.BattleSpellRevealed && e.CardId == "fire_volley");
            int firstAttack = r.Events.FindIndex(e => e.Type == EventTypes.AttackDeclared);
            var hits = r.Events.Take(firstAttack < 0 ? r.Events.Count : firstAttack).Where(e => e.Type == EventTypes.Damage).ToList();
            Assert.Equal(2, hits.Count);
            Assert.Equal(2, hits.Select(h => h.Target).Distinct().Count());
            Assert.All(hits, h => Assert.Contains(h.Target!.Value, guards));
            Assert.All(hits, h => Assert.Equal(2, h.Amount));
        }
        s.RunCombat();
        Assert.Contains(volley, s.A.Discard);
        Assert.Empty(s.A.BattleZone);
    }

    [Fact]
    [Covers("war_hymn")]
    public void Card_war_hymn()
    {
        var s = new Sandbox();
        var ferret = s.Put("ferret");
        s.PutBattle("war_hymn");
        s.Put("stone_wall", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var buff = r.Events.First(e => e.Type == EventTypes.StatsChanged && e.EntityId == ferret.EntityId);
            Assert.Equal((3, Registry.Durations.Combat), (buff.Attack!.Value, buff.Duration));
            Assert.All(r.Of(EventTypes.Damage).Where(e => e.SourceEntityId == ferret.EntityId), e => Assert.Equal(3, e.Amount));
        }
        s.RunCombat();
        Assert.Equal((2, 1), s.AH(ferret));
    }

    [Fact]
    [Covers("mirror_shield")]
    public void Card_mirror_shield()
    {
        var s = new Sandbox();
        s.Put("ferret");
        s.Put("ferret");
        var wall = s.Put("stone_wall", Side.B);
        int mirror = s.PutBattle("mirror_shield", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            // Первая атака по Стене: секрет даёт ей Щит до урона, Щит поглощает удар.
            int first = r.Events.FindIndex(e => e.Type == EventTypes.AttackDeclared);
            int second = r.Events.FindIndex(first + 1, e => e.Type == EventTypes.AttackDeclared);
            var round1 = r.Events.Skip(first).Take(second - first).ToList();
            Assert.Equal(new[] { EventTypes.AttackDeclared, EventTypes.SecretRevealed, EventTypes.KeywordChanged, EventTypes.ShieldBroken },
                round1.Select(e => e.Type));
            Assert.Equal(wall.EntityId, round1[2].EntityId);
            Assert.Single(r.Of(EventTypes.SecretRevealed));
            Assert.Equal(new[] { mirror }, r.SpentCards[(int)Side.B]);
        }
    }

    [Fact]
    [Covers("shieldbearer", "mirror_shield")]
    public void Card_mirror_shield_ignores_target_with_shield()
    {
        var s = new Sandbox();
        s.Put("stone_wall");
        s.Put("shieldbearer", Side.B);
        int mirror = s.PutBattle("mirror_shield", Side.B);
        // Стена A не атакует — секрет B не срабатывает и остаётся в боевой зоне.
        foreach (var (_, r) in FightAll(s))
        {
            Assert.Empty(r.Of(EventTypes.SecretRevealed));
            Assert.Empty(r.SpentCards[(int)Side.B]);
        }
        s.RunCombat();
        Assert.Equal(new[] { mirror }, s.P(Side.B).BattleZone);

        // У Щитоносца есть Щит — фильтр no_keyword не пропускает.
        var s2 = new Sandbox();
        s2.Put("ferret");
        s2.Put("ferret");
        var bearer = s2.Put("shieldbearer", Side.B);
        s2.PutBattle("mirror_shield", Side.B);
        foreach (var (_, r) in FightAll(s2))
        {
            int firstAttack = r.Events.FindIndex(e => e.Type == EventTypes.AttackDeclared && e.Target == bearer.EntityId);
            int firstShieldBreak = r.Events.FindIndex(e => e.Type == EventTypes.ShieldBroken);
            Assert.True(firstAttack >= 0 && firstShieldBreak > firstAttack);
            Assert.DoesNotContain(r.Events.Take(firstShieldBreak), e => e.Type == EventTypes.SecretRevealed);
        }
    }
}
