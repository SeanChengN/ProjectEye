#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run=Join-Path $repo ('artifacts/app-integration/'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $run | Out-Null
$build=Get-Content -LiteralPath (Join-Path $BuildDirectory 'result.json') -Raw -Encoding utf8|ConvertFrom-Json
if(!$build.success){throw 'Successful isolated application build required'}
$app=Join-Path $run 'app'
Copy-Item -LiteralPath $build.outputDirectory -Destination $app -Recurse
$source=Join-Path $run 'source';New-Item -ItemType Directory -Path $source | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'tests/AppIntegration') -File | ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $source $_.Name)}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild=@(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe')|Select-Object -First 1
& $msbuild (Join-Path $source 'AppIntegration.csproj') /t:Build /nologo /v:minimal "/p:AppPath=$app" "/p:EvidencePath=$run" "/p:TargetFrameworkRootPath=$($build.referenceRoot)" /fl "/flp:LogFile=$run/build.log;Encoding=UTF-8"
if($LASTEXITCODE-ne0){throw 'App integration harness build failed'}
$data=Join-Path $app 'Data';New-Item -ItemType Directory -Path $data | Out-Null
# A minimal legacy XML intentionally omits Media. Existing startup fills missing sections.
$xml='<Options><General><WarnTime>1</WarnTime><RestTime>20</RestTime><Startup>false</Startup><Sound>false</Sound><Data>false</Data><LeaveListener>false</LeaveListener></General><Style/><KeyboardShortcuts/><Behavior/><Tomato/></Options>'
[IO.File]::WriteAllText((Join-Path $data 'config.xml'),$xml,[Text.UTF8Encoding]::new($false))
Push-Location -LiteralPath $app
try {
    & (Join-Path $app 'AppIntegration.exe') 2>&1 | Tee-Object -FilePath (Join-Path $run 'runtime.log')
    $code=$LASTEXITCODE
} finally { Pop-Location }
[IO.File]::WriteAllText((Join-Path $run 'result.json'),(@{exitCode=$code;appBuild=$BuildDirectory;completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output "APP_INTEGRATION_EVIDENCE=$run"
if($code-ne0){throw 'App integration runtime failed'}
