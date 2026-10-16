using System.Globalization;
using System.Text;
using System.Text.Json;
using Game.Ai;
using Game.Core;

namespace Game.Sim;

/// <summary>Статистика карты (12.5): сколько раз разыграна и доля побед в матчах, где её разыграла сторона.</summary>
public sealed class CardStat
{
    public string CardId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Played { get; set; }

    /// <summary>Пары (матч, сторона), где сторона разыграла карту хотя бы раз.</summary>
    public int MatchesPlayed { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

/// <summary>Отчёт симулятора (12.5).</summary>
public sealed class SimReport
{
    public string DeckA { get; set; } = "";
    public string DeckB { get; set; } = "";
    public int Matches { get; set; }
    public ulong Seed { get; set; }

    public int WinsA { get; set; }
    public int WinsB { get; set; }
    public int Draws { get; set; }
    public double AverageRounds { get; set; }

    public int Combats { get; set; }
    public int DrawCombats { get; set; }
    public double DrawCombatShare { get; set; }
    public double AverageAttacks { get; set; }
    public int MaxAttacks { get; set; }

    public int Exceptions { get; set; }
    public List<string> ExceptionMessages { get; set; } = new();
    public int LimitCombats { get; set; }

    /// <summary>Все события warning за матчи, включая предел MAX_TRIGGERS в подготовке (9.1).</summary>
    public int Warnings { get; set; }

    public List<CardStat> Cards { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static string ToJson(IEnumerable<SimReport> reports) => JsonSerializer.Serialize(reports, JsonOptions);

    public string ToText()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(ci, $"{DeckA} против {DeckB}: {Matches} матчей, seed {Seed}");
        sb.AppendLine(ci, $"  победы A {WinsA} ({Pct(WinsA, Matches)}), победы B {WinsB} ({Pct(WinsB, Matches)}), ничьи {Draws}");
        sb.AppendLine(ci, $"  средняя длина матча {AverageRounds:0.00} раунда");
        sb.AppendLine(ci, $"  боёв {Combats}, ничьих {DrawCombats} ({DrawCombatShare * 100:0.0} %)");
        sb.AppendLine(ci, $"  атак в бою: в среднем {AverageAttacks:0.00}, максимум {MaxAttacks}");
        sb.AppendLine(ci, $"  исключений {Exceptions}, limit {LimitCombats}, warning {Warnings}");
        foreach (var m in ExceptionMessages) sb.AppendLine(ci, $"    ! {m}");
        return sb.ToString();
    }

    private static string Pct(int x, int n) =>
        n == 0 ? "—" : (100.0 * x / n).ToString("0.0", CultureInfo.InvariantCulture) + " %";
}

/// <summary>Прогон матчей бот против бота (12.5). Матч i играется с seed + i.</summary>
public static class Simulator
{
    /// <summary>Сколько разных сообщений об исключениях сохранять в отчёте.</summary>
    private const int MaxExceptionMessages = 5;

    /// <summary>Все пары тестовых колод, включая зеркала (критерий M4).</summary>
    public static List<(DeckList A, DeckList B)> AllPairs(GameData data)
    {
        var pairs = new List<(DeckList, DeckList)>();
        for (int i = 0; i < data.Decks.Count; i++)
            for (int j = i; j < data.Decks.Count; j++)
                pairs.Add((data.Decks[i], data.Decks[j]));
        return pairs;
    }

    public static SimReport Run(GameData data, DeckList deckA, DeckList deckB, int matches, ulong seed,
        PlanningMode mode = PlanningMode.OpponentFirst)
    {
        var report = new SimReport { DeckA = deckA.Id, DeckB = deckB.Id, Matches = matches, Seed = seed };
        var stats = data.Cards.Cards.Select(c => new CardStat { CardId = c.Id, Name = c.Name }).ToList();
        long rounds = 0, attacks = 0;
        int finished = 0;

        for (int i = 0; i < matches; i++)
        {
            MatchState state;
            try
            {
                state = MatchRunner.Play(data, deckA, deckB, unchecked(seed + (ulong)i), mode).State;
            }
            catch (Exception ex)
            {
                report.Exceptions++;
                string msg = $"{ex.GetType().Name}: {ex.Message}";
                if (report.ExceptionMessages.Count < MaxExceptionMessages && !report.ExceptionMessages.Contains(msg))
                    report.ExceptionMessages.Add(msg);
                continue;
            }

            finished++;
            rounds += state.Round;
            switch (state.Result)
            {
                case Outcome.A: report.WinsA++; break;
                case Outcome.B: report.WinsB++; break;
                default: report.Draws++; break;
            }

            report.Warnings += state.Log.Count(e => e.Type == EventTypes.Warning);
            foreach (var e in state.Log.Where(e => e.Type == EventTypes.CombatEnded))
            {
                report.Combats++;
                int n = e.Count ?? 0;
                attacks += n;
                report.MaxAttacks = Math.Max(report.MaxAttacks, n);
                if (e.Result == Outcome.Draw) report.DrawCombats++;
                if (e.Reason == CombatReasons.Limit) report.LimitCombats++;
            }

            foreach (var side in new[] { Side.A, Side.B })
            {
                var played = state.Log.Where(e => e.Type == EventTypes.CardPlayed && e.Side == side).Select(e => e.CardId!).ToList();
                bool won = state.Result == (side == Side.A ? Outcome.A : Outcome.B);
                foreach (var stat in stats)
                {
                    int count = played.Count(id => id == stat.CardId);
                    if (count == 0) continue;
                    stat.Played += count;
                    stat.MatchesPlayed++;
                    if (won) stat.Wins++;
                }
            }
        }

        report.AverageRounds = Round(finished == 0 ? 0 : (double)rounds / finished);
        report.DrawCombatShare = Round(report.Combats == 0 ? 0 : (double)report.DrawCombats / report.Combats);
        report.AverageAttacks = Round(report.Combats == 0 ? 0 : (double)attacks / report.Combats);
        foreach (var s in stats) s.WinRate = Round(s.MatchesPlayed == 0 ? 0 : (double)s.Wins / s.MatchesPlayed);
        report.Cards = stats.Where(s => s.Played > 0).ToList();
        return report;
    }

    private static double Round(double x) => Math.Round(x, 4, MidpointRounding.AwayFromZero);
}
