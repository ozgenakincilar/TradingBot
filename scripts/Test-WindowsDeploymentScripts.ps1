$ErrorActionPreference = 'Stop'

$scripts = @(
    (Join-Path $PSScriptRoot 'deploy-windows-service.ps1'),
    (Join-Path $PSScriptRoot 'tradingbot-watchdog.ps1'),
    (Join-Path $PSScriptRoot 'remove-windows-service.ps1')
)

foreach ($script in $scripts) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile(
        $script,
        [ref]$tokens,
        [ref]$errors)
    if ($errors.Count -ne 0) {
        $messages = $errors | ForEach-Object Message
        throw "PowerShell syntax errors in $script`: $($messages -join '; ')"
    }
}

$deployment = Get-Content -LiteralPath $scripts[0] -Raw -Encoding UTF8
$watchdog = Get-Content -LiteralPath $scripts[1] -Raw -Encoding UTF8
$requiredDeploymentTokens = @(
    "'start=', 'delayed-auto'",
    "'actions=', 'restart/60000/restart/300000/restart/900000'",
    'New-ScheduledTaskTrigger',
    'Get-NetTCPConnection'
)
$requiredWatchdogTokens = @(
    '/health',
    '/health/forward-evidence',
    '/metrics/forward-evidence',
    'Restart-Service',
    'FailureThreshold'
)

foreach ($token in $requiredDeploymentTokens) {
    if ($deployment.IndexOf($token, [StringComparison]::Ordinal) -lt 0) {
        throw "Deployment contract token is missing: $token"
    }
}

foreach ($token in $requiredWatchdogTokens) {
    if ($watchdog.IndexOf($token, [StringComparison]::Ordinal) -lt 0) {
        throw "Watchdog contract token is missing: $token"
    }
}

Write-Output 'Windows deployment and watchdog script contracts are clean.'
