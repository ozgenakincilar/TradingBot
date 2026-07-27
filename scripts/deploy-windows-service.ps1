[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidatePattern('^[A-Za-z0-9._-]{1,64}$')]
    [string]$ServiceName = 'TradingBot',

    [ValidateRange(1024, 65535)]
    [int]$Port = 5080,

    [string]$ProjectPath,

    [ValidateNotNullOrEmpty()]
    [string]$InstallRoot = (Join-Path $env:ProgramData 'TradingBot'),

    [ValidateNotNullOrEmpty()]
    [string]$SqlInstance = 'localhost\MSSQLSERVER01',

    [ValidatePattern('^[A-Za-z0-9_-]{1,128}$')]
    [string]$DatabaseName = 'TradingBotDb',

    [ValidatePattern('^[A-Za-z0-9$_-]{1,128}$')]
    [string]$SqlServiceName = 'MSSQL$MSSQLSERVER01',

    [string]$ConnectionString = $env:TRADINGBOT_SERVICE_DB_CONNECTION,

    [switch]$SkipSqlPrincipal,

    [switch]$SkipStart
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $PSScriptRoot '..\src\TradingBot.Host\TradingBot.Host.csproj'
}

$serviceAccount = "NT SERVICE\$ServiceName"
$taskName = "$ServiceName-Watchdog"
$applicationRoot = Join-Path $InstallRoot 'releases'
$dataRoot = Join-Path $InstallRoot 'data\forward-evidence'
$operationsRoot = Join-Path $InstallRoot 'ops'
$statePath = Join-Path $InstallRoot 'state\watchdog-state.json'
$watchdogSource = Join-Path $PSScriptRoot 'tradingbot-watchdog.ps1'
$watchdogTarget = Join-Path $operationsRoot 'tradingbot-watchdog.ps1'
$baseAddress = "http://127.0.0.1:$Port"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Windows Service deployment must run from an elevated PowerShell session.'
    }
}

function Assert-SafeInstallRoot {
    $resolvedProgramData = [IO.Path]::GetFullPath($env:ProgramData).TrimEnd('\')
    $resolvedInstallRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
    if (-not $resolvedInstallRoot.StartsWith(
            "$resolvedProgramData\",
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'InstallRoot must be a dedicated directory below ProgramData.'
    }
}

function Get-ServiceConfiguration {
    Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
}

function Assert-ServiceOwnership {
    param([AllowNull()]$ExistingService)

    if ($null -eq $ExistingService) {
        return
    }

    $expectedPrefix = ([IO.Path]::GetFullPath($InstallRoot).TrimEnd('\') + '\')
    $existingPath = [string]$ExistingService.PathName
    if ($existingPath.IndexOf($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Service '$ServiceName' already exists outside the managed install root."
    }
}

function Assert-PortAvailable {
    param([AllowNull()]$ExistingService)

    $listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    foreach ($listener in $listeners) {
        if ($null -eq $ExistingService -or
            [int]$listener.OwningProcess -ne [int]$ExistingService.ProcessId) {
            throw "TCP port $Port is already owned by process $($listener.OwningProcess)."
        }
    }
}

function Assert-PaperModeBeforeStop {
    try {
        $health = Invoke-RestMethod -Uri "$baseAddress/health" -TimeoutSec 5
        if (-not [string]::Equals(
                [string]$health.mode,
                'Paper',
                [StringComparison]::Ordinal)) {
            throw 'Running service is not in Paper mode; deployment is blocked.'
        }
    }
    catch {
        throw "Unable to prove Paper mode before service update: $($_.Exception.Message)"
    }
}

function Invoke-Sc {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & "$env:SystemRoot\System32\sc.exe" @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed: $($Arguments -join ' ')"
    }
}

Assert-SafeInstallRoot
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Set TRADINGBOT_SERVICE_DB_CONNECTION or pass -ConnectionString.'
}

if (-not (Test-Path -LiteralPath $watchdogSource -PathType Leaf)) {
    throw "Watchdog script not found: $watchdogSource"
}

$existingService = Get-ServiceConfiguration
Assert-ServiceOwnership -ExistingService $existingService
Assert-PortAvailable -ExistingService $existingService

if ($WhatIfPreference) {
    Write-Output "Service=$ServiceName"
    Write-Output "InstallRoot=$([IO.Path]::GetFullPath($InstallRoot))"
    Write-Output "Endpoint=$baseAddress"
    Write-Output "WatchdogTask=$taskName"
    return
}

Assert-Administrator

if ($null -ne $existingService -and $existingService.State -ne 'Stopped') {
    Assert-PaperModeBeforeStop
    if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop service for immutable deployment')) {
        Stop-Service -Name $ServiceName -ErrorAction Stop
        (Get-Service -Name $ServiceName).WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(45))
    }
}

$releaseId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss')
$releasePath = Join-Path $applicationRoot $releaseId
if ($PSCmdlet.ShouldProcess($releasePath, 'Publish immutable Windows service release')) {
    New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
    dotnet publish $ProjectPath -c Release -r win-x64 --self-contained false `
        -p:UseAppHost=true -o $releasePath
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed.'
    }
}

$executablePath = Join-Path $releasePath 'TradingBot.Host.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published service executable not found: $executablePath"
}

$productionSettings = [ordered]@{
    ConnectionStrings = [ordered]@{
        TradingBot = $ConnectionString
    }
    WindowsService = [ordered]@{
        ServiceName = $ServiceName
    }
    ForwardEvidence = [ordered]@{
        RootPath = $dataRoot
    }
}
$settingsPath = Join-Path $releasePath 'appsettings.Production.json'
$settingsJson = $productionSettings | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($settingsPath, $settingsJson, [Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Path $operationsRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $statePath) -Force | Out-Null
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
Copy-Item -LiteralPath $watchdogSource -Destination $watchdogTarget -Force

$binaryPath = "`"$executablePath`" --urls $baseAddress"
if ($null -eq $existingService) {
    Invoke-Sc -Arguments @(
        'create', $ServiceName,
        'binPath=', $binaryPath,
        'start=', 'delayed-auto',
        'obj=', $serviceAccount,
        'password=', ''
    )
}
else {
    Invoke-Sc -Arguments @(
        'config', $ServiceName,
        'binPath=', $binaryPath,
        'start=', 'delayed-auto',
        'obj=', $serviceAccount,
        'password=', ''
    )
}

Invoke-Sc -Arguments @('config', $ServiceName, 'depend=', "Tcpip/$SqlServiceName")
Invoke-Sc -Arguments @('description', $ServiceName,
    'Paper-only TradingBot and forward evidence collector')
Invoke-Sc -Arguments @('failure', $ServiceName, 'reset=', '86400',
    'actions=', 'restart/60000/restart/300000/restart/900000')
Invoke-Sc -Arguments @('failureflag', $ServiceName, '1')
Invoke-Sc -Arguments @('sidtype', $ServiceName, 'unrestricted')

if (-not [Diagnostics.EventLog]::SourceExists($ServiceName)) {
    New-EventLog -LogName Application -Source $ServiceName
}

$watchdogEventSource = "$ServiceName.Watchdog"
if (-not [Diagnostics.EventLog]::SourceExists($watchdogEventSource)) {
    New-EventLog -LogName Application -Source $watchdogEventSource
}

& "$env:SystemRoot\System32\icacls.exe" $InstallRoot /inheritance:r `
    /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' `
    "$serviceAccount`:(OI)(CI)RX" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to secure the installation directory ACL.'
}

& "$env:SystemRoot\System32\icacls.exe" (Join-Path $InstallRoot 'data') `
    /grant:r "$serviceAccount`:(OI)(CI)M" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to grant service data-directory access.'
}

