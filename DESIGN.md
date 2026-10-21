# MentorPath — Design

Companion to `PRODUCT.md`. Source of truth for tokens: `src/MentorPath.Web/wwwroot/tokens.css`; shell: `shell.css`; components: `components.css`. Decision record: D-46 in `docs/DECISIONS.md`.

## Direction

An operating tool, not a marketing site: calm, dense enough to scan, quiet chrome so the learner's and mentor's content leads. One teal voice on a cool, teal-tinted ground. Status is always a word plus a dot, never colour alone. Light theme only (dark mode is out of MVP scope).

## Tokens

| Role | Token | Value |
|---|---|---|
| Ground | `--mp-ground` | `#eef2f1` |
| Surface | `--mp-surface` | `#fbfdfc` |
| Sunken | `--mp-surface-sunken` | `#e3e9e8` |
| Ink / soft / muted | `--mp-ink`, `--mp-ink-soft`, `--mp-ink-muted` | `#17222b`, `#33414c`, `#566470` |
| Lines | `--mp-line`, `--mp-line-strong` | `#d6dedc`, `#b9c5c3` |
| Primary | `--mp-primary` (+ `-hover`, `-soft`) | `#0b6b66` |
| Rail | `--mp-rail` (+ raised, active, ink) | `#0e2a2d` |
| Status | success `#17683a`, warning `#7a4306`, danger `#a3231b`, info `#1d4f8f`, neutral `#4b5762` (each with a `-soft` tint) | |
| Spacing | `--mp-space-1…8` | 4px base, 8pt rhythm |
| Radius | `--mp-radius-sm / --mp-radius` | 4px / 8px |

Bootstrap variables (`--bs-*`) are mapped onto these tokens, so Bootstrap `.btn`, `.form-*`, `.alert-*`, `.card`, `.progress`, `.nav-pills` follow the system.

## Typography

IBM Plex, self-hosted from `wwwroot/fonts` (OFL). Sans for UI and body, Serif for h1–h3, Mono for code and links. Fluid scale `--mp-text-xs … --mp-text-2xl` (13–34px); body 15px, line height 1.55, prose 1.7 with a 70ch measure (`.mp-prose`, used by `<Markdown>`).

## Layout

- **Shell** (`MainLayout`): role-aware rail (`AppRail`, collapsible on desktop, drawer below 992px), top bar (`TopBar`: mentee mentoring switcher, user menu), `main#main-content` with a skip link. Signed-out pages use a centred auth shell.
- **Page widths** via `<Page Width="wide|reading|narrow">`: wide for tables and split views, reading (52rem) for long content, narrow (40rem) for account forms.
- **Templates**: `PageHeader` (title, subtitle, breadcrumbs, actions) → `Section` (card with header/body) → `DataList`/`DataRow` for lists, `StatTile` strips for numbers, `SplitView` for main + side columns (stacks on mobile), `Tabs` for sibling views.

## Components

`Icon` (inline SVG set), `Badge` (tone + word + dot), `PageState` (skeleton / not found), `EmptyState` (icon, title, optional action), `Markdown` (only place user Markdown is rendered). Forms: every control has a `<label for>`; narrow inputs use `field-narrow` / `field-medium`.

## Accessibility and responsiveness

Visible focus ring (`--mp-focus`), `aria-current="page"` on the active rail link, drawer sets `inert` on the page behind it, closes on Esc/scrim/link, and moves focus. Touch targets ≥ 40px on mobile. Checked at 360, 768 and 1280px with no horizontal overflow. Respects `prefers-reduced-motion`.

## Craft floor (do not reintroduce)

No eyebrow text above headings, coloured side borders, nested cards, hero-metric templates, gradient text, emoji/glyph icons (use `<Icon>`), or hard offset shadows. No inline `style` for layout; add a class to `components.css` instead.
