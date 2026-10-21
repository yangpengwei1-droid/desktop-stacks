# Desktop Stacks development

Read README.md and docs/DESIGN.md for product intent. This public repository is separate from any personal installation.

- Preserve spatial grouping, original names/icons, original paths and user-controlled visibility. Do not replace spatial groups with inferred business categories.
- Never commit desktop snapshots, local paths, original shortcuts, extracted third-party icons, recordings, logs, credentials or real business documents. Use synthetic test fixtures.
- Desktop presentation and startup are opt-in. Never set Hidden on originals during normal operation: preserve Save As and Explorer visibility. DesktopPresentation.cs controls only the desktop view and a non-topmost top-level launcher owned by the desktop host; do not reparent transparent WPF windows as WS_CHILD. Restore pauses collection. Legacy attribute restoration is a separate migration command. Never move, rename or delete originals, change ACLs or take ownership.
- The launcher supports mouse dragging after the system drag threshold, without opening on drag. Persist its screen position atomically in 桌面入口位置.json, restore it on recreation, constrain it to the working area and recover from corrupt position data. This is local runtime data; never commit it or reposition original shortcuts/files.
- Do not start collection, create login startup, run a real restore/hide or launch user files as part of tests. Test.ps1 uses a unique work directory.
- Build.ps1 is the only build entry. Use Windows PowerShell 5.1 and .NET Framework; Chinese PowerShell sources must retain UTF-8 BOM.
- Keep source, docs, bootstrap scripts and restoration logic aligned. Existing registry keys must never be recreated with New-Item -Force.
- The app is an early prototype; do not claim untested Windows versions, mixed-DPI setups or blanket automation compatibility.

- Save As compatibility is a required invariant: indexing must leave original attributes unchanged. Verify actual launcher rendering and opening through a screenshot or user observation, plus drag/position persistence. Visibility flags alone do not prove that an entry appears on screen. Verify ownership with GetWindow(GW_OWNER), not GetParent. IFolderView item count still enumerates hidden desktop-view items. Do not use the ineffective desktop FilterView path.
