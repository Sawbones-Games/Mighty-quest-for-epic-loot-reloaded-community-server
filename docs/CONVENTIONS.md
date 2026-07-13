# Documentation conventions

How to write a doc in `docs/`. Keep it strict so the set stays consistent and trustworthy — for **humans
and for the AI agents** that read this repo. Built on two sources: the **[Diátaxis](https://diataxis.fr/)**
framework (the four doc types) and current **LLM-/agent-friendly docs** practice (self-contained, retrievable,
single-source-of-truth). Adapted from the Dead Island sister project.

## Folder layout (split by use)

Feature docs live in a folder named for the **functional area**; navigation/meta docs stay at the `docs/`
root. Every folder has a `README.md` index (an [llms.txt](https://llmstxt.org/)-style list: link — one-line
description) so a reader (or agent) can scan a folder without opening every file.

| Folder | What lives there |
|--------|------------------|
| `docs/` (root) | navigation + meta: [README](README.md) (top index), [STATUS](STATUS.md), [CONVENTIONS](CONVENTIONS.md), [_TEMPLATE](_TEMPLATE.md), [POLISH](POLISH.md) |
| `docs/boot/` | launch & connect — launcher boot, session/token, cert, account load |
| `docs/gameplay/` | the in-session loop — progression overview, notifications, wallet, combat rewards, hero progression, objectives |
| `docs/content/` | game content & authoring — tutorial steps, castles, castle building |
| `docs/ops/` | server operations — persistence, admin dashboard, save-states, verification |

Add a new functional area = a new folder with its own `README.md`. **Wire/protocol** docs are NOT here — they
live in [`../code-analysis/`](../code-analysis/README.md) (the source of truth for the game↔server JSON).

## The rules

1. **Single source of truth — link, never duplicate.** Every fact lives in **one** place; everything else
   links to it. If a fact would appear in 2+ docs, give it its **own doc** and link it from each use site
   (e.g. [gameplay/notifications.md](gameplay/notifications.md) is the only home of the notification
   mechanism). Copying a paragraph is a bug. REST endpoints / JSON shapes are owned by
   [`../code-analysis/`](../code-analysis/README.md) — a server doc says *which* endpoint it implements and
   links there; it never restates the JSON.

2. **Self-contained pages (AI-agent rule).** A reader — or an LLM retrieving one page — may land here with
   **no surrounding navigation**. Open with what the page covers + its prerequisites; **forward-link** to
   related docs. Never write "as shown above" / "now that you've finished X" — the reader may not have seen X.

3. **One Diátaxis purpose per section.** The four types: *tutorial* (learning), *how-to* (a task),
   *reference* (facts/lookup), *explanation* (why). The template separates them — *Purpose* = explanation,
   *Key code* / *How it works* / *Data* = reference, *How to…* = how-to. Don't blend them in one blob;
   reference reads best in isolation.

4. **Required frontmatter.** Every doc starts with the YAML block from [`_TEMPLATE.md`](_TEMPLATE.md)
   (`title, summary, category, status, server, updated`). The **`summary`** is the one sentence an agent
   matches on — make it specific. Follow it with a **TL;DR** blockquote (orientation + prerequisites).

5. **Specific, stable headings + descriptive links.** Headings name the thing ("Skill unlock — the
   registry-level gate", not "Details") so links can target them and survive. Link text says what the target
   answers — `[notifications.md](...) — the type-14 delivery mechanism`, not `[here](...)`.

6. **Consistent terminology.** Name a concept once and use that term everywhere (synonyms fragment search).
   Our name first, gloss the original — "the session token (`hyperquest` calls it `LoginToken`)".

7. **Link to code with `file:line`.** `[`Type.Method`](../../MQELServer/path/File.cs#L123)`; client files as
   `game-data/loose/UI/Js/.../File.js:NN`. Paste only the few load-bearing lines when prose can't stand alone.

8. **Status honesty.** `status:` in frontmatter (implemented | partial | planned). Flag stubs, shortcuts, and
   **dead ends** (⛔ — things that look right but aren't) in *Design notes & gaps*. A documented gap is an
   asset; a silent one is a trap. Mirror big-picture status in [STATUS.md](STATUS.md).

9. **Treat docs like code.** They live next to the source, change in the same commit as the behaviour they
   describe, and carry an `updated:` date. Stale docs are bugs.

## Style

- **Markdown links, not bare paths** (clickable in the IDE + parseable by agents).
- **Tables** for "key code" / field lists; **numbered steps** for flows; **prose** for the "why".
- **Active voice, present tense, concise.** Assume a developer working on the server.
- **Absolute, dated facts** — "as of 2026-06" not "recently". Convert relative dates.

## Adding a subsystem doc

1. Pick the **folder** for its functional area (or add a new folder + its `README.md`).
2. Copy [`_TEMPLATE.md`](_TEMPLATE.md); fill the **frontmatter** + **TL;DR**.
3. Write *Purpose* (why) → *Key code* (the map) → *How it works* (mechanism) → link the REST shape in
   `code-analysis/` → persistence → how-to → gaps. Keep each fact in its one canonical home; link the rest.
4. Add a one-line entry to the **folder `README.md`** index **and** the top [README](README.md#index-by-feature).
5. If it changes the big picture, update [STATUS.md](STATUS.md).

## Sources

- [Diátaxis](https://diataxis.fr/) — the four documentation types.
- [llms.txt](https://llmstxt.org/) — the convention for AI-readable, link-indexed docs.
- General LLM-doc practice: self-contained pages, one purpose per section, descriptive frontmatter, single
  source of truth, consistent terminology.
</content>
