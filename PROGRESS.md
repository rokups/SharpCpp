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

- An abstract record without a director has no managed wrapper factory.
- Secondary-base parameters and returns cannot take or produce a derived wrapper (C#
  single inheritance).
- Open containers of a template parameter (`std::vector<T>` in a generic member) are not
  bound.
- Fields of type `T` in class templates are not bound.
- Capacity/count functions with a record output buffer keep the single record handle
  signature (needed for polymorphic buffers with a larger element stride, such as OpenXR
  `XrSwapchainImageBaseHeader`) and also get a `NativeArray<T>` overload that checks the
  capacity against the array length. The overload is not generated for virtual methods.
- A field of type `T**` (for example a counted array of opaque handles, `XrSpaceUserFB*`)
  is exposed as a single `T` wrapper; only record fields with a matching count get
  per-item accessors, and C modules do not.
- Containers of records that are copy-constructible but not copy-assignable, or whose
  allocator is not default-constructible, are not detected; consumers ignore such records
  (RedBolt ignores Jolt's internal `PhysicsUpdateContext`).
- Projected-value arrays (`Marshaller<T*>` / `Marshaller<const T*>` specialized with
  `ProjectedValueSpanMarshallerBase` / `ProjectedValueConstSpanMarshallerBase` and bound to
  `Span<T>` / `ReadOnlySpan<T>` through `SpanAbiMarshaller<>`) work for parameters of
  direct calls only. A return, field, virtual method or director callback parameter, or
  managed callback parameter or return of that pointer type has no marshaller for the
  position, so the declaration is degraded with the diagnostic "span projection '...' can
  only be used for a parameter of a direct call, not ..." (the UTF-8 `ReadOnlySpan<byte>`
  string views are unaffected). The minimum length comes only from
  `MetadataKeys.MinimumSpanLength` set by the consumer; there is no parser heuristic.
