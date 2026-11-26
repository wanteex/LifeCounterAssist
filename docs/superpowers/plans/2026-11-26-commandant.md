# Choix du commandant Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a player pick their MTG commander (Scryfall search) when entering their name, and show its art as the background of their tile on `/ecran` and on phones.

**Architecture:** The browser queries Scryfall directly; the chosen card's name, `art_crop` URL and artist travel with the create/join REST requests. The backend validates the URL host and stores a nullable `Commander` on `PlayerState`, which flows to every client through the existing `GameUpdated` / `ScreenChanged` broadcasts.

**Tech Stack:** ASP.NET Core (.NET 10) + xUnit; React 19, Vite, Tailwind 4, i18next.

**Spec:** `docs/superpowers/specs/2026-11-26-commandant-design.md`

## Global Constraints

- Commander is optional everywhere; create/join without it keeps working unchanged.
- Server accepts `ArtUrl` only if it starts with `https://cards.scryfall.io/art_crop/` and length ≤ 300; `Name` trimmed 1–150 chars; `Artist` trimmed 0–100 chars. Otherwise `DomainException("invalid_commander")`.
- Scryfall: `https://api.scryfall.com/cards/search?q=<text> is:commander&order=edhrec`, called only after ~300 ms without typing and ≥ 2 chars, stale requests aborted, max 8 results shown.
- Double-faced cards: art from `card_faces[0].image_uris.art_crop` when `image_uris` is absent.
- Artist credit shown on every tile that shows art. Art uses `object-fit: cover` only, with a dark overlay so life totals stay readable.
- Scryfall failure must never block joining: show `commander.unavailable` text, form stays submittable.
- One commander per player (no partners). No mid-game change except via reclaim.
- Commits: conventional, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- Reclaim with a new commander changes state: it must be broadcast (today a reclaim skips broadcast). Test in Task 1.
- Reclaim without a commander must keep the existing one, not erase it. Test in Task 1.
- `javascript:` / other-host / `http://cards.scryfall.io` URLs rejected. Test in Task 1.
- Art image fails to load (offline room): tile falls back to the plain background, no broken-image icon. Handled in Task 3 (`onError`).
- Typing fast: results of an older query must not overwrite a newer one. Handled in Task 2 (AbortController), checked in Playwright run of Task 3.

---

### Task 1: Backend commander on player

**Files:**
- Modify: `backend/src/LifeCounterAssist.Api/Domain/GameState.cs` (add record + `PlayerState.Commander`)
- Modify: `backend/src/LifeCounterAssist.Api/Domain/GameRules.cs` (`NormalizeCommander`, `Create`, `Join`)
- Modify: `backend/src/LifeCounterAssist.Api/Services/GameService.cs` (requests, `CreateAsync`, `JoinAsync` broadcast on reclaim change)
- Test: `backend/src/LifeCounterAssist.Api.Tests/GameRulesTests.cs`, `GameServiceTests.cs`

**Interfaces:**
- Produces (JSON camelCase via existing `JsonDefaults`):
  - `public sealed record CommanderInfo(string Name, string ArtUrl, string Artist);`
  - `PlayerState.Commander : CommanderInfo?` → JSON `commander: { name, artUrl, artist } | null`
  - `CreateGameRequest(string Name, int StartingLife = 40, CommanderInfo? Commander = null)`
  - `JoinGameRequest(string Name, CommanderInfo? Commander = null)`
  - `GameRules.NormalizeCommander(CommanderInfo? c) : CommanderInfo?` (null → null; trims; throws `invalid_commander`)
  - `GameRules.Create(..., DateTimeOffset now, CommanderInfo? commander = null)`
  - `GameRules.Join(..., DateTimeOffset now, CommanderInfo? commander = null)` returns `(PlayerState Player, bool Reclaimed)`; on reclaim, sets `existing.Commander` only when `commander` is non-null.
  - New error code `invalid_commander` (400, same path as other `DomainException`s).

- [ ] **Step 1: Write failing tests**

In `GameRulesTests.cs` (use `const string Art = "https://cards.scryfall.io/art_crop/front/4/a/x.jpg";`):
```csharp
[Fact] Create_WithCommander_StoresIt  // host.Commander == new CommanderInfo("Atraxa", Art, "Marta Nael")
[Fact] Join_WithCommander_StoresIt
[Fact] Join_Reclaim_WithoutCommander_KeepsExisting  // Alice joins with Atraxa, rejoins "alice" with null → still Atraxa
[Fact] Join_Reclaim_WithCommander_ReplacesIt
[Theory]
[InlineData("https://evil.example/art_crop/x.jpg")]
[InlineData("http://cards.scryfall.io/art_crop/x.jpg")]
[InlineData("javascript:alert(1)")]
[InlineData("https://cards.scryfall.io/large/front/x.jpg")]
NormalizeCommander_RejectsBadUrl(string url) // throws DomainException with Code "invalid_commander"
[Fact] NormalizeCommander_RejectsEmptyName_AndTooLongName  // "  " and new string('a',151)
[Fact] NormalizeCommander_TrimsNameAndArtist
```
In `GameServiceTests.cs`:
```csharp
[Fact] Join_ReclaimWithNewCommander_Broadcasts  // _notifier.Updated count increases; last state's player has new commander
[Fact] Join_ReclaimWithoutCommander_DoesNotBroadcast  // existing behavior preserved
[Fact] Create_WithBadCommander_ThrowsAndCreatesNothing  // _service.List() empty afterwards
```

- [ ] **Step 2: Run tests, verify they fail to compile/fail**

