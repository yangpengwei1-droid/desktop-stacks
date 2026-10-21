# Desktop Stacks development

Read README.md and docs/DESIGN.md for product intent. This public repository is separate from any personal installation.

- Preserve spatial grouping, original names/icons, original paths and user-controlled visibility. Do not replace spatial groups with inferred business categories.
- Never commit desktop snapshots, local paths, original shortcuts, extracted third-party icons, recordings, logs, credentials or real business documents. Use synthetic test fixtures.
- Hiding and startup are opt-in. Persist recovery metadata and a usable navigation link before hiding. Restore pauses automatic collection. Never recursively hide, move, rename or delete original files, change ACLs or take ownership.
- Do not start collection, create login startup, run a real restore/hide or launch user files as part of tests. Test.ps1 uses a unique work directory.
- Build.ps1 is the only build entry. Use Windows PowerShell 5.1 and .NET Framework; Chinese PowerShell sources must retain UTF-8 BOM.
- Keep source, docs, bootstrap scripts and restoration logic aligned. Existing registry keys must never be recreated with New-Item -Force.
- The app is an early prototype; do not claim untested Windows versions, mixed-DPI setups or blanket automation compatibility.
