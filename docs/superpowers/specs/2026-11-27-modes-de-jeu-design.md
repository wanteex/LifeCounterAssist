# Modes de jeu : suivi des tours et mode Blitz — design

## Intention

Le groupe joue surtout en Commander « normal » (chacun pour soi). Il veut tester un mode **Blitz** : une limite de temps par tour, qui augmente de façon exponentielle au fil de la partie (premiers tours nerveux, plus de temps quand le plateau se complique). Pour que le chrono ait un sens, l'appli doit savoir **à qui c'est le tour** ; ce suivi est utile aussi en mode normal, sans chrono, pour suivre la partie sur l'écran de salle.

Réussite : à l'écran de salle et sur chaque téléphone on voit d'un coup d'œil qui joue, depuis combien de temps / combien il reste, et le tour de table en cours. Passer son tour est un geste unique sur le téléphone du joueur actif.

## Objectifs

- Choix du mode à la création de la partie : **Normal** ou **Blitz**.
- **Suivi des tours dans les deux modes** : joueur actif, numéro du tour de table, bouton « Fin de tour ».
- **Blitz** : chrono par tour, limite croissante par tour de table, **phase de combat** qui fige le chrono, **pause** générale par l'hôte, **alarme** à 0, **pénalité optionnelle** −1 PV par seconde de dépassement.

## Non-objectifs

- Passage de tour automatique à l'expiration (on garde l'alarme ; le joueur finit son action).
- Limite de durée de la phase de combat.
- Suivi des phases de tour autres que le combat (entretien, pioche…).
- Modes en équipes, Archenemy, etc.
- Annuler une « Fin de tour » manuelle (v1 ; voir « Laissé au builder »).

## Anti-objectifs

- Un chrono qui diverge d'un appareil à l'autre ou qui saccade sur un Wi-Fi de salle médiocre.
- Une pénalité qui tombe alors que le chrono est figé (combat, pause) ou qui compte double.
- Régresser l'appui long ± (filtre de version / `ackSeq`) à cause de diffusions plus fréquentes.

## Contraintes

- État en mémoire côté serveur (`GameService`, verrou par partie), règles pures dans `GameRules`, tests xUnit.
- Horloge serveur existante : `GameTicker` appelle `GameService.TickAsync` chaque seconde (déjà utilisé pour le compte à rebours de victoire).
- L'ordre de jeu = ordre des places (`state.Players`, sens horaire, réglé par l'hôte dans le salon — PR #6). L'ordre ne change plus après le lancement.
- i18n fr/en (les clés `fr` doivent égaler celles de `en`).

## Approches considérées

- **A. Le serveur garde l'heure, les clients décomptent eux-mêmes** — choisie. L'état contient le temps déjà consommé et l'instant de reprise ; chaque appareil calcule le temps restant localement, corrigé de l'écart d'horloge avec le serveur. Le ticker ne sert qu'à la pénalité. Fluide, peu de trafic, un seul arbitre. Même principe que `pendingFinishAt` / `FinishCountdown`.
- B. Le serveur diffuse le temps restant chaque seconde — écartée : trafic constant, saccades sur Wi-Fi lent, `Version` qui s'emballe et brouille le filtre de version.
- C. Chrono sur le téléphone du joueur actif — écartée : veille du téléphone, désynchronisation, pénalité dépendante d'un seul appareil.

## Design

### Modèle (backend)

