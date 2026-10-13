namespace Game.Core;

/// <summary>Способность существа: своя (EnchantmentInstanceId == null) или от прикреплённых чар.</summary>
public sealed record AbilityRef(AbilityDef Ability, int Index, int? EnchantmentInstanceId);

/// <summary>
/// Характеристики существа (7.4): атака = база + усиления + чары + ауры; пересчитывается при каждом чтении.
/// Здоровье ауры и чары не дают.
/// </summary>
public static class Stats
{
    /// <summary>Свои способности в порядке записи в данных, затем от чар в порядке прикрепления (9.2).</summary>
    public static List<AbilityRef> Abilities(CardDatabase db, PlayerState p, Creature c)
    {
        var result = new List<AbilityRef>();
        int index = 0;
        foreach (var a in db.Get(c.CardId).Abilities) result.Add(new AbilityRef(a, index++, null));
        foreach (int ench in c.Enchantments)
        {
            var grants = db.Get(p.GetInstance(ench).CardId).Grants;
            if (grants == null) continue;
            foreach (var a in grants.Abilities) result.Add(new AbilityRef(a, index++, ench));
        }
        return result;
    }

    public static int Attack(CardDatabase db, PlayerState p, Creature c)
    {
        int attack = db.Get(c.CardId).Attack + c.BonusAttack;
        foreach (var g in Grants(db, p, c)) attack += g.Attack;
        foreach (var aura in AurasOn(db, p, c)) attack += aura.Attack;
        return attack;
    }

    public static int MaxHealth(CardDatabase db, Creature c) => db.Get(c.CardId).Health + c.BonusHealth;

    /// <summary>В подготовке существо всегда с полным здоровьем (6).</summary>
    public static int Health(CardDatabase db, Creature c) => MaxHealth(db, c);

    /// <summary>Ключевые слова: база, полученные, от чар, от аур — без повторов, в порядке появления.</summary>
    public static List<string> Keywords(CardDatabase db, PlayerState p, Creature c)
    {
        var result = OwnKeywords(db, p, c);
        foreach (var aura in AurasOn(db, p, c))
            foreach (var k in aura.Keywords)
                if (!result.Contains(k)) result.Add(k);
        return result;
    }

    public static bool HasKeyword(CardDatabase db, PlayerState p, Creature c, string keyword) =>
        Keywords(db, p, c).Contains(keyword);

    /// <summary>Ключевые слова без вклада аур.</summary>
    public static List<string> OwnKeywords(CardDatabase db, PlayerState p, Creature c)
    {
        var result = new List<string>();
        void Add(IEnumerable<string> ks)
        {
            foreach (var k in ks) if (!result.Contains(k)) result.Add(k);
        }
        Add(db.Get(c.CardId).Keywords);
        Add(c.GainedKeywords);
        foreach (var g in Grants(db, p, c)) Add(g.Keywords);
        return result;
    }

    private static IEnumerable<GrantsDef> Grants(CardDatabase db, PlayerState p, Creature c)
    {
        foreach (int ench in c.Enchantments)
            if (db.Get(p.GetInstance(ench).CardId).Grants is { } g) yield return g;
    }

    /// <summary>Ауры своей стороны, действующие на c. Источники: живые существа слева направо, затем чары на поле.</summary>
    private static IEnumerable<AuraDef> AurasOn(CardDatabase db, PlayerState p, Creature c)
    {
        foreach (var source in p.Alive)
            foreach (var a in Abilities(db, p, source))
                if (a.Ability.Aura is { } aura && AuraCovers(db, p, source, aura.Target, c))
                    yield return aura;

        foreach (var slot in p.Field)
        {
            if (slot is not int ench) continue;
            foreach (var a in db.Get(p.GetInstance(ench).CardId).Abilities)
                if (a.Aura is { } aura && AuraCovers(db, p, null, aura.Target, c))
                    yield return aura;
        }
    }

    private static bool AuraCovers(CardDatabase db, PlayerState p, Creature? source, Selector s, Creature c)
    {
        if (!c.IsAlive) return false;
        if (s.IsRef) return s.Ref == Registry.Refs.Self && ReferenceEquals(source, c);
        // GDD-QUESTION: фильтры по ключевым словам в селекторе ауры смотрят на ключевые слова без вклада аур —
        // иначе ауры зависят друг от друга по кругу (Q6).
        var candidates = Selectors.Filter(db, p, source, s, OwnKeywords).ToList();
        return Selectors.PickFixed(p, source, s, candidates).Contains(c);
    }
}

/// <summary>Общие части селекторов 11.3 без случайности.</summary>
public static class Selectors
{
    /// <summary>Живые существа стороны слева направо, прошедшие фильтры селектора.</summary>
    public static IEnumerable<Creature> Filter(
        CardDatabase db, PlayerState p, Creature? source, Selector s,
        Func<CardDatabase, PlayerState, Creature, List<string>> keywords)
    {
        foreach (var c in p.Alive)
        {
            if (s.NotSelf && ReferenceEquals(c, source)) continue;
            if (s.Tag != null && !db.Get(c.CardId).Tags.Contains(s.Tag)) continue;
            if (s.Keyword != null && !keywords(db, p, c).Contains(s.Keyword)) continue;
            if (s.NoKeyword != null && keywords(db, p, c).Contains(s.NoKeyword)) continue;
            yield return c;
        }
    }

    /// <summary>Неслучайные pick: all, leftmost, rightmost, adjacent.</summary>
    public static List<Creature> PickFixed(PlayerState p, Creature? source, Selector s, List<Creature> candidates)
    {
        int count = s.Count ?? 1;
        return s.Pick switch
        {
            Registry.Picks.All => candidates,
            Registry.Picks.Leftmost => candidates.Take(count).ToList(),
            Registry.Picks.Rightmost => candidates.Skip(Math.Max(0, candidates.Count - count)).ToList(),
            Registry.Picks.Adjacent => Adjacent(p, source).Where(candidates.Contains).ToList(),
            _ => throw new InvalidOperationException($"pick «{s.Pick}» не бывает без случайности"),
        };
    }

    /// <summary>Ближайшие живые соседи слева и справа; надгробия пропускаются (8.7).</summary>
    public static List<Creature> Adjacent(PlayerState p, Creature? source)
    {
        var result = new List<Creature>();
        if (source == null) return result;
        int i = p.Board.IndexOf(source);
        if (i < 0) return result;
        for (int l = i - 1; l >= 0; l--)
            if (p.Board[l].IsAlive) { result.Add(p.Board[l]); break; }
        for (int r = i + 1; r < p.Board.Count; r++)
            if (p.Board[r].IsAlive) { result.Add(p.Board[r]); break; }
        return result;
    }
}
