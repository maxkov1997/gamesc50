using Game.Content;
using Game.Core;

// Симулятор (12.5). На этапе M0 — только разбор аргументов и проверка данных;
// прогон матчей бот против бота появится на этапе M4.
//   sim --decks pack,bones --matches 1000 --seed 1 [--content <путь к content/>]

string contentDir = Path.Combine(AppContext.BaseDirectory, "content");
string decksArg = "pack,bones";
int matches = 1;
ulong seed = 1;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"После {args[i]} нужно значение");
    switch (args[i])
    {
        case "--decks": decksArg = Next(); break;
        case "--matches": matches = int.Parse(Next()); break;
        case "--seed": seed = ulong.Parse(Next()); break;
        case "--content": contentDir = Next(); break;
        default:
            Console.Error.WriteLine($"Неизвестный аргумент: {args[i]}");
            return 2;
    }
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

var deckIds = decksArg.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
if (deckIds.Length != 2)
{
    Console.Error.WriteLine("--decks: нужно ровно две колоды через запятую");
    return 2;
}
var deckA = data.GetDeck(deckIds[0]);
var deckB = data.GetDeck(deckIds[1]);
var state = Match.CreateMatch(data, deckA, deckB, seed, PlanningMode.OpponentFirst);

Console.WriteLine($"Данные загружены: карт {data.Cards.Cards.Count}, колод {data.Decks.Count}.");
Console.WriteLine($"Матч {deckA.Id} против {deckB.Id} создан, seed {seed}, рука A: {state.Player(Side.A).Hand.Count}, рука B: {state.Player(Side.B).Hand.Count}.");
Console.WriteLine($"Прогон {matches} матчей появится на этапе M4.");
return 0;
