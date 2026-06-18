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
- String support: `const char*`, `std::string_view`, `ea::string_view` / `eastl::string_view` → `ReadOnlySpan<byte>`, `const char**` → `out string`, `std::string` → `StringBase`

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
- `Utf8SpanReturnMarshaller` for null-terminated `const char*` returns
- `Utf8SpanAbiMarshaller` / `Utf8SpanAbiReturnMarshaller` for explicit UTF-8 ptr+len span ABI

### rbfx Consumer Slice
- `rbfx/SharpCpp.Rbfx.Generator` parses `Urho3DAll.h` against SDK CMake flags
- `SharpPtr<T>` / `WeakPtr<T>` projection via marshaller configuration
- `ea::string` / `eastl::string` projected as `Ea.String`
- Cross-module composition: `rbfx/Rbfx.UserCode` imports upstream metadata, extends wrappers

### Infrastructure
- Structured logging (`ILogger` with elapsed timestamps)
- Degradation pass: auto-ignores unsupported constructs with diagnostic reasons
- Record reference return support (`T&` → handle ABI)
- Type alias pipeline (`using` / `typedef` → metadata-backed C# using directives)
- rbfx `FlagSet<TEnum>` lowering to public `[Flags]` enum surfaces

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

- **Silent exclusions now suppress generated `// excluded:` comment stubs**:
  - Added silent-exclusion metadata layered over `IgnoreReason`, preserving ignored-node dependency behavior while allowing renderers to omit selected declarations entirely.
  - Explicit `[[sharpcpp::ignore]]` attributes, deleted native callables, unconfigured external placeholder records, core iterator endpoint functions, core assignment operators, rbfx non-`Urho3D` declarations, rbfx `detail`/`Detail` implementation namespaces, rbfx smart-pointer wrapper declarations, and rbfx flagset-only bitwise operators are now silently excluded.
  - Ordinary non-silent degradation reasons still render diagnostic comment stubs, so unsupported-surface diagnostics remain visible unless a pass explicitly marks the node silent.
  - Grouped rendering still emits stable generated files for silent-only groups, avoiding stale CMake source-list failures while keeping the files free of excluded API comments. Functions whose parent record is silently excluded are now omitted as well, preventing stale member comment stubs under silent records.
  - Bootstrap pipeline pass start/completion lifecycle logs now use trace level; warnings and high-level generator progress remain visible at normal verbosity.
  - Verification: focused model/rendering tests, `cmake --build --preset Debug`, targeted generated-stub checks for rbfx smart-pointer and `PacketType` flagset comments, and `ctest --preset Debug` all pass.

- **rbfx managed namespace projection now uses a shadow AST over the parsed AST**:
  - Added `ManagedBindingProjection` with synthetic namespace nodes and declaration references that do not re-parent original `BindingNode` instances, so original AST traversal remains intact.
  - Added `RbfxManagedNamespaceProjectionPass` as a terminal rbfx shaping step after rbfx type lowering and before render/export. Actual `Urho3D::` declarations from owned rbfx headers such as `Urho3D/<Segment>/**` and grouped relative paths such as `<Segment>/**` now project into `Urho3D.<Segment>`, while configured external wrapper namespaces such as `Ea` remain unchanged even when declared from rbfx headers.
  - Rendering and metadata export resolve record/enum namespaces and fully-qualified type references through the projection when present. Aliases are intentionally excluded from the shadow namespace tree: generated C# aliases remain `global using` aliases because consuming projects compile the alias file directly, while alias metadata keeps its normalized namespace and points at projected target types.
  - rbfx generator now has a small manual handwritten-type suppression list for generated managed types provided by handwritten rbfx sources (currently `Urho3D.Math.Intersection`), avoiding fragile source scanning and keeping rbfx-specific exceptions out of core.
  - Native object registry initialization and rbfx E2E tests now use projected namespaces, so grouped output compiles after records move out of the flat `Urho3D` namespace.
  - Regression coverage verifies original/shadow traversal, projected namespace rendering and alias output, and metadata namespace/type-reference export.
- **String view parameters and returns now use an explicit UTF-8 ptr+len span ABI**:
  - Added core `std::string_view` marshaller configuration and rbfx `ea::string_view` / `eastl::string_view` configuration, all projecting to `global::System.ReadOnlySpan<byte>`.
  - Added `Utf8SpanAbi` plus separate input and return custom marshallers. Input spans are copied to owned unmanaged buffers and freed by the input marshaller; returned views are wrapped without freeing native memory.
  - Parser marshaller extraction now distinguishes `Marshaller<T>` from `ReturnMarshaller<T>` by the actual template declaration token, preventing return marshaller metadata from overwriting parameter marshaller metadata for the same C++ type.
  - Renderer emits `[MarshalUsing]` on configured span parameters, avoids invalid default-value branches for undefaultable ref-struct parameters, and only emits managed `override` when the managed return type remains compatible with the base method.
  - rbfx generator explicitly suppresses `Urho3D::Graph::LoadJSON` in `RbfxUnsupportedApisPass` because the SDK header declares it but the linked `libUrho3D.so` does not define/export the symbol; this keeps the generated native library loadable while documenting the unsupported API reason.
  - E2E coverage verifies `std::string_view` and synthetic `ea::string_view` parameters/returns, including embedded NUL bytes.
  - Verification: focused model/rendering tests, `cmake --build --preset Debug`, and `ctest --preset Debug` all pass.

- **Metadata-driven alias preservation and rbfx `FlagSet<TEnum>` lowering are now green end-to-end**:
  - Alias metadata now uses structural type matching (`BindingTypeKey`) so public aliases are restored from model facts instead of formatted type-name strings, while internal/import/P/Invoke signatures stay concrete and fully qualified.
  - Alias emission now treats `BindingCompilation.TypeAliases` as authoritative and recursively drops aliases whose complete target tree lacks a managed projection, preventing invalid aliases such as unresolved nested EASTL implementation types from leaking into `*.Aliases.g.cs`.
  - rbfx now runs `RbfxFlagSetLoweringPass` after SDK export filtering and before rendering/export. It rewrites `FlagSet<TEnum>` aliases, function types, parameters, and fields to the enum surface, marks the referenced enums as `[Flags]`, suppresses the `FlagSet` record itself, and drops remaining aliases targeting unavailable ignored records.
  - Regression coverage includes alias metadata export/import, hash-map alias parsing, alias pruning for unresolved generic targets, renderer alias emission, and rbfx `FlagSet<TEnum>` lowering.
  - Generated rbfx and user-code aliases include restored public aliases such as `VariantMap`, `StringVariantMap`, `AnimationChannelFlags`, and `RenderOutputFlags`; no generated `FlagSet` type remains.
  - Verification: `dotnet test tests/SharpCpp.Tests.Model/SharpCpp.Tests.Model.csproj`, `dotnet test tests/SharpCpp.Tests.Rendering/SharpCpp.Tests.Rendering.csproj`, `dotnet test tests/SharpCpp.Tests.Metadata/SharpCpp.Tests.Metadata.csproj`, `cmake --build --preset Debug`, and `ctest --preset Debug` all pass.

- **Public alias restoration is now isolated to the public surface, with concrete internal signatures preserved**:
  - `BindingOutputRenderer` now distinguishes public alias presentation from concrete/internal type emission using `BindingType` facts (`PrimitiveKind`, `RecordId`, `GenericArguments`, `ManagedTypeFormatOverride`) instead of aliasing `LibraryImport` signatures directly.
  - Public signatures can collapse exact typedef aliases such as `VariantMap`, `StringVariantMap`, `FileTime`, and `NetworkId`, while internal/import signatures remain concrete and fully qualified.
  - Generated `*.Aliases.g.cs` files now restore alias availability for alias-shaped types used on public record methods as well as free functions/fields, closing the `ThreadID` regression where the public API referenced an alias name without an emitted alias declaration.
  - Direct wrapper-typed `LibraryImport` parameters now use generated per-record `ManagedToUnmanagedIn` custom marshallers that route through `NativeObject.GetHandle(...)`, restoring null-to-zero behavior for nullable wrapper inputs without falling back to raw `nint` public signatures.
  - Verification: `dotnet test tests/SharpCpp.Tests.Rendering/SharpCpp.Tests.Rendering.csproj`, `dotnet test tests/SharpCpp.Tests.Metadata/SharpCpp.Tests.Metadata.csproj`, `dotnet test tests/SharpCpp.Tests.Model/SharpCpp.Tests.Model.csproj`, `cmake --build --preset Debug`, and `ctest --preset Debug` all pass.

- **Alias metadata now round-trips through `SharpCpp.Metadata`, including generic alias targets**:
  - `BindingMetadataDocument` schema version advanced to `3` and now carries `aliases` plus structural `generic_arguments` on `BindingMetadataTypeReference`.
  - `BindingMetadataExporter` now exports alias targets as fully-qualified concrete type references while preserving generic argument structure.
  - `BindingMetadataImportCatalog` now seeds imported aliases back into `BindingCompilation.TypeAliases`, restores imported generic record facts (`ManagedTypeFormat`, generic parameters), and reconstructs alias target types structurally for downstream rendering.
  - This fixes downstream imported alias cases such as `FloatRange` / `UintRange` and keeps user-code alias files coherent after importing upstream rbfx metadata.

- **Desugared EASTL associative-container matching now accepts `unordered_map` / `hash_map` equivalence**:
  - `MarshallerConfigExtractor` now prefers Clang's desugared template argument while preserving the source namespace qualification, so rbfx marshaller metadata authored against EASTL unordered-map surfaces resolves to the actual parsed `eastl::hash_map<...>` container type.
  - `ClangSharpHeaderParser` now desugars typedef targets through AST facts (`CanonicalType.Declaration`) and treats equivalent template heads as the same generic container surface during managed-type-override matching.
  - `DegradeUnsupportedSurfacePass` now treats ignored internal-detail generic arguments as fatal when evaluating container wrapper surfaces, preventing invalid `Ea.UnorderedMap<..., Detail::*...>` wrappers from leaking into generated API.
  - Added `Bootstrap_pipeline_supports_hash_map_alias_return_surface` plus a focused `HashMapAlias` fixture to keep the desugared hash-map projection path covered.
  - Verification: `dotnet test tests/SharpCpp.Tests.Model/SharpCpp.Tests.Model.csproj` passes. The rbfx generator now materializes previously excluded `VariantMap` / `StringVariantMap` surfaces, but the follow-up public-alias restoration work is still incomplete in this snapshot.

- **Incorrect generated bindings from alias/template-name leakage**:
  - `ClangSharpHeaderParser.ResolveQualifiedTypeName` now rewrites top-level template arguments from Clang's canonical spelling while preserving the spelled container surface. This stops native thunk signatures from leaking colliding short names such as `Rml::Vector3` into rbfx APIs that should resolve to fully qualified `Urho3D::*` types.
  - `ClangSharpHeaderParser.ParseTypeToken` now consults alias-origin names when parsing nested generic arguments, so alias-backed value types inside containers resolve to their configured managed types instead of canonical template spellings like `BaseVector3<float>` / `BaseVector2<float>`.
  - `MarshallerMetadataPhase2.ApplyAliasSurfaces` is no longer used to rewrite callable signatures to alias names. This avoids broad alias-surface pollution such as `FileTime`, `ThreadID`, `NetworkId`, and `VariantVector` leaking into generated P/Invoke and public signatures when the underlying concrete managed type is the correct surface.
  - `DegradeUnsupportedSurfacePass` now rejects container surfaces whose nested generic arguments resolve to fatally ignored records (for example `eastl::pair` from excluded namespace roots), while still allowing supported handle containers like `eastl::vector<Urho3D::Object*>`.
  - Handle-returning `LibraryImport` declarations now use the actual managed wrapper type instead of `nint` when the wrapper record is renderable. The renderer emits per-wrapper custom return marshallers (borrowed vs owned) so generated APIs return the public C# type directly without manual `CreateManagedWrapper(...)` code in the calling method.
  - Ordinary handle-like `LibraryImport` parameters now use the wrapper type directly instead of `nint` for non-`ref`/non-`out` inputs. Generated call sites pass the wrapper object directly, while `ref`/`out` handle parameters still use the explicit `nint` bridge path.
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
