# Changement de thème (couleurs, logo, nom)

## Couleurs : un seul fichier

Toutes les couleurs de l'application sont définies dans **un seul fichier** :

```
Socle/src/Barthazon.Shared.UI/Theme/BrandTokens.cs
```

Il contient deux jeux de couleurs, `Light` (mode clair) et `Dark` (mode sombre), utilisés à la fois par MudBlazor (boutons, menus, champs) et par les feuilles de style (variables CSS `--bz-primary`, `--bz-accent`…). Aucune couleur n'est écrite ailleurs : un test automatique (`ThemeRulesTests`) échoue si une couleur en dur apparaît dans un fichier `.razor`, `.css`, `.cs` ou `.js`.

| Jeton | Usage |
|---|---|
| `Primary` / `PrimaryContrast` | Couleur principale (barre du haut, boutons principaux, liens) et texte posé dessus |
| `Accent` / `AccentContrast` | Couleur d'accent (logo, favoris, mises en avant) et texte posé dessus |
| `AccentText` | Accent utilisé **comme couleur de texte** (assez foncé pour être lisible sur fond clair) |
| `Background`, `Surface`, `SurfaceAlt` | Fond de page, cartes, zones secondaires |
| `Text`, `TextSecondary`, `Disabled`, `Lines` | Textes, textes secondaires, éléments inactifs, bordures |
| `Success`, `Warning`, `Error`, `Info` | Pastilles d'état (en vigueur, brouillon, remplacé…) et messages |
| `AppBar`, `AppBarText` | Barre du haut |
| `Highlight` | Surlignage des termes trouvés par la recherche |
| `Print` | Impression (fiche, étiquettes) : toujours noir sur blanc |

Les polices (`FontFamily`), les arrondis (`BorderRadius`) et la taille minimale des zones tactiles (`TouchTarget`, 48 px pour l'atelier) sont dans le même fichier.

### Procédure

1. Modifier les valeurs voulues dans `BrandTokens.cs`, **en mode clair et en mode sombre**.
2. Vérifier les contrastes (accessibilité, niveau AA) : au moins **4,5:1** entre un texte et son fond, 3:1 pour les grands textes et les icônes. Un vérificateur de contraste (par exemple celui intégré aux outils de développement du navigateur) suffit. C'est pour cette raison que `Warning` et `AccentText` sont plus foncés que les teintes d'origine en mode clair.
3. Lancer l'application en développement (`dotnet run --project Portail/src/Barthazon.Portal`, depuis la racine du dépôt), contrôler le portail, la liste des pièces, une fiche (pastilles d'état, bandeau d'indice remplacé) en mode clair **et** sombre (bouton lune / soleil en haut à droite).
4. `dotnet test Barthazon.sln`, puis publier et installer la nouvelle version (voir [Mise à jour](mise-a-jour.md)).

## Logo

Le logo est `Portail/src/Barthazon.Portal/wwwroot/img/logo.svg` (utilisé dans la barre du haut, sur le portail et comme icône d'onglet). Le remplacer par un fichier SVG (de préférence carré) du même nom, ou indiquer un autre fichier dans la configuration :

```json
"App": { "LogoUrl": "img/mon-logo.png" }
```

Un logo placé directement dans le dossier `wwwroot/img` de l'installation est perdu à la mise à jour : le mettre dans le dépôt (`Portail/src/Barthazon.Portal/wwwroot/img/`) pour qu'il soit publié avec le programme.

## Nom de l'application et de l'entreprise

Sans recompiler, dans `appsettings.Production.json` :

```json
"App": {
  "Name": "Barthazon — Outils atelier",
  "CompanyName": "Ets BARTHAZON"
}
```

puis redémarrer le service. Le nom affiché peut aussi être modifié par un administrateur dans *Administration → Paramètres*.

## Textes de l'interface

Les libellés sont dans les fichiers de ressources `Resources/*.resx` de chaque projet (par exemple `Annuaire/src/Barthazon.Catalog.UI/Resources/CatalogTexts.resx`). Ils se modifient avec n'importe quel éditeur de texte ; recompiler ensuite.
