#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory)
$ErrorActionPreference='Stop'
$BuildDirectory=(Resolve-Path -LiteralPath $BuildDirectory).Path
$build=Get-Content (Join-Path $BuildDirectory 'result.json') -Raw -Encoding utf8|ConvertFrom-Json
if(!$build.success){throw 'Successful build required'}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$versioning=Get-ChildItem -LiteralPath $vs -Filter NuGet.Versioning.dll -Recurse|Select-Object -First 1
if(!$versioning){throw 'NuGet version range parser required'}
[Reflection.Assembly]::LoadFrom($versioning.FullName)|Out-Null
$assets=Get-Content (Join-Path $BuildDirectory 'src/Local/ProjectEye/obj/project.assets.json') -Raw -Encoding utf8|ConvertFrom-Json
$packages=@($assets.libraries.PSObject.Properties|Where-Object {$_.Value.type-eq'package'}|ForEach-Object {
    $parts=$_.Name.Split('/');[ordered]@{name=$parts[0];version=$parts[1]}
})
$feeds=Invoke-RestMethod 'https://api.nuget.org/v3/vulnerabilities/index.json'
$hits=@()
foreach($feed in $feeds){
    $data=Invoke-RestMethod $feed.'@id'
    foreach($package in $packages){foreach($advisory in $data.($package.name.ToLowerInvariant())){
        if($advisory-and[NuGet.Versioning.VersionRange]::Parse($advisory.versions).Satisfies([NuGet.Versioning.NuGetVersion]::Parse($package.version))){
            $hits+=[ordered]@{package=$package;severity=$advisory.severity;url=$advisory.url}
        }
    }}
}
$report=[ordered]@{checkedUtc=[datetime]::UtcNow.ToString('o');feeds=$feeds;packages=$packages;advisories=$hits;success=$hits.Count-eq0}
[IO.File]::WriteAllText((Join-Path $BuildDirectory 'dependency-audit.json'),($report|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
if($hits.Count){throw 'Dependency advisories found; inspect dependency-audit.json before packaging'}
Write-Output "DEPENDENCY_AUDIT: $($packages.Count) packages, zero current NuGet advisory matches"
