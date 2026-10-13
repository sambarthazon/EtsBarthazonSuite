# Décisions de réalisation — Stock (et réorganisation du dépôt)

Choix faits pendant la réalisation lorsque la spécification laissait une marge d'interprétation (même format que `Annuaire/docs/decisions.md`).

| # | Sujet | Décision | Raison |
|---|---|---|---|
| 1 | Migrations communes pendant R1 | Au jalon R1, les projets de migrations existants (`Barthazon.Migrations.Sqlite` / `.Postgres`, schéma commun) sont placés provisoirement dans `Portail/src/` ; ils sont remplacés au jalon R2 par des migrations par application. Les tests du référentiel chargent provisoirement le module de l'annuaire (le modèle doit correspondre aux migrations communes). | R1 ne change pas le schéma ; R2 découpe les contextes. |
| 2 | Hôte de test générique | `Socle/tests/Support` expose `TestApplication.CreateAsync(configure, settings, modules)` et `TestApplicationBuilder.AddModule(persistance, services)` ; chaque application a son raccourci (`CatalogTestApp`). | Le support de test ne référence plus l'annuaire (6.2). |
| 3 | Version par application | Chaque dossier a un `Directory.Build.props` qui importe celui de la racine et fixe `<Version>` ; l'état du système affiche la version du socle, du portail et de chaque application (version de l'assemblage d'interface du module). | Section 6.1. |
