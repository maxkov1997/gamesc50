namespace Game.Tests;

/// <summary>Какие карты стартового набора проверяет тест (критерий M3: у каждой карты есть хотя бы один тест).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CoversAttribute : Attribute
{
    public string[] CardIds { get; }

    public CoversAttribute(params string[] cardIds) => CardIds = cardIds;
}
