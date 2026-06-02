# SharpCpp Restart Progress

## Goal

Bootstrap an independent SharpCpp line from the design docs in `doc/`, while selectively copying small, bounded pieces from `../SharpCpp`.

## Current Status

- [x] Git repository initialized
- [x] Agent guide adapted
- [x] Independent repo/build skeleton started
- [x] Model foundation implemented
- [x] Metadata export/import foundation implemented
- [x] Parser skeleton implemented
- [x] Pipeline skeleton implemented
- [x] Rendering skeleton implemented
- [x] Unit test projects wired through CMake/ctest
- [x] First vertical slice: primitive/value parameters + simple handle returns + metadata export
- [x] Generated output contract wired through tool/CMake
- [x] E2E generator and runtime tests wired

## Current Focus

1. Broaden the ClangSharp-backed parser beyond the single E2E sample header.
2. Expand the first slice beyond builtin primitives into more native surface categories.
3. Keep feature scope narrow until the parse -> render -> runtime path is solid.

## Decisions So Far

- Use `SharpCpp.*` managed assembly identities.
- Use `sharpcpp-*` CMake targets.
- Keep structurally independent from `../SharpCpp`; reuse is copy/adapt only.
- Start with the early-grouped project split from `doc/11-project-shape-blueprint.md`: `Model`, `Metadata`, `Parsing`, `Pipeline`, `Rendering`, `Tool`, `Support`.
- The first E2E slice must go through ClangSharp rather than a permanently hardcoded binding model.
- Bootstrap the first exported artifact through metadata before attempting full parser/renderer breadth.

## Relevant Doc Anchors

- `doc/01-architecture.md`
- `doc/06-core-minimal.md`
- `doc/11-project-shape-blueprint.md`
- `doc/12-generated-output-and-module-api.md`
- `doc/13-rewrite-strategy.md`
- `doc/14-diagnostics-and-failure-model.md`

## Reuse Notes

- Candidate early copy: `CodeWriter` from `../SharpCpp/src/SharpCpp.Core/Utilities/CodeWriter.cs`
- Candidate early copy: diagnostic record/collector shape from `../SharpCpp/src/SharpCpp.Core/Diagnostics/`
- Avoid copying parser/rendering logic until the new contracts exist.
- Current generator path parses `tests/SharpCpp.Tests.E2E/Builtin/Builtin.hpp` through ClangSharp and emits the four generated artifacts under the build tree.

## Pending Work

1. Expand the ClangSharp parser from the narrow E2E header to more declarations and type kinds.
2. Introduce phase-2 marshaller metadata extraction contracts in `SharpCpp.Parsing`.
3. Grow the generated managed/native surface beyond the current primitive `int` + simple handle case.
4. Add more focused rendering and parsing tests as the feature surface grows.

## Current Bootstrap Slice

- `SharpCpp.Model` owns the initial mutable binding model, metadata bag, diagnostics, source references, and `CodeWriter`.
- `SharpCpp.Metadata` owns a deterministic metadata document, exporter, and serializer.
- `SharpCpp.Parsing` now contains a minimal ClangSharp-backed header parser for the E2E sample fixture.
- `SharpCpp.Rendering` owns the first generated `.Bindings.g.cs`, `.Bindings.Aliases.g.cs`, and `.Bindings.g.cpp` emitters.
- `SharpCpp.Tool` now drives both metadata-only output and the first generated binding pipeline.
- `tests/SharpCpp.Tests.E2E` now proves the builtin primitive value/in/ref/out surface end-to-end, plus a simple handle return.

## Verification Commands

```bash
cmake --preset dev
cmake --build --preset Debug
ctest --preset Debug
```
