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
- [x] Placeholder records now use `GetManagedTypeHead` to strip `::` qualifiers from `ManagedName`
- [x] Remaining leak: nested generic argument type names (e.g. `Urho3D::BaseVector3<float>` inside `Ea.Vector<...>`) now properly caught via placeholder-record detection and post-instantiation `HasUnresolvedNamespaceColons` check
- [x] More explicit early diagnostics for unsupported combinations — placeholder record types are now flagged in both degradation and validation passes
- [ ] Hardened fail-fast gates before render/native compile (improved: C++ compile errors dropped from 222 to 160 in rbfx SDK; remaining are pre-existing)

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

## Recently Fixed

- **Incorrect generated bindings from alias/template-name leakage**:
  - `ClangSharpHeaderParser.ResolveQualifiedTypeName` now rewrites top-level template arguments from Clang's canonical spelling while preserving the spelled container surface. This stops native thunk signatures from leaking colliding short names such as `Rml::Vector3` into rbfx APIs that should resolve to fully qualified `Urho3D::*` types.
  - `ClangSharpHeaderParser.ParseTypeToken` now consults alias-origin names when parsing nested generic arguments, so alias-backed value types inside containers resolve to their configured managed types instead of canonical template spellings like `BaseVector3<float>` / `BaseVector2<float>`.
  - `MarshallerMetadataPhase2.ApplyAliasSurfaces` is no longer used to rewrite callable signatures to alias names. This avoids broad alias-surface pollution such as `FileTime`, `ThreadID`, `NetworkId`, and `VariantVector` leaking into generated P/Invoke and public signatures when the underlying concrete managed type is the correct surface.
  - `DegradeUnsupportedSurfacePass` now rejects container surfaces whose nested generic arguments resolve to fatally ignored records (for example `eastl::pair` from excluded namespace roots), while still allowing supported handle containers like `eastl::vector<Urho3D::Object*>`.
  - Handle-returning `LibraryImport` declarations now use the actual managed wrapper type instead of `nint` when the wrapper record is renderable. The renderer emits per-wrapper custom return marshallers (borrowed vs owned) so generated APIs return the public C# type directly without manual `CreateManagedWrapper(...)` code in the calling method.
  - Hand-written `Type` references now use `global::System.Type` in `rbfx/SharpCpp.Rbfx.Bindings/Math/StringHash.cs` and `rbfx/SharpCpp.Rbfx.Tests.E2E/StringBindingTests.cs`, avoiding accidental resolution to generated `Urho3D.Type`.
  - Verification: `dotnet test tests/SharpCpp.Tests.Model/SharpCpp.Tests.Model.csproj`, `dotnet test tests/SharpCpp.Tests.Rendering/SharpCpp.Tests.Rendering.csproj`, `cmake --build --preset Debug`, and `ctest --preset Debug` all pass.

- **C++ namespace separators (`::`) leaking into managed type names**: Fixed at 6 sites in `ClangSharpHeaderParser.cs` where `::` could enter `ManagedName`/`PublicTypeName`:
  - Placeholder record creation (3 sites) — use `GetManagedTypeHead` to strip `::` before `ToPascalCase`
  - `RegisterRecord`/`RegisterTemplateRecord` (2 sites) — same fix for nested type spellings from ClangSharp
  - `MapType` enum fallback (1 site) — same fix for enums not found in `enumsByName`
  - `ParseTypeToken` record/enum lookups (4 sites) — use `StripGenericArguments` before lookups to prevent `NotSupportedException` fallback that created types with raw `::` tokens
  - `ParseConcreteTemplateArgumentsOrOpaque` fallback — NO normalization; unresolvable types keep raw `::` in `PublicTypeName` so that `HasUnresolvedNamespaceColons` catches and degrades them. The `::` → `.` string conversion was reverted as architectural violation (string guesswork instead of AST-based configuration).
- **`HasUnresolvedNamespaceColons`** restored to its original role as a valid degradation gate in `DegradeUnsupportedSurfacePass`: types lacking managed type configuration (e.g., external library types not in `NamespaceMappings`) are properly degraded rather than generating broken C# output.
- **Native field setters pass values by pointer (Issue #2)**: Added `field_t<T>` alias (`T*`) in `Marshaller.hpp`. Updated native setter template in `BindingOutputRenderer.cs` from `arg_t<T>` to `field_t<T>` for the value parameter. Managed P/Invoke declarations use `in` modifier for pointer-compatible ABI.
- **Degradation leak via placeholder records (Issue #1)**: `DegradeUnsupportedSurfacePass` now also checks `MetadataKeys.PlaceholderRecord` on Handle-type records — types referencing placeholder records (external library types with no real definition) are properly degraded instead of producing broken C++ output.
- **Post-instantiation unresolvable type check (Issue #1)**: `ValidateBootstrapSurfacePass` now validates generic instantiation type arguments for `HasUnresolvedNamespaceColons` and placeholder records, catching unresolvable types that slip through pre-instantiation degradation.
- **Managed type format override propagation (Issues #3 & #4)**: Added `BindingType.ManagedTypeFormatOverride` property (exposes `_managedTypeFormatOverride`). `SubstituteGenericType` and `SubstituteFunctionGenericType` now consult this override in their fallback chain, ensuring concrete type names use the marshaller's configured managed type name rather than the raw C++ name.
- **Test results**: 4/5 test suites pass (model, metadata, rendering, E2E). Rbfx E2E has 1 pre-existing failure (`BiasParametersTests` — unrelated to these fixes). Net reduction of 62 C# errors in rbfx SDK build (222→160).

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
