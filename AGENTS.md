# SharpCpp — Agent Guide

## 1. Scope

This repository is the clean restart line described in `doc/`.

- `doc/` is authoritative for architecture and sequencing.
- Reuse from `../SharpCpp` is allowed by copy/adapt/re-own only.

## 2. Working Rules

- Think before editing. State assumptions when they matter.
- Prefer the smallest change that moves the current phase forward.
- Keep layering clean: `Model -> Metadata -> Parsing/Pipeline/Rendering -> Tool`; `Support` stays runtime-side.
- Do not broaden feature surface before the current slice is coherent.

## 3. Current Implementation Phase

This repo is in bootstrap mode.

Current priorities:

1. establish independent project/build identities
2. land model + metadata foundations
3. prove narrow vertical slices before expanding breadth

Follow `PROGRESS.md` for active tasks, next steps, and local decisions.

## 4. Required Verification

Until parser/renderers/E2E exist, completion means:

1. relevant unit tests added or updated
2. `cmake --build --preset Debug`
3. `ctest --preset Debug` when tests are wired through CMake
4. `PROGRESS.md` updated
5. git commit created

When the generator and E2E pipeline land, extend the completion contract to include those targets instead of bypassing them.

## 5. Project Shape

Target layout follows `doc/11-project-shape-blueprint.md`.

- `src/SharpCpp.Model/` - binding model, diagnostics, utilities
- `src/SharpCpp.Metadata/` - metadata schema/import/export
- `src/SharpCpp.Parsing/` - parser + marshaller instantiation
- `src/SharpCpp.Pipeline/` - passes and scheduling
- `src/SharpCpp.Rendering/` - renderers
- `src/SharpCpp.Tool/` - CLI/composition entrypoint
- `src/SharpCpp.Support/` - managed runtime support
- `include/SharpCpp/` - native support headers
- `tests/` - unit/golden/E2E coverage by layer

## 6. Naming Rules

- Managed assemblies use `SharpCpp.*` identities.
- CMake targets use `sharpcpp-*` targets.
- Generated outputs belong under the build tree, not the source tree.

## 7. Reuse Policy

Good copy candidates:

- deterministic utilities such as `CodeWriter`
- diagnostics record shapes
- stable metadata serialization helpers
- small bounded support/runtime helpers

## 8. Communication

- Be direct.
- Surface blockers early.
- Record decisions and unfinished work in `PROGRESS.md` before stopping.
