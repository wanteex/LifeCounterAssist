# Version « salle de réunion » — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Objectif :** transformer l'app à comptes en app sans compte : QR code → liste des parties → pseudo → lobby → partie, avec une page `/ecran` qui affiche les PV de tous en grand.

**Architecture :** un seul conteneur ASP.NET Core 10 qui sert l'API REST, le hub SignalR `/hubs/game` et le front React compilé (`wwwroot`). État des parties en mémoire uniquement. Identité par jeton de siège (`seatToken`) gardé dans le `localStorage` du téléphone, jamais diffusé.

**Stack :** .NET 10, SignalR, xUnit ; React 19, Vite, Tailwind 4, i18next, `@microsoft/signalr`, `qrcode.react` ; Bicep (Azure Container Apps).

**Spec :** `docs/superpowers/specs/2026-11-26-salle-reunion-design.md`

## Contraintes globales

* PV de départ autorisés : 20, 25, 30, 40 (40 par défaut). Sinon `invalid_starting_life`.
* Joueurs : 2 à 8 ; 8 places max, pas de paramètre.
* Pseudo : 1 à 20 caractères après `Trim()`, unique par partie, comparaison insensible à la casse. Sinon `invalid_name`.
* Morts : PV ≤ 0, poison ≥ 10, 21 dégâts d'un même commandant, abandon.
* Compte à rebours de fin automatique : 10 s.
* Partie terminée affichée sur l'écran 30 s.
* Parties terminées ou annulées supprimées après 10 min ; parties sans action depuis 6 h supprimées.
* `seatToken` : 128 bits aléatoires (`RandomNumberGenerator`), encodés en hexa. Il ne figure jamais dans `GameState` ni dans aucun message diffusé.
* Une seule instance serveur (min 1 / max 1 réplique).
* Codes d'erreur métier en `snake_case`, traduits côté front (FR/EN).
* Front : thème sombre actuel (Tailwind slate/amber).

## Points de vigilance (Review Focus)

