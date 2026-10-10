# Décisions de réalisation

Choix faits pendant la réalisation lorsque la spécification laissait une marge d'interprétation.

| # | Sujet | Décision | Raison |
|---|---|---|---|
| 1 | Emplacement | Le projet vit dans le sous-dossier `Annuaire/` du dépôt `EtsBarthazonSuite`. | Le dépôt regroupe plusieurs logiciels de l'entreprise. Les futures applications (stock, commandes, pointage) sont prévues comme **modules du même portail** (section 6.1) : elles s'ajouteront dans `Annuaire/src/Modules/`. Le dossier pourra être renommé (ex. `Portail/`) à ce moment-là. |
| 2 | FluentAssertions | Version 7.2 (licence Apache 2.0). | La version 8 est passée sous licence commerciale. |
| 3 | Validation | Validation écrite à la main dans les services `*.Core` (petit utilitaire du Kernel), sans FluentValidation. | La règle « `*.Core` ne référence que `Shared.Kernel` et des `*.Contracts` » exclut toute bibliothèque tierce dans le Core. |
| 4 | `IModule` et persistance | `IModule` (Kernel) décrit l'identité, le menu, l'assemblage des pages et les services ; les mappings EF Core sont déclarés par le projet `*.Infrastructure` du module (`IModulePersistence`). | L'interface (`*.UI`) ne doit pas connaître l'infrastructure : un seul objet ne peut pas porter les deux sans violer les règles de dépendance. |
| 5 | Modules de données partagées | `IModule.IsApplication = false` pour le Référentiel : pas de tuile sur le portail, menu « Données communes » visible depuis toutes les applications. | Les clients et référentiels sont communs à toutes les applications. |
| 6 | Mode sombre | Mémorisé dans un cookie `bz-theme` (lu au prérendu, pas de clignotement) ; à défaut, préférence du système. | |
| 7 | Textes d'interface | Fichiers `.resx` avec génération d'une classe fortement typée à la compilation (`Directory.Build.targets`). | Conforme à la section 15, sans outil Visual Studio. |
