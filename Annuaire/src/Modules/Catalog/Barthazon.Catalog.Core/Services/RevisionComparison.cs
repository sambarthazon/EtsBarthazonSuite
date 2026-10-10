using System.Globalization;
using Barthazon.Catalog.Contracts;

namespace Barthazon.Catalog.Core.Services;

/// <summary>État d'un élément dans la comparaison de deux indices.</summary>
public enum DiffState
{
    Same,
    Changed,
    Added,
    Removed,
}

/// <summary>Champ comparé (clé de libellé, valeur à gauche, valeur à droite).</summary>
public sealed record FieldDiff(string Field, string? Left, string? Right)
{
    public bool IsChanged => !string.Equals(Left ?? string.Empty, Right ?? string.Empty, StringComparison.Ordinal);
}

/// <summary>Élément de liste comparé (caractéristique, opération, document).</summary>
/// <param name="Kind">Type de l'élément (type d'opération, de document) pour un libellé traduit par l'interface.</param>
public sealed record ItemDiff(string Key, string? Left, string? Right, DiffState State, string? Kind = null);

/// <summary>Comparaison de deux indices côte à côte.</summary>
public sealed record RevisionComparison(
    RevisionDetails Left,
    RevisionDetails Right,
    IReadOnlyList<FieldDiff> Fields,
    IReadOnlyList<ItemDiff> Characteristics,
    IReadOnlyList<ItemDiff> Operations,
    IReadOnlyList<ItemDiff> Documents)
{
    public int ChangeCount =>
        Fields.Count(f => f.IsChanged)
        + Characteristics.Count(c => c.State != DiffState.Same)
        + Operations.Count(o => o.State != DiffState.Same)
        + Documents.Count(d => d.State != DiffState.Same);
}

/// <summary>Calcule la comparaison (règles pures, testables).</summary>
public static class RevisionComparer
{
    public static RevisionComparison Compare(RevisionDetails left, RevisionDetails right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var fields = new List<FieldDiff>
        {
            new(nameof(RevisionDetails.MaterialDisplay), left.MaterialDisplay, right.MaterialDisplay),
            new(nameof(RevisionDetails.MaterialNote), left.MaterialNote, right.MaterialNote),
            new(nameof(RevisionDetails.BlankShape), left.BlankShape?.ToString(), right.BlankShape?.ToString()),
            new(nameof(RevisionDetails.BlankDimensions), left.BlankDimensions, right.BlankDimensions),
            new(nameof(RevisionDetails.LengthMm), Num(left.LengthMm), Num(right.LengthMm)),
            new(nameof(RevisionDetails.WidthMm), Num(left.WidthMm), Num(right.WidthMm)),
            new(nameof(RevisionDetails.HeightMm), Num(left.HeightMm), Num(right.HeightMm)),
            new(nameof(RevisionDetails.DiameterMm), Num(left.DiameterMm), Num(right.DiameterMm)),
            new(nameof(RevisionDetails.MassKg), Num(left.MassKg), Num(right.MassKg)),
            new(nameof(RevisionDetails.GeneralTolerance), left.GeneralTolerance, right.GeneralTolerance),
            new(nameof(RevisionDetails.SurfaceRoughness), left.SurfaceRoughness, right.SurfaceRoughness),
            new(nameof(RevisionDetails.HeatTreatment), left.HeatTreatment, right.HeatTreatment),
            new(nameof(RevisionDetails.SurfaceTreatment), left.SurfaceTreatment, right.SurfaceTreatment),
            new(nameof(RevisionDetails.Hardness), left.Hardness, right.Hardness),
            new(nameof(RevisionDetails.Tooling), left.Tooling, right.Tooling),
            new(nameof(RevisionDetails.WorkshopNotes), left.WorkshopNotes, right.WorkshopNotes),
            new(nameof(RevisionDetails.InspectionNotes), left.InspectionNotes, right.InspectionNotes),
        };

        var characteristics = DiffByKey(
            left.Characteristics, right.Characteristics,
            c => c.Name.Trim().ToUpperInvariant(),
            c => c.Name,
            c => $"{c.Value} {c.Unit}".Trim(),
            _ => null);

        var operations = DiffByKey(
            left.Operations, right.Operations,
            o => o.Number.ToString(CultureInfo.InvariantCulture),
            o => o.Number.ToString(CultureInfo.InvariantCulture),
            o => string.Join(" · ", new[] { o.MachineName, o.Description, Num(o.SetupTimeMin), Num(o.CycleTimeMin), o.NcProgramReference, o.Tooling, o.Subcontractor }.Where(s => !string.IsNullOrWhiteSpace(s))),
            o => o.Type.ToString());

        var documents = DiffByKey(
            left.Documents, right.Documents,
            d => $"{d.Type}|{d.Title.Trim().ToUpperInvariant()}",
            d => d.Title,
            d => d.Kind == DocumentKind.Paper ? d.PaperLocation : d.FileName,
            d => d.Type.ToString());

        return new RevisionComparison(left, right, fields, characteristics, operations, documents);
    }

    private static List<ItemDiff> DiffByKey<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, Func<T, string> key, Func<T, string> label, Func<T, string?> value, Func<T, string?> kind)
    {
        var result = new List<ItemDiff>();
        var rightByKey = right.GroupBy(key).ToDictionary(g => g.Key, g => g.First());
        var leftKeys = new HashSet<string>();
        foreach (var item in left)
        {
            var k = key(item);
            if (!leftKeys.Add(k))
            {
                continue;
            }

            if (rightByKey.TryGetValue(k, out var other))
            {
                var l = value(item);
                var r = value(other);
                result.Add(new ItemDiff(label(item), l, r, string.Equals(l, r, StringComparison.Ordinal) && kind(item) == kind(other) ? DiffState.Same : DiffState.Changed, kind(other)));
            }
            else
            {
                result.Add(new ItemDiff(label(item), value(item), null, DiffState.Removed, kind(item)));
            }
        }

        foreach (var item in right.Where(i => !leftKeys.Contains(key(i))))
        {
            result.Add(new ItemDiff(label(item), null, value(item), DiffState.Added, kind(item)));
        }

        return result;
    }

    private static string? Num(decimal? value) => value?.ToString("0.###", CultureInfo.CurrentCulture);
}