& "$env:SystemRoot\System32\icacls.exe" (Join-Path $InstallRoot 'state') `
    /grant:r '*S-1-5-18:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to grant watchdog state-directory access.'
}

if (-not $SkipSqlPrincipal) {
    $sql = @'
SET NOCOUNT ON;
IF DB_ID(N'$(DatabaseName)') IS NULL THROW 51000, 'TradingBot database does not exist.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE [name] = N'NT SERVICE\$(ServiceName)')
    CREATE LOGIN [NT SERVICE\$(ServiceName)] FROM WINDOWS;
USE [$(DatabaseName)];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'NT SERVICE\$(ServiceName)')
    CREATE USER [NT SERVICE\$(ServiceName)] FOR LOGIN [NT SERVICE\$(ServiceName)];
ALTER ROLE [db_datareader] ADD MEMBER [NT SERVICE\$(ServiceName)];
ALTER ROLE [db_datawriter] ADD MEMBER [NT SERVICE\$(ServiceName)];
GRANT EXECUTE TO [NT SERVICE\$(ServiceName)];
'@
    $sqlFile = Join-Path $env:TEMP "$ServiceName-service-principal.sql"
    try {
        [IO.File]::WriteAllText($sqlFile, $sql, [Text.UTF8Encoding]::new($false))
        sqlcmd -S $SqlInstance -E -C -b -v ServiceName=$ServiceName `
            DatabaseName=$DatabaseName -i $sqlFile
        if ($LASTEXITCODE -ne 0) {
            throw 'SQL service principal provisioning failed.'
        }
    }
    finally {
        Remove-Item -LiteralPath $sqlFile -Force -ErrorAction SilentlyContinue
    }
}

$watchdogArguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$watchdogTarget`" -ServiceName $ServiceName -BaseAddress $baseAddress -StatePath `"$statePath`""
$action = New-ScheduledTaskAction -Execute 'PowerShell.exe' -Argument $watchdogArguments
$trigger = New-ScheduledTaskTrigger -Once -At ([DateTime]::Now.AddMinutes(1)) `
    -RepetitionInterval ([TimeSpan]::FromMinutes(1))
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount `
    -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit ([TimeSpan]::FromSeconds(55)) `
    -RestartCount 2 -RestartInterval ([TimeSpan]::FromMinutes(1))
$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Description 'Independent TradingBot health watchdog'
Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null

if (-not $SkipStart) {
    Start-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(45))
}

Write-Output "Service=$ServiceName"
Write-Output "Release=$releasePath"
Write-Output "Endpoint=$baseAddress"
Write-Output "WatchdogTask=$taskName"
