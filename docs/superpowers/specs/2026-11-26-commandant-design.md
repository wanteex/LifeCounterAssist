---
title: Choix du commandant (Scryfall)
description: Le joueur choisit son commandant en saisissant son pseudo ; l'illustration sert de fond à sa tuile
ms.date: 2026-11-26
ms.topic: concept
---

## Intention

Rendre le tableau plus vivant et lisible dans la salle : chaque joueur choisit son commandant au moment où il entre son pseudo, et l'illustration de la carte devient le fond de sa tuile, sur `/ecran` et sur les téléphones.

## Objectifs

* Recherche de commandant dans les trois saisies de pseudo : création de partie, « Rejoindre » de l'accueil, `NameSheet` de la page de partie (rejoindre ou reprendre sa place).
* Illustration (`art_crop`) en fond de la tuile du joueur : écran de salle, carte « moi » et tuiles adverses sur téléphone.
* Crédit de l'artiste visible sur la tuile.

## Non-objectifs

* Commandants partenaires (deux cartes) : v2.
* Changer de commandant en cours de partie (hors reprise de place).
* Proxy ou cache serveur des appels Scryfall.

## Anti-objectifs

* Le choix du commandant ne doit jamais bloquer l'entrée en partie (facultatif, Scryfall indisponible = on rejoint quand même).
* Le serveur ne doit jamais relayer une URL d'image arbitraire vers l'écran de salle.
* PV illisibles à cause de l'illustration.

## Contraintes

* API Scryfall gratuite, sans clé ; appelée directement depuis le navigateur (CORS autorisé). 50 à 100 ms entre requêtes : saisie avec délai (~300 ms) et annulation des requêtes obsolètes.
* Afficher le nom de l'artiste, ne pas retoucher l'image (recadrage `object-fit: cover` accepté).
* Usage non commercial (Wizards Fan Content Policy).

## Approches

1. **Retenue : le client cherche sur Scryfall, le serveur stocke nom + URL + artiste après validation de l'URL.** Simple, pas de dépendance réseau côté serveur, une seule requête de recherche par frappe.
2. Le serveur stocke seulement l'id Scryfall et le client affiche via `api.scryfall.com/cards/{id}?format=image&version=art_crop`. Écartée : un appel API par affichage et par écran, et une redirection à chaque rendu.
3. Proxy serveur vers Scryfall. Écartée : plus de code, aucune valeur pour un usage en salle.

## Conception

### Backend

* Nouveau record `CommanderInfo(string Name, string ArtUrl, string Artist)` ; `PlayerState.Commander` nullable.
* `CreateGameRequest` et `JoinGameRequest` gagnent `CommanderInfo? Commander = null`.
* Validation (`DomainException("invalid_commander")`) : `ArtUrl` commence par `https://cards.scryfall.io/art_crop/`, nom 1 à 150 caractères, artiste 0 à 100 caractères, longueur d'URL ≤ 300.
* Rejoindre : nouveau siège = commandant fourni. Reprise de place = commandant fourni s'il y en a un, sinon on garde l'existant.
* Le commandant suit le clonage de l'état et apparaît dans `GameUpdated` et `ScreenChanged`.

### Frontend

* `CommanderPicker` : champ de recherche, délai ~300 ms à partir de 2 caractères, requête `cards/search?q=<texte> is:commander&order=edhrec`, 8 résultats max avec miniature `art_crop`, sélection affichée avec un bouton pour retirer. Cartes double face : `card_faces[0].image_uris`. Erreur ou réseau coupé : message « Recherche indisponible », formulaire toujours utilisable. Aucun résultat : message dédié.
* `api.createGame` et `api.joinGame` envoient le commandant optionnel ; le dernier commandant choisi est mémorisé localement comme le dernier pseudo, et pré-rempli.
* Fond de tuile commun (`CommanderBackdrop`) : image en `cover`, voile sombre en dégradé, crédit artiste en petit dans un coin. Si l'image ne charge pas, fond actuel.
* Appliqué à `ScreenBoard` (tuile), `GameBoard` (carte « moi »), `PlayerTile` (adversaires et lobby).
* i18n fr/en pour les nouveaux textes et le code d'erreur `invalid_commander`.

### Tests

* Backend : validation de l'URL (refus d'un autre hôte), commandant conservé à la reprise sans nouveau choix, remplacé avec un nouveau choix, création/rejoindre sans commandant toujours valides.
* Frontend : `npm run build` et parcours Playwright (création avec commandant, affichage sur `/ecran`).

## Laissé au développeur

* Détail visuel du voile et taille du crédit, du moment que les PV restent lisibles à distance.
* Gestion des requêtes obsolètes (AbortController ou compteur).
