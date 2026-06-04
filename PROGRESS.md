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
- [x] P/Invoke self parameter changed from `nint` to wrapper type (SafeHandle-derived) for instance methods, operators, field accessors — leverages `LibraryImport` source generator's `AddRef`/`Release` to keep `this` alive during native calls
- [x] Polymorphic wrapping foundation landed: per-record instance caches, runtime type metadata (`typeid(*self)` + normalized handle), registry-backed `CreateManagedWrapper`, and E2E coverage for base-typed returns materializing derived wrappers with stable managed identity
- [x] Director-backed managed virtual overrides landed for the current primitive/handle slice, including native director class emission, callback hookup through generated delegates, and E2E coverage for native virtual dispatch re-entering managed overrides
- [x] Multiple-inheritance surface shaping landed for the current slice: the parser/model now carry full base lists, secondary-base public methods project onto the derived wrapper, native thunks adjust complete-object handles with `dynamic_cast`, and E2E coverage proves both projected secondary-base methods and MI virtual overrides
- [x] First template/generic vertical slice landed: minimal phase-2 marshaller metadata extraction, discovered closed generic instantiations from parsed signatures, and E2E/rendering coverage for a template record surfacing as a managed generic wrapper with per-instantiation dispatch
- [x] Test coverage extended against v1 for already implemented features: default-parameter reflection, operator reflection, director fallback/reuse/modifier checks, MI create-helper reuse, and generated documentation verification are now covered in v3 E2E/tests
- [x] Bootstrap cleanup landed to keep future work maintainable: generic rendering moved out of the main renderer file into a dedicated partial, and post-parse phase sequencing moved into a single pipeline entrypoint instead of ad hoc orchestration in `SharpCpp.Tool`
- [x] First protected-member slice landed for director-capable records: parser/model now preserve protected access, managed wrappers emit `protected` constructors/methods/properties, generated native director helpers legally bridge protected method/field access, and E2E/rendering tests cover protected ctor/method/property shape plus non-director omission

## Current Focus

1. Broaden the ClangSharp-backed parser beyond the current builtin/enum/record-method/field/constructor/static-factory/director/protected/MI/generic-record E2E fixture headers.
2. Extend the first protected/virtual slice toward the richer v1 override semantics (base-call delegation, final-slot handling, abstract-base gating, richer access normalization).
3. Extend the first generic slice beyond primitive/enum type arguments into richer type families and more complete metadata extraction.

## Decisions So Far

- Use `SharpCpp.*` managed assembly identities.
- Use `sharpcpp-*` CMake targets.
- Keep structurally independent from `../SharpCpp`; reuse is copy/adapt only.
- Start with the early-grouped project split from `doc/11-project-shape-blueprint.md`: `Model`, `Metadata`, `Parsing`, `Pipeline`, `Rendering`, `Tool`, `Support`.
- The first E2E slice must go through ClangSharp rather than a permanently hardcoded binding model.
- Bootstrap the first exported artifact through metadata before attempting full parser/renderer breadth.
- Attribute-driven customization points should also be writable through the C# AST API so custom generators can mark nodes directly.
- Land runtime identity/cache and base-return polymorphic wrapping before attempting directors or template/generic projection, since both depend on a coherent wrapper materialization path.
- Land fuller template/generic projection incrementally after the new inheritance/director path is stable, since generic records still need richer phase-2 metadata extraction and a deliberate policy for polymorphic-generic combinations.

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

1. Extend generic projection past the current template-record/primitive slice: more complete phase-2 metadata extraction, richer type arguments, and additional generic member shapes.
2. Extend multiple-inheritance shaping from projected secondary-base methods to fuller interface/reference-type policy where it pays off.
3. Broaden validation/degradation around unsupported virtual/director/generic combinations as more type families come online.
4. Add more focused rendering and E2E tests as the feature surface grows.

## Current Bootstrap Slice

