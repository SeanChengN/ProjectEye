#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunDirectory,
    [ValidateRange(1, 86400)][int]$DurationSeconds = 86400,
    [ValidateRange(1, 300)][int]$IntervalSeconds = 60
)
$ErrorActionPreference = 'Stop'
$run = (Resolve-Path -LiteralPath $RunDirectory).Path
$launch = Get-Content -LiteralPath (Join-Path $run 'launch.json') -Raw -Encoding utf8 | ConvertFrom-Json
$expectedPath = [IO.Path]::GetFullPath((Join-Path $launch.app 'ProjectEye.exe'))
if (![IO.Path]::GetFullPath($launch.app).StartsWith($run + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Application must belong to this isolated evidence directory.'
}
$target = Get-Process -Id $launch.pid
if (![string]::Equals($target.Path, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Process path does not match launch evidence.' }
$started = $target.StartTime.ToUniversalTime()
$utf8 = [Text.UTF8Encoding]::new($false)
$observation = Join-Path $run ('observation-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $observation | Out-Null
$clock = [Diagnostics.Stopwatch]::StartNew()
$samples = 0
$success = $false
$reason = 'Observation interrupted'
try {
    do {
        $current = Get-Process -Id $launch.pid -ErrorAction SilentlyContinue
        if (!$current) { $reason = 'Target exited before observation completed'; break }
        if ($current.StartTime.ToUniversalTime() -ne $started -or ![string]::Equals($current.Path, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
            $reason = 'Target identity changed'; break
        }
        $sample = [ordered]@{
            utc = [DateTime]::UtcNow.ToString('o'); elapsedSeconds = $clock.Elapsed.TotalSeconds
            processId = $current.Id; responding = $current.Responding
            workingSetBytes = $current.WorkingSet64; privateBytes = $current.PrivateMemorySize64
            handles = $current.HandleCount; cpuSeconds = $current.TotalProcessorTime.TotalSeconds
        }
        [IO.File]::AppendAllText((Join-Path $observation 'samples.jsonl'), ($sample | ConvertTo-Json -Compress) + "`n", $utf8)
        $samples++
        if ($clock.Elapsed.TotalSeconds -ge $DurationSeconds) {
            $success = $true; $reason = 'Observation duration reached; manual acceptance still required'; break
        }
        Start-Sleep -Seconds ([Math]::Min($IntervalSeconds, [Math]::Max(1, [Math]::Ceiling($DurationSeconds - $clock.Elapsed.TotalSeconds))))
    } while ($true)
} finally {
    $result = [ordered]@{
        processId = $launch.pid; processStartedUtc = $started.ToString('o')
        requestedSeconds = $DurationSeconds; observedSeconds = $clock.Elapsed.TotalSeconds
        samples = $samples; durationReached = $success; reason = $reason
        completedUtc = [DateTime]::UtcNow.ToString('o')
    }
    [IO.File]::WriteAllText((Join-Path $observation 'result.json'), ($result | ConvertTo-Json) + "`n", $utf8)
    Write-Output "OBSERVATION_EVIDENCE=$observation"
}
if (!$success) { exit 1 }
