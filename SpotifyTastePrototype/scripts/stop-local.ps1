[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..'))
$runDirectory = Join-Path $projectRoot '.run'

function Get-ValidatedRecordedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RecordPath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedProcessName
    )

    if (-not (Test-Path -LiteralPath $RecordPath -PathType Leaf)) {
        return [pscustomobject]@{ State = 'NoRecord'; Process = $null; Record = $null; Reason = $null }
    }

    try {
        $record = Get-Content -LiteralPath $RecordPath -Raw | ConvertFrom-Json

        if ([int]$record.SchemaVersion -ne 1) {
            throw 'unsupported record schema'
        }

        if ([string]$record.Kind -cne $ExpectedKind) {
            throw "expected kind '$ExpectedKind'"
        }

        if (-not [string]::Equals([string]$record.ProjectRoot, $projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'recorded project root does not match this project'
        }

        $processId = [int]$record.Pid
        if ($processId -le 0) {
            throw 'invalid PID'
        }

        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            return [pscustomobject]@{ State = 'Missing'; Process = $null; Record = $record; Reason = $null }
        }

        $process.Refresh()
        if ($process.StartTime.ToUniversalTime().ToFileTimeUtc() -ne [long]$record.StartTimeFileTimeUtc) {
            throw 'PID now belongs to a process with a different start time'
        }

        if (-not [string]::Equals($process.ProcessName, $ExpectedProcessName, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "expected process name '$ExpectedProcessName'"
        }

        if (-not [string]::Equals($process.ProcessName, [string]$record.ProcessName, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'recorded process name does not match the live process'
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$record.ExecutablePath)) {
            if (-not [string]::Equals($process.Path, [string]$record.ExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw 'recorded executable path does not match the live process'
            }
        }

        $win32Process = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $processId" -ErrorAction Stop
        if ($null -eq $win32Process -or [string]::IsNullOrWhiteSpace([string]$win32Process.CommandLine)) {
            throw 'the live command line could not be inspected'
        }

        $commandMarker = [string]$record.CommandMarker
        if ([string]::IsNullOrWhiteSpace($commandMarker) -or $win32Process.CommandLine.IndexOf($commandMarker, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw 'the live command line does not contain the recorded project command marker'
        }

        return [pscustomobject]@{ State = 'Valid'; Process = $process; Record = $record; Reason = $null }
    }
    catch {
        return [pscustomobject]@{ State = 'Invalid'; Process = $null; Record = $null; Reason = $_.Exception.Message }
    }
}

function Stop-RecordedServer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$RecordFile,

        [Parameter(Mandatory = $true)]
        [string]$Kind,

        [Parameter(Mandatory = $true)]
        [string]$ProcessName
    )

    $recordPath = Join-Path $runDirectory $RecordFile
    $result = Get-ValidatedRecordedProcess -RecordPath $recordPath -ExpectedKind $Kind -ExpectedProcessName $ProcessName

    switch ($result.State) {
        'NoRecord' {
            Write-Host "${Name}: no recorded process."
        }
        'Missing' {
            Remove-Item -LiteralPath $recordPath -Force
            Write-Host "${Name}: recorded process is no longer running; removed the stale record."
        }
        'Invalid' {
            Write-Warning "$Name was not stopped because its record could not be validated: $($result.Reason)"
        }
        'Valid' {
            $processId = $result.Process.Id
            Stop-Process -Id $processId -Force -ErrorAction Stop
            $result.Process.WaitForExit(5000) | Out-Null
            Remove-Item -LiteralPath $recordPath -Force
            Write-Host "${Name}: stopped PID $processId."
        }
    }
}

if (-not (Test-Path -LiteralPath $runDirectory -PathType Container)) {
    Write-Host 'SpotifyTaste: no project-local runtime records were found.'
    exit 0
}

Stop-RecordedServer -Name 'SpotifyTaste web app' -RecordFile 'web.json' -Kind 'web' -ProcessName 'node'
Stop-RecordedServer -Name 'SpotifyTaste API' -RecordFile 'api.json' -Kind 'api' -ProcessName 'dotnet'
