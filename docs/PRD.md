# PdfDesk — Product Requirements Document

| | |
|---|---|
| **Status** | v1 implemented (2026-10-13); released as v0.1.0, latest v0.2.0 — see §12 and [CHANGELOG](../CHANGELOG.md) |
| **Date** | 2026-10-13 |
| **Platform** | Windows 11 (x64), .NET 10 LTS, WPF |

## 1. Summary

PdfDesk is a small, fast Windows desktop app for everyday PDF chores that today
need a heavy suite (Acrobat) or an online tool that uploads private documents:

1. **View** PDFs.
2. **Reorder, rotate and delete** pages.
3. **Combine** several files from a folder into one document, sort pages, then save.
4. **Fill in forms** by typing basic text onto the page — either flattened into the page or as an
   editable annotation — and into simple form fields when present.

All edits happen on an **in-memory working document**; nothing is written to disk until
the user clicks **Save / Save As**. Source files are never modified implicitly.

## 2. Background

- The stack is proven: .NET 10 + WPF + MVVM Toolkit + PDFsharp, shipped as one self-contained exe.
- A simple approach to previews is to **rebuild a whole temp PDF** and show it in WebView2 (Edge
  viewer). That does not fit PdfDesk:
  - the user needs to **click on a page to place text**, which a WebView2-hosted viewer cannot expose;
  - rebuilding the full PDF on every reorder of a 200-page merged document is wasteful.
- **Decision (proposed):** render pages ourselves with **PDFium** into WPF images, and
  use **PDFsharp** only to build the output file on save (see §7 and Q1).

## 3. Goals

1. Open and read a PDF quickly (first page visible < 1 s for a typical 50-page file).
2. Reorder pages by drag & drop in a thumbnail strip; rotate and delete pages.
3. Combine files from a folder through a dialog: choose files, choose order, preview, save.
4. Type text onto pages to fill in forms (flat/scanned forms; simple AcroForm text fields and checkboxes).
5. Stay **light**: single exe, no install, no admin, no internet, no telemetry.

### Non-goals (v1)

- Editing existing PDF text or images; redaction.
- Annotations beyond typed text (highlights, drawings, comments, stamps).
- Digital signatures (drawn signature image is a P2 candidate).
- OCR, compression/optimization, PDF/A conversion.
- Printing (use "Open in default app" instead — see Q8).
- Combining images (JPG/PNG) into PDFs — next version (Q4).
- Complex form fields: combo/list boxes, radio groups, signature fields, JavaScript/calculated fields.
- Password removal / encryption of output.
- macOS / Linux.

## 4. Users and scenarios

Individual users handling home and office paperwork.

| # | Scenario |
|---|---|
| S1 | Open a PDF from Explorer ("Open with PdfDesk") and read it. |
| S2 | A scanned document has pages out of order: drag them into place, rotate one upside-down page, save. |
| S3 | A folder holds `invoice.pdf`, `annex1.pdf`, `annex2.pdf`: combine them into one file, move the annex cover page to the end, save as `invoice_full.pdf`. |
| S4 | A bank sends a flat (non-fillable) PDF form: type name, date, IBAN into the blank boxes, save a filled copy. |
| S5 | A government PDF has real form fields: tab through the fields, type values, save. |
| S6 | Drag a second PDF onto an open document to append its pages. |

## 5. Functional requirements

Priority: **P0** = v1 must have, **P1** = v1 should have, **P2** = later.

### 5.1 Viewing

| ID | Requirement | Pri |
|---|---|---|
| F1 | Open PDF via File › Open, drag & drop onto the window, and command-line argument (enables "Open with" / file association). | P0 |
| F2 | Main viewer: continuous vertical scroll, pages rendered on demand (virtualized), crisp at current zoom. | P0 |
| F3 | Zoom: fit width, fit page, 50–400 %, Ctrl+wheel, Ctrl +/-. | P0 |
| F4 | Page navigation: current page / total, Page Up/Down, Home/End, go-to-page box. | P0 |
| F5 | Thumbnail strip synced with the viewer (click thumbnail → scroll to page). | P0 |
| F6 | Recent files list (last 10). | P1 |
| F7 | Text search within the document. | P2 |
| F8 | Password-protected PDFs: prompt for password to open. | P1 |

### 5.2 Page operations

