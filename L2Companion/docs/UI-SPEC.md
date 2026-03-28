# L2Companion UI-SPEC (Main App + Overlay)

Status: approved-for-implementation
Date: 2026-03-28
Platform: WPF (.NET 8, desktop)

## 1) Product UX Goals

- Make text readable at a glance in long farm sessions.
- Reduce cognitive load: fewer tabs, clearer grouping, less duplicated info.
- Keep combat controls fast: top actions always visible, safe defaults.
- Make support features (Buff/Heal/Party) understandable without trial-and-error.

## 2) Information Architecture

Top-level tabs:

1. Overview
2. Combat
3. Support
4. Party
5. Connections
6. Settings

### Mapping from current UI

- Merge former `Data + World` into `Overview`.
- Move logs and diagnostics fully into `Connections`.
- Remove separate `Dashboard/Diagnostic/Character` concepts as independent tabs.
- Keep bot runtime summary visible in `Combat` header and compact copy in `Overview`.

## 3) Layout Contract

### Shell

- Window min size: 1220x760.
- Header strip fixed at top with:
  - session status,
  - `Start/Stop Proxy`,
  - `Start/Stop Bot`,
  - overlay toggle.
- Main content uses two-column adaptive grid on desktop, single column under 1280px width.

### Overview

- Row 1 cards: Character, Session/Proxy.
- Row 2 cards: Party Snapshot, Target Snapshot.
- Row 3 split tables: Nearby NPC, Nearby Items.
- No raw protocol-centric labels by default; advanced IDs shown as secondary text.

### Combat

- Left pane: fight behavior and transport (`04+2F`, fallback).
- Right pane: attack skill rotation list.
- Bottom strip: kill/post-kill summary and anti-stall counters.

### Support

- Section A: Self-heal rules (priority explanation shown).
- Section B: Party-heal rules (`Group/Target`) table.
- Section C: Buff rules table with scope and anti-spam windows.
- Add inline hint block with priority order:
  - `dead-stop > critical-hold > self-heal > party-heal > buff > combat`.

### Party

- Section A: Coordinator mode (Leader/Follower/Standalone).
- Section B: Assist+Follow settings with simple language.
- Section C: Follow distance/tolerance/repath in one compact row.

### Connections

- Proxy controls + endpoints.
- Live logs (filter + copy + clear).
- Diagnostic counters and parser state.
- Remove unrelated gameplay controls from this tab.

## 4) Widget Contract (Overlay)

- Keep transparent HUD style but increase contrast:
  - text min contrast >= WCAG AA equivalent on dark surface.
- Compact blocks:
  - Character/Vitals
  - Target
  - Nearby
  - Session
- State badge colors:
  - running: green,
  - paused: amber,
  - stopped/error: red.
- Font sizes:
  - title 16-18,
  - body 12-13,
  - monospace telemetry 11-12.

## 5) Visual System

### Typography

- Base font: `Segoe UI Variable` (fallback `Segoe UI`).
- Mono telemetry: `Cascadia Mono`.

### Spacing scale

- 4, 8, 12, 16, 24, 32.
- Card padding default: 12.
- Control vertical rhythm: 8.

### Color palette (new)

- Background root: `#0E131B`
- Background elevated: `#151E29`
- Surface card: `#1B2633`
- Border subtle: `#314358`
- Text primary: `#EAF1F8`
- Text secondary: `#B8C7D8`
- Accent primary (interactive): `#3FA7FF`
- Accent success: `#45C97A`
- Accent warning: `#F0B24A`
- Accent danger: `#E46666`

Rules:

- Accent color is reserved for active controls and key state indicators only.
- Do not use the same accent for all buttons.
- Avoid orange-selected-everything pattern for grids and tabs.

## 6) Component Rules

- Buttons:
  - Primary: blue,
  - Secondary: slate,
  - Dangerous: red,
  - Warning/Stop: amber.
- Tab selected state uses subtle surface shift + top accent indicator, not full saturated fill.
- DataGrid:
  - row hover tint, selected row not blinding,
  - alternating rows with low contrast delta,
  - sticky headers style consistent with cards.
- Inputs:
  - same height and padding across `TextBox`/`ComboBox`.

## 7) Content & Labeling Rules

- Replace protocol wording with player wording:
  - `AutoFight` -> `Auto Combat`
  - `CoordMode` -> `Party Coordination`
  - `InFight` -> `Allowed During Combat`
- Explain rules in plain language directly near controls.
- Keep ID-heavy details in secondary text or tooltips.

## 8) Explicit Removals / Simplifications

- Remove `Probe` buttons/controls from user-facing flow.
- Remove duplicate start/stop control clusters where repeated.
- Remove dead-end panels that do not affect bot behavior.

## 9) Interaction States

- Loading, empty, error states must be explicit in each major panel.
- Disabled states must show reason tooltip when possible.
- Long operations show progress text in footer/status line.

## 10) Acceptance Criteria (UI)

1. User can configure combat/support/party without opening hidden tabs.
2. Important text remains readable in default theme at 100% scaling.
3. Logs are found only in `Connections` and never mixed into combat setup.
4. `Overview` contains merged world/data snapshot and replaces previous fragmentation.
5. Overlay remains readable during active combat on bright and dark backgrounds.
6. No “same-color everywhere” issue for buttons, selected tabs, and selected rows.

## 11) Implementation Notes

- Create centralized theme resources (brushes, spacing, typography) in `App.xaml`.
- Refactor `MainWindow.xaml` into reusable styles and section templates.
- Keep behavior in `MainViewModel`; avoid logic in XAML code-behind.
- Migrate incrementally:
  1) palette + typography,
  2) tab IA restructure,
  3) Support/Party clarity,
  4) overlay polish.
