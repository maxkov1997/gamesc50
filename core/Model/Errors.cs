namespace Game.Core;

/// <summary>Коды ошибок проверки данных — стабильны, по ним проверяют тесты.</summary>
public static class ErrorCodes
{
    public const string InvalidJson = "invalid_json";
    public const string InvalidType = "invalid_type";
    public const string InvalidValue = "invalid_value";
    public const string InvalidId = "invalid_id";
    public const string MissingField = "missing_field";
    public const string UnknownField = "unknown_field";
    public const string FieldNotAllowed = "field_not_allowed";
    public const string DuplicateId = "duplicate_id";
    public const string UnknownCardType = "unknown_card_type";
    public const string UnknownKeyword = "unknown_keyword";
    public const string UnknownTrigger = "unknown_trigger";
    public const string UnknownCondition = "unknown_condition";
    public const string UnknownOp = "unknown_op";
    public const string UnknownSelector = "unknown_selector";
    public const string UnknownTag = "unknown_tag";
    public const string UnknownCard = "unknown_card";
    public const string TokenRule = "token_rule";
    public const string ContentRule = "content_rule";
    public const string DeckSize = "deck_size";
    public const string DeckCopies = "deck_copies";
    public const string DeckToken = "deck_token";
}

/// <param name="Path">Где ошибка, например «cards.json[3] (ferret).abilities[0].trigger».</param>
public sealed record ValidationError(string Code, string Path, string Message)
{
    public override string ToString() => $"[{Code}] {Path}: {Message}";
}

/// <summary>Данные или колода не прошли проверку. Message перечисляет все ошибки.</summary>
public sealed class GameDataException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public GameDataException(IReadOnlyList<ValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public bool HasCode(string code) => Errors.Any(e => e.Code == code);

    private static string BuildMessage(IReadOnlyList<ValidationError> errors) =>
        $"Ошибки в данных ({errors.Count}):" + Environment.NewLine +
        string.Join(Environment.NewLine, errors.Select(e => "  " + e));
}
