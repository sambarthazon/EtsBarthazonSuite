# Ets BARTHAZON — Suite d'outils internes

Dépôt regroupant les logiciels internes des Ets BARTHAZON (mécanique générale, usinage à façon). Toutes les applications sont servies par **un seul portail web** (une installation, une connexion, une base de données, une sauvegarde) mais sont développées comme des briques autonomes.

| Dossier | Contenu | État |
|---|---|---|
| [`Socle/`](Socle/) | Commun à toutes les applications : thème, composants, données, référentiel (clients, matières, machines, utilisateurs) | Stable |
| [`Annuaire/`](Annuaire/) | Annuaire de pièces | Version 1 |
| [`Stock/`](Stock/) | Gestion des stocks (matières, bruts, en-cours, pièces finies) | En cours |
| [`Portail/`](Portail/) | Assemblage, installation et exploitation ([installation](Portail/docs/installation.md)) | Version 1 |

Applications prévues ensuite comme modules du même portail : Commandes & livraisons, Pointage.

Développeurs : voir [`CLAUDE.md`](CLAUDE.md) (carte du dépôt, règles entre applications, commandes).
