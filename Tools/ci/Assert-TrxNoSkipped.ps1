param([Parameter(Mandatory = $true)][string] $Path)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing TRX report: $Path" }

[xml] $trx = Get-Content -LiteralPath $Path -Raw
$summary = $trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']")
if ($null -eq $summary) { throw "Missing TRX result summary: $Path" }
$counters = $summary.SelectSingleNode("*[local-name()='Counters']")
$results = $trx.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']")
if ($null -eq $counters -or $null -eq $results) { throw "Malformed TRX report: $Path" }

$total = [int] $counters.GetAttribute('total')
$passed = [int] $counters.GetAttribute('passed')
$executed = [int] $counters.GetAttribute('executed')
$nonPassedCounters = @($counters.Attributes | Where-Object {
    $_.Name -notin @('total', 'executed', 'passed') -and [int] $_.Value -ne 0
})
$unexpected = @($results | Where-Object { $_.GetAttribute('outcome') -ne 'Passed' })
if ($summary.GetAttribute('outcome') -ne 'Completed' -or $total -le 0 -or $passed -ne $total -or
    $executed -ne $total -or $results.Count -ne $total -or $unexpected.Count -ne 0 -or $nonPassedCounters.Count -ne 0) {
    throw "TRX requires all tests passed, none skipped/inconclusive: total=$total passed=$passed executed=$executed results=$($results.Count) nonPassed=$($unexpected.Count) nonPassedCounters=$($nonPassedCounters.Count) ($Path)"
}
Write-Host "TRX zero-skip gate: $passed/$total passed ($Path)"
