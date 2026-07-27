[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidatePattern('^[A-Za-z0-9._-]{1,64}$')]
    [string]$ServiceName = 'TradingBot',

    [ValidateNotNullOrEmpty()]
    [string]$InstallRoot = (Join-Path $env:ProgramData 'TradingBot')
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Windows Service removal must run from an elevated PowerShell session.'
}

$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" `
    -ErrorAction SilentlyContinue
if ($null -ne $service) {
    $managedRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\') + '\'
    if (([string]$service.PathName).IndexOf(
            $managedRoot,
            [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Service '$ServiceName' is outside the managed install root."
    }

    if ($service.State -ne 'Stopped' -and
        $PSCmdlet.ShouldProcess($ServiceName, 'Stop Windows service')) {
        Stop-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(45))
    }

    if ($PSCmdlet.ShouldProcess($ServiceName, 'Delete Windows service registration')) {
        & "$env:SystemRoot\System32\sc.exe" delete $ServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to delete Windows service registration.'
        }
    }
}

$taskName = "$ServiceName-Watchdog"
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    if ($PSCmdlet.ShouldProcess($taskName, 'Delete watchdog scheduled task')) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
}

Write-Output 'Service registration removed. Evidence, state and immutable releases were preserved.'
