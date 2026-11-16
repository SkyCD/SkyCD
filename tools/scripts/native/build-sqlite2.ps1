param(
    [Parameter(Mandatory = $true)][string]$ZigExecutable,
    [string[]]$Runtime = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"),
    [string]$OutputRoot = ""
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$native = Join-Path $repo "src/SkyCD.Native.Sqlite2/native"
$archive = Join-Path $native "sqlite-source-2_8_17.zip"
$expected = "E718E71FEC6FF3757A01C46EC505661A76843E5BBCE0327D12497176AD5ADD49"
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw "SQLite 2 source archive hash mismatch." }
$zig = (Get-Command $ZigExecutable -ErrorAction Stop).Source
if ((& $zig version) -ne "0.15.2") { throw "Reproduction requires Zig 0.15.2." }
if (!$OutputRoot) { $OutputRoot = Join-Path $native "runtimes" }
$work = Join-Path ([IO.Path]::GetTempPath()) ("skycd-sqlite2-build-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Expand-Archive -LiteralPath $archive -DestinationPath $work
    # The official generated archive configures 32-bit pointers; these targets are 64-bit.
    [IO.File]::WriteAllText((Join-Path $work "config.h"), "#define SQLITE_PTR_SZ 8`n")
    # Modern MinGW supplies off_t; remove the obsolete private typedef/guard.
    $osHeader = Join-Path $work "os.h"
    $osText = [IO.File]::ReadAllText($osHeader)
    $first = $osText.IndexOf("# if defined(_MSC_VER) || defined(__BORLANDC__)")
    $last = $osText.IndexOf("# define SQLITE_TEMPNAME_SIZE (MAX_PATH+50)", $first)
    if ($first -lt 0 -or $last -lt 0) { throw "Unexpected SQLite 2 Windows header." }
    [IO.File]::WriteAllText($osHeader, $osText.Substring(0, $first) + "# include <sys/types.h>`n" + $osText.Substring($last))
    # Reject ancestor cycles/depth overflow before linking a native b-tree page.
    # Bound the native cell array even when a corrupt cell links to itself.
    $btree = Join-Path $work "btree.c"
    $btreeText = [IO.File]::ReadAllText($btree)
    $anchor = "  if( pPage->pParent ){"
    $guard = @"
  {
    MemPage *ancestor = pParent;
    int levels = 0;
    while( ancestor ){
      if( ancestor==pPage || ++levels>64 ) return SQLITE_CORRUPT;
      ancestor = ancestor->pParent;
    }
  }
"@
    if (!$btreeText.Contains($anchor)) { throw "Unexpected SQLite 2 initPage layout." }
    $position = $btreeText.IndexOf($anchor, $btreeText.IndexOf("static int initPage("))
    $btreeText = $btreeText.Insert($position, $guard + "`n")
    $btreeText = $btreeText.Replace("    assert( pPage->pParent==pParent );", "    if( pPage->pParent!=pParent ) return SQLITE_CORRUPT;")
    $btreeText = $btreeText.Replace("    pPage->apCell[pPage->nCell++] = pCell;", "    if( pPage->nCell>=MX_CELL ) goto page_format_error;`n    pPage->apCell[pPage->nCell++] = pCell;")
    [IO.File]::WriteAllText($btree, $btreeText)
    $sources = @(Get-ChildItem -LiteralPath $work -Filter "*.c" | Where-Object { $_.Name -notin @("shell.c", "tclsqlite.c") } | Sort-Object Name | ForEach-Object FullName)
    [IO.File]::WriteAllText((Join-Path $work "exports.def"), @"
EXPORTS
sqlite_open
sqlite_close
sqlite_compile
sqlite_step
sqlite_finalize
sqlite_freemem
sqlite_set_authorizer
sqlite_progress_handler
sqlite_libversion
"@)
    foreach ($rid in $Runtime) {
        $target = switch ($rid) {
            "win-x64" { "x86_64-windows-gnu" }
            "linux-x64" { "x86_64-linux-gnu.2.17" }
            "osx-x64" { "x86_64-macos.10.13" }
            "osx-arm64" { "aarch64-macos.11.0" }
            default { throw "Unsupported SQLite 2 runtime '$rid'." }
        }
        $filename = if ($rid.StartsWith("win-")) { "skycd_sqlite2.dll" } elseif ($rid.StartsWith("osx-")) { "libskycd_sqlite2.dylib" } else { "libskycd_sqlite2.so" }
        $directory = Join-Path $OutputRoot "$rid/native"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $binaryDirectory = Join-Path $work $rid
        New-Item -ItemType Directory -Path $binaryDirectory | Out-Null
        $arguments = @("cc", "-target", $target, "-std=gnu89", "-O2", "-g0", "-s", "-shared", "-fPIC", "-DTHREADSAFE=1", "-D_FILE_OFFSET_BITS=64", "-DSQLITE_UTF8=1", "-Wno-int-conversion", "-Wno-incompatible-pointer-types", "-Wno-pointer-to-int-cast", "-I", $work) + $sources
        if ($rid.StartsWith("win-")) { $arguments += (Join-Path $work "exports.def") }
        if ($rid.StartsWith("osx-")) { $arguments += "-Wl,-install_name,@rpath/libskycd_sqlite2.dylib" }
        $arguments += @("-o", (Join-Path $binaryDirectory $filename))
        & $zig @arguments
        if ($LASTEXITCODE -ne 0) { throw "SQLite 2 build failed for $rid." }
        Copy-Item -LiteralPath (Join-Path $binaryDirectory $filename) -Destination (Join-Path $directory $filename)
        Get-FileHash -LiteralPath (Join-Path $directory $filename) -Algorithm SHA256
    }
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (!$resolvedWork.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolvedWork -Leaf).StartsWith("skycd-sqlite2-build-")) { throw "Invalid temporary build path." }
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