| ID | Requirement | Pri |
|---|---|---|
| F9 | Reorder pages by **drag & drop** in the thumbnail strip, including multi-selection (Ctrl/Shift-click). | P0 |
| F10 | Move selected page(s) with buttons / Alt+↑/↓, and "Move to start / end". | P0 |
| F11 | Delete selected page(s) (Del key). | P0 |
| F12 | Rotate selected page(s) 90° left/right. | P0 |
| F13 | Append / insert pages from another PDF (dialog or drag onto the strip at a position). | P1 |
| F14 | **Undo / redo** for all page and text operations (Ctrl+Z / Ctrl+Y). | P1 |
| F15 | Extract selected pages to a new PDF; split every N pages. | P2 |
| F16 | Insert blank page. | P2 |

### 5.3 Combine from folder

| ID | Requirement | Pri |
|---|---|---|
| F17 | "Combine files…" dialog: pick a folder; list its PDFs with name, page count, size, modified date. | P0 |
| F18 | Include/exclude each file with a checkbox (all checked by default). | P0 |
| F19 | Sort the file list by **name (natural sort: `2` before `10`)**, date, or manually (drag / up-down). | P0 |
| F20 | Option: include subfolders. | P1 |
| F21 | Option: also accept images (JPG/PNG) as one page each — next version (Q4). | P2 |
| F22 | "Combine" creates a **new unsaved working document** ("Combined (unsaved)") in the main window, where pages can be reordered page-by-page (§5.2) before saving. | P0 |
| F23 | Files that fail to open (corrupt, password) are flagged in the dialog and skipped, never abort the whole combine. | P0 |

### 5.4 Fill in forms / add text

| ID | Requirement | Pri |
|---|---|---|
| F24 | **Two text tools** (separate toolbar buttons): **"Text"** → *flattened* text, **"Editable text"** → *annotation*. Click anywhere on a page to create a text box; type single- or multi-line text. See Q5. | P0 |
| F25 | Text box: move by dragging, resize, delete; font size (default 11 pt) and color (black/blue). One font family (Helvetica-like sans). Both kinds look the same while editing, with a small badge showing the kind; a context-menu item converts one kind into the other. | P0 |
| F26 | Full Unicode for Latin scripts (á, é, ñ, ü, €, ç…) — font is embedded/subset in the output. | P0 |
| F27 | **Flattened** text is drawn into the page content on save: looks identical in every viewer and cannot be changed by mistake. | P0 |
| F27a | **Editable** text is saved as a standard PDF **FreeText annotation**, with its own appearance stream so it renders the same everywhere, and stays movable/editable in Acrobat, Edge, etc. | P0 |
| F27b | Re-editing FreeText annotations (created by PdfDesk or others) after reopening a saved file in PdfDesk. In v1 they are rendered but read-only. | P2 |
| F28 | **AcroForm fields (low-hanging fruit only):** single-line/multi-line **text fields** and **checkboxes**. Highlight them, allow typing/ticking and Tab between fields; save values to `/V` and set `NeedAppearances`. Anything else is rendered but read-only. | P1 |
| F29 | Quick-insert today's date (format from settings, default `dd/MM/yyyy`). | P2 |
| F30 | Place a signature image (PNG with transparency) on a page. | P2 |

### 5.5 Document state and saving

| ID | Requirement | Pri |
|---|---|---|
| F31 | The working document is **virtual**: a list of page references (source file + page index + rotation + text overlays). No disk writes until save. | P0 |
| F32 | Title bar shows file name and `•` when there are unsaved changes; closing/opening another file with changes asks Save / Discard / Cancel. | P0 |
| F33 | **Save As** writes a copy to a new name/path; the window then works on the new file, and the original stays untouched. Default name for combined docs: `Combined_yyyy-MM-dd_HHmmss.pdf` in the source folder. | P0 |
| F34 | **Save** overwrites the current file: written to a temp file first, then atomically replaced. For a new (combined) document that has never been saved, Save behaves as Save As. See Q6. | P0 |
| F35 | Output preserves page content, links, and document metadata of the first source; bookmarks are dropped in v1. | P0 |
| F36 | After save: "Open folder" action in the status bar. | P1 |
| F37 | Crash recovery: periodically persist the working-document manifest (page refs + overlays) to `%LOCALAPPDATA%\PdfDesk\Session` and offer to restore on next start. | P2 |

### 5.6 Settings

