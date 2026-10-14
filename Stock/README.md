# Gestion des stocks — Ets BARTHAZON

Deuxième application du portail (version 1.0.0) : stock de matières et bruts, suivi des lancements de fabrication opération par opération, pièces finies par indice, inventaires, import / export Excel. Spécification : [`docs/specification.md`](docs/specification.md) ; décisions : [`docs/decisions.md`](docs/decisions.md) ; développeurs : [`CLAUDE.md`](CLAUDE.md).

| Pour… | Voir |
|---|---|
| Paramétrer le stock (emplacements, articles, stock initial, étiquettes) | [`Portail/docs/stock-demarrage.md`](../Portail/docs/stock-demarrage.md) |
| Installer, mettre à jour, sauvegarder | [`Portail/docs/`](../Portail/docs/) |
| Activer / désactiver | `Features:Stock` (tuile grisée si `false`, données conservées) |

Principes : mouvements immuables (une erreur s'annule), soldes tenus dans la même transaction et jamais négatifs, gamme figée au lancement, pièces de l'annuaire lues seulement par ses contrats, fonctionnement sans réseau dans l'atelier grâce à la fiche suiveuse papier.
