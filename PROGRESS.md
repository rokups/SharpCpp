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
- [ ] RedBolt module generators (core, Glow, Network, PhysicsBox2D, PhysicsJolt,
      RmlUI) still carry private copies of the toolchain discovery; switch them once the
      engine's pinned SharpCpp includes `SharpCpp.Toolchain`.
- [x] Unit tests brought up to date with the fully-qualified managed-callback entry
      point, `char**` fields projected as `IntPtr`, and abstract constructors degraded in
      `DegradeUnsupportedSurfacePass`; the Model suite passes.
- [ ] Coverage gate: 93.49% against the 95% minimum; close the gap with E2E fixtures.

## Later stages

Typed model (`CppType`, typed facets, diagnostic codes, `clang::annotate` attributes),
marshaller catalog and crossing resolver, emitters, type library breadth, declarative
consumer configuration. Template specializations are already identified by their
complete canonical arguments; the rest of type identity is still string-based.