| ID | Requirement | Pri |
|---|---|---|
| F38 | Persist window size/position, last folder, recent files, default text size/color, date format, thumbnail size in `%APPDATA%\PdfDesk\settings.json`. | P0 |
| F39 | Light/dark theme following Windows. | P2 |
| F40 | Settings button "Add PdfDesk to *Open with*" / "Remove": per-user registration under `HKCU\Software\Classes` (ProgID + `OpenWithProgids` for `.pdf`), no admin, no installer. Windows 11 does not let apps set themselves as default, so the user picks PdfDesk in *Open with › Always*. The button repairs the exe path if the exe was moved. | P1 |

## 6. UI

Single main window, WPF. One document per window (see Q3).

```
┌───────────────────────────────────────────────────────────────────────────────┐
│ PdfDesk — invoice_full.pdf •                                                  │
├───────────────────────────────────────────────────────────────────────────────┤
│ [Open][Save][Save As] │ [Combine…][+pages] │ [↶][↷] │ [⟲][⟳][↑][↓][🗑] │ [T Text][T✎ Editable] [11▾][Black▾] │ ⋯ │
├──────────────┬────────────────────────────────────────────────────────────────┤
│ PAGES        │                                                                │
│ ┌────────┐   │        ┌──────────────────────────────────────┐                │
│ │   1    │   │        │                                      │                │
│ └────────┘   │        │   Name:  [Alex …              ]◄text │                │
│ ┌────────┐   │        │                                      │                │
│ │   2    │◄  │        │   Date:  [13/10/2026]                │                │
│ └────────┘   │        │                                      │                │
│ ┌────────┐   │        │        page rendered by PDFium       │                │
│ │   3    │   │        │                                      │                │
│ └────────┘   │        └──────────────────────────────────────┘                │
│  drag to     │                     (continuous scroll)                        │
│  reorder     │                                                                │
├──────────────┴────────────────────────────────────────────────────────────────┤
│ Page [2] of 14   3 sources · 14 pages        status message    − 100% + ⇔ ⛶  │
└───────────────────────────────────────────────────────────────────────────────┘
```

Combine dialog:

```
┌ Combine files ───────────────────────────────────────────────┐
│ Folder: [C:\Users\me\Documents\Invoices          ] [Browse…] │
│ [ ] Include subfolders                                       │
│ Sort: (•) Name  ( ) Date  ( ) Manual                         │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ [x] invoice.pdf          3 p   120 KB   2026-10-01       │ │
│ │ [x] annex1.pdf           1 p    40 KB   2026-10-02       │ │
│ │ [x] annex2.pdf           2 p    88 KB   2026-10-02       │ │
│ │ [!] old.pdf              — password protected, skipped   │ │
│ └──────────────────────────────────────────────────────────┘ │
│ [↑][↓]                         6 pages     [Cancel][Combine] │
└──────────────────────────────────────────────────────────────┘
```

Keyboard: Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+Z/Y, Del, Ctrl+A (pages), T (flattened text tool),
Shift+T (editable text tool), Esc (exit tool).
All icon-only buttons have tooltips and automation names.

## 7. Technical design

### 7.1 Stack

| Concern | Choice | Notes |
|---|---|---|
| Runtime | .NET 10 (LTS) | Long-term support until 2028 |
| UI | WPF + MVVM (`CommunityToolkit.Mvvm`) | Mature, Windows-only, no extra runtime |
| **Rendering** | **PDFium** via `bblanchon.PDFium.Win32` (native `pdfium.dll`, BSD/Apache) + thin own P/Invoke wrapper (~300 lines) | Renders pages + form-field appearances to BGRA → `WriteableBitmap`. Alternatives in Q1. |
| **Writing / editing** | **PDFsharp 6.x** (MIT) | Import pages from sources, set `/Rotate`, draw flattened text with `XGraphics`, set AcroForm values, save. |
| Fonts | PDFsharp font resolver → embedded subset of a Windows font (Arial/Segoe UI) | Required for á/ñ/€ (F26). |
| Tests | xUnit; test PDFs generated in code; render-based checks of text placement | Core only |
| Logging | Own ~60-line daily log in `%LOCALAPPDATA%\PdfDesk\logs` (7 days) | No Serilog needed. |

Both libraries are permissive (no AGPL like iText/MuPDF), so the app can be shared freely.

### 7.2 Solution layout

```
PdfDesk.slnx
├─ Directory.Build.props          shared TFM, nullable, version, warnings-as-errors
├─ Directory.Packages.props       central package versions
├─ src/PdfDesk.Core               document model, PDFium wrapper, PDFsharp export, combine logic
├─ src/PdfDesk.App                WPF views, view models, dialogs, settings
└─ tests/PdfDesk.Core.Tests
```