1. **Fuite du jeton** : un `GameState` sérialisé (envoyé à l'écran public) ne doit contenir aucun `seatToken`. → test dans la tâche 2.
2. **Deux téléphones, même pseudo** : la reprise de place invalide l'ancien jeton ; l'ancien téléphone reçoit `invalid_seat` et doit revenir à l'écran de saisie du pseudo au lieu de boucler en erreur. → test dans la tâche 2, gestion front dans la tâche 5.
3. **« Annuler » puis autre action sans changement de survivant** : le compte à rebours ne doit pas redémarrer tout seul. Il redémarre si le survivant unique change ou si un deuxième joueur revient puis repart. → test dans la tâche 1.
4. **Soin pendant le compte à rebours** : un deuxième joueur revenu à > 0 PV annule le compte à rebours. → test dans la tâche 1.
5. **Reconnexion de l'écran** (Wi-Fi coupé, PC en veille) : l'écran doit se ré-abonner après une reconnexion SignalR et afficher la partie courante, pas un état figé. → `onReconnected` dans la tâche 6, vérification manuelle dans la tâche 8.

---

### Tâche 1 : Moteur de règles réduit + compte à rebours + reprise de place

**Fichiers :**
- Modifier : `backend/src/LifeCounterAssist.Api/Domain/GameState.cs`
- Modifier : `backend/src/LifeCounterAssist.Api/Domain/GameRules.cs`
- Réécrire : `backend/src/LifeCounterAssist.Api.Tests/GameRulesTests.cs`

**Interfaces produites :**

```csharp
public enum GameStatus { Lobby, InProgress, Finished, Cancelled }

public sealed class GameState {
    string Id; string HostPlayerId; GameStatus Status; int StartingLife;
    DateTimeOffset CreatedAt; DateTimeOffset? StartedAt; DateTimeOffset? FinishedAt;
    DateTimeOffset LastActivityAt;
    string? WinnerPlayerId;
    string? PendingWinnerPlayerId; DateTimeOffset? PendingFinishAt;
    string? SuppressedWinnerPlayerId;   // posé par « Annuler », effacé dès que les survivants ne sont plus 1
    List<PlayerState> Players; List<GameLogEntry> Log; long Version;
    PlayerState? FindPlayer(string playerId);
    PlayerState? FindPlayerByName(string name); // insensible à la casse
}
public sealed class PlayerState {
    string PlayerId; string DisplayName; int Life; int Poison;
    Dictionary<string,int> CommanderDamage; bool Conceded; bool IsEliminated { get; }
}
// GameLogEntry : renommer ActorUserId/TargetUserId en ActorPlayerId/TargetPlayerId.

public static class GameRules {
    const int LethalCommanderDamage = 21, LethalPoison = 10, MinPlayers = 2, MaxPlayers = 8, MaxNameLength = 20, MaxLogEntries = 100;
    static readonly TimeSpan FinishCountdown = TimeSpan.FromSeconds(10);
    static readonly int[] AllowedStartingLife = [20, 25, 30, 40];

    static string NormalizeName(string? name);                       // Trim, valide 1-20, sinon DomainException("invalid_name")
    static GameState Create(string id, string hostPlayerId, string hostName, int startingLife, DateTimeOffset now);
    static (PlayerState Player, bool Reclaimed) Join(GameState s, string newPlayerId, string name, DateTimeOffset now);
    static void Leave(GameState s, string playerId, DateTimeOffset now);       // lobby seulement, pas l'hôte
    static void Cancel(GameState s, string actorPlayerId, DateTimeOffset now); // hôte, lobby seulement → Cancelled
    static void Start(GameState s, string actorPlayerId, DateTimeOffset now);
    static void AdjustLife(GameState s, string actor, string target, int delta, DateTimeOffset now);
    static void AdjustCommanderDamage(GameState s, string actor, string target, string source, int delta, DateTimeOffset now);
    static void AdjustPoison(GameState s, string actor, string target, int delta, DateTimeOffset now);
    static void ToggleConcede(GameState s, string actor, string target, DateTimeOffset now);
    static void CancelPendingFinish(GameState s, string actor, DateTimeOffset now);
    static void Finish(GameState s, string actor, DateTimeOffset now);          // manuel, sans gagnant
    static bool CompletePendingFinish(GameState s, DateTimeOffset now);         // true si la partie vient de se terminer
}
```

Règles :
- Supprimer énergie, expérience, taxe, monarque, initiative, decks, invitations, `JoinCode`, `MaxPlayers`, `CounterType`, `DeckInfo`, `SelectDeck`, `SuggestWinner`.
- Chaque mutation met `LastActivityAt = now` et incrémente `Version` (via `AddLog`).
- Après chaque `Adjust*` et `ToggleConcede`, appeler la fonction privée `RefreshPendingFinish(s, now)`. Elle compte les vivants.
  - S'il y a exactement 1 vivant, que ce n'est pas `SuppressedWinnerPlayerId` et qu'il n'y a pas déjà ce même `PendingWinnerPlayerId` : poser `PendingWinnerPlayerId` et `PendingFinishAt = now + 10 s`.
  - Sinon, s'il n'y a pas exactement 1 vivant : effacer `Pending*` et `SuppressedWinnerPlayerId`.
  - Avec 0 vivant (élimination simultanée), pas de fin automatique.
- `CancelPendingFinish` : passe `SuppressedWinnerPlayerId` à la valeur de `PendingWinnerPlayerId`, puis efface `Pending*`. Sans compte à rebours en cours, ne fait rien.
- `CompletePendingFinish` : si la partie est `InProgress` et que `PendingFinishAt <= now`, alors `Status = Finished`, `WinnerPlayerId = PendingWinnerPlayerId`, `FinishedAt = now`, et efface `Pending*`.
- `Finish` (manuel) : passe la partie à `Finished` avec `WinnerPlayerId = null` et efface `Pending*`.
- `Join` :
  - Pseudo déjà présent : retourne `(siège existant, true)`, quel que soit le statut sauf `Finished`/`Cancelled` (→ `game_over`).
  - Sinon, statut différent de `Lobby` → `game_already_started` ; partie pleine → `game_full`.
  - Sinon, crée un siège avec `Life = StartingLife`.

- [ ] **Étape 1 : écrire les tests qui échouent** dans `GameRulesTests.cs`. Helper : `GameState NewGame(int players = 2)`, qui crée l'hôte `"p1"`/`"Alice"` puis fait rejoindre `"p2"`/`"Bob"`, `"p3"`/`"Chloé"`…

```csharp
[Theory][InlineData(19)][InlineData(41)][InlineData(0)]
void Create_RejectsStartingLifeOutsideAllowedValues(int life)  // code "invalid_starting_life"
[Theory][InlineData("")][InlineData("   ")][InlineData("123456789012345678901")]
void Create_RejectsInvalidName(string name)                    // code "invalid_name"
void Create_TrimsNameAndSeatsHostWithStartingLife()            // "  Alice " → "Alice", Life 40
void Join_SameNameDifferentCase_ReclaimsExistingSeat()         // Join(s,"pX","alice") → PlayerId "p1", Reclaimed true, Players.Count inchangé
void Join_ReclaimWorksWhileInProgress()
void Join_NewNameWhileInProgress_Throws()                      // "game_already_started"
void Join_NinthPlayer_Throws()                                 // "game_full"
void Join_FinishedGame_Throws()                                // "game_over"
void Start_RequiresHostAndTwoPlayers()                         // "host_only" (ForbiddenException), "not_enough_players"
void Cancel_ByHostInLobby_SetsCancelled()
void Leave_HostCannotLeave()                                   // "host_cannot_leave"
void CommanderDamage_AlsoReducesLife()                         // +5 de p2 sur p1 → CommanderDamage["p2"]==5, Life 35
void Poison_NeverBelowZero()
void LastPlayerStanding_StartsTenSecondCountdown()             // p2 à 0 PV → PendingWinnerPlayerId "p1", PendingFinishAt == Now+10s, Status InProgress
void Healing_DuringCountdown_CancelsIt()                       // p2 -40 puis +1 → Pending* null
void CompletePendingFinish_BeforeDeadline_DoesNothing()        // Now+9s → false, InProgress
void CompletePendingFinish_AtDeadline_FinishesWithWinner()     // Now+10s → true, Finished, WinnerPlayerId "p1"
void CancelPendingFinish_ThenUnrelatedChange_DoesNotRestart()  // 3 joueurs : p2,p3 morts → countdown ; Cancel ; p1 -1 PV → Pending* null
void CancelPendingFinish_ThenSurvivorChanges_Restarts()        // après Cancel, p2 soigné (2 vivants), puis re-tué → countdown de nouveau
void AllPlayersDead_NoCountdown()                              // 2 joueurs tous deux à 0 → Pending* null
void ManualFinish_HasNoWinner()
void AdjustLife_AfterFinish_Throws()                           // "game_not_in_progress"
void EveryMutation_UpdatesLastActivityAt()
```

- [ ] **Étape 2 : lancer et vérifier l'échec.** `cd backend; dotnet test` → erreurs de compilation, ou échecs sur les nouveaux membres.
- [ ] **Étape 3 : implémenter** `GameState.cs` et `GameRules.cs` selon les interfaces ci-dessus. `Cancel` pose aussi `FinishedAt = now`.

  Le projet API doit compiler pour que les tests tournent. **Dans cette même tâche** :
  - supprimer `Auth/`, `Infrastructure/TableStore.cs`, `Infrastructure/Entities.cs`, `Services/UserService.cs`, `FriendService.cs`, `DeckService.cs`, `DeckImporter.cs`, `Codes.cs` et `DeckImporterTests.cs` ;
  - réduire `GameService`, `GameHub`, `ApiEndpoints`, `Notifier` et `Program` à des coquilles minimales qui compilent (les vraies versions arrivent aux tâches 2 et 3) ;
  - retirer les packages `Azure.Data.Tables`, `Azure.Identity` et `Microsoft.AspNetCore.Authentication.JwtBearer`.
- [ ] **Étape 4 : lancer les tests.** `cd backend; dotnet test` → tous PASS.
- [ ] **Étape 5 : commit.** `git commit -m "feat: guest game rules with finish countdown and seat reclaim"`

---

### Tâche 2 : `GameService` en mémoire, jetons de siège, partie affichée, nettoyage

**Fichiers :**
- Créer : `backend/src/LifeCounterAssist.Api/Services/IGameNotifier.cs`
- Réécrire : `backend/src/LifeCounterAssist.Api/Services/GameService.cs`
- Créer : `backend/src/LifeCounterAssist.Api.Tests/GameServiceTests.cs`

**Interfaces :**
- Consomme : `GameRules`, `GameState` (tâche 1).
- Produit :

```csharp
public sealed record CreateGameRequest(string Name, int StartingLife = 40);
public sealed record JoinGameRequest(string Name);
public sealed record SeatDto(GameState Game, string PlayerId, string SeatToken);
public sealed record GameSummaryDto(string Id, string HostName, int PlayerCount, int StartingLife, GameStatus Status, DateTimeOffset CreatedAt);

public interface IGameNotifier {
    Task GameUpdatedAsync(GameState state, CancellationToken ct);
    Task ListChangedAsync(IReadOnlyList<GameSummaryDto> games, CancellationToken ct);
    Task ScreenChangedAsync(GameState? featured, CancellationToken ct);
}

public sealed class GameService(IGameNotifier notifier, TimeProvider clock) {
    Task<SeatDto> CreateAsync(CreateGameRequest r, CancellationToken ct = default);
    Task<SeatDto> JoinAsync(string gameId, JoinGameRequest r, CancellationToken ct = default);
    GameState Get(string gameId);                                   // NotFoundException("game_not_found")
    IReadOnlyList<GameSummaryDto> List();                           // Lobby + InProgress, plus récentes d'abord
    GameState? GetFeatured();
    Task<GameState> ActAsync(string gameId, string seatToken, Action<GameState, string> action, CancellationToken ct = default);
    Task TickAsync(CancellationToken ct = default);                 // CompletePendingFinish + nettoyage
}
```

Comportement :
- `_games : ConcurrentDictionary<string, GameState>` et `_seats : ConcurrentDictionary<string, Dictionary<string, string>>` (gameId → token → playerId), avec un `SemaphoreSlim` par partie, comme le code actuel. On garde la technique « muter une copie clonée puis remplacer ».
- `JoinAsync` en reprise de place : retire l'ancien token de ce `playerId` puis en émet un nouveau.
- `ActAsync` : token inconnu → `ForbiddenException("invalid_seat")`. Partie inconnue → `NotFoundException("game_not_found")`. L'action reçoit l'état et le `playerId` de l'appelant.
- Après chaque changement : `GameUpdatedAsync(state)`, `ListChangedAsync(List())`, `ScreenChangedAsync(GetFeatured())`.
- `GetFeatured` : par ordre de priorité,
  1. la partie `InProgress` au `StartedAt` le plus récent ;
  2. sinon la partie `Finished` dont `FinishedAt` date de moins de 30 s (la plus récente) ;
  3. sinon le `Lobby` au `CreatedAt` le plus récent ;
  4. sinon `null`.
- `TickAsync` :
  - pour chaque partie, `CompletePendingFinish(now)` sous verrou, puis notifier en cas de changement ;
  - supprimer les parties `Finished`/`Cancelled` dont `FinishedAt` (ou `LastActivityAt` si annulée) date de plus de 10 min, et toute partie dont `LastActivityAt` date de plus de 6 h ;
  - notifier la liste et l'écran si quelque chose a changé, y compris le passage des 30 s d'affichage d'une partie terminée (comparer l'id de la partie affichée avant et après).

