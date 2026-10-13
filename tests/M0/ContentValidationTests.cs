using Game.Content;
using Game.Core;
using Xunit;

namespace Game.Tests.M0;

/// <summary>M0: некорректные данные отклоняются с понятной ошибкой.</summary>
public class ContentValidationTests
{
    private const string Spark = """
        { "id": "tok_spark", "name": "Искорка", "type": "creature", "token": true,
          "cost": 0, "attack": 1, "health": 1, "tags": ["spirit"], "text": "" }
        """;

    private static GameDataException RejectCard(string cardJson)
    {
        var ex = Assert.Throws<GameDataException>(() => ContentLoader.LoadCards($"[{Spark}, {cardJson}]"));
        Assert.NotEmpty(ex.Errors);
        Assert.All(ex.Errors, e => Assert.False(string.IsNullOrWhiteSpace(e.Message)));
        return ex;
    }

    private static void AssertError(GameDataException ex, string code, string pathPart)
    {
        Assert.True(ex.Errors.Any(e => e.Code == code && e.Path.Contains(pathPart)),
            $"Ожидалась ошибка {code} в «{pathPart}», получено:{Environment.NewLine}{ex.Message}");
    }

    [Fact]
    public void M0_valid_minimal_cards_load()
    {
        var db = ContentLoader.LoadCards($"[{Spark}]");
        Assert.Single(db.Cards);
    }

