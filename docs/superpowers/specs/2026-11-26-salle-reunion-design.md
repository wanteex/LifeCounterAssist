# Life Counter Assist — version « salle de réunion »

Date : 2026-11-26

## Intention

Jouer à Magic (format Commander) entre collègues dans une salle de réunion, avec l'écran de la salle comme tableau des scores. Personne ne doit installer d'app ni créer de compte : on scanne un QR code, on tape un pseudo, on joue. Les points de vie de tout le monde sont visibles en grand sur l'écran.

La version précédente (comptes Google via Entra External ID, amis, decks, profils, historique) est trop lourde pour cet usage. On la remplace.

## Objectifs

* Un QR code affiché sur l'écran de la salle mène à la page web de l'app.
* La page liste les parties en cours (lobby ou en jeu).
* Créer une partie avec un seul paramètre : les PV de départ (20 / 25 / 30 / 40, 40 par défaut).
* Rejoindre une partie en saisissant un pseudo.
* Lobby : on voit les joueurs arriver en direct. Le créateur a un bouton « Démarrer ».
* Pendant la partie, chaque joueur gère ses compteurs sur son téléphone : PV, dégâts de commandant reçus (par adversaire), poison.
* Le grand écran affiche en temps réel les PV (et poison / dégâts de commandant) de tous les joueurs.

## Non-objectifs

* Comptes, connexion, amis, decks, illustration de commandant, profil, statistiques, historique.
* Énergie, expérience, taxe de commandant, monarque, initiative.
* Plusieurs salles / plusieurs écrans gérés séparément.
* Persistance : une partie perdue au redémarrage du serveur est acceptable.
* Mise à l'échelle sur plusieurs instances.

## Anti-objectifs

