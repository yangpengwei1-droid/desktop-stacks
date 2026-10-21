# Desktop Stacks development

Read README.md and docs/DESIGN.md for product intent. This public repository is separate from any personal installation.

- Preserve spatial grouping, original names/icons, original paths and user-controlled visibility. Do not replace spatial groups with inferred business categories.
- Never commit desktop snapshots, local paths, original shortcuts, extracted third-party icons, recordings, logs, credentials or real business documents. Use synthetic test fixtures.
- Desktop presentation and startup are opt-in. Never set Hidden on originals during normal operation: preserve Save As and Explorer visibility. DesktopPresentation.cs controls only the desktop view and its own non-topmost child launcher. Restore pauses collection. Legacy attribute restoration is a separate migration command. Never move, rename or delete originals, change ACLs or take ownership.
- Do not start collection, create login startup, run a real restore/hide or launch user files as part of tests. Test.ps1 uses a unique work directory.
- Build.ps1 is the only build entry. Use Windows PowerShell 5.1 and .NET Framework; Chinese PowerShell sources must retain UTF-8 BOM.
- Keep source, docs, bootstrap scripts and restoration logic aligned. Existing registry keys must never be recreated with New-Item -Force.
- The app is an early prototype; do not claim untested Windows versions, mixed-DPI setups or blanket automation compatibility.

- Save As compatibility is a required invariant: indexing must leave original attributes unchanged. Test presentation via desktop view flags plus launcher parent/visibility, not IFolderView item count; the latter still enumerates hidden desktop-view items. Do not use the ineffective desktop FilterView path.
