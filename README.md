[![Download SkyCD](https://img.shields.io/sourceforge/dt/skycd.svg)](https://sourceforge.net/projects/skycd/files/latest/download) [![License](https://img.shields.io/github/license/SkyCD/SkyCD.svg)](LICENSE)
# SkyCD
SkyCD is a program for indexing your files in CDs and CDs also. All indexing information is saved in text files, so anyone can edit or view with existing text editor/viewer. You can also send these files to your friends & they will know what CD you have.

License: BSD-2-Clause.

## Development (v3 Default)
New development is done in the v3 stack only.

Primary entrypoints:
- solution: `SkyCD.V3.slnx`
- CI workflow: `.github/workflows/v3-ci.yml`
- architecture docs: `docs/architecture/`

Useful local commands:
```powershell
dotnet restore SkyCD.V3.slnx
dotnet build SkyCD.V3.slnx --configuration Release
dotnet test SkyCD.V3.slnx --configuration Release
dotnet format SkyCD.V3.slnx --verify-no-changes
```

Legacy VB.NET/WinForms code is archived for reference/migration and is non-default for new feature work.

![](https://a.fsdn.com/con/app/proj/skycd/screenshots/94408.jpg)

[![Download SkyCD](https://a.fsdn.com/con/app/sf-download-button)](https://sourceforge.net/projects/skycd/files/latest/download)
