[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..'))
$apiProject = Join-Path $projectRoot 'backend\SpotifyTaste.Api\SpotifyTaste.Api.csproj'
$apiOutputDirectory = Join-Path $projectRoot 'backend\SpotifyTaste.Api\bin\Debug\net8.0'
$apiAssembly = Join-Path $apiOutputDirectory 'SpotifyTaste.Api.dll'
$frontendDirectory = Join-Path $projectRoot 'frontend\spotify-taste-web'
$frontendPackage = Join-Path $frontendDirectory 'package.json'
$viteScript = Join-Path $frontendDirectory 'node_modules\vite\bin\vite.js'
$runDirectory = Join-Path $projectRoot '.run'
$apiRecordPath = Join-Path $runDirectory 'api.json'
$webRecordPath = Join-Path $runDirectory 'web.json'

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found at '$Path'."
    }
}

function Resolve-DotNetExecutable {
    $localCandidates = @(
        (Join-Path $projectRoot '.tools\dotnet8\dotnet.exe'),
        (Join-Path $projectRoot '.dotnet\dotnet.exe'),
        (Join-Path (Split-Path -Parent $projectRoot) '.tools\dotnet8\dotnet.exe'),
        (Join-Path (Split-Path -Parent $projectRoot) 'EK\.tools\dotnet8\dotnet.exe')
    )

    foreach ($candidate in $localCandidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $dotnetCommand = Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        $dotnetCommand = Get-Command 'dotnet' -ErrorAction SilentlyContinue
    }

    if ($null -eq $dotnetCommand) {
        throw 'A .NET SDK was not found in the project-local tool directories or on PATH.'
    }

    return $dotnetCommand.Source
}

function Resolve-NodeExecutable {
    $nodeCommand = Get-Command 'node.exe' -ErrorAction SilentlyContinue
    if ($null -eq $nodeCommand) {
        $nodeCommand = Get-Command 'node' -ErrorAction SilentlyContinue
    }

    if ($null -eq $nodeCommand) {
        throw 'Node.js was not found on PATH.'
    }

    return $nodeCommand.Source
}

function Test-PortInUse {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    return [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains $Port
}

function Get-RecordedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RecordPath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedKind
    )

    if (-not (Test-Path -LiteralPath $RecordPath -PathType Leaf)) {
        return $null
    }

    try {
        $record = Get-Content -LiteralPath $RecordPath -Raw | ConvertFrom-Json
        if ([string]$record.Kind -cne $ExpectedKind) {
            return $null
        }

        if (-not [string]::Equals([string]$record.ProjectRoot, $projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $null
        }

        $processId = [int]$record.Pid
        if ($processId -le 0) {
            return $null
        }

        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            Remove-Item -LiteralPath $RecordPath -Force
            return $null
        }

        $process.Refresh()
        if ($process.StartTime.ToUniversalTime().ToFileTimeUtc() -ne [long]$record.StartTimeFileTimeUtc) {
            return $null
        }

        if (-not [string]::Equals($process.ProcessName, [string]$record.ProcessName, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $null
        }

        return [pscustomobject]@{
            Process = $process
            Record = $record
        }
    }
    catch {
        return $null
    }
}

function Save-ProcessRecord {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [string]$Kind,

        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$CommandMarker,

        [Parameter(Mandatory = $true)]
        [string]$RecordPath
    )

    $Process.Refresh()
    $executablePath = $null
    try {
        $executablePath = $Process.Path
    }
    catch {
        # The start time and command marker provide the remaining validation data.
    }

    [pscustomobject]@{
        SchemaVersion = 1
        Kind = $Kind
        Pid = $Process.Id
        ProcessName = $Process.ProcessName
        ExecutablePath = $executablePath
        StartTimeFileTimeUtc = $Process.StartTime.ToUniversalTime().ToFileTimeUtc()
        StartedAtUtc = $Process.StartTime.ToUniversalTime().ToString('o')
        ProjectRoot = $projectRoot
        CommandMarker = $CommandMarker
        Port = $Port
    } | ConvertTo-Json | Set-Content -LiteralPath $RecordPath -Encoding UTF8
}

