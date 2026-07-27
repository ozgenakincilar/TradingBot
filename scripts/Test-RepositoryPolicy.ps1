$ErrorActionPreference = 'Stop'

$trackedFiles = @(git -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked files.'
}

$forbiddenNames = @(
    '.env',
    'appsettings.Production.json',
    'secrets.json'
)
$forbiddenExtensions = @('.pfx', '.p12', '.pem', '.key')
$sensitiveFiles = @(
    $trackedFiles | Where-Object {
        $leaf = Split-Path -Leaf $_
        $extension = [System.IO.Path]::GetExtension($_)
        $leaf -in $forbiddenNames -or $extension -in $forbiddenExtensions
    }
)
if ($sensitiveFiles.Count -gt 0) {
    throw "Forbidden secret-bearing file(s) are tracked: $($sensitiveFiles -join ', ')"
}

$assignmentPattern = '(?i)(api[_-]?(key|secret)|password|connectionstring)\s*[:=]\s*["''][^"'']{8,}["'']'
$scanFiles = $trackedFiles | Where-Object {
    $_ -notmatch '^docs/' -and
    $_ -ne 'instructions.md' -and
    $_ -ne 'scripts/Test-RepositoryPolicy.ps1'
}
$secretFindings = foreach ($file in $scanFiles) {
    if ((Test-Path -LiteralPath $file -PathType Leaf) -and
        (Select-String -LiteralPath $file -Pattern $assignmentPattern -Quiet)) {
        $file
    }
}
if (@($secretFindings).Count -gt 0) {
    throw "Potential hard-coded secret assignment found in: $($secretFindings -join ', ')"
}

$matrixPath = 'docs/13-instructions-uyumluluk-matrisi.md'
$matrix = Get-Content -Encoding UTF8 -LiteralPath $matrixPath
$ruleRows = @($matrix | Where-Object { $_ -match '^\|\s*(\d{1,3})\s*\|' })
$numbers = @(
    $ruleRows | ForEach-Object {
        [int]([regex]::Match($_, '^\|\s*(\d{1,3})\s*\|').Groups[1].Value)
    }
)
$expectedNumbers = @(1..100)
if ($numbers.Count -ne 100 -or
    (Compare-Object -ReferenceObject $expectedNumbers -DifferenceObject ($numbers | Sort-Object -Unique))) {
    throw 'Instructions compliance matrix must contain each rule number from 1 through 100 exactly once.'
}

$statuses = @{}
foreach ($row in $ruleRows) {
    $statusName = ($row -split '\|')[3].Trim()
    if (-not $statuses.ContainsKey($statusName)) {
        $statuses[$statusName] = 0
    }

    $statuses[$statusName]++
}
if ($statuses.Count -ne 4) {
    throw 'Compliance matrix must contain exactly four status categories.'
}

foreach ($status in $statuses.GetEnumerator()) {
    $summaryPattern = "^\| $([regex]::Escape($status.Key)) \| $($status.Value) \|$"
    if (-not ($matrix | Where-Object { $_ -match $summaryPattern })) {
        throw "Compliance summary is stale for status '$($status.Key)'."
    }
}

Write-Output 'Repository secret and compliance policy checks are clean.'
