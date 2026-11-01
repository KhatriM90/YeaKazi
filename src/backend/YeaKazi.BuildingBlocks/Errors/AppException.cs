namespace YeaKazi.BuildingBlocks.Errors;

/// <summary>Base for expected failures. Code is a stable, documented error code (API-01).</summary>
public abstract class AppException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class NotFoundException(string entity)
    : AppException(404, "not_found", $"{entity} was not found.");

public sealed class ForbiddenException(string message = "You do not have permission for this action.")
    : AppException(403, "forbidden", message);

public sealed class UnauthorizedException(string code, string message)
    : AppException(401, code, message);

public sealed class ConflictException(string code, string message)
    : AppException(409, code, message);

public sealed class BusinessRuleException(string code, string message)
    : AppException(422, code, message);

public sealed class ValidationException(IDictionary<string, string[]> errors)
    : AppException(400, "validation_failed", "One or more fields are invalid.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Collects field errors and throws a single ValidationException.</summary>
public sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.OrdinalIgnoreCase);

    public Validator Required(string field, string? value, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, "This field is required.");
        }
        else if (value.Trim().Length > maxLength)
        {
            Add(field, $"Must be at most {maxLength} characters.");
        }

        return this;
    }

    public Validator Optional(string field, string? value, int maxLength = 200)
    {
        if (value is not null && value.Trim().Length > maxLength)
        {
            Add(field, $"Must be at most {maxLength} characters.");
        }

        return this;
    }

    public Validator Must(bool condition, string field, string message)
    {
        if (!condition)
        {
            Add(field, message);
        }

        return this;
    }

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
    }

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw new ValidationException(_errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
        }
    }
}
