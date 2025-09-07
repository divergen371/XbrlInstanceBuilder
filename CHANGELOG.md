# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]
- CI: Matrix build (Linux/Windows/macOS), test results and coverage artifacts, coverage summary in job summary.
- Editor: .editorconfig added to unify formatting (LF, UTF-8, 4 spaces, etc.).
- Housekeeping: Expanded .gitignore, Dependabot for NuGet and GitHub Actions.
- Tests: Property-based tests with FsCheck/Xunit v3 (more invariants to be added).

## [0.2.0] - 2025-09-07
### Added
- Namespaces: `buildDocumentWithNamespaces` to generate root xmlns and explicitMember QNames with external `PrefixMap`.
- Validation: `Validation.validateDocument` with lightweight checks
  - `MissingSchemaRef`, `MissingNamespacePrefix`, `InvalidExplicitMember`.
- Builder API: `Builder.init/addContext/addUnit/addFact/addFactWithRefs/build` with de-duplication and auto IDs.
- Config/Resolver: `Config.tryLoad` / `tryGetDefaultSchemaRefHref` / `schemaRefUrl`, and `Resolver` helpers.
- C# friendly API: `CSharp` module with simple constructors and `TryBuild`-like outcome.
- Default schemaRef: 2020-11-01 JPPFS entry point auto-insert, and http→https upgrade for EDINET host.

### Changed
- Rounding: `roundWith RoundHalfUp` now supports negative `decimals` via scaling (no `ArgumentOutOfRangeException`).

### Tests
- FsUnit + xUnit v3 + FsCheck.Xunit.v3 setup.
- Properties:
  - Truncate does not overshoot sign.
  - Truncate lies between RoundDown and RoundHalfUp (with sign-aware bound).
  - RoundHalfUp error bound within theory.
  - Builder: de-duplication and order invariance.
  - Namespaces: root xmlns completeness; `buildDocumentWithNamespaces` declares only provided prefixes; `qnameOfWith` fallback.

[Unreleased]: https://example.com/compare/v0.2.0...HEAD
[0.2.0]: https://example.com/releases/tag/v0.2.0
