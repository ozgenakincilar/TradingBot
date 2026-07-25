param(
    [Parameter(Mandatory = $true)]
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'

function Find-Vulnerability {
    param([object] $Node)

    if ($null -eq $Node -or $Node -is [string]) {
        return
    }

    if ($Node -is [System.Collections.IEnumerable] -and
        $Node -isnot [System.Management.Automation.PSCustomObject]) {
        foreach ($item in $Node) {
            Find-Vulnerability -Node $item
        }

        return
    }

    foreach ($property in $Node.PSObject.Properties) {
        if ($property.Name -eq 'vulnerabilities') {
            foreach ($vulnerability in @($property.Value)) {
                if ($null -ne $vulnerability) {
                    $vulnerability
                }
            }
        }
        else {
            Find-Vulnerability -Node $property.Value
        }
    }
}

$report = Get-Content -Raw -LiteralPath $ReportPath | ConvertFrom-Json
$findings = @(Find-Vulnerability -Node $report)
if ($findings.Count -gt 0) {
    throw "NuGet vulnerability scan found $($findings.Count) vulnerable dependency record(s)."
}

Write-Output 'NuGet vulnerability scan is clean.'
