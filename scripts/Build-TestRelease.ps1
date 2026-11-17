#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Plan, [Parameter(Mandatory)][string]$IsccPath)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseProfile = Get-Content (Join-Path $repo 'packaging/test-release.json') -Raw -Encoding utf8 | ConvertFrom-Json
$iscc = (Resolve-Path -LiteralPath $IsccPath).Path
if ((Get-AuthenticodeSignature -LiteralPath $iscc).Status -ne 'Valid') { throw 'Installer compiler signature must be valid.' }
$utf8 = [Text.UTF8Encoding]::new($false)
$inputs = @('src', 'modules', 'scripts', 'packaging') | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $repo $_) -File -Recurse | Where-Object {
        [IO.Path]::GetRelativePath($repo, $_.FullName) -notmatch '(^|[\\/])(bin|obj|packages|\.recycle-bin)([\\/])'
    }
} | Sort-Object FullName | ForEach-Object {
    [ordered]@{path=[IO.Path]::GetRelativePath($repo, $_.FullName); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
}
$inputJson = $inputs | ConvertTo-Json -Depth 5 -Compress
$sha = [Security.Cryptography.SHA256]::Create()
try { $fingerprint = [Convert]::ToHexString($sha.ComputeHash($utf8.GetBytes($inputJson))) } finally { $sha.Dispose() }
$planRecord = [ordered]@{
    version=$releaseProfile.version; channel=$releaseProfile.channel; configuration=$releaseProfile.configuration
    sourceCommit=(& git -C $repo rev-parse HEAD); inputFingerprint=$fingerprint
    iscc=$iscc; isccSha256=(Get-FileHash -LiteralPath $iscc -Algorithm SHA256).Hash
    freeBytes=(Get-PSDrive ([IO.Path]::GetPathRoot($repo).Substring(0,1))).Free
    outputRoot=(Join-Path $repo 'artifacts/test-release'); cacheReuse=$false; publicationAllowed=$false
}
if ($Plan) { $planRecord | ConvertTo-Json; return }
$run = Join-Path $planRecord.outputRoot ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $releaseProfile.version)
New-Item -ItemType Directory -Path $run | Out-Null
function Write-Json($Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12) + "`n", $utf8) }
Write-Json (Join-Path $run 'plan.json') $planRecord
Write-Json (Join-Path $run 'inputs.json') $inputs
$result = [ordered]@{version=$releaseProfile.version; startedUtc=[DateTime]::UtcNow.ToString('o'); success=$false; sourceCommit=$planRecord.sourceCommit; inputFingerprint=$fingerprint}
try {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $buildOutput = @(& (Join-Path $PSScriptRoot 'Build-Baseline.ps1') -Configuration Release 2>&1 | Tee-Object -FilePath (Join-Path $run 'baseline-console.log'))
    $evidenceLine = $buildOutput | Where-Object { "$_" -like 'BASELINE_EVIDENCE=*' } | Select-Object -Last 1
    if (!$evidenceLine) { throw 'Build did not report evidence.' }
    $buildDirectory = "$evidenceLine".Substring('BASELINE_EVIDENCE='.Length)
    $build = Get-Content -LiteralPath (Join-Path $buildDirectory 'result.json') -Raw -Encoding utf8 | ConvertFrom-Json
    if (!$build.success) { throw 'Baseline build failed.' }
    $result.buildDirectory = $buildDirectory
    $result.buildSeconds = $clock.Elapsed.TotalSeconds
    $payload = Join-Path $run ('ProjectEye-Enhanced-' + $releaseProfile.version)
    New-Item -ItemType Directory -Path $payload | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $build.outputDirectory -File -Recurse) {
        if ($file.Name -eq 'ProjectEyeUp.exe' -or $file.Extension -notin @('.exe','.dll','.config')) { continue }
        $relative = [IO.Path]::GetRelativePath($build.outputDirectory, $file.FullName)
        if ($relative -match '(^|[\\/])(Data|Log|Update)([\\/])') { throw 'Runtime data must not be packaged.' }
        $target = Join-Path $payload $relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    foreach ($file in @($releaseProfile.requiredFiles | Where-Object { $_ -ne 'ProjectEye.Maintenance.exe' })) { if (!(Test-Path -LiteralPath (Join-Path $payload $file))) { throw "Missing payload: $file" } }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'ProjectEye.exe')).ProductVersion
    if ($version -ne $releaseProfile.version) { throw "Wrong product version: $version" }
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $repo 'docs/TEST-VERSION.md') -Destination (Join-Path $payload 'README.md')
    Copy-Item -LiteralPath (Join-Path $repo 'CHANGELOG.md') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $repo 'packaging/THIRD-PARTY-NOTICES.md') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $repo 'packaging/licenses') -Destination (Join-Path $payload 'licenses') -Recurse
    Write-Json (Join-Path $payload 'test-package.json') @{product='ProjectEye Enhanced'; version=$releaseProfile.version; sourceCommit=$planRecord.sourceCommit; inputFingerprint=$fingerprint; signed=$false}
    $trusted = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
        [ordered]@{ path=[IO.Path]::GetRelativePath($payload,$_.FullName); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $trustedJson = $trusted | ConvertTo-Json -Compress -Depth 8
    $legacy = @($releaseProfile.legacyManifestHashes)
    $maintenance = Join-Path $run 'maintenance-source'
    New-Item -ItemType Directory -Path $maintenance | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repo 'packaging/Maintenance') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $maintenance $_.Name) }
    $identity = Join-Path $maintenance 'BuildIdentity.cs'
    $identityText = '[assembly:System.Reflection.AssemblyFileVersion("1.0.0.' + ($releaseProfile.version -replace '^.*beta\.','') + '")]' + "`n" +
        '[assembly:System.Reflection.AssemblyInformationalVersion("' + $releaseProfile.version + '")]' + "`n" +
        'internal static class BuildIdentity { internal const string Version="' + $releaseProfile.version + '"; internal const string TrustedJson=@"' + $trustedJson.Replace('"','""') + '"; internal static readonly string[] LegacyManifestHashes=new[]{' + (($legacy | ForEach-Object { '"'+$_+'"' }) -join ',') + '}; internal static readonly string[] LegacyMaintenanceHashes=new[]{' + (($releaseProfile.legacyMaintenanceHashes | ForEach-Object { '"'+$_+'"' }) -join ',') + '}; }'
    [IO.File]::WriteAllText($identity,$identityText,$utf8)
    $compiler = $build.msbuild
    $maintenanceEvidence = Join-Path $run 'maintenance'
    & $compiler (Join-Path $maintenance 'Maintenance.csproj') /t:Build /nologo /v:minimal "/p:EvidencePath=$maintenanceEvidence" "/p:TargetFrameworkRootPath=$($build.referenceRoot)" "/p:IdentitySource=$identity" "/p:ProductIcon=$repo/src/Local/ProjectEye/Resources/sunglasses.ico" 2>&1 | Tee-Object -FilePath (Join-Path $run 'maintenance-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Maintenance build failed.' }
    Copy-Item -LiteralPath (Join-Path $maintenanceEvidence 'bin/ProjectEye.Maintenance.exe') -Destination $payload
    $result.maintenanceSha256 = (Get-FileHash -LiteralPath (Join-Path $payload 'ProjectEye.Maintenance.exe') -Algorithm SHA256).Hash
    $manifest = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($payload,$_.FullName); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    Write-Json (Join-Path $payload 'payload-manifest.json') $manifest
    $zip = Join-Path $run ('ProjectEye-Enhanced-' + $releaseProfile.version + '-Portable.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip)
    $zipCheck = Join-Path $run 'zip-verification'
    [IO.Compression.ZipFile]::ExtractToDirectory($zip,$zipCheck)
    foreach ($file in $manifest) {
        if ((Get-FileHash -LiteralPath (Join-Path $zipCheck $file.path) -Algorithm SHA256).Hash -ne $file.sha256) { throw "ZIP mismatch: $($file.path)" }
    }
    & $iscc ("/DPayload=$payload") ("/DCandidate=$run") ("/DReleaseVersion=$($releaseProfile.version)") (Join-Path $repo 'packaging/TestVersion.iss') 2>&1 | Tee-Object -FilePath (Join-Path $run 'installer-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $setup = Join-Path $run ('ProjectEye-Enhanced-' + $releaseProfile.version + '-Setup.exe')
    $artifacts = @($zip,$setup) | ForEach-Object { [ordered]@{file=[IO.Path]::GetFileName($_); bytes=(Get-Item -LiteralPath $_).Length; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} }
    Write-Json (Join-Path $run 'artifacts.json') $artifacts
    [IO.File]::WriteAllText((Join-Path $run 'SHA256SUMS.txt'), (($artifacts | ForEach-Object { "$($_.sha256)  $($_.file)" }) -join "`n") + "`n", $utf8)
    $result.payload = $payload; $result.setup = $setup; $result.zip = $zip
    $result.totalSeconds = $clock.Elapsed.TotalSeconds; $result.success = $true
} catch { $result.error=$_.Exception.Message; throw }
finally {
    $result.completedUtc=[DateTime]::UtcNow.ToString('o')
    Write-Json (Join-Path $run 'result.json') $result
    Write-Output "TEST_RELEASE_EVIDENCE=$run"
}
