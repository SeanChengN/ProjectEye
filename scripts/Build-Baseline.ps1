#Requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path $repo ('artifacts/baseline/{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'), $Configuration)
$utf8 = [Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $run | Out-Null
function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12) + "`n", $utf8)
}
function Copy-Source([string]$From, [string]$To) {
    New-Item -ItemType Directory -Path $To -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $From -Force) {
        if ($item.PSIsContainer) {
            if ($item.Name -notin @('bin', 'obj', 'packages', '.vs', '.recycle-bin')) {
                Copy-Source $item.FullName (Join-Path $To $item.Name)
            }
        } else { Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $To $item.Name) }
    }
}
$metadata = [ordered]@{ configuration=$Configuration; startedUtc=[DateTime]::UtcNow.ToString('o'); sourceCommit=(& git -C $repo rev-parse HEAD); runDirectory=$run; success=$false }
try {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe is required (Visual Studio Build Tools).' }
    $msbuild = @(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe') | Select-Object -First 1
    if (!$msbuild -or !(Test-Path -LiteralPath $msbuild)) { throw 'Full Framework MSBuild was not found.' }
    $metadata.msbuild = $msbuild
    $metadata.msbuildVersion = (& $msbuild /version /nologo | Out-String).Trim()
    if ([version]$metadata.msbuildVersion -lt [version]'17.8') { throw 'MSBuild 17.8 or newer is required for -getProperty reference validation.' }
    $metadata.os = [Environment]::OSVersion.VersionString
    $packageName = 'microsoft.netframework.referenceassemblies.net45.1.0.3.nupkg'
    $expectedHash = '23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3'
    $cache = Join-Path $repo 'artifacts/cache/net45-1.0.3'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $package = Join-Path $cache $packageName
    if (!(Test-Path -LiteralPath $package)) {
        $oldPackage = Join-Path $repo "artifacts/phase0-20261116/$packageName"
        if (Test-Path -LiteralPath $oldPackage) { Copy-Item -LiteralPath $oldPackage -Destination $package }
        else { Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net45/1.0.3/$packageName" -OutFile $package }
    }
    $metadata.referencePackageSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    if ($metadata.referencePackageSha256 -ne $expectedHash) { throw 'Reference package SHA256 mismatch; cache retained for investigation.' }
    $refs = Join-Path $run 'reference-assemblies'
    [IO.Compression.ZipFile]::ExtractToDirectory($package, $refs)
    # MSBuild appends .NETFramework/v4.5 to this root itself.
    $refRoot = Join-Path $refs 'build'
    $metadata.referenceRoot = $refRoot
    $source = Join-Path $run 'src'
    Copy-Source (Join-Path $repo 'src') $source
    Copy-Source (Join-Path $repo 'modules') (Join-Path $run 'modules')
    $kit=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
    $sdk=Get-ChildItem (Join-Path $kit 'UnionMetadata') -Directory | Where-Object { $_.Name -match '^10\.0\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName 'Windows.winmd')) } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if(!$sdk){throw 'Windows SDK UnionMetadata required'}
    $windowsMetadata=Join-Path $sdk.FullName 'Windows.winmd'
    $bridge=Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319/System.Runtime.WindowsRuntime.dll'
    $projects = @(Get-ChildItem -LiteralPath $source -Filter '*.csproj' -Recurse)
    if ($projects.Count -ne 4) { throw "Expected four baseline projects, found $($projects.Count)." }
    # These settings apply only to the disposable source copy. Keep weaving enabled.
    foreach ($project in $projects) {
        $weaver = Join-Path $project.DirectoryName 'FodyWeavers.xml'
        if (Test-Path -LiteralPath $weaver) {
            [xml]$xml = [IO.File]::ReadAllText($weaver, $utf8)
            $xml.SelectSingleNode('/Weavers/Costura').SetAttribute('DisableCleanup', 'true')
            $settings = [Xml.XmlWriterSettings]::new()
            $settings.Encoding = $utf8
            $settings.Indent = $true
            $settings.NewLineChars = "`n"
            $writer = [Xml.XmlWriter]::Create($weaver, $settings)
            try { $xml.Save($writer) } finally { $writer.Dispose() }
        }
        $result = @(& $msbuild $project.FullName /t:GetReferenceAssemblyPaths "/p:TargetFrameworkRootPath=$refRoot" /nologo /v:quiet '-getProperty:TargetFrameworkVersion,_TargetFrameworkDirectories')
        $code = $LASTEXITCODE
        [IO.File]::WriteAllText((Join-Path $run ($project.BaseName + '-references.json')), ($result -join "`n") + "`n", $utf8)
        if ($code -ne 0) { throw "Reference resolution failed for $($project.Name)." }
        $resolved = ($result -join "`n") | ConvertFrom-Json
        if ($resolved.Properties.TargetFrameworkVersion -ne 'v4.5' -or $resolved.Properties._TargetFrameworkDirectories.TrimEnd('\') -ne (Join-Path $refRoot '.NETFramework/v4.5')) {
            throw "Unexpected reference resolution for $($project.Name)."
        }
    }
    $solution = Join-Path $source 'Local/ProjectEye/ProjectEye.Local.sln'
    $arguments = @($solution, '/restore', '/t:Build', "/p:Configuration=$Configuration", '/p:Platform=Any CPU', '/p:RestorePackagesConfig=true', "/p:TargetFrameworkRootPath=$refRoot", '/p:FodyGenerateXsd=false', '/nologo', '/v:minimal', '/fl', "/flp:LogFile=$run/build.log;Encoding=UTF-8;Verbosity=normal", "/bl:$run/build.binlog")
    $arguments += @("/p:EvidencePath=$run/media", "/p:WindowsMetadata=$windowsMetadata", "/p:WinRtBridge=$bridge")
    Write-Json (Join-Path $run 'command.json') @{ executable=$msbuild; arguments=$arguments }
    & $msbuild @arguments
    $metadata.exitCode = $LASTEXITCODE
    if ($metadata.exitCode -ne 0) { throw "Baseline build failed (exit $($metadata.exitCode)). Evidence: $run" }
    $output = Join-Path $source "Local/ProjectEye/bin/$Configuration"
    foreach ($file in @('ProjectEye.exe', 'ProjectEyeUp.exe', 'ProjectEyeBug.exe', 'Project1.UI.dll', 'x86/SQLite.Interop.dll', 'x64/SQLite.Interop.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $output $file))) { throw "Missing output $file" }
    }
    $assembly = [Reflection.Assembly]::LoadFile((Join-Path $output 'ProjectEye.exe'))
    $resources = @($assembly.GetManifestResourceNames())
    foreach ($resource in @('costura.project1.ui.dll.compressed', 'costura32.sqlite.interop.dll', 'costura64.sqlite.interop.dll')) {
        if ($resource -notin $resources) { throw "Missing woven resource $resource" }
    }
    Write-Json (Join-Path $run 'embedded-resources.json') $resources
    $manifest = @(Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object {
        [ordered]@{ path=[IO.Path]::GetRelativePath($output,$_.FullName); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    Write-Json (Join-Path $run 'output-manifest.json') $manifest
    $metadata.outputDirectory = $output
    $metadata.success = $true
    Write-Output "BASELINE_OUTPUT=$output"
} catch {
    $metadata.error = $_.Exception.Message
    throw
} finally {
    $metadata.finishedUtc = [DateTime]::UtcNow.ToString('o')
    Write-Json (Join-Path $run 'result.json') $metadata
    Write-Output "BASELINE_EVIDENCE=$run"
}
