namespace Game.Core;

public enum AbilitySource { Own, Enchantment, Field }

/// <summary>
/// Способность существа: своя, от прикреплённых чар или выданная чарами на поле (Q5).
/// Index — номер в общем списке существа (12.2, UseAbility); Key — стабильный ключ для флага «использована».
/// </summary>
public sealed record AbilityRef(AbilityDef Ability, int Index, AbilitySource Source = AbilitySource.Own,
    int? SourceInstanceId = null, int SourceIndex = 0)
{
    public string Key => Source switch
    {
        AbilitySource.Own => $"own:{SourceIndex}",
        AbilitySource.Enchantment => $"enchantment:{SourceInstanceId}:{SourceIndex}",
        _ => $"field:{SourceInstanceId}:{SourceIndex}",
    };
}

/// <summary>
/// Характеристики существа (7.4): атака = база + усиления + чары + ауры; пересчитывается при каждом чтении.
/// Здоровье ауры и чары не дают.
/// </summary>
public static class Stats
{
    /// <summary>
    /// Свои способности в порядке записи в данных, затем от чар в порядке прикрепления (9.2),
    /// затем выданные чарами на поле по слотам (Q5).
    /// </summary>
    public static List<AbilityRef> Abilities(CardDatabase db, PlayerState p, Creature c)
    {
        var result = OwnAndAttached(db, p, c);
        int index = result.Count;
        for (int slot = 0; slot < p.Field.Count; slot++)
        {
            if (p.Field[slot] is not int ench) continue;
            var def = db.Get(p.GetInstance(ench).CardId);
            for (int i = 0; i < def.Abilities.Count; i++)
            {
                var a = def.Abilities[i];
                if (a.Watch == null || !Watches(db, p, c, a.Watch)) continue;
                result.Add(new AbilityRef(a, index++, AbilitySource.Field, ench, i));
            }
        }
        return result;
    }

    /// <summary>Свои способности и от прикреплённых чар — без выданных полем (ауры бывают только среди них).</summary>
    private static List<AbilityRef> OwnAndAttached(CardDatabase db, PlayerState p, Creature c)
    {
        var result = new List<AbilityRef>();
        int index = 0;
        var own = db.Get(c.CardId).Abilities;
        for (int i = 0; i < own.Count; i++) result.Add(new AbilityRef(own[i], index++, AbilitySource.Own, null, i));
        foreach (int ench in c.Enchantments)
        {
            var grants = db.Get(p.GetInstance(ench).CardId).Grants;
            if (grants == null) continue;
            for (int i = 0; i < grants.Abilities.Count; i++)
                result.Add(new AbilityRef(grants.Abilities[i], index++, AbilitySource.Enchantment, ench, i));
        }
        return result;
    }

    private static bool Watches(CardDatabase db, PlayerState p, Creature c, FilterDef watch)
    {
        if (!c.IsAlive) return false;
        if (watch.Tag != null && !db.Get(c.CardId).Tags.Contains(watch.Tag)) return false;
        if (watch.Keyword == null && watch.NoKeyword == null) return true;
        var keywords = Keywords(db, p, c);
        if (watch.Keyword != null && !keywords.Contains(watch.Keyword)) return false;
        if (watch.NoKeyword != null && keywords.Contains(watch.NoKeyword)) return false;
        return true;
    }

    /// <summary>Атака: база + усиления + чары (слой 7c) + ауры; ауры — по итогам слоя ключевых слов (7.4).</summary>
    public static int Attack(CardDatabase db, PlayerState p, Creature c)
    {
        int attack = db.Get(c.CardId).Attack + c.BonusAttack + c.CombatAttack;
        foreach (var g in Grants(db, p, c)) attack += g.Attack;
        return attack + Layers.AuraAttack(db, p, c);
    }

    public static int MaxHealth(CardDatabase db, Creature c) => db.Get(c.CardId).Health + c.BonusHealth + c.CombatHealth;

    /// <summary>Здоровье = максимальное − урон. Урон бывает только в бою: в подготовке здоровье полное (6).</summary>
    public static int Health(CardDatabase db, Creature c) => MaxHealth(db, c) - c.Damage;

    /// <summary>Ключевые слова: база, полученные, от чар, затем от аур по слоям (7.4) — без повторов, в порядке появления.</summary>
    public static List<string> Keywords(CardDatabase db, PlayerState p, Creature c) => Layers.Keywords(db, p, c);

