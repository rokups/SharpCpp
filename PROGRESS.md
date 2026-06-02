# SharpCpp v2 Restart Progress

## Goal

Bootstrap an independent SharpCpp v2 line from the design docs in `doc/`, while selectively copying small, bounded pieces from `../SharpCpp`.

## Current Status

- [x] Git repository initialized
- [x] Agent guide added and adapted for v2 bootstrap
- [x] Independent repo/build skeleton started
- [x] Model foundation implemented
- [x] Metadata export/import foundation implemented
- [x] Parser skeleton implemented
- [x] Pipeline skeleton implemented
- [x] Rendering skeleton implemented
- [x] Unit test projects wired through CMake/ctest
- [ ] First vertical slice: primitive/value parameters + simple handle returns + metadata export
- [ ] Generated output contract wired through tool/CMake
- [ ] E2E generator and runtime tests wired

## Current Focus

1. Finish the first real vertical slice by replacing sample/tool bootstrap with parsed input.
2. Add the first renderer-owned generated artifacts beside metadata output.
3. Keep feature scope narrow until primitive/value + handle flows are end-to-end.

## Decisions So Far

- Use `SharpCpp.V2.*` managed assembly identities during the rewrite.
- Use `sharpcpp2-*` CMake targets during the rewrite.
- Keep v2 structurally independent from `../SharpCpp`; reuse is copy/adapt only.
- Start with the early-grouped project split from `doc/11-project-shape-blueprint.md`: `Model`, `Metadata`, `Parsing`, `Pipeline`, `Rendering`, `Tool`, `Support`.
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
- Avoid copying parser/rendering logic until the new contracts exist in v2-owned code.
- Current tool bootstrap emits deterministic sample metadata only; it is not yet a real header-driven generator path.

## Pending Work

1. Replace the sample metadata path with a real parsed-input pipeline.
2. Add the first generated `.Bindings.g.cs` and `.Bindings.g.cpp` artifacts.
3. Introduce phase-2 marshaller metadata extraction contracts in `SharpCpp.Parsing`.
4. Keep unit coverage moving downward from future E2E scenarios.

## Current Bootstrap Slice

- `SharpCpp.Model` now owns the initial mutable binding model, metadata bag, diagnostics, source references, and `CodeWriter`.
- `SharpCpp.Metadata` now owns a deterministic metadata document, exporter, and serializer.
- `SharpCpp.Tool` can emit a sample metadata artifact to stdout or a file to validate the export contract shape.

## Verification Commands

```bash
cmake --preset dev
cmake --build --preset Debug
ctest --preset Debug
```
