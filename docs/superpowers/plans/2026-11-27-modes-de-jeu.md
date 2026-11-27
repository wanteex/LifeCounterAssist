# Modes de jeu (suivi des tours + Blitz) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Track the active player and round in every game, and add a Blitz mode with a growing per-turn clock, combat/pause freezes, an alarm, and an optional −1 life per overtime second.

**Architecture:** The server owns the turn state (`GameState.Turn`) as "elapsed so far + running since", stamped with `ServerTime` on every outgoing state; clients count locally with a server-offset correction. One pure function `GameRules.SyncTurn` recomputes whether the clock runs and whether an eliminated active player's turn is due to be skipped, and is called after every mutation. `GameTicker` (1 s) applies penalties and due skips.

**Tech Stack:** ASP.NET Core .NET 10 + SignalR, xUnit; React 19 + Vite + Tailwind 4 + i18next.

**Spec:** `docs/superpowers/specs/2026-11-27-modes-de-jeu-design.md`

## Global Constraints

- Modes: `Normal` (default) and `Blitz`. Enums serialize as strings (`JsonStringEnumConverter` already registered), camelCase JSON.
- Blitz base seconds ∈ {15, 30, 60}, default 30; growth ×1.5 per round; cap 300 s. Limit of round r = `min(300, round(base × 1.5^(r−1)), MidpointRounding.AwayFromZero)` → base 30: 30, 45, 68, 101, 152, 228, 300.
- Penalty off by default; −1 life per full overtime second, only while the clock runs.
- Auto-skip delay when the active player is eliminated: 5 s (`TurnSkipDelay`), cancelled if they come back.
- Clock frozen when: combat, pause, active player eliminated, ≤ 1 player alive, game not in progress.
- Combat and pause are Blitz-only (`blitz_only`); pause is host-only (`host_only`); EndTurn/ToggleCombat by active player or host (`not_your_turn`).
- Normal mode clock counts up (elapsed), no limit, no alarm.
- First player = seat 1 (first non-eliminated in `Players` order).
- i18n: every new key in `en.ts` and `fr.ts` (`fr: Translation` must match `en`).
- Never touch `PlayerState.AckSeq` outside `Acknowledge`.

## Review Focus

