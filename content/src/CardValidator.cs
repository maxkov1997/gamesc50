using System.Text.RegularExpressions;
using Game.Core;

namespace Game.Content;

/// <summary>
/// Смысловая проверка карт: имена из реестров ядра, ссылки на карты, правила контента 11.6
/// и изоляция сторон в подготовке (5.6).
/// </summary>
internal sealed class CardValidator
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private enum Owner { Creature, Field, Grants }

    /// <summary>Контекст, в котором срабатывает эффект.</summary>
    private sealed record EffectContext(
        Phase Phase,
        bool HasCreatureSource,
        bool HasChosen,
        IReadOnlyList<string> EventRefs,
        string? Condition,
        bool IsPlayTrigger);

    private readonly List<ValidationError> _errors;
    private readonly IReadOnlyList<CardDef> _cards;
    private readonly Dictionary<string, CardDef> _byId = new(StringComparer.Ordinal);

    public CardValidator(IReadOnlyList<CardDef> cards, List<ValidationError> errors)
    {
        _cards = cards;
        _errors = errors;
    }

    /// <summary>Возвращает карты без дубликатов id (первая побеждает).</summary>
    public List<CardDef> Validate(string source)
    {
        var unique = new List<CardDef>();
        foreach (var c in _cards)
        {
            string path = $"{source} ({c.Id})";
            if (!SnakeCase.IsMatch(c.Id))
                Err(ErrorCodes.InvalidId, path, $"id «{c.Id}» должен быть в snake_case");
            if (!_byId.TryAdd(c.Id, c))
                Err(ErrorCodes.DuplicateId, path, $"id «{c.Id}» уже занят другой картой");
            else
                unique.Add(c);
        }
        foreach (var c in unique) ValidateCard(c, $"{source} ({c.Id})");
        return unique;
    }

    private void Err(string code, string path, string message) => _errors.Add(new ValidationError(code, path, message));

    private void ValidateCard(CardDef c, string path)
    {
        switch (c.Type)
        {
            case CardType.Creature: ValidateCreature(c, path); break;
            case CardType.Spell:
                ValidateKeywords(c.Keywords, $"{path}.keywords", k => k.ForSpells, "заклинания");
                if (c.PlayTarget != null) ValidateTarget(c.PlayTarget, $"{path}.play_target", hasCreatureSource: false);
                ValidateEffects(c.Effects, $"{path}.effects",
                    new EffectContext(Phase.Planning, false, c.PlayTarget != null, Array.Empty<string>(), null, false));
                break;
            case CardType.BattleSpell:
                ValidateEffects(c.Effects, $"{path}.effects",
                    new EffectContext(Phase.Combat, false, false, Array.Empty<string>(), null, false));
                break;
            case CardType.Secret: ValidateSecret(c, path); break;
            case CardType.Enchantment: ValidateEnchantment(c, path); break;
        }

        if (c.Type != CardType.Creature && (c.Token || c.Id.StartsWith(Registry.TokenPrefix, StringComparison.Ordinal)))
            Err(ErrorCodes.TokenRule, path, "токеном может быть только существо");

        if ((c.Type == CardType.Spell || c.Type == CardType.BattleSpell || c.Type == CardType.Secret) && c.Effects.Count == 0)
            Err(ErrorCodes.MissingField, $"{path}.effects", "у карты нет эффектов");
    }

    private void ValidateCreature(CardDef c, string path)
    {
        bool prefixed = c.Id.StartsWith(Registry.TokenPrefix, StringComparison.Ordinal);
        if (c.Token && !prefixed)
            Err(ErrorCodes.TokenRule, $"{path}.id", $"id токена должен начинаться с «{Registry.TokenPrefix}»");
        if (!c.Token && prefixed)
            Err(ErrorCodes.TokenRule, $"{path}.token", $"карта с префиксом «{Registry.TokenPrefix}» должна быть токеном (token: true)");
        if (c.Token && c.Cost != 0)
            Err(ErrorCodes.TokenRule, $"{path}.cost", "стоимость токена — 0");

        ValidateTags(c.Tags, $"{path}.tags");
        ValidateKeywords(c.Keywords, $"{path}.keywords", k => k.ForCreatures, "существа");
        ValidateAbilities(c.Abilities, $"{path}.abilities", Owner.Creature, c.Cost);
    }

    private void ValidateSecret(CardDef c, string path)
    {
        var cond = c.Condition!;
        var spec = Registry.FindCondition(cond.Event);
        if (spec == null)
        {
            Err(ErrorCodes.UnknownCondition, $"{path}.condition.event", $"неизвестное условие секрета «{cond.Event}»");
            return;
        }
        if (cond.Target != null)
        {
            string fp = $"{path}.condition.target";
            if (spec.EventRefs.Count == 0)
                Err(ErrorCodes.FieldNotAllowed, fp, $"у условия «{cond.Event}» нет участника, которого можно отфильтровать");
            CheckKeyword(cond.Target.Keyword, fp + ".keyword");
            CheckKeyword(cond.Target.NoKeyword, fp + ".no_keyword");
            CheckTag(cond.Target.Tag, fp + ".tag");
        }
        ValidateEffects(c.Effects, $"{path}.effects",
            new EffectContext(Phase.Combat, false, false, spec.EventRefs, cond.Event, false));
    }

    private void ValidateEnchantment(CardDef c, string path)
    {
        if (c.Attach == AttachKind.Creature)
        {
            if (c.PlayTarget == null) Err(ErrorCodes.MissingField, $"{path}.play_target", "чарам на существо нужна цель play_target");
            else ValidateTarget(c.PlayTarget, $"{path}.play_target", hasCreatureSource: false);
            if (c.Grants == null) Err(ErrorCodes.MissingField, $"{path}.grants", "чарам на существо нужно поле grants");
            if (c.Abilities.Count > 0) Err(ErrorCodes.FieldNotAllowed, $"{path}.abilities", "у чар на существо способности задаются в grants.abilities");
            if (c.Grants != null)
            {
                // 7.4, 11.6: чары дают только атаку и статичные ключевые слова (и способности).
                ValidateKeywords(c.Grants.Keywords, $"{path}.grants.keywords", k => k.ForCreatures, "существа");
                RequireStatic(c.Grants.Keywords, $"{path}.grants.keywords");
                ValidateAbilities(c.Grants.Abilities, $"{path}.grants.abilities", Owner.Grants, c.Cost);
            }
        }
        else if (c.Attach == AttachKind.Field)
        {
            if (c.PlayTarget != null) Err(ErrorCodes.FieldNotAllowed, $"{path}.play_target", "чары на поле не выбирают цель");
            if (c.Grants != null) Err(ErrorCodes.FieldNotAllowed, $"{path}.grants", "grants — только у чар на существо");
            if (c.Abilities.Count == 0) Err(ErrorCodes.MissingField, $"{path}.abilities", "у чар на поле нет способностей");
            ValidateAbilities(c.Abilities, $"{path}.abilities", Owner.Field, c.Cost);
        }
    }

    private void ValidateAbilities(IReadOnlyList<AbilityDef> abilities, string path, Owner owner, int cardCost)
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            var a = abilities[i];
            string ap = $"{path}[{i}]";
            var t = Registry.FindTrigger(a.Trigger);
            if (t == null)
            {
                Err(ErrorCodes.UnknownTrigger, $"{ap}.trigger", $"неизвестный триггер «{a.Trigger}»");
                continue;
            }
            bool creatureSource = owner != Owner.Field;
            if (!creatureSource && t.SelfCreature)
                Err(ErrorCodes.ContentRule, $"{ap}.trigger", $"триггер «{t.Id}» бывает только у существа");

            if (t.NeedsCount && a.Count == null) Err(ErrorCodes.MissingField, $"{ap}.count", $"триггеру «{t.Id}» нужен count");
            if (!t.NeedsCount && a.Count != null) Err(ErrorCodes.FieldNotAllowed, $"{ap}.count", $"у триггера «{t.Id}» нет count");
            if (a.Count is < 1) Err(ErrorCodes.InvalidValue, $"{ap}.count", "count должен быть не меньше 1");
            if (t.NeedsCost && a.Cost == null) Err(ErrorCodes.MissingField, $"{ap}.cost", $"триггеру «{t.Id}» нужен cost");
            if (!t.NeedsCost && a.Cost != null) Err(ErrorCodes.FieldNotAllowed, $"{ap}.cost", $"у триггера «{t.Id}» нет cost");
            if (a.Cost is < 0) Err(ErrorCodes.InvalidValue, $"{ap}.cost", "cost не бывает ниже 0");

            if (a.Target != null)
            {
                if (!t.AllowsTarget || !creatureSource)
                    Err(ErrorCodes.FieldNotAllowed, $"{ap}.target", $"у триггера «{t.Id}» игрок не выбирает цель");
                else
                    ValidateTarget(a.Target, $"{ap}.target", hasCreatureSource: true);
            }

            // 11.6: карта с «При розыгрыше» стоит не меньше 1 — иначе цикл отзыва бесконечен.
            if (t.IsPlayTrigger && cardCost < 1)
                Err(ErrorCodes.ContentRule, ap, "карта с «При розыгрыше» должна стоить не меньше 1 (11.6)");

            if (t.IsAura)
            {
                if (a.HasEffects) Err(ErrorCodes.FieldNotAllowed, $"{ap}.effects", "у ауры нет effects, только aura");
                if (a.Aura == null) Err(ErrorCodes.MissingField, $"{ap}.aura", "у ауры нет объекта aura");
                else ValidateAura(a.Aura, $"{ap}.aura", creatureSource);
            }
            else
            {
                if (a.Aura != null) Err(ErrorCodes.FieldNotAllowed, $"{ap}.aura", "aura бывает только у триггера «aura»");
                if (a.Effects.Count == 0) Err(ErrorCodes.MissingField, $"{ap}.effects", "у способности нет эффектов");
                ValidateEffects(a.Effects, $"{ap}.effects",
                    new EffectContext(t.Phase, creatureSource, a.Target != null, t.EventRefs, null, t.IsPlayTrigger));
            }
        }
    }

    private void ValidateAura(AuraDef aura, string path, bool creatureSource)
    {
        ValidateSelector(aura.Target, $"{path}.target",
            new EffectContext(Phase.Both, creatureSource, false, Array.Empty<string>(), null, false));
        ValidateKeywords(aura.Keywords, $"{path}.keywords", k => k.ForCreatures, "существа");
        // 7.4: аура даёт только атаку и статичные ключевые слова.
        RequireStatic(aura.Keywords, $"{path}.keywords");
    }

    private void ValidateEffects(IReadOnlyList<EffectDef> effects, string path, EffectContext ctx)
    {
        for (int i = 0; i < effects.Count; i++) ValidateEffect(effects[i], $"{path}[{i}]", ctx);
    }

    private void ValidateEffect(EffectDef e, string path, EffectContext ctx)
    {
        var op = Registry.FindOp(e.Op);
        if (op == null)
        {
            Err(ErrorCodes.UnknownOp, $"{path}.op", $"неизвестная операция «{e.Op}»");
            return;
        }

        foreach (var name in EffectDef.ParamNames)
        {
            var param = op.Params.FirstOrDefault(p => p.Name == name);
            if (param == null && e.Has(name))
                Err(ErrorCodes.FieldNotAllowed, $"{path}.{name}", $"у операции «{op.Id}» нет параметра «{name}»");
            else if (param is { Required: true } && !e.Has(name))
                Err(ErrorCodes.MissingField, $"{path}.{name}", $"операции «{op.Id}» нужен параметр «{name}»");
        }

        if (op.CombatOnly && ctx.Phase != Phase.Combat)
            Err(ErrorCodes.ContentRule, $"{path}.op", $"операция «{op.Id}» допустима только в эффектах боя: вне боя урона нет (11.6)");
        if (op.RequiredCondition != null && ctx.Condition != op.RequiredCondition)
            Err(ErrorCodes.ContentRule, $"{path}.op", $"операция «{op.Id}» допустима только в секрете с условием «{op.RequiredCondition}»");
        if (op.GrantsMana && ctx.IsPlayTrigger)
            // GDD-QUESTION: 11.6 «не возвращает ману» — трактуем строго: в «При розыгрыше» нельзя и бонус на следующий раунд (Q1).
            Err(ErrorCodes.ContentRule, $"{path}.op", "«При розыгрыше» не должен давать ману (11.6)");

        if (e.Target != null) ValidateSelector(e.Target, $"{path}.target", ctx);
        if (e.Count is < 1) Err(ErrorCodes.InvalidValue, $"{path}.count", "count должен быть не меньше 1");
        if (e.Amount is < 1) Err(ErrorCodes.InvalidValue, $"{path}.amount", "amount должен быть не меньше 1");
        CheckKeyword(e.Keyword, $"{path}.keyword");

        if (e.Duration != null)
        {
            if (!Registry.DurationList.Contains(e.Duration))
                Err(ErrorCodes.InvalidValue, $"{path}.duration", $"неизвестная длительность «{e.Duration}»");
            else if (ctx.Phase == Phase.Planning && e.Duration != Registry.Durations.Permanent)
                Err(ErrorCodes.ContentRule, $"{path}.duration", "эффекты подготовки всегда «навсегда» (7.3)");
        }

        if (e.Card != null)
        {
            if (!_byId.TryGetValue(e.Card, out var target))
                Err(ErrorCodes.UnknownCard, $"{path}.card", $"карта «{e.Card}» не существует");
            else if (target.Type != CardType.Creature)
                Err(ErrorCodes.InvalidValue, $"{path}.card", $"призвать можно только существо, а «{e.Card}» — не существо");
        }

        if (e.At != null)
        {
            if (!Registry.PlacementList.Contains(e.At))
                Err(ErrorCodes.InvalidValue, $"{path}.at", $"неизвестная позиция призыва «{e.At}»");
            // GDD-QUESTION: у заклинаний и секретов нет источника-существа — им доступен только «rightmost» (Q4).
            else if (e.At != Registry.Placements.Rightmost && !ctx.HasCreatureSource)
                Err(ErrorCodes.ContentRule, $"{path}.at", $"позиция «{e.At}» требует источника-существа");
        }
    }

    private void ValidateSelector(Selector s, string path, EffectContext ctx)
    {
        if (s.IsRef)
        {
            string r = s.Ref!;
            if (!Registry.RefList.Contains(r))
                Err(ErrorCodes.UnknownSelector, path, $"неизвестный селектор «{r}»");
            else if (r == Registry.Refs.Self && !ctx.HasCreatureSource)
                Err(ErrorCodes.ContentRule, path, "«self» допустим только у способности существа");
            else if (r == Registry.Refs.Chosen && !ctx.HasChosen)
                Err(ErrorCodes.ContentRule, path, "«chosen» требует цели, которую выбирает игрок (play_target или target)");
            else if ((r == Registry.Refs.EventTarget || r == Registry.Refs.EventAttacker) && !ctx.EventRefs.Contains(r))
                Err(ErrorCodes.ContentRule, path, $"у этого события нет участника «{r}»");
            return;
        }

        if (s.Pick == null) return; // ошибка «нет pick» уже записана разбором
        if (!Registry.PickList.Contains(s.Pick))
        {
            Err(ErrorCodes.UnknownSelector, $"{path}.pick", $"неизвестный способ выбора «{s.Pick}»");
            return;
        }
        if (s.Pick == Registry.Picks.Adjacent)
        {
            if (!ctx.HasCreatureSource) Err(ErrorCodes.ContentRule, $"{path}.pick", "«adjacent» требует источника-существа");
            if (s.Side != null) Err(ErrorCodes.FieldNotAllowed, $"{path}.side", "у «adjacent» сторона — сторона источника");
        }
        else if (s.Side == null)
        {
            Err(ErrorCodes.MissingField, $"{path}.side", "селектору нужна сторона side");
        }
        if (s.Side != null)
        {
            if (!Registry.SideList.Contains(s.Side))
                Err(ErrorCodes.InvalidValue, $"{path}.side", $"неизвестная сторона «{s.Side}»");
            else if (s.Side == Registry.Sides.Enemy && ctx.Phase != Phase.Combat)
                // 5.6: в подготовке эффект не читает и не меняет противника.
                Err(ErrorCodes.ContentRule, $"{path}.side", "эффект, который может сработать в подготовке, не может касаться противника (5.6)");
        }
        if (s.Count != null)
        {
            if (s.Pick == Registry.Picks.All || s.Pick == Registry.Picks.Adjacent)
                Err(ErrorCodes.FieldNotAllowed, $"{path}.count", $"у «{s.Pick}» нет count");
            else if (s.Count < 1)
                Err(ErrorCodes.InvalidValue, $"{path}.count", "count должен быть не меньше 1");
        }
        CheckTag(s.Tag, $"{path}.tag");
        CheckKeyword(s.Keyword, $"{path}.keyword");
        CheckKeyword(s.NoKeyword, $"{path}.no_keyword");
    }

    private void ValidateTarget(TargetSpec t, string path, bool hasCreatureSource)
    {
        // 5.4: в подготовке цель — только своё.
        if (t.Side != Registry.Sides.Friendly)
            Err(ErrorCodes.InvalidValue, $"{path}.side", $"цель игрока может быть только «friendly», а не «{t.Side}» (5.4)");
        CheckTag(t.Tag, $"{path}.tag");
        if (t.NotSelf && !hasCreatureSource)
            Err(ErrorCodes.FieldNotAllowed, $"{path}.not_self", "not_self имеет смысл только у способности существа");
    }

    private void ValidateTags(IReadOnlyList<string> tags, string path)
    {
        for (int i = 0; i < tags.Count; i++) CheckTag(tags[i], $"{path}[{i}]");
    }

    private void ValidateKeywords(IReadOnlyList<string> keywords, string path, Func<KeywordSpec, bool> allowed, string where)
    {
        for (int i = 0; i < keywords.Count; i++)
        {
            var spec = Registry.FindKeyword(keywords[i]);
            if (spec == null)
                Err(ErrorCodes.UnknownKeyword, $"{path}[{i}]", $"неизвестное ключевое слово «{keywords[i]}»");
            else if (!allowed(spec))
                Err(ErrorCodes.ContentRule, $"{path}[{i}]", $"ключевое слово «{spec.Id}» не бывает у {where}");
        }
    }

    private void RequireStatic(IReadOnlyList<string> keywords, string path)
    {
        for (int i = 0; i < keywords.Count; i++)
        {
            if (Registry.FindKeyword(keywords[i]) is { Static: false } spec)
                Err(ErrorCodes.ContentRule, $"{path}[{i}]", $"«{spec.Id}» нельзя дать аурой или чарами — только статичные ключевые слова (7.4)");
        }
    }

    private void CheckKeyword(string? keyword, string path)
    {
        if (keyword != null && Registry.FindKeyword(keyword) == null)
            Err(ErrorCodes.UnknownKeyword, path, $"неизвестное ключевое слово «{keyword}»");
    }

    private void CheckTag(string? tag, string path)
    {
        if (tag != null && !Registry.TagList.Contains(tag))
            Err(ErrorCodes.UnknownTag, path, $"неизвестное племя «{tag}»");
    }
}