    [Fact]
    public void M0_rejects_unknown_op()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "spell", "cost": 1, "text": "",
              "effects": [{ "op": "explode", "amount": 3 }] }
            """);
        AssertError(ex, ErrorCodes.UnknownOp, "(bad).effects[0].op");
        Assert.Contains("explode", ex.Message);
    }

    [Fact]
    public void M0_rejects_unknown_keyword()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "creature", "cost": 1, "attack": 1, "health": 1,
              "keywords": ["lifesteal"], "text": "" }
            """);
        AssertError(ex, ErrorCodes.UnknownKeyword, "(bad).keywords[0]");
        Assert.Contains("lifesteal", ex.Message);
    }

    [Fact]
    public void M0_rejects_unknown_keyword_in_effect()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "spell", "cost": 1, "text": "",
              "play_target": { "side": "friendly" },
              "effects": [{ "op": "give_keyword", "target": "chosen", "keyword": "stealth" }] }
            """);
        AssertError(ex, ErrorCodes.UnknownKeyword, "(bad).effects[0].keyword");
    }

    [Fact]
    public void M0_rejects_unknown_trigger()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
              "abilities": [{ "trigger": "on_sunrise", "effects": [{ "op": "draw", "count": 1 }] }] }
            """);
        AssertError(ex, ErrorCodes.UnknownTrigger, "(bad).abilities[0].trigger");
    }

    [Fact]
    public void M0_rejects_unknown_secret_condition()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "secret", "cost": 1, "text": "",
              "condition": { "event": "moon_rises" }, "effects": [{ "op": "draw", "count": 1 }] }
            """);
        AssertError(ex, ErrorCodes.UnknownCondition, "(bad).condition.event");
    }

    [Fact]
    public void M0_rejects_reference_to_missing_card()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "spell", "cost": 1, "text": "",
              "effects": [{ "op": "summon", "card": "tok_dragon", "count": 1, "at": "rightmost" }] }
            """);
        AssertError(ex, ErrorCodes.UnknownCard, "(bad).effects[0].card");
        Assert.Contains("tok_dragon", ex.Message);
    }

    [Fact]
    public void M0_rejects_unknown_selector()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "battle_spell", "cost": 1, "text": "",
              "effects": [{ "op": "damage", "target": "everyone", "amount": 1 }] }
            """);
        AssertError(ex, ErrorCodes.UnknownSelector, "(bad).effects[0].target");
    }

    [Fact]
    public void M0_rejects_unknown_and_misplaced_fields()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "spell", "cost": 1, "text": "", "attack": 3, "colour": "red",
              "effects": [{ "op": "draw", "count": 1 }] }
            """);
        AssertError(ex, ErrorCodes.FieldNotAllowed, "(bad).attack");
        AssertError(ex, ErrorCodes.UnknownField, "(bad).colour");
    }

    [Fact]
    public void M0_rejects_missing_required_fields_and_wrong_types()
    {
        var ex = RejectCard("""
            { "id": "bad", "type": "creature", "cost": "one", "attack": 1, "text": "" }
            """);
        AssertError(ex, ErrorCodes.MissingField, "(bad).name");
        AssertError(ex, ErrorCodes.MissingField, "(bad).health");
        AssertError(ex, ErrorCodes.InvalidType, "(bad).cost");
    }

    [Fact]
    public void M0_rejects_unknown_card_type()
    {
        var ex = RejectCard("""{ "id": "bad", "name": "Плохая", "type": "artifact", "cost": 1, "text": "" }""");
        AssertError(ex, ErrorCodes.UnknownCardType, "(bad).type");
    }

    [Fact]
    public void M0_rejects_duplicate_and_malformed_ids()
    {
        var ex = Assert.Throws<GameDataException>(() => ContentLoader.LoadCards($"[{Spark}, {Spark}]"));
        AssertError(ex, ErrorCodes.DuplicateId, "tok_spark");

        var ex2 = RejectCard("""{ "id": "BadCard", "name": "Плохая", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "" }""");
        AssertError(ex2, ErrorCodes.InvalidId, "BadCard");
    }

    [Fact]
    public void M0_rejects_broken_token_rules()
    {
        // Токен без префикса tok_.
        var ex = RejectCard("""{ "id": "imp", "name": "Бес", "type": "creature", "token": true, "cost": 0, "attack": 1, "health": 1, "text": "" }""");
        AssertError(ex, ErrorCodes.TokenRule, "(imp)");
        // Префикс tok_ без token: true.
        var ex2 = RejectCard("""{ "id": "tok_imp", "name": "Бес", "type": "creature", "cost": 0, "attack": 1, "health": 1, "text": "" }""");
        AssertError(ex2, ErrorCodes.TokenRule, "(tok_imp)");
    }

    [Fact]
    public void M0_rejects_invalid_json()
    {
        var ex = Assert.Throws<GameDataException>(() => ContentLoader.LoadCards("[{ \"id\": "));
        Assert.True(ex.HasCode(ErrorCodes.InvalidJson));
    }

    // --- Правила контента 11.6 и изоляция 5.6 ---

    [Fact]
    public void M0_rejects_damage_outside_combat()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "spell", "cost": 1, "text": "",
              "play_target": { "side": "friendly" },
              "effects": [{ "op": "damage", "target": "chosen", "amount": 1 }] }
            """);
        AssertError(ex, ErrorCodes.ContentRule, "(bad).effects[0].op");
    }

    [Fact]
    public void M0_rejects_enemy_effects_in_planning()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "creature", "cost": 2, "attack": 1, "health": 1, "text": "",
              "abilities": [{ "trigger": "on_death",
                "effects": [{ "op": "destroy", "target": { "pick": "random", "side": "enemy" } }] }] }
            """);
        AssertError(ex, ErrorCodes.ContentRule, "(bad).abilities[0].effects[0].target.side");
    }

    [Fact]
    public void M0_rejects_free_on_play_card()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "creature", "cost": 0, "attack": 1, "health": 1, "text": "",
              "abilities": [{ "trigger": "on_play", "effects": [{ "op": "draw", "count": 1 }] }] }
            """);
        AssertError(ex, ErrorCodes.ContentRule, "(bad).abilities[0]");
    }

    [Fact]
    public void M0_rejects_aura_with_non_static_keyword()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "creature", "cost": 2, "attack": 1, "health": 1, "text": "",
              "abilities": [{ "trigger": "aura",
                "aura": { "target": { "pick": "all", "side": "friendly" }, "keywords": ["shield"] } }] }
            """);
        AssertError(ex, ErrorCodes.ContentRule, "(bad).abilities[0].aura.keywords[0]");
    }

    [Fact]
    public void M0_rejects_chosen_without_player_target()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "spell", "cost": 1, "text": "",
              "effects": [{ "op": "buff", "target": "chosen", "attack": 1 }] }
            """);
        AssertError(ex, ErrorCodes.ContentRule, "(bad).effects[0].target");
    }

    [Fact]
    public void M0_rejects_wrong_op_params()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "spell", "cost": 1, "text": "",
              "effects": [{ "op": "draw", "amount": 2 }] }
            """);
        AssertError(ex, ErrorCodes.FieldNotAllowed, "(bad).effects[0].amount");
        AssertError(ex, ErrorCodes.MissingField, "(bad).effects[0].count");
    }

    [Fact]
    public void M0_rejects_self_trigger_on_field_enchantment()
    {
        var ex = RejectCard("""
            { "id": "bad", "name": "Плохая", "type": "enchantment", "attach": "field", "cost": 1, "text": "",
              "abilities": [{ "trigger": "on_attack", "effects": [{ "op": "draw", "count": 1 }] }] }
            """);
        AssertError(ex, ErrorCodes.ContentRule, "(bad).abilities[0].trigger");
    }

    // --- Колоды ---

    private static DeckList LoadDeck(params string[] cards)
    {
        string list = string.Join(", ", cards.Select(c => $"\"{c}\""));
        return ContentLoader.LoadDeck($$"""{ "id": "test", "name": "Тест", "cards": [{{list}}] }""",
            TestData.Data.Cards, TestData.Data.Config);
    }

    private static readonly string[] FifteenDistinct =
    {
        "ferret", "apprentice", "venom_toad", "watchdog", "wolf", "harpy", "iron_boar", "pack_leader",
        "blessing_of_might", "tempering", "holy_veil", "war_hymn", "mirror_shield", "riposte", "sanctuary",
    };

    [Fact]
    public void M0_valid_deck_loads()
    {
        var deck = LoadDeck(FifteenDistinct);
        Assert.Equal(15, deck.Cards.Count);
    }

    [Fact]
    public void M0_rejects_token_in_deck()
    {
        var cards = FifteenDistinct.Take(14).Append("tok_spark").ToArray();
        var ex = Assert.Throws<GameDataException>(() => LoadDeck(cards));
        AssertError(ex, ErrorCodes.DeckToken, "test");
        Assert.Contains("tok_spark", ex.Message);
    }

    [Fact]
    public void M0_rejects_extra_copies()
    {
        var cards = FifteenDistinct.Take(12).Concat(new[] { "ferret", "ferret" }).ToArray();
        var ex = Assert.Throws<GameDataException>(() => LoadDeck(cards));
        AssertError(ex, ErrorCodes.DeckCopies, "test");
        Assert.Contains("ferret", ex.Message);
        Assert.Single(ex.Errors);
    }

    [Fact]
    public void M0_rejects_deck_size_out_of_bounds()
    {
        var small = Assert.Throws<GameDataException>(() => LoadDeck(FifteenDistinct.Take(9).ToArray()));
        AssertError(small, ErrorCodes.DeckSize, "test");

        var big = FifteenDistinct.Concat(FifteenDistinct).Append("gate_guard").ToArray(); // 31 карта
        var ex = Assert.Throws<GameDataException>(() => LoadDeck(big));
        AssertError(ex, ErrorCodes.DeckSize, "test");
    }

    [Fact]
    public void M0_rejects_unknown_card_in_deck()
    {
        var cards = FifteenDistinct.Take(14).Append("dragon").ToArray();
        var ex = Assert.Throws<GameDataException>(() => LoadDeck(cards));
        AssertError(ex, ErrorCodes.UnknownCard, "test");
    }

    [Fact]
    public void M0_create_match_rejects_invalid_deck()
    {
        var good = TestData.Data.GetDeck("pack");
        var bad = new DeckList { Id = "bad", Name = "Плохая", Cards = good.Cards.Take(14).Append("tok_gear").ToList() };
        var ex = Assert.Throws<GameDataException>(() =>
            Match.CreateMatch(TestData.Data, good, bad, 1, PlanningMode.OpponentFirst));
        Assert.True(ex.HasCode(ErrorCodes.DeckToken));
        Assert.Contains("сторона B", ex.Message);
    }

    // --- Конфиг ---

    [Fact]
    public void M0_rejects_incomplete_or_unknown_config()
    {
        var ex = Assert.Throws<GameDataException>(() => ContentLoader.LoadConfig("""{ "HERO_HP": 40, "HERO_MP": 5 }"""));
        AssertError(ex, ErrorCodes.UnknownField, "HERO_MP");
        AssertError(ex, ErrorCodes.MissingField, "BOARD_SIZE");
    }
}
