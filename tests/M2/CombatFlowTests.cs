using Game.Core;
using Xunit;

namespace Game.Tests.M2;

/// <summary>Бой внутри раунда (4.3, 8.1, 8.9) сверх тест-кейсов раздела 13.</summary>
public class CombatFlowTests
{
    private static void ReadyBoth(Sandbox s)
    {
        foreach (var side in new[] { Side.A, Side.B }) s.Apply(new Ready(), side);
    }

    [Fact]
    public void M2_full_round_with_combat()
    {
        var s = new Sandbox();
        s.Rules.StartRound(s.State);
        s.Apply(new PlayCard(s.AddToHand("ferret"), Slot: 0));
        ReadyBoth(s);

        var events = s.Rules.RunCombat(s.State);
        Assert.Equal(EventTypes.CombatStarted, events[0].Type);
        Assert.Contains(events, e => e.Type == EventTypes.CombatEnded && e.Result == Outcome.A);
        Assert.Equal(s.Data.Config.HeroHp - 2, s.P(Side.B).HeroHp);
        Assert.Equal(RoundStage.CombatDone, s.State.Stage);

        s.Rules.EndRound(s.State);
        s.Rules.StartRound(s.State);
        Assert.Equal(2, s.State.Round);
        Assert.Single(s.A.Board);
        Assert.DoesNotContain(s.State.Log, e => e.Type == EventTypes.Warning);
    }

    [Fact]
    public void M2_hero_death_ends_match()
    {
        var s = new Sandbox().Planning();
        s.Put("ferret");
        s.P(Side.B).HeroHp = 2;
        ReadyBoth(s);

        s.Rules.RunCombat(s.State);

        Assert.Equal(0, s.P(Side.B).HeroHp);
        Assert.Equal(Outcome.A, s.State.Result);
        Assert.Equal(RoundStage.MatchOver, s.State.Stage);
        Assert.Contains(s.State.Log, e => e.Type == EventTypes.MatchEnded && e.Result == Outcome.A);
        Assert.Empty(s.Legal());
        Assert.Throws<InvalidOperationException>(() => s.Rules.StartRound(s.State));
    }

    [Fact]
    public void M2_combat_requires_both_ready()
    {
        var s = new Sandbox().Planning();
        s.Apply(new Ready());
        Assert.Throws<InvalidOperationException>(() => s.Rules.RunCombat(s.State));
    }

    [Fact]
    public void M2_snapshot_is_a_copy_without_hidden_zones()
    {
        var s = new Sandbox().Planning();
        var ferret = s.Put("ferret");
        s.AddToHand("harpy");
        s.AddToDrawPile("wolf");
        string before = StateSerializer.Serialize(s.State);

        var snapshot = Combat.Snapshot(s.State);
        Assert.All(snapshot.Sides, p => Assert.Empty(p.Hand));
        Assert.All(snapshot.Sides, p => Assert.Empty(p.DrawPile));

        var engine = new CombatEngine(s.Data.Cards, snapshot, new Pcg32(1, 1));
        engine.Destroy(ferret.EntityId);
        Assert.Empty(engine.Side(Side.A).Board);
        Assert.Equal(before, StateSerializer.Serialize(s.State));
    }

    [Fact]
    public void M2_empty_side_loses_without_attacks()
    {
        var s = new Sandbox();
        s.Put("harpy");
        var r = s.Rules.SimulateCombat(Combat.Snapshot(s.State), 7);
        Assert.Equal((Outcome.A, 0, 4), (r.Result, r.Attacks, r.HeroDamage));

        var empty = new Sandbox();
        var draw = empty.Rules.SimulateCombat(Combat.Snapshot(empty.State), 7);
        Assert.Equal((Outcome.Draw, CombatReasons.Wipe, 0), (draw.Result, draw.Reason, draw.HeroDamage));
    }

    [Fact]
    public void M2_reborn_creature_waits_for_next_sweep()
    {
        // A: [Костяной слуга, Хорёк, Хорёк]; B: [Броневепрь 3/6, Провокация]. A первым.
        // 1) Слуга бьёт Вепря и гибнет → перерождается новой сущностью. 2) Вепрь бьёт кого-то из A.
        // 3) Следующим в проходе A атакует Хорёк, а не переродившийся Слуга (8.5).
        var s = new Sandbox();
        var servant = s.Put("bone_servant");
        var ferrets = new[] { s.Put("ferret").EntityId, s.Put("ferret").EntityId };
        s.Put("iron_boar", Side.B);
        foreach (var (_, r) in CombatTestKit.FightAll(s))
        {
            var reborn = Assert.Single(r.Of(EventTypes.Reborn));
            var attacks = CombatTestKit.Attacks(r);
            Assert.Equal(servant.EntityId, attacks[0].Attacker);
            Assert.Contains(attacks[2].Attacker, ferrets);
            Assert.NotEqual(reborn.EntityId, attacks[2].Attacker);
        }
    }
}
