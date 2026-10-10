namespace Barthazon.Shared.Kernel.Results;

/// <summary>Nature d'une erreur métier renvoyée par un service applicatif.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthenticated,
    Unexpected,
}

/// <summary>Erreur métier. <paramref name="Field"/> désigne le champ concerné (formulaire).</summary>
public sealed record Error(ErrorKind Kind, string Message, string? Field = null)
{
    public static Error Validation(string message, string? field = null) => new(ErrorKind.Validation, message, field);

    public static Error NotFound(string message) => new(ErrorKind.NotFound, message);

    public static Error Conflict(string message) => new(ErrorKind.Conflict, message);

    public static Error Forbidden(string message) => new(ErrorKind.Forbidden, message);

    public static Error Unauthenticated(string message) => new(ErrorKind.Unauthenticated, message);
}

/// <summary>Résultat d'une opération : succès, ou liste d'erreurs métier (pas d'exception pour les cas prévus).</summary>
public class Result
{
    private static readonly Result SuccessInstance = new([]);

    protected Result(IReadOnlyList<Error> errors)
    {
        Errors = errors;
    }

    public bool IsSuccess => Errors.Count == 0;

    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<Error> Errors { get; }

    /// <summary>Première erreur, pratique pour un message de notification.</summary>
    public Error? FirstError => Errors.Count > 0 ? Errors[0] : null;

    public bool HasError(ErrorKind kind) => Errors.Any(e => e.Kind == kind);

    public static Result Success() => SuccessInstance;

    public static Result Failure(params Error[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
        {
            throw new ArgumentException("Au moins une erreur est attendue.", nameof(errors));
        }

        return new Result(errors);
    }

    public static Result Failure(IEnumerable<Error> errors) => Failure(errors.ToArray());

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
}

/// <summary>Résultat porteur d'une valeur en cas de succès.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, IReadOnlyList<Error> errors)
        : base(errors)
    {
        _value = value;
    }

    /// <summary>Valeur (lève une exception si le résultat est un échec).</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Impossible de lire la valeur d'un résultat en échec.");

    public static Result<T> Success(T value) => new(value, []);

    public static new Result<T> Failure(params Error[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
        {
            throw new ArgumentException("Au moins une erreur est attendue.", nameof(errors));
        }

        return new Result<T>(default, errors);
    }

    public static new Result<T> Failure(IEnumerable<Error> errors) => Failure(errors.ToArray());

    public static implicit operator Result<T>(Error error) => Failure(error);

    public static implicit operator Result<T>(T value) => Success(value);
}