- [ ] **Étape 1 : écrire les tests qui échouent.** `FakeNotifier` enregistre les appels. Horloge : `FakeTimeProvider` (package `Microsoft.Extensions.TimeProvider.Testing`, à ajouter au projet de tests).

```csharp
void Create_ReturnsSeatTokenOf32HexChars()
void SerializedState_NeverContainsSeatToken()   // JsonSerializer.Serialize(seat.Game, JsonDefaults.Options) ne contient pas seat.SeatToken
void Act_WithUnknownToken_ThrowsInvalidSeat()
void Join_SameName_ReturnsSamePlayerIdAndNewToken_OldTokenRejected()
void Act_BroadcastsGameListAndScreen()          // 1 appel de chaque sur FakeNotifier
void Featured_PrefersInProgressOverNewerLobby()
void Featured_ShowsFinishedGameFor30SecondsThenNextLobby() // avance l'horloge de 31 s puis TickAsync → ScreenChanged(lobby)
void Tick_CompletesCountdownAfter10Seconds()
void Tick_RemovesFinishedGamesAfter10Minutes()
void Tick_RemovesIdleGamesAfter6Hours()
void List_ExcludesFinishedAndCancelled()
```

- [ ] **Étape 2 :** `cd backend; dotnet test --filter GameServiceTests` → FAIL.
- [ ] **Étape 3 : implémenter** `IGameNotifier.cs` et `GameService.cs`.
- [ ] **Étape 4 :** `cd backend; dotnet test` → tous PASS.
- [ ] **Étape 5 : commit.** `git commit -m "feat: in-memory game service with seat tokens and screen selection"`

