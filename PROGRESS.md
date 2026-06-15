# SharpCpp Restart Progress

## Goal

Bootstrap an independent SharpCpp line from `doc/`, selectively adapting small bounded pieces from `../SharpCpp`.

## What Works Today

All 5 test suites pass (model, metadata, rendering, E2E, rbfx-E2E). The full pipeline — C++ headers → generated C# wrappers + native thunks — runs end-to-end for both synthetic fixtures and the real rbfx SDK.

### Model & Metadata
- Binding model: records, enums, free functions, methods, constructors, fields, operators, templates/generics
- Metadata export/import (deterministic JSON, suppress-duplicate-emission contract)
- Diagnostics, source references, `CodeWriter`

### Parser (ClangSharp-backed)
- Parses records, enums, free functions, member methods/fields/constructors, operators, class/function templates
- Managed ClangSharp API (`Decl` subtypes) — no raw `CXCursor`/`VisitChildren`
- `[[sharpcpp::ignore]]`, `[[sharpcpp::rename]]`, `[[sharpcpp::director]]`, `[[sharpcpp::flags]]`, `[[sharpcpp::instantiate]]`
- Documentation comment extraction (`///`, `/** */`)
- Marshaller config extraction via AST walk (not regex)

### Rendering
- Generated `.Bindings.g.cs`, `.Bindings.Aliases.g.cs`, `.Bindings.g.cpp`
- Fully-qualified C# type names (`global::Ns.Type`)
- Per-group code splitting (one file per header group)
- Unity-build compatible native thunks

### Type System
- Primitives, handles/records, enums, references, pointers
- `Marshaller<T>` / `ReturnMarshaller<T>` for custom type interop
- Managed type overrides (`managed_type`, `managed_base_type`, `managed_base_type_constraints`)
- Container wrappers: `vector`, `list`, `map`, `unordered_map`
- Math primitives as value types: `Vector3`, `Matrix3`, `StringHash`, etc.
- String support: `const char*` → `ReadOnlySpan<byte>`, `const char**` → `out string`, `std::string` → `StringBase`

### Inheritance & Polymorphism
- Single inheritance with managed class hierarchy
- Polymorphic wrapping: runtime type ID, instance caches, `CreateManagedWrapper`
- Director/Managed virtual overrides (native→managed dispatch)
- Multiple inheritance: projected secondary-base methods, managed `I*` interfaces
- Protected member access for director-capable records
- Base-call delegation (`callbase_*` entrypoints)

### Generics / Templates
- Generic records (`BoxOf<T>`) and free functions
- Template member methods inside generic records
- Explicit instantiation metadata (`[[sharpcpp::instantiate]]`)
- Handle-typed generic arguments

### Runtime Support (`SharpCpp.Support`)
- `NativeObject : SafeHandle` with lifecycle management
- `DefaultNativeInstanceCache`, `NativeObjectRegistry`
- `StringBase : NativeObject, IReadOnlyList<char>`
- `Utf8SpanReturnMarshaller` (custom P/Invoke marshaller)

### rbfx Consumer Slice
- `rbfx/SharpCpp.Rbfx.Generator` parses `Urho3DAll.h` against SDK CMake flags
- `SharpPtr<T>` / `WeakPtr<T>` projection via marshaller configuration
- `ea::string` / `eastl::string` projected as `Ea.String`
- Cross-module composition: `rbfx/Rbfx.UserCode` imports upstream metadata, extends wrappers

### Infrastructure
- Structured logging (`ILogger` with elapsed timestamps)
- Degradation pass: auto-ignores unsupported constructs with diagnostic reasons
- Record reference return support (`T&` → handle ABI)
- Type alias pipeline (`using` / `typedef` → C# using directives)

## Remaining Work

### Parser Breadth
- [ ] Additional template shapes beyond current fixture patterns
- [ ] Richer default-argument forms and attribute-placement variants
- [ ] More real-world header patterns (the parser works on rbfx SDK but hasn't been stress-tested broadly)

### Generic Breadth
- [ ] Additional generic member shapes beyond single-parameter method templates
- [ ] Polymorphic-generic combinations (generic records with virtual methods / directors)
- [ ] Richer phase-2 metadata for generic surfaces

### Multiple Inheritance
- [ ] Fuller interface inheritance policy for secondary bases
- [ ] Virtual-base inheritance (currently explicitly unsupported)
- [ ] Edge cases: protected/virtual/secondary-base interactions

### Validation & Degradation
- [ ] More explicit early diagnostics for unsupported combinations
- [ ] Hardened fail-fast gates before render/native compile

### Cross-Module Composition
- [ ] Validate downstream native support requirements for imported surfaces
- [ ] Extend beyond the first `rbfx/Rbfx.UserCode` → `SharpCpp.Rbfx.Bindings` slice

### Type System Growth
- [ ] Additional container families (set, deque, etc.)
- [ ] Return-string lifetime: evaluate `ReadOnlySpan<char>` for `const char*` returns
- [ ] Custom C# marshaller for `IntPtr`→`string` P/Invoke (replacing renderer-side `PtrToStringUTF8`)

## Key Decisions

- `SharpCpp.*` managed assembly identities, `sharpcpp-*` CMake targets
- Independent from `../SharpCpp`; reuse is copy/adapt only
- Project split follows `doc/11-project-shape-blueprint.md`
- ClangSharp-backed parser (not permanently hardcoded model)
- Attribute-driven customization writable through C# AST API
- Landed polymorphic wrapping + directors before template/generic projection
- All type-specific behaviors configured through C++/C# marshallers, never hardcoded in core

## Doc Anchors

- `doc/01-architecture.md` — overall architecture
- `doc/02-marshaller-system.md` — marshaller contract
- `doc/03-two-phase-parsing.md` — two-phase parse
- `doc/06-core-minimal.md` — minimal core surface
- `doc/07-marshaller-contract.md` — marshaller ABI contract
- `doc/08-metadata-contract.md` — metadata schema
- `doc/11-project-shape-blueprint.md` — project layout
- `doc/12-generated-output-and-module-api.md` — generated output contract
- `doc/13-rewrite-strategy.md` — v1→v3 rewrite strategy
- `doc/14-diagnostics-and-failure-model.md` — diagnostics model

## Verification

```bash
cmake --preset dev
cmake --build --preset Debug
ctest --preset Debug
```
