using Game.Core;
using Xunit;

namespace Game.Tests;

/// <summary>Помощники для тестов боя.</summary>
public static class CombatTestKit
{
    /// <summary>«Любой seed» — минимум 50 seed (раздел 13).</summary>
    public static readonly ulong[] AnySeed = Enumerable.Range(1, 50).Select(i => (ulong)i).ToArray();

    /// <summary>Бой на снимке состояния песочницы для каждого seed.</summary>
    public static IEnumerable<(ulong Seed, CombatResult Result)> FightAll(Sandbox s)
    {
        var snapshot = Combat.Snapshot(s.State);
        foreach (var seed in AnySeed) yield return (seed, s.Rules.SimulateCombat(snapshot, seed));
    }

    public static IEnumerable<GameEvent> Of(this CombatResult r, string type) => r.Events.Where(e => e.Type == type);

    public static void AssertNoWarnings(CombatResult r) => Assert.DoesNotContain(r.Events, e => e.Type == EventTypes.Warning);

    public static void AssertWinner(CombatResult r, Outcome winner, int heroDamage)
    {
        Assert.Equal(winner, r.Result);
        Assert.Equal(heroDamage, r.HeroDamage);
        if (winner == Outcome.Draw) Assert.Null(r.DamagedSide);
        else Assert.Equal(winner == Outcome.A ? Side.B : Side.A, r.DamagedSide);
    }

    /// <summary>Атаки боя: (атакующий entityId, цель entityId).</summary>
    public static List<(int Attacker, int Target)> Attacks(CombatResult r) =>
        r.Of(EventTypes.AttackDeclared).Select(e => (e.SourceEntityId!.Value, e.Target!.Value)).ToList();
}

/// <summary>RNG-заглушка: значение броска задаёт функция от n.</summary>
public sealed class ScriptedRandom : IRandom
{
    private readonly Func<int, int> _roll;

    public ScriptedRandom(Func<int, int> roll) => _roll = roll;

    public uint NextUInt() => 0;

    public int Int(int n) => _roll(n);
}
