[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9._-]{1,64}$')]
    [string]$ServiceName = 'TradingBot',

    [ValidatePattern('^http://127\.0\.0\.1:\d{1,5}/?$')]
    [string]$BaseAddress = 'http://127.0.0.1:5080',

    [ValidateNotNullOrEmpty()]
    [string]$StatePath = "$env:ProgramData\TradingBot\watchdog-state.json",

    [ValidateRange(1, 10)]
    [int]$FailureThreshold = 3,

    [ValidateRange(1, 30)]
    [int]$RequestTimeoutSeconds = 5,

    [ValidateRange(60, 3600)]
    [int]$MaximumHeartbeatAgeSeconds = 300,

    [ValidateRange(60, 3600)]
    [int]$RestartCooldownSeconds = 600,

    [switch]$ProbeOnly
)

$ErrorActionPreference = 'Stop'
$eventSource = "$ServiceName.Watchdog"
$stateDirectory = Split-Path -Parent $StatePath
$lockPath = Join-Path $stateDirectory 'watchdog.lock'

function Write-WatchdogEvent {
    param(
        [Parameter(Mandatory)]
        [string]$Message,

        [ValidateSet('Information', 'Warning', 'Error')]
        [string]$EntryType = 'Information',

        [int]$EventId = 7300
    )

    try {
        Write-EventLog -LogName Application -Source $eventSource -EntryType $EntryType `
            -EventId $EventId -Message $Message -ErrorAction Stop
    }
    catch {
        Write-Warning $Message
    }
}

function Read-WatchdogState {
    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
        return [pscustomobject]@{
            ConsecutiveFailures = 0
            LastRestartUtc = $null
        }
    }

    try {
        $state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        return [pscustomobject]@{
            ConsecutiveFailures = [int]$state.ConsecutiveFailures
            LastRestartUtc = if ($state.LastRestartUtc) {
                [DateTimeOffset]::Parse(
                    [string]$state.LastRestartUtc,
                    [Globalization.CultureInfo]::InvariantCulture)
            }
            else {
                $null
            }
        }
    }
    catch {
        return [pscustomobject]@{
            ConsecutiveFailures = 0
            LastRestartUtc = $null
        }
    }
}

function Write-WatchdogState {
    param(
        [Parameter(Mandatory)]
        [int]$ConsecutiveFailures,

        [AllowNull()]
        [Nullable[DateTimeOffset]]$LastRestartUtc
    )

    $temporaryPath = "$StatePath.tmp"
    $payload = [ordered]@{
        ConsecutiveFailures = $ConsecutiveFailures
        LastRestartUtc = if ($null -eq $LastRestartUtc) {
            $null
        }
        else {
            $LastRestartUtc.ToUniversalTime().ToString('O')
        }
    } | ConvertTo-Json
    [IO.File]::WriteAllText($temporaryPath, $payload, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $StatePath -Force
}

function Invoke-JsonProbe {
    param([Parameter(Mandatory)][string]$Path)

    $uri = [Uri]::new(([Uri]::new($BaseAddress)), $Path)
    $response = Invoke-WebRequest -Uri $uri -Method Get -UseBasicParsing `
        -TimeoutSec $RequestTimeoutSeconds
    if ([int]$response.StatusCode -ne 200) {
        throw "Probe $Path returned HTTP $([int]$response.StatusCode)."
    }

    return $response.Content | ConvertFrom-Json
}

function Test-Health {
    $null = Invoke-JsonProbe -Path '/health'
    $forward = Invoke-JsonProbe -Path '/health/forward-evidence'
    $metrics = Invoke-JsonProbe -Path '/metrics/forward-evidence'
    if (-not [bool]$forward.isHealthy -or -not [bool]$metrics.isHealthy) {
        throw 'Forward evidence health state is unhealthy.'
    }

    if (-not $metrics.lastSuccessfulCycleAt) {
        throw 'Forward evidence heartbeat is missing.'
    }

    $heartbeat = [DateTimeOffset]::Parse(
        [string]$metrics.lastSuccessfulCycleAt,
        [Globalization.CultureInfo]::InvariantCulture)
    $age = [DateTimeOffset]::UtcNow - $heartbeat.ToUniversalTime()
    if ($age.TotalSeconds -gt $MaximumHeartbeatAgeSeconds) {
        throw "Forward evidence heartbeat is stale ($([int]$age.TotalSeconds) seconds)."
    }
}

New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$lockStream = $null
try {
    try {
        $lockStream = [IO.File]::Open(
            $lockPath,
            [IO.FileMode]::OpenOrCreate,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None)
    }
    catch [IO.IOException] {
        exit 0
    }

    $state = Read-WatchdogState
    try {
        Test-Health
        if ($state.ConsecutiveFailures -ne 0) {
            Write-WatchdogState -ConsecutiveFailures 0 `
                -LastRestartUtc $state.LastRestartUtc
        }

        exit 0
    }
    catch {
        $failureCount = [Math]::Min($state.ConsecutiveFailures + 1, $FailureThreshold)
        Write-WatchdogState -ConsecutiveFailures $failureCount `
            -LastRestartUtc $state.LastRestartUtc
        Write-WatchdogEvent -EntryType Warning -EventId 7301 `
            -Message "TradingBot health probe failed ($failureCount/$FailureThreshold): $($_.Exception.Message)"

        if ($ProbeOnly -or $failureCount -lt $FailureThreshold) {
            exit 1
        }

        $now = [DateTimeOffset]::UtcNow
        if ($null -ne $state.LastRestartUtc -and
            ($now - $state.LastRestartUtc).TotalSeconds -lt $RestartCooldownSeconds) {
            Write-WatchdogEvent -EntryType Warning -EventId 7302 `
                -Message 'TradingBot restart suppressed by watchdog cooldown.'
            exit 1
        }

        $service = Get-Service -Name $ServiceName -ErrorAction Stop
        if ($service.Status -eq [ServiceProcess.ServiceControllerStatus]::Stopped) {
            Start-Service -Name $ServiceName
        }
        else {
            Restart-Service -Name $ServiceName -Force
        }

        $service.WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromSeconds(45))
        Write-WatchdogState -ConsecutiveFailures 0 -LastRestartUtc $now
        Write-WatchdogEvent -EntryType Error -EventId 7303 `
            -Message 'TradingBot service restarted after repeated health failures.'
        exit 0
    }
}
finally {
    if ($null -ne $lockStream) {
        $lockStream.Dispose()
    }
}
