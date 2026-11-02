param(
    [string]$BaseUrl = 'http://localhost:5080',
    [string]$Account = ('DEMO-' + [Guid]::NewGuid().ToString('N').Substring(0, 8).ToUpperInvariant())
)
$ErrorActionPreference = 'Stop'
$trade = @{
    externalRef = "$Account-AAPL"; accountId = $Account; instrumentId = 'US0378331005'
    side = 'BUY'; quantity = 120; price = 185.40; tradeDate = '2025-03-01'; asOf = '2025-03-01T10:00:00Z'
}
function Submit-Trade($payload, $expected) {
    $result = Invoke-RestMethod -Uri "$BaseUrl/api/trades" -Method Post -ContentType 'application/json' -Body ($payload | ConvertTo-Json)
    if ($result.outcome -ne $expected) { throw "Expected $expected; got $($result.outcome). Use a fresh account for each run." }
    Write-Host "$($payload.externalRef): $($result.outcome), revision $($result.revisionId)"
}
Submit-Trade $trade 'accepted'
Submit-Trade $trade 'duplicate'
$correction = $trade.Clone(); $correction.quantity = 100; $correction.asOf = '2025-03-01T11:00:00Z'
Submit-Trade $correction 'corrected'
Submit-Trade $trade 'stale'
$snapshot = Invoke-RestMethod "$BaseUrl/api/accounts/$Account/snapshot?date=2025-03-01"
if ($snapshot.positions[0].quantity -ne 100 -or $snapshot.totalMarketValueUsd -ne 18600 -or $snapshot.unrealizedPnlUsd -ne 60) {
    throw 'Corrected snapshot did not match expected totals.'
}
$historic = Invoke-RestMethod "$BaseUrl/api/accounts/$Account/snapshot?date=2025-03-01&asOf=2025-03-01T10:30:00Z"
if ($historic.positions[0].quantity -ne 120 -or $historic.totalMarketValueUsd -ne 22320) {
    throw 'Historical cutoff did not retain the original version.'
}
Write-Host 'Verified: latest = 100 units / $18,600 / $60 unrealized P&L; earlier cutoff = 120 units / $22,320.'
Write-Host "Dashboard: $BaseUrl/?account=$Account&date=2025-03-01"
$snapshot | ConvertTo-Json -Depth 5
