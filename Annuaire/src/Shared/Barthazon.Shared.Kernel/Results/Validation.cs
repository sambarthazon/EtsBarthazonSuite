using System.Net.Mail;

namespace Barthazon.Shared.Kernel.Results;

/// <summary>
/// Petit outil de validation des entrées dans les services applicatifs (sans dépendance externe).
/// Les messages sont fournis par l'appelant (ressources du module).
/// </summary>
public sealed class Validation
{
    private readonly List<Error> _errors = [];

    public bool IsValid => _errors.Count == 0;

    public IReadOnlyList<Error> Errors => _errors;

    public Validation Required(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, message);
        }

        return this;
    }

    public Validation Required<T>(T? value, string field, string message)
        where T : struct
    {
        if (value is null || EqualityComparer<T>.Default.Equals(value.Value, default))
        {
            Add(field, message);
        }

        return this;
    }

    public Validation MaxLength(string? value, int max, string field, string message)
    {
        if (value is not null && value.Trim().Length > max)
        {
            Add(field, message);
        }

        return this;
    }

    public Validation Positive(decimal? value, string field, string message)
    {
        if (value is < 0)
        {
            Add(field, message);
        }

        return this;
    }

    public Validation Email(string? value, string field, string message)
    {
        if (!string.IsNullOrWhiteSpace(value) && !MailAddress.TryCreate(value.Trim(), out _))
        {
            Add(field, message);
        }

        return this;
    }

    public Validation When(bool condition, string field, string message)
    {
        if (condition)
        {
            Add(field, message);
        }

        return this;
    }

    public Validation Add(string? field, string message)
    {
        _errors.Add(Error.Validation(message, field));
        return this;
    }

    public Result ToResult() => IsValid ? Result.Success() : Result.Failure(_errors);
}

/// <summary>Outils de saisie.</summary>
public static class Inputs
{
    /// <summary>Texte nettoyé (espaces en trop), null si vide.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Texte obligatoire nettoyé.</summary>
    public static string CleanRequired(string? value) => value?.Trim() ?? string.Empty;
}
