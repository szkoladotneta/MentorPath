# Product

<!-- impeccable:product-schema 1 -->

> Context captured from `docs/MVP_SPEC.md`, `docs/DECISIONS.md`, the existing UI and the layout-refresh brief. The interview round was not answered live (autonomous session); facts below that are inferences are marked *(inferred)* and should be confirmed by the owner.

## Platform

web

## Stack

Blazor Web App (Interactive Server), .NET 10, Bootstrap 5 (vendored in `wwwroot/lib`, kept), scoped and global CSS. Polish-only UI (D-28). No new NuGet packages or projects without an ADR.

## Users

- **Mentor** — owner of one workspace. Works at a desk, in long sessions: invites mentees, authors topics and exercises (manually or with AI drafts), builds individual roadmaps, reviews submissions and writes feedback. Mostly reads and writes long Markdown and scans queues. *(frequency of use: daily during active mentoring — inferred)*
- **Mentee** — a developer growing architectural decision-making. Works between other jobs, often on a laptop in the evening, sometimes on a phone to check feedback. Reads an exercise scenario, writes a decision with rationale, submits a link or file, reads feedback, reflects. May belong to several mentors' workspaces and switches between them (D-08: navigation state only).
- **PlatformOperator** — no UI in the MVP (CLI only).

## Product Purpose

MentorPath helps developers practise independent architectural decisions through one-to-one mentoring: Scenario → Decision → Rationale → Feedback → Reflection → Progress. It is not a video course and not an automatic code checker. The mentor owns the quality of the path and the feedback; AI only helps the mentor draft content. Success: a full mentoring cycle runs end to end, the mentee always knows the next step, and the mentor always knows what waits for review.

## Positioning

A private, per-mentor workspace where the unit of learning is a *decision with its rationale* and a human review of it — not a lesson, a quiz score or an AI verdict.

## Operating Context

- Multi-tenant modular monolith; each mentor workspace is a hard isolation boundary. A resource that does not exist and one the user cannot see look identical (`NotFound`).
- Content is long-form Markdown (scenarios, rationale, feedback, rubric levels) and must stay comfortable to read.
- Mentee-facing screens must never show mentor-private notes or correct answers before they are allowed.
- Workflows are sequential and stateful (draft → published, pending review → accepted / needs revision), so status must be scannable everywhere.

## Capabilities and Constraints

- MVP (P0) scope plus items in `docs/BACKLOG_NEXT.md`; no gamification, rankings, chat, calendar or marketplace.
- UI text is Polish; code, comments and commit messages are English.
- Every page requires authentication; authorization lives in services, never in the UI.
- Layout and visual refresh is purely presentational: no schema, `Contracts/` or service changes.
- Browser smoke test `tests/e2e/ui-smoke.js` depends on current structural selectors (`.card`, `.list-row`, `main`, text of buttons) and must keep passing.

## Brand Commitments

Name: MentorPath. Tone *(inferred)*: calm, precise, collegial — a senior engineer's notebook, not a gamified learning app. No logo asset exists yet.

## Evidence on Hand

Demo data via `seed-demo` (Mentor A/B, mentees Ola and Piotr). No real testimonials, customers or usage metrics — do not fabricate any.

## Product Principles

1. The next action is always obvious: each screen leads with what the user must do or read next.
2. Decisions and reasoning deserve reading comfort: long Markdown is typeset, not dumped.
3. Status is a first-class signal: state is shown consistently with the same words, colours and shapes on every screen.
4. Isolation is invisible but absolute: UI never hints at resources outside the user's workspace.
5. Quiet by default: a working tool for focused people, not a dashboard of decoration.

## Accessibility & Inclusion

Target WCAG 2.2 AA: visible keyboard focus, AA contrast, 44px touch targets on mobile, reduced-motion respected, no information by colour alone. Polish diacritics must render in every shipped font. Layout verified at 360, 768 and 1280px. *(standard inferred)*