- `GameState.Mode` : enum `GameMode { Normal, Blitz }`, défaut `Normal`.
- `GameState.Blitz` : `BlitzSettings? ` (null en Normal) — record `BlitzSettings(int BaseSeconds, bool Penalty)`.
  - `BaseSeconds` ∈ {15, 30, 60}, défaut 30 *(choix de l'assistant)*.
  - Croissance et plafond **constants** *(choix de l'assistant)* : `BlitzGrowth = 1.5`, `BlitzMaxSeconds = 300`.
  - Limite du tour de table `r` (1-based) : `min(300, round(BaseSeconds × 1.5^(r−1)))` → 30, 45, 68, 101, 152, 228, 300…
- `GameState.Turn` : `TurnState?`, null en salon, posé au lancement :
  - `ActivePlayerId`
  - `Round` (tour de table, 1-based)
  - `ElapsedMs` : temps déjà consommé du tour, hors période en cours
  - `RunningSince` : `DateTimeOffset?` — instant de reprise ; null quand figé (combat, pause, joueur actif éliminé, un seul survivant). Tourne aussi en mode Normal (temps écoulé affiché, sans limite)
  - `Combat` : bool
  - `Paused` : bool
  - `PenaltySeconds` : secondes de dépassement déjà pénalisées ce tour
  - `SkipAt` : `DateTimeOffset?` — passage automatique programmé quand le joueur actif est éliminé
- Temps consommé à l'instant `now` = `ElapsedMs + (RunningSince ? now − RunningSince : 0)`.
- `GameState.ServerTime` : `DateTimeOffset`, horodaté à chaque diffusion (`BroadcastAsync`) et à chaque `GetGame`. Le client calcule `offset = serverTime − Date.now()` à la réception et l'utilise pour tous ses calculs de chrono.

### Règles (`GameRules`)

- `Create(..., mode, blitz)` : valide `BaseSeconds` (sinon `invalid_blitz`) ; `Blitz` ignoré/null en Normal.
- `Start` : `Turn = { ActivePlayerId = premier joueur non éliminé dans l'ordre des places, Round = 1, ElapsedMs = 0, RunningSince = now }` (les deux modes).
- `EndTurn(state, actor, now)` :
  - partie en cours ; `actor` = joueur actif ou hôte, sinon `ForbiddenException("not_your_turn")`.
  - Joueur suivant = prochain joueur **non éliminé** dans l'ordre des places, en boucle. Si on repasse par le début de la liste (index suivant ≤ index courant), `Round++`.
  - Reset : `ElapsedMs = 0`, `PenaltySeconds = 0`, `Combat = false` ; `SkipAt = null` ; `RunningSince = now` si non `Paused`, sinon null.
  - Log `turn_ended`.
- `ToggleCombat(state, actor, now)` : Blitz uniquement (`blitz_only`), actif ou hôte. Fige (cumule dans `ElapsedMs`, `RunningSince = null`) ou relance (`RunningSince = now` si non `Paused`). Log `combat_started` / `combat_ended`.
- `TogglePause(state, actor, now)` : Blitz uniquement, **hôte uniquement**. Même mécanique de gel ; la reprise ne relance le chrono que si `Combat` est faux.
- **Joueur actif éliminé** (PV, poison, dégâts de commandant, abandon, pénalité) — *décidé par l'utilisateur : passage après un court délai* : le chrono est gelé et `SkipAt = now + 5 s` (constante `TurnSkipDelay`). Si le joueur revient à la vie avant (correction de PV, annulation d'abandon), `SkipAt = null` et son chrono reprend là où il était. À l'échéance, le ticker passe le tour (même logique que `EndTurn`, log `turn_skipped`). Point d'accroche : là où `RefreshPendingFinish` est appelé. Si un seul survivant reste, le compte à rebours de victoire existant s'applique.
- **Un seul survivant (ou aucun)** *(choix de l'assistant)* : le chrono est gelé et la pénalité ne s'applique plus ; une pénalité ne peut donc jamais tuer le dernier survivant ni laisser la partie sans gagnant. Le chrono reprend si un joueur revient (annulation d'abandon, correction de PV). Le passage auto ne boucle pas quand personne n'est vivant à côté : le tour reste sur le survivant.
- Un joueur qui revient (annulation d'abandon) reprend simplement sa place dans la rotation.
- `ApplyTimePenalty(state, now)` (appelé par le ticker) : si Blitz, `Penalty`, chrono en marche, dépassement `d = floor((consommé − limite)/1 s)` > `PenaltySeconds` → `Life −= d − PenaltySeconds`, `PenaltySeconds = d`, log `time_penalty` (valeur = PV retirés), puis `RefreshPendingFinish` et passage auto si éliminé. Retourne vrai s'il a modifié l'état.

### Service / hub

- `CreateGameRequest` gagne `Mode` et `Blitz`.
- Hub : `EndTurn(gameId, seatToken)`, `ToggleCombat(gameId, seatToken)`, `TogglePause(gameId, seatToken)`.
- `TickAsync` : en plus de `CompletePendingFinish`, appelle `ApplyTimePenalty` et le passage de tour programmé (`SkipAt`), et diffuse si l'état a changé. La pénalité change `Life` sans toucher à `AckSeq` : les deltas optimistes en attente restent corrects.

### Frontend

- **Création** : choix Normal / Blitz ; en Blitz, temps de départ (15 / 30 / 60 s) et case « Pénalité : −1 PV par seconde de dépassement » (décochée). Un rappel du principe (« ×1,5 à chaque tour de table, max 5 min »).
- **Horloge partagée** : hook `useTurnClock(game)` → `{ elapsedMs, remainingMs (Blitz), overtime, running }`, calculé avec l'offset serveur, rafraîchi ~4 fois/s.
- **Téléphone** :
  - Bandeau « Au tour de Bea · Tour 3 » + temps écoulé (Normal, compte vers le haut) ou restant (Blitz).
  - Joueur actif : bouton **Fin de tour** (gros) ; en Blitz, **⚔ Combat** / **Fin du combat**.
  - Hôte : peut passer le tour, déclencher/finir le combat, **Pause** / **Reprendre** (Blitz).
- **`/ecran`** : tuile du joueur actif mise en avant (anneau/couleur), grand chrono (restant en Blitz, écoulé en Normal), « ⚔ Combat » ou « Pause » quand figé (chrono grisé), « Tour N » en en-tête.
- **Alarme à 0** (Blitz) : clignotement rouge de la tuile active sur `/ecran` et du bandeau sur le téléphone actif + bip Web Audio sur le téléphone actif. Le chrono passe en négatif (« +0:07 »), et affiche la pénalité cumulée si active.
- Textes i18n fr/en pour tout ce qui précède, et entrées de journal (`turn_ended`, `turn_skipped`, `combat_*`, `paused`/`resumed`, `time_penalty`).

## Décidé par l'utilisateur

- Modes : Normal (chacun pour soi) et Blitz.
- Blitz : limite par tour, croissance exponentielle au fil des tours.
- À 0 : alarme simple ; pénalité −1 PV/s en option.
- Phase de combat qui met le chrono en pause.
- Suivi du joueur actif + « Fin de tour » aussi en Normal, sans chrono.
- Approche A.
- Élimination du joueur actif : passage du tour après un court délai, annulé s'il revient à la vie.
- Mode Normal : temps écoulé du tour affiché.

## Choix de l'assistant (à corriger si besoin)

- Croissance par **tour de table** (pas par tour individuel), ×1,5, plafond 5 min ; seul le temps de départ est réglable (15/30/60 s, défaut 30).
- Pénalité décochée par défaut.
- Premier joueur = place 1.
- Combat et pause : Blitz uniquement ; pause réservée à l'hôte ; l'hôte peut aussi passer le tour et gérer le combat.
- Délai de passage automatique : 5 s.
- Bip sonore sur le téléphone du joueur actif seulement (l'écran de salle n'a pas d'interaction utilisateur, donc pas d'audio autorisé par le navigateur) ; l'écran clignote.

## Laissé au builder

- Rendu exact (couleurs, tailles, animation du clignotement), formulation des textes.
- Emplacement du hook et découpage des composants.
- Annulation d'une fin de tour par erreur : hors v1 ; l'hôte peut au pire refaire tourner jusqu'au bon joueur.
