using System.Diagnostics;
using Game.Content;
using Game.Core;
using Game.Sim;

// Симулятор (12.5): матчи бот против бота и отчёт.
//   sim --decks pack,bones --matches 1000 --seed 1 [--out report.json] [--content <путь к content/>]
//   sim --decks all ...  — все пары тестовых колод, включая зеркала.
// Краткий текст — в stdout; JSON — в файл --out (без --out — в stdout после текста).
// Код выхода 1, если были исключения, бои с limit или события warning.

string contentDir = Path.Combine(AppContext.BaseDirectory, "content");
string decksArg = "pack,bones";
int matches = 1000;
ulong seed = 1;
string? outPath = null;

try
{
    for (int i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"После {args[i]} нужно значение");
        switch (args[i])
        {
            case "--decks": decksArg = Next(); break;
            case "--matches": matches = int.Parse(Next()); break;
            case "--seed": seed = ulong.Parse(Next()); break;
            case "--content": contentDir = Next(); break;
            case "--out": outPath = Next(); break;
            default: throw new ArgumentException($"Неизвестный аргумент: {args[i]}");
        }
    }
}
catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

GameData data;
try
{
    data = ContentLoader.LoadDirectory(contentDir);
}
catch (GameDataException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

List<(DeckList A, DeckList B)> pairs;
if (decksArg == "all")
{
    pairs = Simulator.AllPairs(data);
}
else
{
    var ids = decksArg.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (ids.Length != 2)
    {
        Console.Error.WriteLine("--decks: нужно две колоды через запятую или all");
        return 2;
    }
    try
    {
        pairs = new() { (data.GetDeck(ids[0]), data.GetDeck(ids[1])) };
    }
    catch (KeyNotFoundException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

var reports = new List<SimReport>();
foreach (var (a, b) in pairs)
{
    var watch = Stopwatch.StartNew();
    var report = Simulator.Run(data, a, b, matches, seed);
    watch.Stop();
    reports.Add(report);
    Console.Write(report.ToText());
    Console.WriteLine($"  время {watch.Elapsed.TotalSeconds:0.0} с");
}

string json = reports.Count == 1 ? reports[0].ToJson() : SimReport.ToJson(reports);
if (outPath != null) File.WriteAllText(outPath, json);
else Console.WriteLine(json);

return reports.Any(r => r.Exceptions > 0 || r.LimitCombats > 0 || r.Warnings > 0) ? 1 : 0;
