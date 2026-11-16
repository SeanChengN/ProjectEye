#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Live)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path $repo ('artifacts/media-control/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$utf8 = [Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $run | Out-Null
function Write-Json([string]$Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 8) + "`n", $utf8) }
$record = [ordered]@{startedUtc=[DateTime]::UtcNow.ToString('o'); success=$false; targetFramework='v4.5'}
try {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $msbuild = @(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe') | Select-Object -First 1
    if (!$msbuild) { throw 'Full Framework MSBuild is required.' }
    $kit = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
    $metadata = Get-ChildItem -LiteralPath (Join-Path $kit 'UnionMetadata') -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.\d+$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'Windows.winmd')) } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (!$metadata) { throw 'Windows 10 SDK UnionMetadata/Windows.winmd is required.' }
    $windowsMetadata = Join-Path $metadata.FullName 'Windows.winmd'
    $framework = if ([Environment]::Is64BitOperatingSystem) { 'Framework64' } else { 'Framework' }
    $bridge = Join-Path $env:windir "Microsoft.NET/$framework/v4.0.30319/System.Runtime.WindowsRuntime.dll"
    if (!(Test-Path -LiteralPath $bridge)) { throw '.NET Framework WinRT await bridge is required.' }
    $packageName = 'microsoft.netframework.referenceassemblies.net45.1.0.3.nupkg'
    $cache = Join-Path $repo 'artifacts/cache/net45-1.0.3'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $package = Join-Path $cache $packageName
    if (!(Test-Path -LiteralPath $package)) {
        Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net45/1.0.3/$packageName" -OutFile $package
    }
    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    if ($hash -ne '23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3') { throw 'net45 reference package hash mismatch; cache retained.' }
    [IO.Compression.ZipFile]::ExtractToDirectory($package, (Join-Path $run 'references'))
    $source = Join-Path $run 'source'
    New-Item -ItemType Directory -Path $source | Out-Null
    foreach ($relative in @('modules/MediaControl', 'tests/MediaControl')) {
        $target = Join-Path $source $relative
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $repo $relative) -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.Name) }
    }
    $record.msbuild = $msbuild
    $record.msbuildVersion = (& $msbuild /version /nologo | Out-String).Trim()
    $record.windowsSdk = $metadata.Name
    $record.windowsMetadataSha256 = (Get-FileHash -LiteralPath $windowsMetadata -Algorithm SHA256).Hash
    $record.referencePackageSha256 = $hash
    $record.winRtBridge = $bridge
    $record.winRtBridgeVersion = (Get-Item -LiteralPath $bridge).VersionInfo.FileVersion
    $buildArguments = @((Join-Path $source 'tests/MediaControl/MediaControl.Tests.csproj'), '/t:Build', '/nologo', '/v:minimal', "/p:EvidencePath=$run", "/p:TargetFrameworkRootPath=$run/references/build", "/p:WinRtBridge=$bridge", "/p:WindowsMetadata=$windowsMetadata", '/fl', "/flp:LogFile=$run/build.log;Encoding=UTF-8;Verbosity=normal")
    Write-Json (Join-Path $run 'command.json') @{executable=$msbuild; arguments=$buildArguments}
    & $msbuild @buildArguments
    $record.buildExitCode = $LASTEXITCODE
    if ($record.buildExitCode -ne 0) { throw 'Media control module build failed.' }
    $exe = Join-Path $run 'bin/MediaControl.Tests.exe'
    $record.executable = $exe
    $record.sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    & $exe 2>&1 | Tee-Object -FilePath (Join-Path $run 'tests.log')
    $record.testExitCode = $LASTEXITCODE
    if ($record.testExitCode -ne 0) { throw 'Media control tests failed.' }
    if ($Live) {
        & $exe --live 2>&1 | Tee-Object -FilePath (Join-Path $run 'live-enumeration.log')
        $record.liveExitCode = $LASTEXITCODE
        if ($record.liveExitCode -ne 0) { throw 'Read-only live adapter test failed.' }
    }
    $manifest = @(Get-ChildItem -LiteralPath (Join-Path $run 'bin') -File | ForEach-Object {
        @{name=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    Write-Json (Join-Path $run 'output-manifest.json') $manifest
    $record.success = $true
    Write-Output "TEST_EXE=$exe"
} catch { $record.error=$_.Exception.Message; throw }
finally { $record.finishedUtc=[DateTime]::UtcNow.ToString('o'); Write-Json (Join-Path $run 'result.json') $record; Write-Output "MEDIA_EVIDENCE=$run" }
