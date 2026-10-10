using Microsoft.AspNetCore.Components;

namespace Barthazon.Catalog.UI.Components;

/// <summary>Onglet de la fiche pièce (onglet sur ordinateur, panneau d'accordéon sur téléphone).</summary>
public sealed record PartTab(string Key, string Title, string Icon, RenderFragment Content, bool Disabled = false);
