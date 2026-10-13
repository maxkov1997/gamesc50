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
}