Run: `cd backend; dotnet test`
Expected: build errors on `CommanderInfo` / new parameters.

- [ ] **Step 3: Implement** the Interfaces above. Validate in `Create` before building state. In `JoinAsync`, broadcast when `!reclaimed || r.Commander is not null` (adjust the comment). Validate the commander before the "game_over" check is fine either way; keep `invalid_name` checked first.

- [ ] **Step 4: Run all tests**

Run: `cd backend; dotnet test`
Expected: all pass (48 existing + new).

- [ ] **Step 5: Commit** `feat(api): optional commander on player seats`

---

### Task 2: Frontend data layer and Scryfall search

**Files:**
- Modify: `frontend/src/api/types.ts`, `frontend/src/api/client.ts`, `frontend/src/seats.ts`
- Create: `frontend/src/api/scryfall.ts`

**Interfaces:**
- Consumes: Task 1 JSON shape.
- Produces:
  - `types.ts`: `export interface Commander { name: string; artUrl: string; artist: string }`; `PlayerState.commander?: Commander | null`.
  - `client.ts`: `api.createGame(name, startingLife, commander?: Commander | null)`, `api.joinGame(id, name, commander?: Commander | null)` — send `commander` in body (omit/null when none); on success call `setLastCommander(commander ?? null)` only when a commander was passed (keep previous otherwise).
  - `seats.ts`: `getLastCommander(): Commander | null`, `setLastCommander(c: Commander | null): void` (localStorage key `lca.commander`, validate shape on read, try/catch like existing helpers).
  - `scryfall.ts`: `searchCommanders(text: string, signal: AbortSignal): Promise<Commander[]>` — GET the Global Constraints URL with `encodeURIComponent`, `Accept: application/json`; 404 → `[]` (Scryfall's "no results"); other non-OK → throw; map each card to `Commander` (art from `image_uris` or `card_faces[0].image_uris`, skip cards without art_crop), first 8.

- [ ] **Step 1: Implement the four files.**
- [ ] **Step 2: Verify** `cd frontend; npm run build` → 0 errors.
- [ ] **Step 3: Commit** `feat(web): commander types, api payload and Scryfall search`

---

### Task 3: Picker in name forms, art backdrop on tiles

**Files:**
- Create: `frontend/src/components/CommanderPicker.tsx`, `frontend/src/components/CommanderBackdrop.tsx`
- Modify: `frontend/src/pages/CreateGamePage.tsx`, `frontend/src/pages/HomePage.tsx`, `frontend/src/components/NameSheet.tsx`
- Modify: `frontend/src/components/ScreenBoard.tsx` (`Tile`), `frontend/src/components/GameBoard.tsx` (my card), `frontend/src/components/PlayerTile.tsx`
- Modify: `frontend/src/i18n/fr.ts`, `frontend/src/i18n/en.ts`

**Interfaces:**
- Consumes: Task 2 `Commander`, `searchCommanders`, `getLastCommander`, `api.*` signatures.
- Produces:
  - `CommanderPicker({ value, onChange }: { value: Commander | null; onChange: (c: Commander | null) => void })` — when `value` set: thumbnail + name + "Changer" button (`commander.change`) that clears it; else search input (placeholder `commander.search`) with debounced (300 ms, ≥2 chars) `searchCommanders`, AbortController per query, results as buttons (thumbnail + name) → `onChange`. States: loading spinner, `commander.noResults`, `commander.unavailable`. Label `commander.label` with "(facultatif)" wording in the string.
  - `CommanderBackdrop({ commander }: { commander?: Commander | null })` — renders nothing if null; else absolutely positioned (`inset-0`, behind content) `<img>` with `object-cover`, a dark gradient overlay, and artist credit `commander.artist` ("Illus. {{artist}}") bottom-left in small text; on `onError` hides itself (state). Parents add `relative overflow-hidden` and put content in a `relative` layer above.
  - i18n keys (fr / en): `commander.label` "Ton commandant (facultatif)" / "Your commander (optional)"; `commander.search` "Chercher un commandant…" / "Search a commander…"; `commander.change` "Changer" / "Change"; `commander.noResults` "Aucun commandant trouvé" / "No commander found"; `commander.unavailable` "Recherche indisponible, tu peux jouer sans" / "Search unavailable, you can play without one"; `commander.artist` "Illus. {{artist}}" / "Art {{artist}}"; `errors.invalid_commander` "Commandant invalide." / "Invalid commander.".

- [ ] **Step 1: Picker.** Add `CommanderPicker` under the name input in the three forms, state initialised with `getLastCommander()` (on sheet open for `HomePage` / `NameSheet`, like the name), passed to `api.createGame` / `api.joinGame`.
- [ ] **Step 2: Backdrop.** Add `CommanderBackdrop` to `ScreenBoard` `Tile`, the "me" `Card` in `GameBoard` (wrap content so it sits above), and `PlayerTile`. Eliminated/conceded tiles keep their existing dimming.
- [ ] **Step 3: Verify build** `cd frontend; npm run build` → 0 errors.
- [ ] **Step 4: Verify in browser** with API + Vite running: Playwright script (scratchpad) creates a game picking "Atraxa" via the picker, a second phone joins picking "Edgar Markov", starts; screenshots of a phone and `/ecran` show the art behind each tile, artist credit visible, life readable; a third phone joins a new lobby without commander and its tile has the plain background. Also type "atr" then quickly "edgar" and check the list shows Edgar results.
- [ ] **Step 5: Commit** `feat(web): commander picker and art backdrop on tiles`
