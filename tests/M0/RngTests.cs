using Game.Core;
using Xunit;

namespace Game.Tests.M0;

/// <summary>M0: RNG воспроизводим (раздел 10).</summary>
public class RngTests
{
    [Fact]
    public void M0_pcg32_matches_reference_output()
    {
        // Эталон pcg32-demo: initstate 42, initseq 54.
        var rng = new Pcg32(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        foreach (var e in expected) Assert.Equal(e, rng.NextUInt());
    }

    [Fact]
    public void M0_same_seed_gives_same_sequence()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var a = RngStreams.Derive(seed, RngStreams.Combat, 3);
            var b = RngStreams.Derive(seed, RngStreams.Combat, 3);
            for (int i = 0; i < 100; i++) Assert.Equal(a.Int(1000), b.Int(1000));
        }
    }

    [Fact]
    public void M0_streams_are_independent()
    {
        var first = new[]
        {
            RngStreams.PlanningStream(1, Side.A).NextUInt(),
            RngStreams.PlanningStream(1, Side.B).NextUInt(),
            RngStreams.CombatStream(1, 1).NextUInt(),
            RngStreams.CombatStream(1, 2).NextUInt(),
            RngStreams.PlanningStream(2, Side.A).NextUInt(),
        };
        Assert.Equal(first.Length, first.Distinct().Count());
    }

    [Fact]
    public void M0_rng_state_resumes_after_serialization()
    {
        var rng = RngStreams.PlanningStream(7, Side.A);
        for (int i = 0; i < 10; i++) rng.NextUInt();

        var state = new MatchState { PlanningRng = { rng } };
        var restored = StateSerializer.Deserialize(StateSerializer.Serialize(state)).PlanningRng[0];

        for (int i = 0; i < 100; i++) Assert.Equal(rng.NextUInt(), restored.NextUInt());
    }

    [Fact]
    public void M0_int_stays_in_range_and_hits_every_value()
    {
        var rng = new Pcg32(1, 1);
        var seen = new bool[7];
        for (int i = 0; i < 1000; i++)
        {
            int v = rng.Int(7);
            Assert.InRange(v, 0, 6);
            seen[v] = true;
        }
        Assert.All(seen, Assert.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Int(0));
    }

    [Fact]
    public void M0_shuffle_and_sample_are_deterministic()
    {
        var list1 = Enumerable.Range(0, 15).ToList();
        var list2 = Enumerable.Range(0, 15).ToList();
        RngStreams.PlanningStream(5, Side.B).Shuffle(list1);
        RngStreams.PlanningStream(5, Side.B).Shuffle(list2);
        Assert.Equal(list1, list2);
        Assert.Equal(Enumerable.Range(0, 15), list1.OrderBy(x => x));

        var s = new Pcg32(3, 3).Sample(new[] { 1, 2, 3, 4, 5 }, 3);
        Assert.Equal(s, new Pcg32(3, 3).Sample(new[] { 1, 2, 3, 4, 5 }, 3));
        Assert.Equal(3, s.Distinct().Count());
        // Допустимых меньше N — берутся все (9.3).
        Assert.Equal(2, new Pcg32(3, 3).Sample(new[] { 1, 2 }, 5).Count);
    }
}
