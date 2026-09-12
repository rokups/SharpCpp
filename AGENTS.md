# SharpCpp agent guide

## Architecture and semantics

Keep layering `Model -> Metadata -> Parsing/Pipeline/Rendering -> Tool`;
`Support` stays runtime-side. Derive binding and type semantics from parsed
AST/model facts, never rendered C++ type-name strings. Dependency-specific
behavior belongs in marshallers/configuration, not generic core special cases.
Generated artifacts belong in the configured build tree.

Use `doc/` for the relevant architecture decision, `PROGRESS.md` for active
work and unfinished decisions, and `codemap.md` when navigating an unfamiliar
subsystem. Read only the material needed for the current task. Determine the
current implementation state from source and tests instead of assuming bootstrap.

## Tools and collaboration

Use available tools suited to the question: text search for exact text,
ast-grep for structural relationships, and symbol/call-hierarchy tools when
available and useful. Do not require a named connector before ordinary edits.
Use independent subagents only when permitted by the session and useful for
bounded work. No fixed roles, line-count thresholds, or obligatory plan/review
sequence. An independent review is appropriate when risk or unresolved
uncertainty warrants it. One owner coordinates sequential build invocations.

## Verification and completion

Continue through implementation, relevant checks, and fixes for regressions
caused by the change. Build affected projects and run tests of changed behavior;
expand to the solution and dependent E2E suites for shared pipeline/native/build
changes. For a RedBolt integration change, follow the enclosing RedBolt E2E
requirements. Documentation-only changes need consistency and link checks.

Use `dotnet build SharpCpp.slnx --configuration Debug` for aggregate validation;
the solution owns generator ordering and invokes native CMake through
`SharpCpp.Native`. Use existing CMake build presets for native-only work after
`cmake --build --list-presets`, rather than guessing binary directories.

Update `PROGRESS.md` when implementation status or durable decisions change.
Coherent local commits after relevant validation remain authorized. Preserve
unrelated changes and do not commit unresolved merges. Remote publication uses
only the applicable enclosing workflow or explicit task authorization. Report
actual blockers without turning optional tools or reviews into approval gates.

## Project shape

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
