# Boot — launch & connect

> How the launcher and game get from a cold start to a live, authenticated session against our server.
> Part of the [docs set](../README.md) · conventions: [../CONVENTIONS.md](../CONVENTIONS.md).

- [boot-flow.md](boot-flow.md) — the full client launch→game sequence (config → version → login → packages →
  cert bypass → token handoff → account load) and the handler we return at each gate. The spine; start here.

> Deeper wire/RE for this area lives in [`../../code-analysis/decompiled/signin/`](../../code-analysis/README.md)
> (session establishment, sign-in slot writers, boot-abort analysis).
</content>