* Devoir installer quoi que ce soit, ou se connecter, pour jouer.
* Un grand écran illisible à 3 m (texte petit, trop d'infos).
* Une infra lourde à maintenir (« pas besoin d'une grosse techno »).

## Contraintes

* Un seul lieu : une salle de réunion, une table, l'écran ou le projecteur branché sur le PC de la salle.
* Les téléphones et le PC de la salle accèdent à l'app via Internet (réseau d'entreprise : pas de dépendance au réseau local).
* Le serveur tourne sur une seule instance (état en mémoire).

## Approches envisagées

1. **Alléger le projet existant (retenue).** Garder .NET 10 + SignalR + React/Vite/Tailwind, le moteur de règles et ses tests. Supprimer auth, amis, decks, profils, Table Storage. Ajouter joueurs invités, page écran, QR code. On garde du code testé qui marche et la stack est déjà en place.
2. **Réécriture minimale en Node.** Un serveur unique WebSocket + front. Plus léger mais on jette un backend fonctionnel et testé. Écartée.
3. **Le PC de la salle héberge la partie.** Les téléphones devraient joindre le PC sur le réseau d'entreprise : fragile (pare-feu, isolation Wi-Fi). Écartée.

## Décisions prises avec l'utilisateur

* Usage : salle de réunion, un seul lieu, en général une partie à la fois.
* L'écran affiche une partie à la fois et suit son cycle : QR code au repos → lobby → plateau → retour au QR.
* Compteurs suivis : PV, dégâts de commandant, poison.
* Approche 1, en mémoire, sans base de données.

## Conception

### Vue d'ensemble

```text
Téléphones (React)  ──REST + SignalR──►  App .NET 10 (1 conteneur, 1 instance)
PC de la salle /ecran ──SignalR (lecture)─┘   sert aussi le front (fichiers statiques)
                                              état des parties en mémoire
```

**Un seul conteneur** *(choix de Claude)* : l'API ASP.NET Core sert aussi le front compilé (`wwwroot`). Même origine : plus de CORS, plus de `config.js` généré, plus de conteneur nginx. Une seule Container App sur Azure.

### Écrans côté téléphone

1. **Accueil `/`** : liste des parties non terminées (créateur, nombre de joueurs, PV de départ, statut Lobby / En cours) + bouton « Créer une partie ». Mise à jour en direct.
2. **Créer** : pseudo + PV de départ (20/25/30/40). Crée la partie et y assoit le créateur.
3. **Rejoindre** : clic sur une partie en lobby → saisie du pseudo → lobby. Une partie en cours n'est pas joignable (affichée en lecture seule).
4. **Lobby `/parties/:id`** : liste des joueurs en direct. Le créateur voit « Démarrer » (actif à partir de 2 joueurs). Les autres peuvent quitter. Le créateur peut annuler la partie *(choix de Claude, évite une partie fantôme bloquée)*.
5. **Partie** : le téléphone met en avant **son propre joueur** en grand : PV avec tap ±1 / appui long ±10 (composant existant `HoldButton`), poison, dégâts de commandant reçus par adversaire, bouton « J'abandonne ». En dessous, liste compacte des autres joueurs. *(Choix de Claude)* : un appui sur un autre joueur ouvre ses compteurs pour corriger, si son téléphone est éteint (le serveur autorise tout joueur assis à modifier n'importe qui, comme aujourd'hui).
6. **Fin** : gagnant affiché, retour à l'accueil.

Le pseudo saisi est mémorisé (`localStorage`) pour pré-remplir la fois suivante.

### Page écran `/ecran`

Ouverte sur le PC de la salle, plein écran, aucune interaction requise.

* **Partie affichée** *(choix de Claude)* : la plus récemment démarrée parmi les parties en cours ; s'il n'y en a aucune, la plus récemment créée parmi les lobbys ; sinon aucune.
* **Repos** : QR code géant (URL racine de l'app, déduite de `window.location.origin`) + « Scanne pour jouer » + liste courte des lobbys ouverts.
* **Lobby** : nom de la partie (« Partie de Bob »), PV de départ, pseudos des joueurs qui arrivent, QR code en plus petit pour les retardataires.
* **Partie** : une tuile par joueur, grille adaptée au nombre (2 → 2 colonnes, 3-4 → 2×2, 5-6 → 3×2, 7-8 → 4×2). PV en très gros, poison et dégâts de commandant reçus non nuls en plus petit. Joueur éliminé grisé. Le dernier changement de PV clignote brièvement (`useRecentLifeChanges` existe déjà).
* **Fin** : gagnant affiché 30 s, puis retour à l'état suivant (repos ou autre partie).
* Garder l'écran allumé (Wake Lock, déjà utilisé) et se reconnecter seul après coupure.

### Identité des joueurs (sans compte)

* Rejoindre ou créer renvoie `{ playerId, seatToken }`. `seatToken` est un secret aléatoire (128 bits) propre à ce siège.
* Le téléphone garde `seatToken` dans `localStorage`, indexé par id de partie. Après un rafraîchissement ou une mise en veille, il reprend sa place.
* Chaque action d'un joueur envoie le `seatToken`. Le serveur retrouve le joueur, sinon `403`.
* Pseudo : 1 à 20 caractères, espaces retirés aux bords. Unique dans une partie (comparaison insensible à la casse).
* **Reprendre sa place** *(décidé avec l'utilisateur)* : rejoindre une partie (lobby ou en cours) avec un pseudo déjà présent redonne cette place : le serveur renvoie le `playerId` existant et un nouveau `seatToken` (l'ancien devient invalide). Pas d'erreur `name_taken`. Confiance entre collègues : pas de protection supplémentaire. Une partie en cours reste fermée aux nouveaux pseudos, mais accepte ces reprises ; l'accueil propose donc « Reprendre ma place » sur une partie en cours.
* Le créateur qui reprend sa place garde ses droits (démarrer, annuler).
* La page écran et la liste des parties sont publiques en lecture. Acceptable : l'URL n'est connue que des gens de la salle, et il n'y a aucune donnée sensible.

### Règles de partie

* Joueurs : 2 à 8. Pas de paramètre de nombre de places : 8 places max.
* PV de départ : 20, 25, 30 ou 40.
* Mort : PV ≤ 0, poison ≥ 10, 21 dégâts d'un même commandant, ou abandon (règles existantes de `GameRules`).
* Dégâts de commandant : retirent aussi des PV (règle existante).
* **Fin automatique avec délai** *(décidé avec l'utilisateur)* : dès qu'il ne reste qu'un joueur non éliminé, un compte à rebours de 10 s démarre (« Bob gagne dans 10 s… » sur l'écran et les téléphones), avec un bouton **Annuler** pour tout joueur. Toute modification qui ramène un deuxième joueur en vie annule aussi le compte à rebours. À 0, la partie se termine avec ce gagnant. Le compte à rebours est porté par le serveur (date d'échéance dans l'état de la partie, vérifiée par une tâche de fond) pour que tous les clients restent d'accord.
* Un bouton « Terminer la partie » (tout joueur, avec confirmation) permet aussi de finir sans gagnant.
* Annuler un abandon reste possible tant que la partie n'est pas finie.

### Cycle de vie et nettoyage *(choix de Claude)*

* Les parties terminées ou annulées sont supprimées de la mémoire après 10 minutes.
* Une partie sans aucune action depuis 6 heures est supprimée.
* Une tâche de fond fait ce ménage toutes les minutes.

### Backend : ce qui change

Garder :
* `Domain/GameRules.cs`, `Domain/GameState.cs` réduits aux compteurs retenus (retirer énergie, expérience, taxe, monarque, initiative, decks, invitations, code de partie).
* `GameHub` (SignalR), `DomainExceptionHubFilter`, `DomainExceptionHandler`, `JsonDefaults`.
* Le verrou par partie et la diffusion des changements de `GameService`, sans la persistance.

Supprimer :
* `Auth/` (Entra, mode Dev), `Infrastructure/TableStore.cs`, `Infrastructure/Entities.cs`.
* `UserService`, `FriendService`, `DeckService`, `DeckImporter`, `Codes` (si plus utilisé).
* Packages Azure Storage / Identity / JwtBearer devenus inutiles.

Ajouter :
* REST : `GET /api/games` (liste), `POST /api/games` (créer : pseudo + PV, refusé au-delà de 20 parties actives), `POST /api/games/{id}/join` (pseudo ; reprise de place si le pseudo existe). Quitter (`LeaveGame`) et annuler (`CancelGame`, créateur) passent par le hub, pas par REST, avec le `seatToken` en argument.
* Hub : `WatchLobbyList` (liste en direct), `Watch(gameId)` (lecture publique), `WatchScreen` (reçoit la partie affichée et ses changements), actions de jeu avec `seatToken`.
* Service de nettoyage en tâche de fond.
* Servir `wwwroot` avec repli sur `index.html` pour les routes du front.

### Frontend : ce qui change

* Supprimer MSAL, `authClient`, pages Login / Decks / Friends / Profile, `DeckSelect`, navigation par onglets.
* Pages : Accueil, Créer, Partie (lobby / plateau / fin), Écran.
* Ajouter une petite bibliothèque de QR code (génération côté navigateur, par exemple `qrcode`).
* Garder i18n FR/EN (déjà en place), Tailwind, client SignalR, `HoldButton`, mises à jour optimistes.
* `apiUrl` = même origine par défaut ; en dev, le proxy Vite redirige `/api` et `/hubs` vers l'API.

### Infra et déploiement

* Bicep : une seule Container App (min 1, max 1 instance), ACR, Log Analytics, identité managée pour tirer l'image (AcrPull). Supprimer le compte de stockage, le rôle Table et le paramètre `auth`.
* Docker : un Dockerfile multi-étapes (build Node du front → copié dans `wwwroot` → publish .NET).
* `docker-compose.yml` : un seul service.
* README réécrit pour la nouvelle version.

### Tests

* Mettre à jour `GameRulesTests` (compteurs retirés, compte à rebours de fin et son annulation, reprise de place par pseudo).
* Ajouter des tests : jeton de siège invalide ou remplacé refusé, choix de la partie affichée à l'écran, nettoyage des parties.
* Vérification manuelle : une partie à 2 navigateurs + `/ecran` dans un troisième.

## Laissé au constructeur

* Mise en page détaillée et style des écrans (garder le thème sombre actuel).
* Structure exacte des messages SignalR et des DTO.
* Choix précis de la bibliothèque QR.
* Ordre de découpage du travail.
