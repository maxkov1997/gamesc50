using Game.Core;
using Game.Sim;
using Xunit;

namespace Game.Tests.M4;

/// <summary>Симулятор 12.5. Полный прогон по 1000 матчей — командой sim (см. CLAUDE.md), здесь — короткий.</summary>
public class SimulatorTests
{
    public static IEnumerable<object[]> Pairs => Simulator.AllPairs(TestData.Data).Select(p => new object[] { p.A.Id, p.B.Id });

    [Theory]
    [MemberData(nameof(Pairs))]
    public void M4_simulator_pairs_have_no_exceptions_or_limits(string deckA, string deckB)
    {
        var data = TestData.Data;
        var report = Simulator.Run(data, data.GetDeck(deckA), data.GetDeck(deckB), matches: 40, seed: 1);

        Assert.Equal(40, report.Matches);
        Assert.Equal(0, report.Exceptions);
        Assert.Equal(0, report.LimitCombats);
        Assert.Equal(0, report.Warnings);
        Assert.Equal(report.Matches, report.WinsA + report.WinsB + report.Draws);
        Assert.True(report.AverageRounds > 0);
        Assert.True(report.MaxAttacks >= report.AverageAttacks);
        Assert.NotEmpty(report.Cards);
        Assert.All(report.Cards, c => Assert.InRange(c.WinRate, 0, 1));
    }

    [Fact]
    public void M4_report_is_deterministic_and_serializable()
    {
        var data = TestData.Data;
        var r1 = Simulator.Run(data, data.GetDeck("pack"), data.GetDeck("mechs"), matches: 20, seed: 7);
        var r2 = Simulator.Run(data, data.GetDeck("pack"), data.GetDeck("mechs"), matches: 20, seed: 7);
        Assert.Equal(r1.ToJson(), r2.ToJson());
        Assert.Contains("\"wins_a\"", r1.ToJson());
        Assert.Contains("pack", r1.ToText());
    }

    [Fact]
    public void M4_all_pairs_cover_pairs_and_mirrors()
    {
        var pairs = Simulator.AllPairs(TestData.Data).Select(p => $"{p.A.Id}-{p.B.Id}").ToList();
        Assert.Equal(new[] { "bones-bones", "bones-mechs", "bones-pack", "mechs-mechs", "mechs-pack", "pack-pack" }, pairs);
    }
}