- `SharpCpp.Model` owns the initial mutable binding model, metadata bag, diagnostics, source references, and `CodeWriter`.
- `SharpCpp.Metadata` owns a deterministic metadata document, exporter, and serializer.
- `SharpCpp.Parsing` now contains a minimal ClangSharp-backed header parser for multiple E2E fixture headers, including explicit `[[sharpcpp::in|ref|out]]` parameter overrides and `[[sharpcpp::flags]]` enums.
- `SharpCpp.Rendering` owns the first generated `.Bindings.g.cs`, `.Bindings.Aliases.g.cs`, and `.Bindings.g.cpp` emitters.
- `SharpCpp.Tool` now drives both metadata-only output and the first generated binding pipeline.
- `tests/SharpCpp.Tests.E2E` now proves builtin primitive value/in/ref/out, explicit attribute-driven pointer pass kinds, enum/flags emission, and a simple handle return end-to-end.
- The same customization points are available through the binding model API today via `ParameterBinding.Direction`, `ParameterBinding.AbiKind`, `EnumBinding.IsFlags`, and `EnumBinding.Values`.
- Record instance methods now parse from C++ records, emit native self thunks, and surface as instance methods on managed handle wrappers.
- Public record fields now parse from C++ records, emit getter/setter thunks, and surface as managed properties on handle wrappers.
- Public record constructors now parse from C++ records, emit native allocation thunks, and surface as managed constructors on handle wrappers.
- Public static record methods now parse from C++ records, emit static thunks, and surface as `public static` members on managed wrappers.
- Handle-typed parameters on free/static methods now surface as wrapper objects in managed signatures and pass native handles through generated thunks.
- Handle-typed record fields now surface as wrapper-valued managed properties with getter/setter thunking.
- Handle-typed `ref` / `out` parameters now round-trip through wrapper-local ABI temps, so managed signatures stay wrapper-shaped even when native thunks mutate pointer values.
- Enums and flags now compose inside record constructors, static methods, and instance methods, not just as top-level free-function signatures.
- Read-only C++ fields now surface as getter-only managed properties.
- Simple single inheritance now maps to managed class inheritance, so derived wrappers reuse base methods and properties.
- Base-typed parameters and returns now accept derived wrappers and flow through the inherited wrapper hierarchy cleanly.
- Builtin and enum literal default parameters now surface directly in free functions, constructors, static methods, and instance methods.
- Minimal operator projection now works for member unary/binary operators and primitive conversion operators on records.
- Comparison operator pairs (`==`, `!=`, `<`, `>`) now project cleanly for record wrappers.
- Wrappers now track owned-vs-borrowed handle semantics and generated constructors opt into disposal through native delete thunks.
- Non-nullable wrapper parameters now treat runtime `null` as `nint.Zero`, so null-handle flows remain expressible without turning every handle surface into `T?`.
- Top-level free-function overloads now get distinct generated entrypoints and private import helpers, so overloads are safe in the first slice.
- Record static methods, instance methods, and constructors now get the same overload-safe naming treatment.
- Partial comparison/equality operator sets now fall back to named methods when C# requires a missing companion operator.
- Custom generators can now explicitly mark handle-returning functions as owned through the AST, without relying on naming heuristics.
- Record static and instance overloads now get distinct generated entrypoints and private import helpers too.
- C++ namespace-qualified types now correctly round-trip through the parser and renderer: `CppQualifiedName` on `RecordBinding` and `FunctionBinding` stores the full `ns1::ns2::Name` path, native C++ thunks use it for `reinterpret_cast`, `new`, `delete`, and `arg_t<>/ret_t<>` type arguments, while managed C# code uses the unqualified `NativeName`-derived PascalCase name. Entrypoint names remain flat (unqualified) since C linkage cannot encode `::`.
- `Marshaller<T>` now carries `ConvertToAbi(cpp_value)` and `ConvertToCpp(abi_value)` methods, with identity implementations for primitives/pointers/void* and address-of/dereference for references.
- Native thunks now use `MarshallerInvoker` invocation helpers (`invoke_as<FT>`, `invoke<FT>`, `construct`, `destroy`, `get_field`, `set_field`) instead of inline `reinterpret_cast`, `*param`, or pointer-pass-through. Generated code emits `using FT = Ret(Args...);` typedefs for clean template dispatch.
- `IsConstMethod` on `FunctionBinding` detected via `clang_CXXMethod_isConst` from ClangSharp, with `const` propagated to the `FT` member-function-pointer signature.
- `SharpCpp.Support` now contains `NativeObject : SafeHandle` — the base class for all generated wrapper classes. Ported from `../SharpCpp`, it provides GC-safe critical finalization, handle lifecycle management (`OwnsNativeHandle`, `ReleaseNativeHandle`, `DisposeFromNativeRuntimeCore`), `GCHandle`-backed managed keep-alive for shared ownership, virtual method override detection (`HasManagedOverride`), and static handle-extraction helpers (`GetObject`, `RequireObject`, `GetHandle`, `RequireHandle`).
- `INativeInstanceCache` interface was ported alongside `NativeObject` since the base class references it as the return type of its virtual `InstanceCache` property. The interface itself is minimal (3 methods); full instance cache implementations will land in a later slice.
- Wrapper class rendering now inherits from `global::SharpCpp.Support.NativeObject` instead of `global::System.IDisposable` directly. Root wrapper constructors chain to `base(handle, ownsHandle)` and surface `Handle`, `OwnsHandle`, and `IsNull` as forwarding properties to the base class. The previous `Dispose()` / `DisposeCore()` boilerplate is replaced by a `ReleaseNativeHandle` override that conditionally calls the native delete thunk only when `OwnsNativeHandle` is true.
- `SharpCpp.Support` now also contains `DefaultNativeInstanceCache`, `NativeObjectMetadata`, `INativeObjectRegistration<T>`, and `NativeObjectRegistry`, ported/adapted from v1 as the minimal runtime identity layer for polymorphic wrapping.
- `RecordBinding` and `FunctionBinding` now carry the first polymorphism facts needed by the pipeline (`IsAbstract`, `IsPolymorphic`, `IsVirtual`, `IsPureVirtual`). `ClangSharpHeaderParser` populates the native facts, and `SharpCpp.Pipeline.BindingCompilationPolymorphismPass` recomputes transitive record polymorphism after the per-header ASTs are merged.
- Generated wrapper classes now declare per-record `DefaultNativeInstanceCache` fields plus `CreateManagedWrapper(handle, ownsHandle)` factories. All managed handle materialization sites now route through those factories instead of directly constructing wrappers, so borrowed/owned returns, handle fields/properties, and handle `ref`/`out` flows all participate in cache-backed identity.
- Polymorphic records now emit native `sharpcpp_e2e_<record>_type_id` and `sharpcpp_e2e_<record>_metadata` thunks using `typeid(*self).hash_code()` and `dynamic_cast<void*>(self)`, and managed wrappers implement `INativeObjectRegistration<T>` so base-typed returns can resolve to derived wrapper types at runtime.
- `tests/SharpCpp.Tests.E2E/Records/Inheritance.hpp` and `Inheritance.cs` now prove the first polymorphic wrapping slice end-to-end: base-typed static/instance/field/free-function returns materialize `DerivedCounter` wrappers, and repeated wrapping of the same native base pointer returns the same managed object identity from the cache.
- `RecordBinding` now carries full base-edge information (`Bases`) and `IsDirector`, while `FunctionBinding` now carries `NativeDispatchRecordCppTypeName` for projected secondary-base dispatch. `ClangSharpHeaderParser` records all direct bases, marks `[[sharpcpp::director]]` records, and preserves virtual/pure-virtual facts.
- `SharpCpp.Pipeline.BindingCompilationInheritancePass` now projects secondary-base public methods onto the derived wrapper surface before rendering, while `RecordBindingExtensions` computes primary/secondary base chains plus effective virtual slots for director hookup.
- Generated managed wrappers now emit C# `virtual` / `override` for native virtual methods, generate delegate-backed callback pointers for effective virtual slots, and call `ConnectDirector()` after constructing director-capable records so native virtual calls can re-enter managed overrides.
- Generated native bindings now emit per-record C++ director subclasses, `sharpcpp_e2e_<record>_connect_director` entrypoints, director-aware constructor/delete thunks, and adjusted secondary-base method dispatch through `dynamic_cast<secondary*>(instance)` plus `MarshallerInvoker::invoke_instance(...)`.
- `tests/SharpCpp.Tests.E2E/Records/Director.hpp` / `Director.cs` now prove single-inheritance director callbacks, and `tests/SharpCpp.Tests.E2E/Records/MultipleInheritance.hpp` / `MultipleInheritance.cs` now prove projected secondary-base methods plus managed overrides across a multiply inherited record.
- `BindingType` now models generic parameters and generic arguments, `RecordBinding` now tracks generic parameters plus discovered supported instantiations, and `BindingGenericParameter` / `RecordGenericInstantiationBinding` provide the first generic AST facts needed for a narrow template-record slice.
- `ClangSharpHeaderParser` now recognizes `CXCursor_ClassTemplate`, extracts template parameter scopes, maps generic parameter types inside record members, and resolves concrete template specializations used in the parsed API back to their generic record definition with concrete type arguments.
- `SharpCpp.Parsing.MarshallerMetadataPhase2` now performs a minimal post-parse metadata sweep over project/marshaller headers: it extracts `[[sharpcpp::managed_type(public_type = ... , marshaller_type = ...)]]` from `Marshaller<...>` exact/partial specializations, applies managed type format overrides to matching records, and rebinds already-parsed handle references to the extracted managed type family name.
- `SharpCpp.Pipeline.BindingCompilationGenericInstantiationPass` now discovers closed instantiation sets for generic records by walking parsed field/function signatures and collecting concrete `RecordGenericInstantiationBinding` entries per generic record.
- `BindingOutputRenderer` now has a dedicated generic-record path: it emits open generic wrapper classes (e.g. `BoxOf<T>`), routes constructors/static methods/instance methods through per-instantiation dispatch branches, emits closed-instantiation C ABI thunks for generic record members, and keeps the `LibraryImport` declarations outside the generic type in the module-level import holder.
- `tests/SharpCpp.Tests.E2E/Records/Generic.hpp` / `Generic.cs` now prove the first generic slice end-to-end: a C++ `value_box<T>` template projects as managed `BoxOf<T>`, closed instantiations are discovered from free-function signature uses, and unsupported type arguments fail at runtime. `BindingOutputRendererTests` now assert the managed/native generic emission shape too.
- Additional v1-inspired gap coverage is now in place: `tests/SharpCpp.Tests.E2E/Records/Defaults.cs` checks reflected default values and named-argument skipping, `Records/Operator.cs` checks emitted operator member presence via reflection, `Records/Director.cs` checks virtual modifiers plus no-override fallback and create-helper reuse, `Records/MultipleInheritance.cs` checks MI director-instance reuse, and `Doc/Documentation.cs` verifies the generated `.Bindings.g.cs` carries the expected XML documentation for the currently implemented comment surface.
- `src/SharpCpp.Pipeline/BindingCompilationBootstrapPipeline.cs` now owns the bootstrap ordering for phase-2 metadata application plus the generic/inheritance/polymorphism shaping passes, so future work can extend a single pipeline entrypoint instead of duplicating pass order in `Program.cs`.
- `src/SharpCpp.Rendering/BindingOutputRenderer.Generic.cs` now owns the generic-record managed/native emission path. This keeps `BindingOutputRenderer.cs` focused on the non-generic surface and gives future generic expansion a dedicated home instead of further bloating the main renderer file.
- `BindingAccessModifier` is now explicit in the v3 AST and `ClangSharpHeaderParser` preserves `protected` access on record constructors/methods/fields for director-capable records instead of dropping them. Non-director records still prune protected surface at parse time.
- `BindingOutputRenderer` now respects protected/public visibility on managed constructors, instance/static methods, accessor-backed properties, and field-backed properties. For protected native members, generated C++ director subclasses now emit helper methods/field accessors and thunks that enforce the current slice's runtime contract: protected member access requires a director-backed instance.
- `tests/SharpCpp.Tests.E2E/Records/Protected.hpp` / `Protected.cs` now cover the first protected-member path end-to-end: protected ctor/method/property reflection on a director-capable record, managed subclass access to protected methods/fields, protected-director wrapper reuse, and non-director omission of protected surface. `BindingOutputRendererTests` now assert the protected managed/native emission shape too.
- `[[sharpcpp::ignore]]` attribute support: the parser detects `[[sharpcpp::ignore]]` (with optional reason string like `[[sharpcpp::ignore("reason")]]`) on records, enums, free functions, member methods, constructors, and fields using a combination of cursor-extent source text and a pre-scanned annotation index. When detected, `MetadataKeys.IgnoreReason` is set on the binding node. The renderer emits commented-out pseudo-code stubs (e.g. `// class Name; // excluded: reason`) instead of full bindings for any ignored element. Ignored elements are also excluded from C++ native thunk generation.
- `[[sharpcpp::rename("NewName")]]` attribute support: the parser detects `[[sharpcpp::rename(...)]]` on records, enums, free functions, instance/static methods, and fields. For records, sets both `ManagedName` (head) and `ManagedTypeFormat` (full format). The `{Name}` placeholder resolves to the PascalCase native name of the element. Generic placeholders `{0}`/`{1}` are supported for template records. When a rename and a `managed_type(public_type = ...)` marshaller disagree, rename takes precedence with a stderr warning. `tests/SharpCpp.Tests.E2E/Rename/` provides E2E coverage across all supported element types.
- All `[LibraryImport(...)]` attribute lines are now immediately followed by the method signature with no intervening blank line, regardless of whether a `[return: MarshalAs(...)]` attribute is present.
- C++ documentation comments (`///` line comments and `/** */` block comments) are now carried over to the C# public API. The parser extracts `cursor.BriefCommentText` from libclang at each binding node creation site and stores it via `MetadataKeys.DocumentationComment`. The renderer emits `/// <summary>{escaped_text}</summary>` XML doc comments before managed records, enums, free functions, instance/static methods, constructors, operators, and field properties. XML special characters (`<`, `>`, `&`, `"`) are automatically escaped.
- Removed `AbiTypeAliasExpression` regex, `AbiTypeName` property from `MarshallerDescriptor`/`BindingType`, and `AbiType` field from metadata `BindingMetadataTypeReference`. Also dropped dead renderer methods `GetNativeAbiTypeName`, `BuildNativeFunctionSignatureWithSelf`, and dead `HasCustomMarshaller` property. Marshaller config loading (`ManagedTypeMarshallerExpression`, `PublicTypeExpression`, `LoadManagedTypeOverrides`) retained as scaffolding.
- All `[LibraryImport]` P/Invoke declarations are now grouped into a nested `internal static partial class __PInvoke` inside each wrapper class and the free-function static class, instead of being interleaved as `private static partial` members directly on the wrapper. The `__PInvoke_` prefix is dropped from method names; call sites use `__PInvoke.MethodName(...)`. Derived wrappers use `new` to override the base `__PInvoke`.
- Overload disambiguation for P/Invoke method names now uses a simple counter instead of parameter-type suffixes: the first overload keeps the base `ManagedName`, the second gets `ManagedName2`, etc. Counter assignment runs as a post-pass in both the parser and the renderer (`AssignImportMethodNames`), with per-group scoping (per-record for members, per-compilation for free functions). Parameter-type-based `SanitizeManagedImportNamePart` was removed.

## Verification Commands

```bash
cmake --preset dev
cmake --build --preset Debug
ctest --preset Debug
```
