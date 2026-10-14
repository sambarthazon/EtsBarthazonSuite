# Démarrer avec le stock

Ce guide décrit le paramétrage initial de l'application **Stock** : emplacements, articles, pièces suivies, reprise du stock existant et impression des étiquettes. Il suppose le portail installé ([installation](installation.md)) et un compte **éditeur** ou **administrateur**.

> Le stock est actif par défaut (`Features:Stock = true`). Pour le masquer pendant le paramétrage, mettre `"Features": { "Stock": false }` dans `appsettings.Production.json` et redémarrer : rien n'est perdu, l'annuaire n'est pas touché.

## 1. Vérifier les réglages

*Administration → Stock* affiche la version de l'application et les réglages de la section `Stock` d'`appsettings.json` (numérotation des articles et des lancements, emplacements par défaut, antidatage, délai d'annulation). Ils se modifient dans `appsettings.Production.json`, puis en redémarrant le service.

## 2. Emplacements

*Référentiels → Emplacements*. Trois emplacements sont créés à l'installation : **MAG** (magasin), **ATELIER** (pièces en cours) et **EXPE** (expédition). Ajouter les rayonnages, armoires et zones utiles (ex. `RAYON-A`), et un emplacement de type **Sous-traitant** par sous-traitant (traitement thermique, rectification…) : les pièces envoyées chez lui y sont transférées, puis reviennent à l'atelier à la déclaration de l'opération.

Gardez des codes courts : ils sont imprimés sur les étiquettes et tapés au téléphone.

## 3. Articles

*Stock → Articles → Nouvel article* : matières (barres, tubes, tôles… en m, kg, m² ou unités), bruts (débits, pièces moulées ou forgées) et fournitures. Renseigner la matière (référentiel), les dimensions, l'unité, le **seuil** (alerte « sous le seuil »), l'emplacement habituel et, si la matière est fournie par le client, le **propriétaire**. Cocher « suivi par lot » pour les matières dont on veut tracer la coulée.

Pour beaucoup d'articles, préférer l'import (section 5).

## 4. Pièces suivies et sources de brut

Une pièce de l'annuaire devient « suivie » dès son premier lancement ou depuis *Stock → Pièces*. Sur sa fiche (onglet **Stock** de la fiche pièce, ou *Stock → Pièces → la pièce*) :

- **Seuil** de pièces finies et **rangement habituel** ;
- **Sources de brut** : ce que consomme un lancement. Un article (ex. 0,423 m de rond Ø50 par pièce, l'aide de saisie calcule longueur de débit + trait de scie) ou une autre pièce, finie ou prise **après une opération** de sa gamme (semi-fini commun). Un même brut peut servir à plusieurs pièces ; une source peut être limitée à un indice.

## 5. Reprendre le stock existant

Trois façons, combinables :

1. **Import Excel** (*Stock → Inventaires → Importer le stock initial*, ou *Articles → Importer*). Télécharger le **modèle** : un classeur à trois feuilles.
   - « Articles » : code, désignation, type (Matière, Brut, Autre), matière, forme, dimensions, unité (u, m, kg, m²), propriétaire (code client), seuil, emplacement, quantité, lot, prix de référence. Un article peut occuper plusieurs lignes (plusieurs emplacements ou lots).
   - « Pièces finies » : référence de la pièce, indice (vide = indice en vigueur), emplacement, quantité, lot.
   - « En-cours » : référence, indice, n° de l'opération que les pièces **attendent**, quantité, emplacement. Chaque ligne crée un **lancement de reprise** (sans consommation) : n'importer cette feuille qu'une fois.

   L'aperçu liste les erreurs ligne par ligne et propose de créer les emplacements, matières et clients inconnus. Seules les lignes valides sont importées. La quantité d'une ligne **remplace** celle de la case (correction « Stock initial ») : réimporter les feuilles « Articles » et « Pièces finies » après correction ne double pas le stock.
2. **Inventaire sur un stock vide** : *Stock → Inventaires → Ouvrir un inventaire*, puis à chaque emplacement « Ajouter un objet trouvé » ; la validation crée le stock.
3. **Saisies une à une** : réceptions d'articles et entrées de pièces finies (*Saisie rapide*).

L'état du stock s'exporte à tout moment (*Articles → Exporter l'état du stock*) dans le même format que l'import.

## 6. Imprimer les étiquettes

*Stock → Articles → Imprimer les étiquettes* (et, depuis *Référentiels → Emplacements*, les étiquettes d'emplacements) : planche A4 de 3 × 8 étiquettes avec QR code. On peut décocher des étiquettes et sauter les premières cases d'une planche entamée. Le QR code d'un article ouvre la saisie rapide pré-remplie, celui d'un emplacement son contenu ; celui de la fiche suiveuse ouvre le lancement.

Les QR codes contiennent l'**adresse publique** du portail (*Administration → Paramètres*) : elle doit être joignable depuis les téléphones.

## 7. Au quotidien

- **Lancer** (éditeur) : *Lancements → Nouveau lancement* ; la gamme de l'indice est **figée** ; imprimer la **fiche suiveuse**, qui accompagne la caisse.
- **Déclarer** (tout compte connecté, rôle Lecteur suffisant) : depuis le lancement (QR de la fiche) ou, sans réseau dans l'atelier, en ressaisissant la fiche suiveuse au bureau (*Saisir la fiche suiveuse*, dates antidatées).
- **Corriger** : une erreur ne s'efface pas, elle s'**annule** (contre-mouvement visible dans le journal).
- **Inventaire** : ouvrir, imprimer la feuille de comptage, saisir les quantités emplacement par emplacement (téléphone ou ordinateur), valider : les écarts deviennent des corrections « Inventaire ».
- **Contrôle** : *Administration → Stock → Vérifier les soldes* recalcule les soldes à partir des mouvements (aucun écart attendu).
