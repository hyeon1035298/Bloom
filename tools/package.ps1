# Builds the release copy of Bloom into dist\app and checks it.
#   framework-dependent win-x64 (needs the .NET 10 Desktop Runtime, already on this PC).
#   -ReadyToRun precompiles the code (faster first start); it needs the crossgen2 pack from NuGet
#   (Microsoft.NETCore.App.Crossgen2.win-x64), which is not in the local cache yet.
#   -Addon <folder>[,<folder>...] puts add-on bundles (folders with addon.json) in dist\app\addons\<id>, for a local
#   install or a test build; Bloom then uses them instead of downloading them. Releases carry none (Settings > Add-ons
#   downloads them from the release addons\catalog.json pins).
#   -Out <folder> builds there instead of dist\app (the release workflow's publish folder); it is emptied first.
param([switch]$ReadyToRun, [string[]]$Addon, [string]$Out)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\Bloom\Bloom.csproj'
$out = if ($Out) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out) } else { Join-Path $root 'dist\app' }

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
$r2r = if ($ReadyToRun) { 'true' } else { 'false' }
& dotnet publish $proj -c Release -r win-x64 --self-contained false "-p:PublishReadyToRun=$r2r" -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
# the elevated Win-key helper (issue #3), NativeAOT beside Bloom.exe: needs the C++ build tools (Visual Studio's
# "Desktop development with C++" or the Build Tools)
& dotnet publish (Join-Path $root 'src\WinKeyHelper\WinKeyHelper.csproj') -c Release -r win-x64 -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "WinKeyHelper publish failed ($LASTEXITCODE)" }

# what the installed copy needs
foreach ($name in 'Bloom.exe', 'Bloom.dll', 'Bloom.runtimeconfig.json', 'WinKeyHelper.exe') {
    if (-not (Test-Path -LiteralPath (Join-Path $out $name))) { throw "missing $name" }
}
# the helper is native (no .NET host reading the user's DOTNET_* variables when it runs elevated) and its file
# version's major number is the protocol Bloom checks
if (Test-Path -LiteralPath (Join-Path $out 'WinKeyHelper.dll')) { throw 'WinKeyHelper is not NativeAOT' }
$proto = [regex]::Match((Get-Content -LiteralPath (Join-Path $root 'src\WinKeyHelper\Shared\WinKeyProtocol.cs') -Raw), 'const string Version = "(\d+)"').Groups[1].Value
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $out 'WinKeyHelper.exe')).FileMajorPart -ne [int]$proto) { throw 'WinKeyHelper.exe file version is not the protocol' }
$rc = Get-Content -LiteralPath (Join-Path $out 'Bloom.runtimeconfig.json') -Raw | ConvertFrom-Json
$gc = $rc.runtimeOptions.configProperties
if ($gc.'System.GC.Concurrent' -ne $false -or $gc.'System.GC.ConserveMemory' -ne 5) { throw 'runtimeconfig lacks the GC settings' }
# no debug leftovers
$pdb = @(Get-ChildItem -LiteralPath $out -Filter *.pdb)
foreach ($f in $pdb) { Remove-Item -LiteralPath $f.FullName }

# each add-on: only the files its addon.json lists, each with the SHA-256 it gives, and a host protocol this Bloom
# speaks. LICENSE and SOURCE.txt are notices addon.json does not hash; they go along when the bundle has them.
foreach ($a in @($Addon | Where-Object { $_ })) {
    $manifest = Get-Content -LiteralPath (Join-Path $a 'addon.json') -Raw | ConvertFrom-Json
    if ($manifest.id -notmatch '^[a-z0-9-]+$') { throw "add-on id '$($manifest.id)' is not lowercase letters, digits and -" }
    $known = [regex]::Match((Get-Content -LiteralPath (Join-Path $root 'src\Bloom\Addons\AddonProtocol.cs') -Raw), 'const int Version = (\d+);').Groups[1].Value
    if ($manifest.host.protocol -ne [int]$known) { throw "add-on host protocol $($manifest.host.protocol), this Bloom speaks $known" }
    $dir = Join-Path $out "addons\$($manifest.id)"
    if (Test-Path -LiteralPath $dir) { throw "add-on '$($manifest.id)' is given twice" }
    New-Item -ItemType Directory -Path $dir | Out-Null
    foreach ($f in $manifest.files.PSObject.Properties) {
        if ($f.Name -notmatch '^[\w.-]+$') { throw "add-on file '$($f.Name)' is not a plain file name" }
        $from = Join-Path $a $f.Name
        if ((Get-FileHash -LiteralPath $from -Algorithm SHA256).Hash -ne $f.Value) { throw "$($f.Name) does not match addon.json" }
        Copy-Item -LiteralPath $from -Destination $dir
    }
    Copy-Item -LiteralPath (Join-Path $a 'addon.json') -Destination $dir
    $notices = @('LICENSE', 'SOURCE.txt' | Where-Object { Test-Path -LiteralPath (Join-Path $a $_) })
    foreach ($n in $notices) { Copy-Item -LiteralPath (Join-Path $a $n) -Destination $dir -Force }
    if (-not (Test-Path -LiteralPath (Join-Path $dir $manifest.host.exe))) { throw 'the host exe is not among the files' }
    $noticeText = if ($notices.Count) { $notices -join ', ' } else { 'none' }
    'addons\{0}: {1} ({2}), {3} files, notices: {4}' -f $manifest.id, $manifest.version, $manifest.license, @($manifest.files.PSObject.Properties).Count, $noticeText
}

$files = @(Get-ChildItem -LiteralPath $out -Recurse -File)
$mb = ($files | Measure-Object Length -Sum).Sum / 1MB
$hash = (Get-FileHash -LiteralPath (Join-Path $out 'Bloom.dll') -Algorithm SHA256).Hash
'{4}: {0} files, {1:0.0} MB, ReadyToRun {2}, Bloom.dll SHA256 {3}' -f $files.Count, $mb, $r2r, $hash, $out
