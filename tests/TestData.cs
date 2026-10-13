using Game.Content;
using Game.Core;

namespace Game.Tests;

/// <summary>Доступ к данным content/, скопированным рядом с тестами.</summary>
public static class TestData
{
    public static string ContentDir => Path.Combine(AppContext.BaseDirectory, "content");

    private static readonly Lazy<GameData> Lazy = new(() => ContentLoader.LoadDirectory(ContentDir));

    public static GameData Data => Lazy.Value;

    public static string ReadContent(string relative) => File.ReadAllText(Path.Combine(ContentDir, relative));

    /// <summary>Стартовый набор плюс синтетические карты, описанные в тесте (раздел 13).</summary>
    public static GameData WithExtraCards(string extraCardsJson)
    {
        string cards = ReadContent(ContentLoader.CardsFile).TrimEnd();
        cards = cards[..^1] + "," + extraCardsJson + "]";
        return new GameData { Config = Data.Config, Cards = ContentLoader.LoadCards(cards), Decks = Data.Decks };
    }
}
