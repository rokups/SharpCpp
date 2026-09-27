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
- [ ] Reorganise E2E fixtures by C++ feature; retire the coverage-campaign directories.
- [ ] Move test-only wiring out of `src/SharpCpp.Tool/Program.cs` into a test driver.
- [ ] Delete `SharpCpp.Tests.Model` / `SharpCpp.Tests.Metadata` after moving their
      scenarios to E2E.
- [x] `AssignImportMethodNames` in one place; annotation caches reset per parse (the
      statics themselves go with the Stage 1 `ParseSession`).
- [x] `SharpCpp.Toolchain`: compiler-driven toolchain discovery used by the E2E driver
      and both SharpCpp.Rbfx generators.
- [ ] RedBolt module generators (core, Glow, Network, PhysicsBox2D, PhysicsJolt,
      RmlUI) still carry private copies of the toolchain discovery; switch them once the
      engine's pinned SharpCpp includes `SharpCpp.Toolchain`.
- [ ] Unit tests: `ManagedCallbackRenderingTests.Callback_entry_point_is_emitted_on_top_level_binding_class`
      and `ClangSharpHeaderParserFieldTests.Parse_keeps_unsupported_nested_pointer_fields_ignored`
      already failed before this work; `Parse_forced_director_records_preserves_abstract_constructors`
      pins the parser-level rule that moved to `DegradeUnsupportedSurfacePass`.
- [ ] Coverage gate: 93.56% against the 95% minimum; close the gap with E2E fixtures.

## Later stages

Typed model (`CppType`, typed facets, diagnostic codes, `clang::annotate` attributes),
marshaller catalog and crossing resolver, emitters, type library breadth, declarative
consumer configuration. Template specializations are already identified by their
complete canonical arguments; the rest of type identity is still string-based.