---

### Tâche 3 : Hub, REST, ticker, service des fichiers statiques

**Fichiers :**
- Réécrire : `backend/src/LifeCounterAssist.Api/Hubs/GameHub.cs`, `Hubs/Notifier.cs` (implémente `IGameNotifier`)
- Réécrire : `backend/src/LifeCounterAssist.Api/Endpoints/ApiEndpoints.cs`
- Créer : `backend/src/LifeCounterAssist.Api/Services/GameTicker.cs` (`BackgroundService`, `PeriodicTimer` d'1 s qui appelle `TickAsync`, et loggue les exceptions sans s'arrêter)
- Réécrire : `backend/src/LifeCounterAssist.Api/Program.cs`, `appsettings*.json`, `Properties/launchSettings.json` (port 5075 inchangé)

**Interfaces :**
- Consomme : `GameService` (tâche 2).
- Produit (contrat consommé par le front) :
  - REST :
    - `GET /api/games` → `GameSummaryDto[]`
    - `POST /api/games` `{name, startingLife}` → `SeatDto`
    - `POST /api/games/{id}/join` `{name}` → `SeatDto`
    - `GET /api/games/{id}` → `GameState`
  - Événements serveur → client :
    - `GameUpdated(GameState)` au groupe `game:{id}`
    - `ListChanged(GameSummaryDto[])` au groupe `list`
    - `ScreenChanged(GameState | null)` au groupe `screen`
  - Méthodes du hub :
    - `Watch(gameId) → GameState`, `Unwatch(gameId)`, `WatchList() → GameSummaryDto[]`, `WatchScreen() → GameState?`
    - `StartGame(gameId, seatToken)`, `CancelGame(gameId, seatToken)`, `LeaveGame(gameId, seatToken)`
    - `AdjustLife(gameId, seatToken, targetPlayerId, delta)`
    - `AdjustCommanderDamage(gameId, seatToken, targetPlayerId, sourcePlayerId, delta)`
    - `AdjustPoison(gameId, seatToken, targetPlayerId, delta)`
    - `ToggleConcede(gameId, seatToken, targetPlayerId)`
    - `CancelPendingFinish(gameId, seatToken)`, `FinishGame(gameId, seatToken)`
  - Les deltas sont bornés à ±100 (comme aujourd'hui).
- Program :
  - aucune authentification ;
  - CORS seulement si `Cors:AllowedOrigins` n'est pas vide (dev Vite) ;
  - `UseDefaultFiles()`, `UseStaticFiles()`, puis `MapFallbackToFile("index.html")` après `MapApi` et `MapHub` ;
  - `AddHostedService<GameTicker>()` ;
  - `GameService` et `Notifier` en singletons ; `IGameNotifier` résolu vers `Notifier`.

- [ ] **Étape 1 : implémenter** les fichiers ci-dessus. Aucun test unitaire nouveau : le hub et les endpoints sont de simples passe-plats, et la logique est testée en tâches 1-2.
- [ ] **Étape 2 :** `cd backend; dotnet build` → 0 erreur, 0 avertissement nouveau ; `dotnet test` → PASS.
- [ ] **Étape 3 : test de fumée.** `dotnet run --project src/LifeCounterAssist.Api --launch-profile http` puis :
  - `curl -s -X POST localhost:5075/api/games -H "Content-Type: application/json" -d '{"name":"Alice","startingLife":40}'` → JSON avec `seatToken` ;
  - `curl -s localhost:5075/api/games` → 1 partie ;
  - `curl -s -X POST localhost:5075/api/games -H "Content-Type: application/json" -d '{"name":"","startingLife":40}'` → 400 avec `code: "invalid_name"`.
- [ ] **Étape 4 : commit.** `git commit -m "feat: guest hub, REST endpoints and game ticker"`

---

### Tâche 4 : Front — socle sans compte, accueil, création, rejoindre

**Fichiers :**
- Supprimer : `src/auth/`, `src/session.ts`, `src/pages/LoginPage.tsx`, `DecksPage.tsx`, `FriendsPage.tsx`, `ProfilePage.tsx`, `src/components/DeckSelect.tsx`, `public/config.js`, la balise `<script src="/config.js">` d'`index.html`, et la dépendance `@azure/msal-browser`
- Réécrire : `src/config.ts` (`apiUrl = ''`, c'est-à-dire la même origine), `src/api/types.ts`, `src/api/client.ts`, `src/api/hub.ts`, `src/App.tsx`, `src/pages/HomePage.tsx`, `src/pages/CreateGamePage.tsx`
- Créer : `src/seats.ts`
- Modifier : `vite.config.ts` (proxy `/api` et `/hubs` → `http://localhost:5075`, `ws: true` pour `/hubs`), `src/i18n/fr.ts`, `src/i18n/en.ts` (retirer les clés mortes, ajouter les nouvelles et les codes d'erreur `invalid_name`, `invalid_starting_life`, `invalid_seat`, `game_full`, `game_already_started`, `game_over`, `game_not_found`)

**Interfaces produites :**

```ts
// types.ts : miroir des DTO C# de la tâche 2 (camelCase), GameStatus = 'Lobby'|'InProgress'|'Finished'|'Cancelled'
// seats.ts
export interface Seat { playerId: string; seatToken: string }
export function getSeat(gameId: string): Seat | null
export function saveSeat(gameId: string, seat: Seat): void
export function clearSeat(gameId: string): void
export function getLastName(): string            // pseudo mémorisé, '' par défaut
export function setLastName(name: string): void
// Tout accès à localStorage passe par try/catch.

// client.ts
export const api = { games(), game(id), createGame(name, startingLife), joinGame(id, name) }  // createGame/joinGame → SeatDto, et appellent saveSeat + setLastName
export function errorCode(e: unknown): string    // inchangé

// hub.ts : getHub/onHubEvent/onReconnected conservés (sans accessTokenFactory) ; gameHub = une fonction par méthode du hub de la tâche 3
```

Routes : `/` (accueil), `/nouvelle` (création), `/parties/:id` (tâche 5), `/ecran` (tâche 6), et `*` → `/`.

- Accueil :
  - abonnement `WatchList` + `ListChanged` ; bouton « Créer une partie » ;
  - par partie : « Partie de {hostName} », nombre de joueurs/8, PV, statut ;
  - si j'ai déjà un siège dans cette partie : « Reprendre » ;
  - lobby : « Rejoindre » ;
  - partie en cours : « Reprendre ma place » (ouvre la saisie du pseudo) ;
  - clic sur Rejoindre / Reprendre ma place : feuille (`Sheet`) avec le champ pseudo pré-rempli, puis `api.joinGame` et navigation vers `/parties/:id`.
- Création : pseudo + 4 boutons PV (40 sélectionné), puis `api.createGame` et navigation.

- [ ] **Étape 1 : implémenter.** `npm uninstall @azure/msal-browser`.
- [ ] **Étape 2 :** `cd frontend; npm run build` → 0 erreur TypeScript. `GamePage.tsx` peut rester temporairement une page minimale qui affiche `game.status`.
- [ ] **Étape 3 : vérifier à la main.** API (tâche 3) + `npm run dev`, puis ouvrir `http://localhost:5180` : créer une partie, la voir apparaître en direct dans un second onglet, la rejoindre avec un autre pseudo.
- [ ] **Étape 4 : commit.** `git commit -m "feat(web): guest home, create and join flows"`

---

### Tâche 5 : Front — page partie (lobby, plateau téléphone, compte à rebours, fin)

**Fichiers :**
- Réécrire : `src/pages/GamePage.tsx`, `src/hooks/useGame.ts` (événement `GameUpdated`), `src/components/PlayerDetails.tsx`
- Adapter : `src/components/PlayerTile.tsx` (sans illustration, monarque ni initiative), `src/hooks/useOptimistic.ts` (clés `playerId`)
- Créer : `src/components/FinishCountdown.tsx` (`{ game: GameState, onCancel?: () => void }` : affiche « {nom} gagne dans N s… » à partir de `pendingFinishAt` et de l'horloge locale ; bouton Annuler si `onCancel` est fourni), réutilisé par l'écran en tâche 6 (sans `onCancel`)

Comportement :
- **Sans siège pour cette partie** : partie en lobby → feuille de saisie du pseudo ; sinon, lecture seule avec un bouton « Reprendre ma place ».
- **Erreur `invalid_seat` reçue** : `clearSeat(gameId)` et ouverture de la saisie du pseudo (point de vigilance 2).
- **Lobby** :
  - liste des pseudos en direct ;
  - l'hôte voit « Démarrer » (désactivé sous 2 joueurs) et « Annuler la partie » ;
  - les autres voient « Quitter » ;
  - partie `Cancelled` → message puis retour à l'accueil.
- **Plateau** :
  - mon joueur en grand : PV (`HoldButton`, ±1 au tap, ±10 en appui long), poison ±1, dégâts de commandant reçus ±1 par adversaire, bouton « J'abandonne » / « Je reviens » ;
  - les autres joueurs en liste compacte (nom, PV, poison si > 0) ; un appui ouvre `PlayerDetails` pour corriger leurs compteurs ;
  - `FinishCountdown` avec `onCancel` quand `pendingFinishAt` n'est pas nul ;
  - menu « Terminer la partie » avec confirmation.
- **Fin** : gagnant ou « Partie terminée », bouton Accueil.
- Les mises à jour optimistes des PV sont conservées (`usePendingDeltas`).

- [ ] **Étape 1 : implémenter.**
- [ ] **Étape 2 :** `cd frontend; npm run build` → 0 erreur.
- [ ] **Étape 3 : vérifier à la main** avec deux navigateurs (Alice, Bob) :
  - démarrer ; Bob à 0 PV → compte à rebours chez les deux ;
  - Annuler → il ne repart pas quand Alice perd 1 PV ;
  - Bob +1 puis −1 → il repart ; à 0 → écran de fin.
  - Ouvrir la partie en navigation privée avec « bob » → reprise de place ; l'ancien onglet de Bob revient à la saisie du pseudo à sa prochaine action.
- [ ] **Étape 4 : commit.** `git commit -m "feat(web): lobby, phone board and finish countdown"`

---

### Tâche 6 : Front — page écran `/ecran`

**Fichiers :**
- Créer : `src/pages/ScreenPage.tsx`, `src/components/ScreenBoard.tsx`
- Modifier : `package.json` (`npm install qrcode.react`), i18n FR/EN

Comportement :
- `WatchScreen` à l'ouverture, écoute de `ScreenChanged`, ré-abonnement dans `onReconnected` (point de vigilance 5). Bandeau discret « Reconnexion… » pendant une coupure.
- Pas de partie : `QRCodeSVG` de `window.location.origin` en environ 60 vh de haut, « Scanne pour jouer », et la liste des lobbys ouverts (via `WatchList`).
- Lobby : « Partie de {hôte} · {PV} PV », pseudos en gros, QR code d'environ 25 vh dans un coin.
- En cours : `ScreenBoard`.
  - Colonnes selon le nombre de joueurs : 2 → 2, 3-4 → 2, 5-6 → 3, 7-8 → 4. La grille remplit `100dvh` sans défilement.
  - Sur chaque tuile : nom, PV en `clamp(4rem, 15vh, 12rem)`, poison si > 0, dégâts de commandant reçus non nuls (« ⚔ {source} 12 »).
  - Joueur éliminé grisé. Le dernier delta reste affiché 2 s (`useRecentLifeChanges`).
  - `FinishCountdown` sans bouton.
- Terminée : gagnant en très grand (« 🏆 Alice »), ou « Partie terminée » s'il n'y a pas de gagnant.
- Aucun bouton requis. Le Wake Lock existant (`main.tsx`) s'applique.

- [ ] **Étape 1 : implémenter.**
- [ ] **Étape 2 :** `cd frontend; npm run build` → 0 erreur.
- [ ] **Étape 3 : vérifier à la main.**
  - `/ecran` dans une fenêtre 1920×1080 : QR code → lobby à la création → plateau au démarrage → gagnant → QR code environ 30 s après la fin.
  - Couper l'API puis la relancer : l'écran revient tout seul (la partie est perdue, c'est voulu, et l'écran revient au QR code).
- [ ] **Étape 4 : commit.** `git commit -m "feat(web): room screen with QR code and live board"`

---

### Tâche 7 : Empaquetage unique, infra simplifiée, README

**Fichiers :**
- Créer : `Dockerfile` à la racine. Étapes :
  1. `node:22-alpine` : `npm ci` + `npm run build` dans `frontend/` ;
  2. `dotnet/sdk:10.0` : publish de l'API ;
  3. `dotnet/aspnet:10.0` : copie de la publication, plus `frontend/dist` vers `/app/wwwroot` ; `ASPNETCORE_HTTP_PORTS=8080`, `USER $APP_UID`.
- Créer : `.dockerignore` à la racine (`**/node_modules`, `**/bin`, `**/obj`, `frontend/dist`, `.git`)
- Supprimer : `backend/Dockerfile`, `backend/.dockerignore`, `frontend/Dockerfile`, `frontend/.dockerignore`, `frontend/nginx.conf`, `frontend/docker/`, `infra/bicep/modules/storage.bicep`
- Réécrire : `docker-compose.yml` (un service `app`, port `8080:8080`, `ASPNETCORE_ENVIRONMENT: Production`)
- Modifier :
  - `infra/bicep/main.bicep`, `types.bicep`, `main.bicepparam` : plus de stockage ni de paramètre `auth`, sortie `appUrl` ;
  - `modules/identity.bicep` : AcrPull uniquement ;
  - `modules/container-apps.bicep` : une seule app `lca-app`, ingress externe sur 8080, min 1 / max 1, sonde `/health`.
- Réécrire : `README.md`. Contenu :
  - présentation de la nouvelle version ;
  - architecture ;
  - dev local (API + `npm run dev` ; `/ecran`) ;
  - Docker (`docker compose up --build` puis `http://localhost:8080`) ;
  - déploiement Azure en deux passes (`az acr build --registry $acr --image "lca-app:$tag" .`) ;
  - limites : état en mémoire, une instance, URL publique sans protection.

- [ ] **Étape 1 : implémenter.**
- [ ] **Étape 2 :** `az bicep build --file infra/bicep/main.bicep` → 0 erreur (si `az` est absent, le signaler et passer).
- [ ] **Étape 3 :** `docker compose up --build` → `http://localhost:8080` sert l'accueil, `http://localhost:8080/ecran` le QR code et `/health` répond 200. Si Docker est absent, à la place : `npm run build`, copier `dist` dans `backend/src/LifeCounterAssist.Api/wwwroot`, `dotnet run`, puis vérifier les mêmes URL sur le port 5075. Ajouter `wwwroot/` au `.gitignore` de l'API.
- [ ] **Étape 4 : commit.** `git commit -m "build: single container, simplified infra and README"`

---

### Tâche 8 : Recette de bout en bout

- [ ] **Étape 1 :** `cd backend; dotnet test` → PASS ; `cd frontend; npm run build` → OK.
- [ ] **Étape 2 :** sur l'app servie par un seul processus (tâche 7, étape 3), trois navigateurs : `/ecran`, Alice, Bob. Dérouler le parcours complet de la spec :
  - scan (ouvrir l'URL), création à 30 PV, rejoindre, démarrer ;
  - dégâts de commandant 21 → mort ;
  - compte à rebours, annulation, fin ;
  - retour de l'écran au QR code.
  Noter tout écart.
- [ ] **Étape 3 :** corriger les écarts (une étape de test par correction si la logique est côté serveur), puis commit `fix: ...`.
