using System.Text.Json;
using Game.Core;

namespace Game.Content;

/// <summary>Строгое чтение JSON-объекта: типы полей, обязательные поля, неизвестные поля.</summary>
internal sealed class ObjectReader
{
    private readonly JsonElement _el;
    private readonly List<ValidationError> _errors;

    public string Path { get; }

    public ObjectReader(JsonElement el, string path, List<ValidationError> errors)
    {
        _el = el;
        Path = path;
        _errors = errors;
    }

    public void Error(string code, string message, string? field = null) =>
        _errors.Add(new ValidationError(code, field == null ? Path : $"{Path}.{field}", message));

    public bool Has(string name) => _el.TryGetProperty(name, out _);

    /// <summary>
    /// Каждое поле объекта должно быть в known; известное, но не разрешённое для этого вида — отдельная ошибка.
    /// Порядок обхода — порядок в документе.
    /// </summary>
    public void CheckFields(IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> known)
    {
        foreach (var prop in _el.EnumerateObject())
        {
            if (allowed.Contains(prop.Name)) continue;
            if (known.Contains(prop.Name))
                Error(ErrorCodes.FieldNotAllowed, $"поле «{prop.Name}» недопустимо здесь", prop.Name);
            else
                Error(ErrorCodes.UnknownField, $"неизвестное поле «{prop.Name}»", prop.Name);
        }
    }

    public string? String(string name, bool required)
    {
        if (!_el.TryGetProperty(name, out var v))
        {
            if (required) Error(ErrorCodes.MissingField, $"нет обязательного поля «{name}»", name);
            return null;
        }
        if (v.ValueKind != JsonValueKind.String)
        {
            Error(ErrorCodes.InvalidType, $"поле «{name}» должно быть строкой", name);
            return null;
        }
        return v.GetString();
    }

    public int? Int(string name, bool required, int? min = null)
    {
        if (!_el.TryGetProperty(name, out var v))
        {
            if (required) Error(ErrorCodes.MissingField, $"нет обязательного поля «{name}»", name);
            return null;
        }
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int i))
        {
            Error(ErrorCodes.InvalidType, $"поле «{name}» должно быть целым числом", name);
            return null;
        }
        if (min != null && i < min)
        {
            Error(ErrorCodes.InvalidValue, $"поле «{name}» должно быть не меньше {min}, а равно {i}", name);
            return null;
        }
        return i;
    }

    public bool? Bool(string name)
    {
        if (!_el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            Error(ErrorCodes.InvalidType, $"поле «{name}» должно быть true или false", name);
            return null;
        }
        return v.GetBoolean();
    }

    public List<string> StringArray(string name, bool required = false)
    {
        var result = new List<string>();
        if (!_el.TryGetProperty(name, out var v))
        {
            if (required) Error(ErrorCodes.MissingField, $"нет обязательного поля «{name}»", name);
            return result;
        }
        if (v.ValueKind != JsonValueKind.Array)
        {
            Error(ErrorCodes.InvalidType, $"поле «{name}» должно быть массивом строк", name);
            return result;
        }
        int i = 0;
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) result.Add(item.GetString()!);
            else Error(ErrorCodes.InvalidType, "ожидалась строка", $"{name}[{i}]");
            i++;
        }
        return result;
    }

    public ObjectReader? Object(string name, bool required)
    {
        if (!_el.TryGetProperty(name, out var v))
        {
            if (required) Error(ErrorCodes.MissingField, $"нет обязательного поля «{name}»", name);
            return null;
        }
        if (v.ValueKind != JsonValueKind.Object)
        {
            Error(ErrorCodes.InvalidType, $"поле «{name}» должно быть объектом", name);
            return null;
        }
        return new ObjectReader(v, $"{Path}.{name}", _errors);
    }

    /// <summary>Массив объектов; элементы-не-объекты — ошибка.</summary>
    public List<ObjectReader> ObjectArray(string name, bool required)
    {
        var result = new List<ObjectReader>();
        if (!_el.TryGetProperty(name, out var v))
        {
            if (required) Error(ErrorCodes.MissingField, $"нет обязательного поля «{name}»", name);
            return result;
        }
        if (v.ValueKind != JsonValueKind.Array)
        {
            Error(ErrorCodes.InvalidType, $"поле «{name}» должно быть массивом", name);
            return result;
        }
        int i = 0;
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object) result.Add(new ObjectReader(item, $"{Path}.{name}[{i}]", _errors));
            else Error(ErrorCodes.InvalidType, "ожидался объект", $"{name}[{i}]");
            i++;
        }
        return result;
    }

    /// <summary>Сырой элемент поля (для полей «строка или объект»).</summary>
    public bool TryGetRaw(string name, out JsonElement value) => _el.TryGetProperty(name, out value);

    public List<ValidationError> Errors => _errors;
}
