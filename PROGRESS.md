# Open work

History lives in git. This file lists only what is open.

## Stage 0: freeze the contract

- [x] rbfx consumer split into its own repository (`SharpCpp.Rbfx`); its E2E suite
      builds and passes against the shared-library SDK.
- [x] `EntryPointPrefix` honoured by every renderer entry point.
- [x] Cross-module composition: per-module shared-wrapper helpers, imported aliases,
      marshaller headers and managed generic shapes carried in metadata.
- [x] Hygiene: personal tooling removed; `doc/` archived; absolute paths removed from
      the coverage script.
- [x] E2E fixtures grouped by C++ feature; separately generated modules live under `Modules/`.
- [x] Test-only wiring moved from `src/SharpCpp.Tool` into the
      `tests/SharpCpp.Tests.E2E.Generator` driver; it no longer counts toward coverage.
- [x] `SharpCpp.Tests.Model` / `SharpCpp.Tests.Metadata` deleted; their scenarios are
      E2E fixtures.
- [x] `AssignImportMethodNames` in one place; annotation caches reset per parse (the
      statics themselves go with the Stage 1 `ParseSession`).
- [x] `SharpCpp.Toolchain`: compiler-driven toolchain discovery used by the E2E driver
      and both SharpCpp.Rbfx generators.
- [x] RedBolt module generators (core, Glow, Network, PhysicsBox2D, PhysicsJolt,
      RmlUI, XR) use `SharpCpp.Toolchain` instead of private copies of the toolchain
      discovery.
- [x] Unit tests brought up to date with the fully-qualified managed-callback entry
      point, `char**` fields projected as `IntPtr`, and abstract constructors degraded in
      `DegradeUnsupportedSurfacePass`; the Model suite passes.
- [x] Coverage gate: 95.21% line coverage (`eng/collect_coverage.sh`, minimum 95%).

## Later stages

Typed model (`CppType`, typed facets, diagnostic codes, `clang::annotate` attributes),
marshaller catalog and crossing resolver, emitters, type library breadth, declarative
consumer configuration. Template specializations are already identified by their
complete canonical arguments; the rest of type identity is still string-based.

Known gaps (unbound or degraded today, with a diagnostic where the generator sees them):

- An abstract record without a director gets a hidden concrete wrapper
  (`__{Name}Native`) whose overrides call native code, so native pointers to unbound
  implementations wrap. The factory stays null (returning such a pointer throws) for generic
  records, records whose managed base is not rendered here, records that inherit abstract
  members from a director base, and pure virtuals that are operators, accessor properties,
  generic methods, or take pointer-array, undefaultable or native-default-dispatch parameters.
- Secondary-base pointer/reference parameters take the `I{Base}` view and polymorphic
  secondary-base pointer returns resolve to the most derived wrapper. What remains
  class-typed: by-value parameters, returns of non-polymorphic secondary bases, virtual
  methods, constructors, operators and accessor properties. Secondary bases that are generic,
  shared from another module, native interface wrappers, have no rendered interface members, or
  are reached by a non-plain record or through an ambiguous path also stay class-typed (logged
  as a warning on the base record). Objects constructed from managed code resolve through their
  director class's type id, registered as an alias of the record's wrapper (abstract director
  records have no registration, so their secondary-base returns still get a base wrapper). A
  secondary base that has its own secondary base with an interface fails to compile (CS0108:
  the interface redeclares the inherited base-interface methods; predates the view work).
- Open containers of a template parameter (`std::vector<T>` in a generic member) are not
  bound; such fields are reported instead of leaking an open type.
- Fields of class templates are bound per instantiation only as public, non-static value
  members (no references, pointers, arrays or bitfields). If the template has an explicit
  full specialization, all its fields are left unbound for every instantiation (reported,
  naming the specializations), and primary-template methods still render against the
  specialization.
- Capacity/count functions with a record output buffer keep the single record handle
  signature (needed for polymorphic buffers with a larger element stride, such as OpenXR
  `XrSwapchainImageBaseHeader`) and also get a `NativeArray<T>` overload that checks the
  capacity against the array length. The overload is not generated for virtual methods.
- A counted pointer-array field (matching `Num{X}`/`count{X}`/`{x}Count`) keeps its single
  pointer property and gets a read-only `Get{Field}At(nuint index)` that throws
  `ArgumentOutOfRangeException` past the count, in C and C++ modules: `T*`/`T**` of records,
  `T**` of opaque handles (`XrSpaceUserFB*`) returning the handle wrapper, and
  `const char* const*`/`char**` returning `string?` (UTF-8). Only record-value `T*` arrays get
  `NativeArray<T>` support; there are no setters. A `const char*` item typedef'd to another
  name is not recognized as a string array, and a pointer-to-pointer of other element kinds
  (for example primitives) still has no per-item accessor.
- Containers whose element is not copy-assignable, or whose allocator (a record declaring
  `allocate`) is not default-constructible, are degraded with a diagnostic. The element check
  uses the same coarse heuristic as the non-copyable one (the container has a member taking or
  returning the element by value), so it can over-degrade. The allocator flag is set only on
  the AST template-argument path, not the CX fallback.
- Projected-value arrays (`Marshaller<T*>` / `Marshaller<const T*>` specialized with
  `ProjectedValueSpanMarshallerBase` / `ProjectedValueConstSpanMarshallerBase` and bound to
  `Span<T>` / `ReadOnlySpan<T>` through `SpanAbiMarshaller<>`) work for parameters of
  direct calls only. A return, field, virtual method or director callback parameter, or
  managed callback parameter or return of that pointer type has no marshaller for the
  position, so the declaration is degraded with the diagnostic "span projection '...' can
  only be used for a parameter of a direct call, not ..." (the UTF-8 `ReadOnlySpan<byte>`
  string views are unaffected). The minimum length comes only from
  `MetadataKeys.MinimumSpanLength` set by the consumer; there is no parser heuristic.