    public static bool HasKeyword(CardDatabase db, PlayerState p, Creature c, string keyword) =>
        Keywords(db, p, c).Contains(keyword);

    /// <summary>Ключевые слова без вклада аур: база, полученные эффектами, от прикреплённых чар, минус потерянные в бою.</summary>
    public static List<string> OwnKeywords(CardDatabase db, PlayerState p, Creature c)
    {
        var result = new List<string>();
        void Add(IEnumerable<string> ks)
        {
            foreach (var k in ks) if (!result.Contains(k)) result.Add(k);
        }
        Add(db.Get(c.CardId).Keywords);
        Add(c.GainedKeywords);
        if (c.CombatKeywords != null) Add(c.CombatKeywords);
        foreach (var g in Grants(db, p, c)) Add(g.Keywords);
        if (c.RemovedKeywords != null) result.RemoveAll(c.RemovedKeywords.Contains);
        return result;
    }

    private static IEnumerable<GrantsDef> Grants(CardDatabase db, PlayerState p, Creature c)
    {
        foreach (int ench in c.Enchantments)
            if (db.Get(p.GetInstance(ench).CardId).Grants is { } g) yield return g;
    }

    /// <summary>Свои способности и от прикреплённых чар (ауры бывают только среди них) — для слоёв.</summary>
    internal static List<AbilityRef> AuraCarriers(CardDatabase db, PlayerState p, Creature c) => OwnAndAttached(db, p, c);
}

/// <summary>Эффект ауры (7.4). Порядок в слое — timestamp, затем источник (существа по позиции, потом поле по слотам).</summary>
internal sealed record AuraEffect(Creature? Source, AuraDef Aura, int Timestamp, int Kind, int Position, int AbilityIndex)
{
    /// <summary>На кого действует, зависит от ключевых слов — значит, от других аур (зависимость, 7.4).</summary>
    public bool DependsOnKeywords => !Aura.Target.IsRef && (Aura.Target.Keyword != null || Aura.Target.NoKeyword != null);
}

/// <summary>
/// Слои аур по образцу MTG (7.4, Q6). Слой ключевых слов: сначала собственные и полученные эффектами слова,
/// затем ауры по одной: следующей — та, что не зависит от оставшихся (их применение не меняет, на кого она действует);
/// среди таких — раньше по timestamp; если все зависят друг от друга по кругу — по timestamp.
/// Затем слой атаки — по итоговым ключевым словам. Кому эффект ауры с ключевыми словами дал слова, тому он даёт и атаку
/// (набор фиксируется, когда эффект применён в первом слое). Конечный алгоритм: зацикливания нет.
/// </summary>
internal static class Layers
{
    public static List<string> Keywords(CardDatabase db, PlayerState p, Creature c)
    {
        var own = Stats.OwnKeywords(db, p, c);
        if (!c.IsAlive) return own;
        var effects = Effects(db, p);
        if (effects.Count == 0) return own;
        if (!effects.Any(e => e.DependsOnKeywords))
        {
            // Быстрый путь: наборы аур не зависят от ключевых слов — порядок применения ни на что не влияет.
            foreach (var e in effects)
                if (e.Aura.Keywords.Count > 0 && SetOf(db, p, e, null).Contains(c)) AddKeywords(own, c, e.Aura.Keywords);
            return own;
        }
        return Resolve(db, p, effects).Keywords[c];
    }

    public static int AuraAttack(CardDatabase db, PlayerState p, Creature c)
    {
        if (!c.IsAlive) return 0;
        var effects = Effects(db, p);
        if (effects.Count == 0) return 0;
        int attack = 0;
        if (!effects.Any(e => e.DependsOnKeywords))
        {
            foreach (var e in effects)
                if (e.Aura.Attack != 0 && SetOf(db, p, e, null).Contains(c)) attack += e.Aura.Attack;
            return attack;
        }
        var r = Resolve(db, p, effects);
        foreach (var e in effects)
            if (e.Aura.Attack != 0 && r.Sets[e].Contains(c)) attack += e.Aura.Attack;
        return attack;
    }