Central package management with lock files, shared build props, a log file, and the document logic in Core (`WorkingDocument`, `PageOperations`) so the app view models stay
thin: `MainViewModel` (commands and workflow), `PageViewModel`, `OverlayViewModel`, `FieldViewModel`,
`CombineViewModel`.

### 7.3 Document model

```
WorkingDocument
├─ Sources:  [ SourceFile { Id, Path, Bytes, Password?, PdfiumDocument, Geometry[], FormFields[] } ]
├─ State:    DocumentState (immutable record)                     ← what the user edits
│    ├─ Pages:      [ PageItem { Id, SourceId, PageIndex, Rotation, Overlays[] } ]
│    └─ FormValues: { (SourceId, FieldName) → value }
└─ Undo / redo: stacks of DocumentState snapshots (max 200); dirty = State ≠ last saved snapshot

TextOverlay { Id, Kind (Flattened | Annotation), X, Y, Rotation, Text, FontSize, Color }
```

- **Edits are pure functions on the immutable state** (`PageOperations`): reorder/delete/rotate/
  combine/text/form values never rebuild a PDF until save, and undo is just restoring a snapshot.
- **Sources are read once into memory.** Files are never locked, Save can overwrite the opened
  file, and saving always uses the bytes that were opened.
- **Coordinates** (`PageGeometry`): overlays and form fields live in the page's *base frame*: the
  page as PDFium draws it at rotate 0 (crop box with the page's own `/Rotate`), points, top-left
  origin. The user's extra rotation is a WPF `LayoutTransform` on top. Text typed on a rotated page
  gets the opposite `Rotation` so it is upright on screen.
- **Rendering:** PDFium on one dedicated thread (not thread-safe), queued requests that went stale
  while scrolling are skipped. The viewer renders the pages in view ±1 at screen resolution and
  frees bitmaps more than 4 pages away. The thumbnail list is virtualized.

### 7.4 Save pipeline

The spike (M0) showed that PDFsharp's Import mode drops the `/AcroForm` dictionary, so there are
two paths:

1. **In place:** all pages come from one unencrypted source with no duplicates (the common "open,
   edit, save" case). The source is opened in Modify mode; pages are removed and moved
   (`PdfPages.MovePage`). Form fields, bookmarks and metadata survive.
2. **Import:** pages from several sources (or an encrypted one) are imported into a new document.
   Sources with filled-in fields are first filled in a copy in memory, then imported, so the
   values stay visible, but the fields are no longer interactive. The output is not encrypted.

Then, for each page:

- **Flattened** text: `XGraphics.FromPdfPage(page, Append)`. PDFsharp draws in the unrotated media
  box with its origin at user (0, media height), ignoring the media box offset; `PageGeometry`
  maps base frame → user space → graphics frame and rotates the text (verified by render tests for
  all four `/Rotate` values, landscape pages and offset media boxes).
- **Editable** text: a hand-built `/FreeText` annotation (`/Contents`, `/DA`, `/DS`, `/NM`) with an
  `/AP` appearance stream drawn with `XForm` and the same embedded Arial, plus a `/Matrix` for rotated
  text. `XForm.PdfForm` is internal in PDFsharp 6, so the form's reference is taken from a
  scratch page it was drawn on (public API only).
- Form values: `PdfTextField.Text` / `PdfCheckBoxField.Checked`, plus `NeedAppearances`.
- `/Rotate` = page's own rotation + the user's rotation.

The file is written to `<target>.pdfdesk-tmp` and moved over the target (atomic on the same
volume). Output is flate-compressed, but PDFsharp cannot write object streams, so files that used
them (e.g. the IRS W-9: 140 KB) grow when saved in place (~265 KB).

### 7.5 Distribution

- `dotnet publish -c Release -r win-x64` with a conditional single-file block
  → one `PdfDesk.exe` (~65–75 MB incl. `pdfium.dll`, self-extracted). No WebView2 dependency.
- "Open with" integration: per-user HKCU registration from Settings (F40). No installer in v1.
- Release workflow: tag `v*` → test → publish → GitHub release, **plus** a build+test workflow
  on every push/PR and Dependabot.
- Since v0.2.0 the version comes from the git tag (MinVer) and the release notes from the tag's
  section in `CHANGELOG.md`. The exe embeds `THIRD-PARTY-NOTICES.txt` (shown in About), as the
  bundled licenses require.

### 7.6 Dependency trust

NuGet figures as of 2026-10-13:

| Package | Version | Total downloads | Maintainer | Last release | Assessment |
|---|---|---|---|---|---|
| `PDFsharp` | 6.2.4 | ~73 M | empira GmbH ("PDFsharp Team"), project active since 2005 | 2026-01 | Industry standard for .NET PDF writing; MIT. |
| `bblanchon.PDFium.Win32` | 157.0.8100 | ~9.4 M | Benoît Blanchon (NuGet-verified prefix) | 2026-10 (tracks Chromium weekly) | Binary build of **Google's PDFium** (the Chrome PDF engine, fuzzed continuously by OSS-Fuzz). Builds run in public GitHub Actions from Chromium sources. Used underneath PDFtoImage and other wrappers. |
| `CommunityToolkit.Mvvm` | 8.4.2 | ~29 M | Microsoft (.NET Foundation) | 2026-03 | Safe. |
| `Serilog.Sinks.File` (optional) | 7.0.0 | ~1.2 B | Serilog contributors | 2025-04 | Safe. Optional: a ~50-line own logger avoids the dependency. |
| xUnit, Microsoft.NET.Test.Sdk | — | — | .NET Foundation / Microsoft | — | Test-only, not shipped. |

Rejected as less maintained: `Docnet.Core` (last release 2023-09).

**Main supply-chain risk:** `pdfium.dll` is a native binary built by a single maintainer.
Mitigations:
- pin exact versions with Central Package Management + `packages.lock.json` (`RestoreLockedMode` in CI);
- Dependabot PRs reviewed by hand rather than auto-merged, so PDFium security fixes still arrive;
- the app parses untrusted PDFs, so keeping PDFium current matters more than freezing it;
- fallback: build PDFium from source with the same public scripts, or switch to `Windows.Data.Pdf` (§9).

## 8. Non-functional requirements

| ID | Requirement |
|---|---|
| N1 | Cold start < 1.5 s; first page of a 50-page PDF visible < 1 s after open. |
| N2 | Smooth scrolling and drag & drop with 500-page documents (virtualized lists, lazy rendering). |
| N3 | Memory < 400 MB for a 500-page document (bitmap cache capped). |
| N4 | No admin rights, no internet calls, no telemetry; documents never leave the PC. |
| N5 | Never corrupt or silently modify a source file; saves are atomic (temp + replace). |
| N6 | Clear, non-blocking error messages (corrupt file, password, file locked by another app). |
| N7 | High-DPI aware (per-monitor v2); keyboard-operable. |

## 9. Alternatives considered

| Option | Verdict |
|---|---|
| WebView2 (Edge viewer) for preview | Rejected for the main viewer: no click-to-place text, full rebuild per change. Edge can fill AcroForms itself, but its saved output is not controllable from the app. |
| `Windows.Data.Pdf` (built-in WinRT renderer) | Zero extra payload, but needs a Windows-SDK TFM, no form-field rendering control, no password API. Fallback if PDFium causes issues. |
| `PDFtoImage` / `Docnet.Core` (PDFium wrappers) | Viable shortcut; `PDFtoImage` pulls in SkiaSharp (+~10 MB), `Docnet.Core` is little maintained. Own thin wrapper preferred. |
| iText, MuPDF | AGPL — rejected. |
| Syncfusion / Telerik / Apryse PDF viewer controls | Commercial licenses / heavy — rejected. |
| WinUI 3 | Heavier packaging; WPF is enough. |

## 10. Open questions

| # | Question | Answer | Status |
|---|---|---|---|
| Q1 | Rendering engine: own PDFium rendering vs. a WebView2 (Edge) viewer? | **PDFium** (own rendering). Required for click-to-place text and fast reorder. Adds ~5 MB and ~300 lines of interop. | **Decided** |
| Q2 | Form filling scope for v1? | Flat text **P0**. AcroForm **P1, low-hanging fruit only**: text fields and checkboxes (F28). | **Decided** |
| Q3 | One document per window, tabs, or single instance? | **One document per window, multiple windows allowed** (no single-instance mutex), so "Open with" on a second file just opens a new window. Tabs = P2. | **Decided** |
| Q4 | Combine dialog: PDFs only, or images too? | PDFs only in v1; images next version (F21 → P2). | **Decided** |
| Q5 | Typed text: flattened or editable annotation? | **Both**, as two toolbar buttons (F24, F27, F27a). | **Decided** |
| Q6 | Allow "Save" to overwrite the original file? | Yes, with atomic replace. Save As makes a copy with a new name/path (F33–F34). | **Decided** |
| Q7 | Register as Windows PDF handler / "Open with"? | Per-user HKCU registration button, no installer (F40). | **Decided** |
| Q8 | Printing? | Out of scope v1: "Open in default app" button. P2: print via PDFium render → `PrintDialog`. | **Decided** |
| Q9 | App name. | "PdfDesk". Checked 2026-10-17: no product with this exact name found; TMview (EUIPO, OEPM and other offices) shows no PDFDESK mark, only a dead US filing for DESKPDF (Docudesk, 2005). Names containing "Win"/"Windows" were rejected, since Microsoft's trademark guidelines bar its marks in product names; "for Windows" is used as a description instead. Repository renamed to `pdfdesk`. | **Decided** |

## 11. Milestones

| # | Milestone | Content |
|---|---|---|
| M0 | Spike (1–2 days) | PDFium render to `WriteableBitmap`; PDFsharp import/rotate/save of 5 real-world PDFs (scanned, encrypted, AcroForm, large); flattened Unicode text; FreeText annotation with `/AP` checked in Acrobat Reader / Edge / Chrome. Confirms Q1/Q2/Q5. |
| M1 | Core | Document model, render service + cache, save pipeline, combine logic (natural sort), unit tests. |
| M2 | Viewer MVP | Main window, open/drag-drop, virtualized viewer, zoom, thumbnails, Save/Save As, dirty tracking. |
| M3 | Page editing + combine | Drag & drop reorder, multi-select, rotate, delete, undo/redo, Combine dialog. |
| M4 | Text & forms | Both text tools (place/move/resize/edit/convert), flattened + FreeText output, AcroForm text fields/checkboxes (P1). |
| M5 | Polish & ship | Error handling, logging, settings, recent files, "Open with" registration (F40), single-file publish, CI, lock files. |

## 12. Implementation status (2026-10-13)

Milestones M0–M5 are implemented. "Verified" means exercised in the running app (UI automation and
screenshots) and/or covered by unit tests, including render-based checks (36 tests).

| Area | Requirements | Status |
|---|---|---|
| M0 spike | — | Done with generated PDFs, a real scanned document, the public IRS W-9 (AcroForm, object streams) and an AES-256 encrypted file. All checks passed; findings in §7.4. Output checked with PDFium (Chrome's engine) only: **Acrobat Reader and Edge not checked yet** (not installed). |
| Viewing | F1–F6, F8 | Done, verified. A 300-page file is shown in ~0.6 s and uses ~260 MB. Password prompt with retry verified. |
| Search | F7 | Not started (P2). |
| Page operations | F9–F14 | Done, verified: drag & drop including multi-select, Alt+↑/↓, rotate, delete, insert by drop, undo/redo. |
| Extract / split, blank page | F15–F16 | Extract selected pages and split into one file per page done (⋯ menu, open document unchanged); blank page not started (P2). |
| Combine | F17–F20, F22–F23 | Done, verified: natural sort, damaged/password files flagged, opens next to an open document. F21 (images) is P2. |
| Text tools | F24–F27a | Done, verified: both kinds, move by handle, size/colour, convert, rotated pages. "Resize" is the font size (text boxes do not wrap). |
| Re-edit annotations | F27b | Not started (P2). |
| Form fields | F28 | Done, verified on the W-9 (text fields including `/Q` alignment, checkboxes). |
| Insert date, signature | F29–F30 | Insert date done (into the selected text box); signature not started (P2). |
| Saving | F31–F36 | Done, verified: in place over the opened file, Save As copy, combined document, atomic replace, close prompt. |
| Crash recovery | F37 | Not started (P2). |
| Settings, Open with | F38, F40 | Done; Open-with registration verified (registry written and removed). F39 (theme) is P2. |
| Distribution | §7.5 | Single 66 MB `PdfDesk.exe`; document shown in ~1.0 s cold, 0.75 s warm. CI runs on every PR, Dependabot is active, and releases v0.1.0–v0.2.0 were published by the release workflow. |
| Branding | — | App icon and About dialog (version, privacy statement, third-party notices) added in v0.2.0. Exe not code-signed yet (SmartScreen warns on first start). |

Known limitations:
- Thumbnails do not show typed text.
- Saved copies of encrypted PDFs are not encrypted.
- Combined documents lose interactive form fields and bookmarks.
- Files that use object streams grow when saved.