- A life/poison/commander change during combat or pause must not restart the clock → `SyncTurn` test in Task 3.
- Ending pause while combat is still on keeps the clock frozen (and vice-versa) → test in Task 3.
- Two ticks within the same overtime second must not penalise twice → test in Task 5.
- Penalty must not change `AckSeq` (optimistic deltas on the active player's phone) → test in Task 5.
- Host EndTurn while a skip is pending clears `SkipAt` and moves exactly one seat → test in Task 4.

---

### Task 1: Mode, Blitz settings and turn state on creation/start

**Files:**
- Modify: `backend/src/LifeCounterAssist.Api/Domain/GameState.cs`
- Modify: `backend/src/LifeCounterAssist.Api/Domain/GameRules.cs` (`Create`, `Start`)
- Modify: `backend/src/LifeCounterAssist.Api/Services/GameService.cs` (`CreateGameRequest`, `CreateAsync`)
- Test: `backend/src/LifeCounterAssist.Api.Tests/GameRulesTests.cs`

**Interfaces:**
- Produces:
  - `enum GameMode { Normal, Blitz }`; `GameState.Mode` (default `Normal`), `GameState.Blitz : BlitzSettings?`, `GameState.Turn : TurnState?`, `GameState.ServerTime : DateTimeOffset`.
  - `sealed record BlitzSettings(int BaseSeconds, bool Penalty)`.
  - `sealed class TurnState { string ActivePlayerId; int Round; long ElapsedMs; DateTimeOffset? RunningSince; bool Combat; bool Paused; int PenaltySeconds; DateTimeOffset? SkipAt; int? LimitSeconds }` (`LimitSeconds` null in Normal).
  - `GameRules.Create(string id, string hostPlayerId, string hostName, int startingLife, DateTimeOffset now, CommanderInfo? commander = null, GameMode mode = GameMode.Normal, BlitzSettings? blitz = null)`.
  - `GameRules.TurnLimitSeconds(int baseSeconds, int round) : int`; constants `BlitzGrowth = 1.5`, `BlitzMaxSeconds = 300`, `TurnSkipDelay = 5 s`.
  - `CreateGameRequest(string Name, int StartingLife = 40, CommanderInfo? Commander = null, GameMode Mode = GameMode.Normal, BlitzSettings? Blitz = null)`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Create_DefaultsToNormalWithoutBlitz()  // Mode == Normal, Blitz null, Turn null
[Fact] public void Create_Blitz_DefaultSettingsWhenNull()   // mode Blitz, blitz null → Blitz == new(30, false)
[Theory, InlineData(0), InlineData(20), InlineData(90)]
public void Create_Blitz_RejectsBaseSeconds(int s)          // Throws DomainException "invalid_blitz"
[Fact] public void Create_Normal_IgnoresBlitzSettings()     // mode Normal + blitz(15,true) → Blitz null
[Theory, InlineData(30,1,30), InlineData(30,2,45), InlineData(30,3,68), InlineData(15,2,23), InlineData(60,5,300), InlineData(30,9,300)]
public void TurnLimitSeconds_GrowsAndCaps(int b, int r, int expected)
[Fact] public void Start_SetsTurnOnFirstSeat_ClockRunning() // Normal: ActivePlayerId p1, Round 1, ElapsedMs 0, RunningSince Now, LimitSeconds null
[Fact] public void Start_Blitz_SetsLimitOfRoundOne()        // Blitz(30): LimitSeconds 30
```

- [ ] **Step 2: Run** `dotnet test backend --filter "FullyQualifiedName~GameRulesTests"` — Expected: build FAIL (missing members).
- [ ] **Step 3: Implement** the types and members above; `Start` sets `Turn` then calls nothing else yet. Thread `Mode`/`Blitz` through `CreateAsync`.
- [ ] **Step 4: Run** same command — Expected: PASS, previous tests still green.
- [ ] **Step 5: Commit** `feat: game mode and turn state`

### Task 2: EndTurn rotation

**Files:** `GameRules.cs`, `GameRulesTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces: `GameRules.EndTurn(GameState state, string actor, DateTimeOffset now)`; private `AdvanceTurn(GameState state, DateTimeOffset now, string logMessage)` (shared with Task 4); private `ElapsedMs(TurnState t, DateTimeOffset now) : long`.

Rotation algorithm (spec): from active index `i`, for k = 1..n take `j = (i+k) % n`, first non-eliminated wins; if `i + k >= n`, `Round++`. If none other alive, `j = i` and `Round++`. Reset `ElapsedMs=0, PenaltySeconds=0, Combat=false, SkipAt=null, LimitSeconds` for new round (Blitz), `RunningSince = Paused ? null : now`. Log `turn_ended` (actor, target = new active) or the given message.

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public void EndTurn_PassesToNextSeat()                   // 3 players: p1 → p2, Round 1
[Fact] public void EndTurn_WrapIncrementsRound()                // p1→p2→p3→p1: Round 2
[Fact] public void EndTurn_SkipsEliminatedSeats()               // p2 killed: p1 → p3
[Fact] public void EndTurn_ByHostWhenNotActive_Allowed()        // active p2, actor p1 (host) → p3
[Fact] public void EndTurn_ByOtherPlayer_Forbidden()            // active p1, actor p2 → ForbiddenException "not_your_turn"
[Fact] public void EndTurn_ResetsClockAndCombat()               // ElapsedMs 0, RunningSince == now, Combat false
[Fact] public void EndTurn_Blitz_NewRoundRaisesLimit()          // Blitz(30) after wrap: LimitSeconds 45
[Fact] public void EndTurn_NotInProgress_Throws()               // lobby → "game_not_in_progress"
```

- [ ] **Step 2: Run** filter `GameRulesTests` — Expected: FAIL (missing `EndTurn`).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `feat: end turn rotation`

### Task 3: Combat, pause and `SyncTurn`

**Files:** `GameRules.cs`, `GameRulesTests.cs`

**Interfaces:**
- Produces: `GameRules.ToggleCombat(state, actor, now)`, `GameRules.TogglePause(state, actor, now)`, `GameRules.SyncTurn(GameState state, DateTimeOffset now)` (public, called by every mutation and by the service).

`SyncTurn` (algorithm the tests do not fully pin):

```
if Turn is null: return
alive = players not eliminated; active = FindPlayer(ActivePlayerId)
// skip scheduling (Task 4 fills tests; implement here)
if active eliminated and alive.Count >= 1 and Status InProgress: SkipAt ??= now + TurnSkipDelay
else SkipAt = null
shouldRun = Status == InProgress && !Combat && !Paused && SkipAt is null && !active.IsEliminated && alive.Count >= 2
if shouldRun && RunningSince is null: RunningSince = now
if !shouldRun && RunningSince is not null: ElapsedMs += (now - RunningSince).ms; RunningSince = null
```

Call `SyncTurn` at the end of `RefreshPendingFinish`, `Finish`, `CompletePendingFinish`, `Start`, `EndTurn`/`AdvanceTurn`, `ToggleCombat`, `TogglePause`. Log `combat_started`/`combat_ended`, `paused`/`resumed`.

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public void ToggleCombat_FreezesThenResumes()          // Blitz; +10s combat → ElapsedMs 10000, RunningSince null, Combat; +20s end combat → RunningSince == t
[Fact] public void ToggleCombat_Normal_Throws()               // "blitz_only"
[Fact] public void ToggleCombat_ByOtherPlayer_Forbidden()     // "not_your_turn"
[Fact] public void TogglePause_HostOnly()                     // actor p2 → ForbiddenException "host_only"
[Fact] public void Resume_WhileCombat_StaysFrozen()           // combat on, pause on, pause off → RunningSince null
[Fact] public void LifeChange_DuringCombat_DoesNotRestartClock()
[Fact] public void Finish_FreezesClock()                       // RunningSince null, ElapsedMs accumulated
[Fact] public void SingleSurvivor_FreezesClock()              // 2 players, kill p2 (not active) → RunningSince null
```

- [ ] **Step 2–4:** run filter `GameRulesTests` (FAIL → implement → PASS).
- [ ] **Step 5: Commit** `feat: combat and pause freeze the turn clock`

### Task 4: Delayed skip of an eliminated active player

**Files:** `GameRules.cs`, `GameRulesTests.cs`

**Interfaces:**
- Consumes: `SyncTurn`, `AdvanceTurn`.
- Produces: `GameRules.CompletePendingSkip(GameState state, DateTimeOffset now) : bool` (true when it advanced; log `turn_skipped`).

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public void ActiveEliminated_SchedulesSkipAndFreezes()  // 3 players, kill p1 at Now → SkipAt == Now+5s, RunningSince null
[Fact] public void ActiveRevived_CancelsSkip_ClockResumes()    // kill p1, +2s life +40 → SkipAt null, RunningSince == Now+2s, ElapsedMs unchanged
[Fact] public void CompletePendingSkip_BeforeDelay_NoOp()      // +4s → false
[Fact] public void CompletePendingSkip_AfterDelay_Advances()   // +5s → true, active p2, log "turn_skipped"
[Fact] public void HostEndTurn_WhilePendingSkip_MovesOneSeat() // SkipAt null, active p2
[Fact] public void ConcedeActive_SchedulesSkip()
```

- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat: skip an eliminated active player after a delay`

### Task 5: Overtime penalty

**Files:** `GameRules.cs`, `GameRulesTests.cs`

**Interfaces:**
- Produces: `GameRules.ApplyTimePenalty(GameState state, DateTimeOffset now) : bool`.

Rule: only if Blitz, `Penalty`, InProgress, `RunningSince` not null; `over = floor((ElapsedMs(now) − LimitSeconds×1000) / 1000)`; if `over > PenaltySeconds`: active `Life −= over − PenaltySeconds`, `PenaltySeconds = over`, log `time_penalty` (target = active, value = −lost), then `RefreshPendingFinish` (which syncs). Returns true when changed.

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public void Penalty_Off_NoLifeLoss()                    // Blitz(15,false), +20s → false, life 40
[Fact] public void Penalty_TakesOnePerFullSecond()             // Blitz(15,true), +15.5s → false; +17.2s → life 38, PenaltySeconds 2
[Fact] public void Penalty_SameSecondTwice_NotDoubled()        // +17.2s then +17.9s → life 38
[Fact] public void Penalty_NotDuringCombat()                   // combat at +10s, check at +40s → life 40
[Fact] public void Penalty_ResetsOnNewTurn()                   // overtime p1, EndTurn → PenaltySeconds 0
[Fact] public void Penalty_DoesNotTouchAckSeq()                // Acknowledge(p1, 7); penalty → AckSeq 7
[Fact] public void Penalty_LethalSchedulesSkip()               // life 1 in overtime → eliminated, SkipAt set, clock frozen
```

- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat: blitz overtime penalty`

### Task 6: Service, hub, ticker, server time

**Files:**
- Modify: `Services/GameService.cs` (`TickAsync`, clone → instance `Snapshot` stamping `ServerTime = clock.GetUtcNow()` on every state leaving the service: returns, `GameUpdatedAsync`, screen feed)
- Modify: `Hubs/GameHub.cs`: `EndTurn(gameId, seatToken)`, `ToggleCombat(gameId, seatToken)`, `TogglePause(gameId, seatToken)`
- Test: `GameServiceTests.cs`

`TickAsync` per game: `changed = CompletePendingFinish || CompletePendingSkip || ApplyTimePenalty` (evaluate all three, OR results); if changed, store + broadcast (existing path).

- [ ] **Step 1: Failing tests** (fake clock as existing tests do)

```csharp
[Fact] public async Task Create_WithBlitz_StoresSettings()      // Mode Blitz, Blitz(15,true)
[Fact] public async Task Get_StampsServerTime()                 // ServerTime == clock now
[Fact] public async Task Tick_AppliesPenaltyAndBroadcasts()     // Blitz(15,true) started, advance 17s, Tick → life 38, one GameUpdated
[Fact] public async Task Tick_CompletesPendingSkip()            // kill active, advance 5s, Tick → active changed
[Fact] public async Task Tick_NothingDue_NoBroadcast()
```

- [ ] **Step 2–4:** `dotnet test backend` FAIL → implement → PASS (whole suite).
- [ ] **Step 5: Commit** `feat: turn hub methods and ticker`

### Task 7: Frontend API + create page

**Files:** `frontend/src/api/types.ts`, `api/client.ts`, `api/hub.ts`, `pages/CreateGamePage.tsx`, `i18n/en.ts`, `i18n/fr.ts`

**Interfaces:**
- Produces (TS): `type GameMode = 'Normal' | 'Blitz'`; `interface BlitzSettings { baseSeconds: number; penalty: boolean }`; `interface TurnState { activePlayerId; round; elapsedMs; runningSince?: string|null; combat; paused; penaltySeconds; skipAt?: string|null; limitSeconds?: number|null }`; `GameState.mode`, `.blitz?`, `.turn?`, `.serverTime`; `BLITZ_BASE_OPTIONS = [15, 30, 60]`.
- `api.createGame(name, life, commander?, mode = 'Normal', blitz?)`.
- `gameHub.endTurn(gameId, seatToken)`, `.toggleCombat(...)`, `.togglePause(...)`.
- i18n keys: `create.mode`, `create.modeNormal`, `create.modeBlitz`, `create.blitzBase`, `create.blitzPenalty`, `create.blitzHint`; `turn.of` ("Au tour de {{name}}" / "{{name}}'s turn"), `turn.yours`, `turn.round` ("Tour {{n}}"), `turn.end`, `turn.combat` ("⚔ Combat"), `turn.endCombat`, `turn.pause`, `turn.resume`, `turn.paused`, `turn.inCombat`, `turn.skipping`, `turn.penalty` ("−{{n}} PV"); `errors.not_your_turn`, `errors.blitz_only`, `errors.invalid_blitz`.

Create page: segmented Normal/Blitz buttons (same `Button` style as starting life); Blitz shows base buttons (15/30/60 s, default 30), a penalty checkbox (unchecked), and the hint "×1,5 à chaque tour de table, max 5 min".

- [ ] **Step 1:** implement; **Step 2:** `npm run build` in `frontend` — Expected: no error.
- [ ] **Step 3: Commit** `feat: choose game mode when creating a game`

### Task 8: Turn clock hook + phone turn bar

**Files:**
- Create: `frontend/src/hooks/useTurnClock.ts`
- Create: `frontend/src/components/TurnBar.tsx`
- Modify: `pages/GamePage.tsx` (render `TurnBar` above the board when `game.status === 'InProgress'`; wire hub calls through existing `run`)

**Interfaces:**
- `useTurnClock(game: GameState | null): { elapsedMs: number; remainingMs: number | null; overtime: boolean; frozen: boolean }` — offset = `Date.parse(game.serverTime) − Date.now()` captured when `game` changes; refresh every 250 ms; `remainingMs` null in Normal.
- `formatClock(ms: number): string` → `m:ss`, overtime as `+m:ss`.
- `TurnBar({ game, myId, onEndTurn, onCombat, onPause })`: banner "Au tour de X · Tour N · clock"; "À toi" when mine; buttons: End turn (active or host), Combat/End combat (Blitz, active or host), Pause/Resume (Blitz, host); shows `turn.skipping` when `skipAt` set, `turn.paused`/`turn.inCombat` when frozen; red pulse when overtime; penalty total when `penaltySeconds > 0`.
- Beep: when `overtime` flips false→true and I am the active player, play a ~300 ms Web Audio tone (wrap in try/catch).

- [ ] **Step 1:** implement; **Step 2:** `npm run build` — Expected: no error.
- [ ] **Step 3: Commit** `feat: turn bar on phones`

### Task 9: Room screen

**Files:** `components/ScreenBoard.tsx` (and `pages/ScreenPage.tsx` if the header lives there)

- Active tile: highlight ring (e.g. `ring-4 ring-amber-400`), large clock under the life (remaining in Blitz, elapsed in Normal), grey + "⚔ Combat"/"Pause" when frozen, red pulse when overtime, `turn.skipping` when `skipAt`.
- Header: `turn.round` with the current round.
- Uses `useTurnClock` and `formatClock` from Task 8.

- [ ] **Step 1:** implement; **Step 2:** `npm run build` — Expected: no error.
- [ ] **Step 3: Commit** `feat: show the active player and clock on the room screen`

### Task 10: End-to-end check

**Files:** scratchpad `turns.js` (Playwright, same harness as `seating.js`; not committed)

Scenario (API on 5075, Vite on 5180): host creates Blitz 15 s with penalty, 3 players, start.
- Screen tile of seat 1 highlighted; phones show "À toi" for p1 only; clock decreasing.
- p1 Combat → clock frozen on screen for 3 s; End combat → resumes.
- p1 End turn → p2 active; p3 End turn button absent; host can end p2's turn → p3.
- Wait for overtime on p3 → screen pulse, p3 life drops by ~1/s.
- Set p3 life to 0 → "Passage au suivant…", after 5 s p1 active, round 2, limit 23 s.
- Normal game: clock counts up, no Combat/Pause buttons.
- No page errors; long-press ± still no bounce (re-run `holdtrace.js` quickly).

- [ ] **Step 1:** write and run; Expected: all PASS, `ERRORS: none`.
- [ ] **Step 2:** `dotnet test backend` and `npm run build` green; final commit if any fix.
