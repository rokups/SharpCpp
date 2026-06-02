# SharpCpp v2 Restart Progress

## Goal

Bootstrap an independent SharpCpp v2 line from the design docs in `doc/`, while selectively copying small, bounded pieces from `../SharpCpp`.

## Current Status

- [x] Git repository initialized
- [x] Agent guide added and adapted for v2 bootstrap
- [x] Independent repo/build skeleton started
- [ ] Model foundation implemented
- [ ] Metadata export/import foundation implemented
- [ ] Parser skeleton implemented
- [ ] Pipeline skeleton implemented
- [ ] Rendering skeleton implemented
- [ ] First vertical slice: primitive/value parameters + simple handle returns + metadata export
- [ ] Generated output contract wired through tool/CMake
- [ ] E2E generator and runtime tests wired

## Current Focus

1. Stand up the independent v2 project graph and build entrypoints.
2. Land the first compileable model/metadata foundation.
3. Keep the first feature slice narrow and fully verified before broadening.

## Decisions So Far

- Use `SharpCpp.V2.*` managed assembly identities during the rewrite.
- Use `sharpcpp2-*` CMake targets during the rewrite.
- Keep v2 structurally independent from `../SharpCpp`; reuse is copy/adapt only.
- Start with the early-grouped project split from `doc/11-project-shape-blueprint.md`: `Model`, `Metadata`, `Parsing`, `Pipeline`, `Rendering`, `Tool`, `Support`.

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

## Pending Work

1. Add unit-tested `SharpCpp.Model` primitives.
2. Add deterministic metadata document + serializer in `SharpCpp.Metadata`.
3. Wire CMake test target once unit test projects are in place.
4. Start the first real vertical slice through tool-driven metadata generation.

## Verification Commands

```bash
cmake --preset dev
cmake --build --preset Debug
ctest --preset Debug
```