    private static List<AuraEffect> Effects(CardDatabase db, PlayerState p)
    {
        var list = new List<AuraEffect>();
        int position = 0;
        foreach (var source in p.Alive)
        {
            foreach (var a in Stats.AuraCarriers(db, p, source))
            {
                if (a.Ability.Aura is not { } aura) continue;
                int ts = a.Source == AbilitySource.Enchantment ? p.GetInstance(a.SourceInstanceId!.Value).Timestamp : source.Timestamp;
                list.Add(new AuraEffect(source, aura, ts, 0, position, a.Index));
            }
            position++;
        }
        for (int slot = 0; slot < p.Field.Count; slot++)
        {
            if (p.Field[slot] is not int ench) continue;
            var inst = p.GetInstance(ench);
            var abilities = db.Get(inst.CardId).Abilities;
            for (int i = 0; i < abilities.Count; i++)
                if (abilities[i].Aura is { } aura) list.Add(new AuraEffect(null, aura, inst.Timestamp, 1, slot, i));
        }
        return list;
    }

    private sealed record Resolution(
        Dictionary<Creature, List<string>> Keywords,
        Dictionary<AuraEffect, List<Creature>> Sets);

    private static Resolution Resolve(CardDatabase db, PlayerState p, List<AuraEffect> effects)
    {
        var ordered = effects
            .OrderBy(e => e.Timestamp).ThenBy(e => e.Kind).ThenBy(e => e.Position).ThenBy(e => e.AbilityIndex)
            .ToList();
        var keywords = new Dictionary<Creature, List<string>>(ReferenceEqualityComparer.Instance);
        foreach (var c in p.Alive) keywords[c] = Stats.OwnKeywords(db, p, c);
        var sets = new Dictionary<AuraEffect, List<Creature>>(ReferenceEqualityComparer.Instance);

        var remaining = ordered.Where(e => e.Aura.Keywords.Count > 0).ToList();
        while (remaining.Count > 0)
        {
            AuraEffect? pick = null;
            foreach (var e in remaining)
            {
                if (!DependsOnAny(db, p, e, remaining, keywords)) { pick = e; break; }
            }
            // Петля зависимостей — по timestamp.
            pick ??= remaining[0];
            var set = SetOf(db, p, pick, keywords);
            sets[pick] = set;
            foreach (var c in set) AddKeywords(keywords[c], c, pick.Aura.Keywords);
            remaining.Remove(pick);
        }
        foreach (var e in ordered)
            if (!sets.ContainsKey(e)) sets[e] = SetOf(db, p, e, keywords);
        return new Resolution(keywords, sets);
    }

    /// <summary>e зависит от f, если применение f меняет, на кого действует e.</summary>
    private static bool DependsOnAny(CardDatabase db, PlayerState p, AuraEffect e, List<AuraEffect> remaining,
        Dictionary<Creature, List<string>> keywords)
    {
        if (!e.DependsOnKeywords) return false;
        var current = SetOf(db, p, e, keywords);
        foreach (var f in remaining)
        {
            if (ReferenceEquals(f, e)) continue;
            var applied = new Dictionary<Creature, List<string>>(ReferenceEqualityComparer.Instance);
            foreach (var c in p.Alive) applied[c] = new List<string>(keywords[c]);
            foreach (var c in SetOf(db, p, f, keywords)) AddKeywords(applied[c], c, f.Aura.Keywords);
            if (!SetOf(db, p, e, applied).SequenceEqual(current)) return true;
        }
        return false;
    }

    /// <summary>На кого действует аура при данных ключевых словах; keywords == null — фильтров по словам нет.</summary>
    private static List<Creature> SetOf(CardDatabase db, PlayerState p, AuraEffect e, Dictionary<Creature, List<string>>? keywords)
    {
        var s = e.Aura.Target;
        if (s.IsRef)
            return s.Ref == Registry.Refs.Self && e.Source is { IsAlive: true } self ? new List<Creature> { self } : new List<Creature>();
        var candidates = Selectors.Filter(db, p, e.Source, s,
            (_, _, c) => keywords != null ? keywords[c] : Stats.OwnKeywords(db, p, c)).ToList();
        return Selectors.PickFixed(p, e.Source, s, candidates);
    }

    /// <summary>Потерянные в бою слова (потраченный Щит) аура не возвращает.</summary>
    private static void AddKeywords(List<string> target, Creature c, IEnumerable<string> keywords)
    {
        foreach (var k in keywords)
            if (!target.Contains(k) && !(c.RemovedKeywords?.Contains(k) ?? false)) target.Add(k);
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
