namespace Game.Client;

/// <summary>Ключи строк интерфейса (content/strings.json, раздел ui). Тест проверяет, что все есть в данных.</summary>
public static class UiKeys
{
    public const string Title = "title";
    public const string NewMatch = "new_match";
    public const string PlayerDeck = "player_deck";
    public const string BotDeck = "bot_deck";
    public const string Seed = "seed";
    public const string Start = "start";
    public const string Restart = "restart";
    public const string SaveReplay = "save_replay";
    public const string LoadReplay = "load_replay";
    public const string ReplaySaved = "replay_saved";
    public const string ReplayMode = "replay_mode";
    public const string ReplayNext = "replay_next";
    public const string ReplayDone = "replay_done";
    public const string Round = "round";
    public const string You = "you";
    public const string Opponent = "opponent";
    public const string Hp = "hp";
    public const string Mana = "mana";
    public const string ManaBonus = "mana_bonus";
    public const string DeckCount = "deck_count";
    public const string DiscardCount = "discard_count";
    public const string ExileCount = "exile_count";
    public const string HandCount = "hand_count";
    public const string Hand = "hand";
    public const string Board = "board";
    public const string Field = "field";
    public const string BattleZone = "battle_zone";
    public const string EmptySlot = "empty_slot";
    public const string CardBack = "card_back";
    public const string Fight = "fight";
    public const string NextRound = "next_round";
    public const string Skip = "skip";
    public const string Speed = "speed";
    public const string ChooseTarget = "choose_target";
    public const string ChoosePosition = "choose_position";
    public const string Cancel = "cancel";
    public const string Ability = "ability";
    public const string Used = "used";
    public const string DropRecall = "drop_here_recall";
    public const string DropDismiss = "drop_here_dismiss";
    public const string DiscardTitle = "discard_title";
    public const string Close = "close";
    public const string CombatLog = "combat_log";
    public const string ResultWin = "result_win";
    public const string ResultLoss = "result_loss";
    public const string ResultDraw = "result_draw";
    public const string CombatWin = "combat_win";
    public const string CombatLoss = "combat_loss";
    public const string CombatDraw = "combat_draw";
    public const string PlanningHint = "planning_hint";
    public const string BotReady = "bot_ready";
    public const string Error = "error";
    public const string AttackHealth = "attack_health";
    public const string Cost = "cost";
    public const string EnchantedBy = "enchanted_by";
    public const string KeywordsTitle = "keywords_title";

    public static readonly IReadOnlyList<string> All = typeof(UiKeys)
        .GetFields()
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();
}
