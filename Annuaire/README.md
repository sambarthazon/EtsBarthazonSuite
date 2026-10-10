# Annuaire de pièces — Ets BARTHAZON

Outil interne pour retrouver en quelques secondes une pièce usinée, ses plans, son client, son **indice en vigueur** et sa gamme de fabrication, depuis un ordinateur, une tablette ou un téléphone, sur le réseau local de l'usine (sans accès Internet).

C'est la première application d'un **portail** qui accueillera ensuite les stocks, les commandes et livraisons, et le pointage.

- Spécification : [docs/specification.md](docs/specification.md)
- Conventions et commandes pour les développeurs : [CLAUDE.md](CLAUDE.md)
- Décisions de réalisation : [docs/decisions.md](docs/decisions.md)

## Démarrage rapide (développement)

```bash
cd Annuaire
dotnet run --project src/Host/Barthazon.Portal
# puis ouvrir http://localhost:5080
```

## Technique

C# / .NET 10, Blazor Web App (Interactive Server), MudBlazor 9, EF Core 10 (SQLite, PostgreSQL plus tard), ClosedXML, QRCoder, Serilog.