function Wait-ForServer {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$ErrorLog
    )

    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "$Name exited before opening port $Port. Check '$ErrorLog'."
        }

        if (Test-PortInUse -Port $Port) {
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "$Name did not open port $Port within 45 seconds. Check '$ErrorLog'."
}

Assert-RequiredFile -Path $apiProject -Description 'The API project'
Assert-RequiredFile -Path $frontendPackage -Description 'The frontend package file'
Assert-RequiredFile -Path $viteScript -Description 'The local Vite entry point (run npm install in the frontend first)'

$dotnetExecutable = Resolve-DotNetExecutable
$nodeExecutable = Resolve-NodeExecutable
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

$apiState = Get-RecordedProcess -RecordPath $apiRecordPath -ExpectedKind 'api'
$webState = Get-RecordedProcess -RecordPath $webRecordPath -ExpectedKind 'web'
$startedThisRun = New-Object System.Collections.Generic.List[object]

try {
    if ($null -eq $apiState) {
        if (Test-PortInUse -Port 5143) {
            throw 'Port 5143 is already in use by a process not validated by this project launcher.'
        }

        & $dotnetExecutable build $apiProject --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "The API build failed with exit code $LASTEXITCODE."
        }

        Assert-RequiredFile -Path $apiAssembly -Description 'The built API assembly'
        $apiOutLog = Join-Path $runDirectory 'api.out.log'
        $apiErrorLog = Join-Path $runDirectory 'api.err.log'
        $apiProcess = Start-Process -FilePath $dotnetExecutable `
            -ArgumentList @("`"$apiAssembly`"", '--urls', 'http://127.0.0.1:5143') `
            -WorkingDirectory $apiOutputDirectory `
            -WindowStyle Hidden `
            -RedirectStandardOutput $apiOutLog `
            -RedirectStandardError $apiErrorLog `
            -PassThru

        Save-ProcessRecord -Process $apiProcess -Kind 'api' -Port 5143 -CommandMarker $apiAssembly -RecordPath $apiRecordPath
        $startedThisRun.Add([pscustomobject]@{ Process = $apiProcess; RecordPath = $apiRecordPath })
        $apiState = Get-RecordedProcess -RecordPath $apiRecordPath -ExpectedKind 'api'
    }

    if ($null -eq $webState) {
        if (Test-PortInUse -Port 5173) {
            throw 'Port 5173 is already in use by a process not validated by this project launcher.'
        }

        $webOutLog = Join-Path $runDirectory 'web.out.log'
        $webErrorLog = Join-Path $runDirectory 'web.err.log'
        $webProcess = Start-Process -FilePath $nodeExecutable `
            -ArgumentList @("`"$viteScript`"", '--host', '127.0.0.1', '--port', '5173', '--strictPort') `
            -WorkingDirectory $frontendDirectory `
            -WindowStyle Hidden `
            -RedirectStandardOutput $webOutLog `
            -RedirectStandardError $webErrorLog `
            -PassThru

        Save-ProcessRecord -Process $webProcess -Kind 'web' -Port 5173 -CommandMarker $viteScript -RecordPath $webRecordPath
        $startedThisRun.Add([pscustomobject]@{ Process = $webProcess; RecordPath = $webRecordPath })
        $webState = Get-RecordedProcess -RecordPath $webRecordPath -ExpectedKind 'web'
    }

    Wait-ForServer -Process $apiState.Process -Port 5143 -Name 'SpotifyTaste API' -ErrorLog (Join-Path $runDirectory 'api.err.log')
    Wait-ForServer -Process $webState.Process -Port 5173 -Name 'SpotifyTaste web app' -ErrorLog (Join-Path $runDirectory 'web.err.log')

    Write-Host "SpotifyTaste API: http://127.0.0.1:5143 (PID $($apiState.Process.Id))"
    Write-Host "SpotifyTaste web: http://127.0.0.1:5173 (PID $($webState.Process.Id))"
    Write-Host "Runtime records and logs: $runDirectory"
}
catch {
    foreach ($started in $startedThisRun) {
        try {
            $started.Process.Refresh()
            if (-not $started.Process.HasExited) {
                Stop-Process -Id $started.Process.Id -Force -ErrorAction SilentlyContinue
            }
        }
        finally {
            Remove-Item -LiteralPath $started.RecordPath -Force -ErrorAction SilentlyContinue
        }
    }

    throw
}
