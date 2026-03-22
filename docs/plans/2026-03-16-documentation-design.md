# Documentation Design — ZeroAlloc.Analyzers

**Date**: 2026-03-16
**Status**: Approved

## Goal

Write extensive documentation for ZeroAlloc.Analyzers targeting both library consumers (NuGet users) and contributors (new analyzer authors). Files live in `docs/`, use Mermaid for diagrams, and real-world examples throughout.

## Audience

- **Consumers**: Developers installing ZeroAlloc.Analyzers via NuGet who need to understand rules, configure severity, and suppress diagnostics.
- **Contributors**: Developers adding new analyzers, code fixes, or tests, who need to understand architecture, patterns, and TFM-awareness.

## Chosen Approach: Category-Grouped (Option B)

Rules are grouped by diagnostic namespace (ZA01xx → `collections.md`) so related rules share context and motivation. Anchor links (`collections.md#za0105`) provide individual rule deep-linking.

## File Structure

```
docs/
  getting-started.md
  configuration.md
  rules/
    collections.md        # ZA0101–ZA0109
    strings.md            # ZA0201–ZA0209
    memory.md             # ZA0301–ZA0302
    logging.md            # ZA0401
    boxing.md             # ZA0501–ZA0504
    linq.md               # ZA0601–ZA0607
    regex.md              # ZA0701
    enums.md              # ZA0801–ZA0803
    sealing.md            # ZA0901
    serialization.md      # ZA1001
    async.md              # ZA1101–ZA1104
    delegates.md          # ZA1401
    value-types.md        # ZA1501–ZA1502
  contributing/
    architecture.md
    adding-a-rule.md
    tfm-awareness.md
```

## Per-Rule Template

Each rule entry in a category file:

1. **Header**: `## ZA0105 — Title` with severity badge line
2. **Why**: 2–4 sentences on the allocation/performance impact
3. **Mermaid diagram** (selective — only where a visual adds value over text)
4. **Before/After**: Minimal code snippets showing the problem and fix
5. **Real-world example**: Larger realistic scenario (cache layer, request handler, game loop, etc.)
6. **Suppression**: pragma + .editorconfig snippet

## Diagrams

Mermaid graphs are used selectively for:
- Rules where allocation flow benefits from visualization (e.g. LINQ pipeline materialization)
- Enumeration and async state machine rules
- Architecture/contributor docs (Roslyn pipeline, analyzer registration flow)

Plain before/after code is preferred over diagrams for simple single-substitution rules.

## Contributing Docs

Three files:
- **architecture.md**: Project layout, Roslyn compilation pipeline, how analyzers register and fire
- **adding-a-rule.md**: End-to-end walkthrough — new analyzer class, descriptor, code fix, tests
- **tfm-awareness.md**: How `CompilerVisibleProperty` flows TFM, `TfmHelper` API, how to gate a rule
