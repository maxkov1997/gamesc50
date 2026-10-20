using Game.Ai;
using Game.Core;
using Game.Sim;
using Xunit;

namespace Game.Tests.M5;

/// <summary>12.3: стол, восстановленный только из лога, совпадает с итогом боя.</summary>
public class LogBoardTests
{
    [Fact]
    public void M5_log_board_reconstructs_every_combat()
    {
        var data = TestData.Data;
        var rules = new GameRules(data);
        var bot = new Bot(data);
        int combats = 0;
        foreach (var (a, b) in Simulator.AllPairs(data))
        {
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var state = Match.CreateMatch(data, a, b, seed, PlanningMode.OpponentFirst);
                while (state.Result == null)
                {
                    rules.StartRound(state);
                    bot.PlayPlanning(rules, state, Side.B);
                    bot.PlayPlanning(rules, state, Side.A);

                    var result = rules.SimulateCombat(Combat.Snapshot(state), state.Seed);
                    var log = new LogBoard();
                    foreach (var e in result.Events) log.Apply(e);
                    AssertSameBoard(result.FinalSides, log);
                    Assert.Equal(result.Result, log.Result);
                    combats++;

                    rules.RunCombat(state);
                    if (state.Result != null) break;
                    rules.EndRound(state);
                }
            }
        }
        Assert.True(combats > 100);
    }

    [Fact]
    public void M5_aura_loss_is_visible_in_log()
    {
        // A первым (2 > 1): Вожак атакует Стену, Капкан уничтожает его до урона. Атака Хорька падает 3 → 2
        // без отдельного эффекта — лог дописывает stats_changed с причиной refresh.
        var s = new Sandbox();
        s.Put("pack_leader");
        var ferret = s.Put("ferret");
        s.Put("stone_wall", Side.B);
        s.PutBattle("snare", Side.B);
        foreach (var (_, r) in CombatTestKit.FightAll(s))
        {
            var refresh = r.Events.First(e => e.Type == EventTypes.StatsChanged && e.EntityId == ferret.EntityId);
            Assert.Equal(("refresh", 2), (refresh.Reason, refresh.Attack!.Value));
            var log = new LogBoard();
            foreach (var e in r.Events) log.Apply(e);
            AssertSameBoard(r.FinalSides, log);
        }
    }

    private static void AssertSameBoard(List<SideView> actual, LogBoard log)
    {
        for (int i = 0; i < 2; i++)
        {
            var real = actual[i];
            var seen = log.Sides[i];
            Assert.Equal(real.HeroHp, seen.HeroHp);
            Assert.Equal(real.Board.Select(c => c.EntityId), seen.Board.Select(c => c.EntityId));
            for (int k = 0; k < real.Board.Count; k++)
                Assert.True(LogBoard.Same(real.Board[k], seen.Board[k]),
                    $"{real.Board[k].CardId}: в бою {real.Board[k].Attack}/{real.Board[k].Health} [{string.Join(",", real.Board[k].Keywords)}], " +
                    $"в логе {seen.Board[k].Attack}/{seen.Board[k].Health} [{string.Join(",", seen.Board[k].Keywords)}]");
        }
    }
}
