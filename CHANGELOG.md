# Changelog

Notable changes to PdfDesk, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/).

Add each user-facing change under **Unreleased** in the same PR. When releasing, rename
**Unreleased** to the new version and date, then push the tag: the release workflow uses that section
as the release notes and fails if it is missing.

## [Unreleased]

### Added

- App icon.
- **⋯ › About PdfDesk**: version, privacy statement, links, and third-party license notices.

## [0.1.1] - 2026-10-17

### Security

- Updated the bundled `System.Security.Cryptography.Pkcs` library to 10.0.13, fixing
  [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx) (high) and
  [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) (moderate).
  Updating from 0.1.0 is recommended.

## [0.1.0] - 2026-10-13

First release.

### Added

- View PDFs with continuous scrolling, zoom and thumbnails.
- Reorder pages by dragging thumbnails, rotate and delete pages, with undo/redo.
- Combine the PDFs of a folder, and insert pages from other PDFs by dropping files.
- Fill in forms: drawn-in text, editable text annotations, and fillable PDF fields.
- Open password-protected PDFs.
- Optional "Open with" registration in Explorer.

[Unreleased]: https://github.com/Miguel897/custom-pdf-app/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/Miguel897/custom-pdf-app/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Miguel897/custom-pdf-app/releases/tag/v0.1.0
