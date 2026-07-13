---
title: <Subsystem> — <one-line summary>
summary: <ONE sentence stating exactly what question this doc answers — this is what an AI agent / search matches on. Be specific, e.g. "How the server credits in-dungeon gold/life-force and caps it." NOT "wallet stuff".>
category: boot | gameplay | content | ops | meta
status: implemented | partial | planned
server: gameserver | distribution | launcher
updated: YYYY-MM-DD
---

<!--
HOW TO USE THIS TEMPLATE — delete this comment block in the real doc.
Read CONVENTIONS.md first. Keep section order; OMIT a section only if it genuinely doesn't apply (don't pad).
The frontmatter above is REQUIRED and machine-read — fill every field (it powers the indexes + AI retrieval).
Diátaxis: each section has ONE purpose — explanation (Purpose), reference (Key code / How it works / Data),
or how-to (How to…). Don't mix them in one blob.
AI-agent rules (why the shape is what it is):
  • Self-contained: a reader may land here with NO surrounding navigation. Open by stating what this covers
    and its prerequisites; forward-link to related docs (never "as shown above / now that you've…").
  • One purpose per section; one canonical home per fact. If a fact would appear in 2+ docs, it gets its OWN
    doc and everyone links to it (e.g. notifications.md). Copying a paragraph is a bug.
  • Specific, stable headings ("Skill unlock — the registry-level gate", not "Details"). Links target them.
  • Descriptive link text: "[notifications.md](...) — the type-14 delivery mechanism", not "[here](...)".
  • REST/JSON shapes live in code-analysis/ — LINK, never restate.
NOTE: the example paths below are written as they'd appear from a doc in docs/<folder>/ (depth 2 → repo root
is ../../). Adjust the ../ depth to wherever your doc actually lives.
-->

# <Subsystem> — <one-line summary>

> **TL;DR:** 2–3 sentences an agent or newcomer can read first — what this is, why it exists, where it sits
> in the system. End with **Prerequisites:** links to any doc you must understand first (or "none").

## Purpose

*(Explanation — 1–3 short paragraphs.)* What this subsystem is for and **why it exists / why it's built this
way**. Design rationale lives here, not buried in the flow. Assume the reader has no prior context from other
pages — state what they need and link it.

## Key code

*(Reference — the map. One row per load-bearing type/file; link each to `file:line`.)*

| Type / file | Role |
|-------------|------|
| [`Thing`](../../MQELServer/path/File.cs#L1) | what it does |

## How it works

*(Reference — the mechanism, step by step, with code refs. The bulk of the doc. Numbered steps for flows.)*

1. …

## REST / wire

*(Single source of truth — LINK, don't restate.)* Name **which** endpoint(s) are involved and any
server-specific framing decision; the JSON shapes live in
[`../../code-analysis/rest-api/...`](../../code-analysis/README.md). If there's no wire surface, say so.

## Data / persistence

*(Reference.)* Where state lives — `AccountState`, SQLite tables, transient scratch. Link
[ops/persistence.md](../ops/persistence.md) rather than re-describing the store.

## How to …

*(How-to — goal-oriented recipes. One sub-heading per task. Omit the whole section if there are none.)*

### How to <do the common task>
1. …

## Design notes & gaps

*(Explanation.)* Decisions worth recording, known limitations, what's stubbed/deferred, and **dead ends**
(things that look right but aren't — mark ⛔). An honest, flagged gap is an asset; a silent one is a trap.

## Related

*(Descriptive links — "name — what it answers". These are how an agent hops to the next fact.)*

- [other doc](.) — one-line why it's related / what it answers
- [code-analysis REST doc](../../code-analysis/rest-api/) — the authoritative wire shape
</content>
