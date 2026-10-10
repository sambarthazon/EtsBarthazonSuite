namespace Barthazon.Referential.Contracts;

/// <summary>Famille de matière.</summary>
public enum MaterialFamily
{
    Steel = 0,
    Stainless = 1,
    Aluminium = 2,
    CopperAlloy = 3,
    CastIron = 4,
    Plastic = 5,
    Titanium = 6,
    Other = 99,
}

/// <summary>Type de machine / poste.</summary>
public enum MachineType
{
    ConventionalLathe = 0,
    CncLathe = 1,
    ConventionalMill = 2,
    CncMill = 3,
    Drill = 4,
    Grinder = 5,
    Saw = 6,
    Inspection = 7,
    Other = 99,
}
