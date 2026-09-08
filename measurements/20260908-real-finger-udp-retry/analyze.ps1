param(
    [string]$CsvPath = "$PSScriptRoot\android-touch.csv",
    [string]$ReceiverLogPath = "$PSScriptRoot\receiver.log"
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$testBySession = [ordered]@{ '1' = 'A'; '2' = 'B'; '3' = 'C'; '4' = 'D' }

function Read-Fields([string]$line) {
    $fields = @{}
    foreach ($match in [regex]::Matches($line, '(\w+)=([^\s]+)')) {
        $fields[$match.Groups[1].Value] = $match.Groups[2].Value
    }
    return $fields
}

function Percentile([double[]]$values, [double]$p) {
    if ($values.Count -eq 0) { return $null }
    $ordered = @($values | Sort-Object)
    if ($ordered.Count -eq 1) { return [double]$ordered[0] }
    # R-7 linear interpolation, also used by NumPy's default percentile method.
    $position = ($ordered.Count - 1) * $p
    $lower = [math]::Floor($position)
    $upper = [math]::Ceiling($position)
    if ($lower -eq $upper) { return [double]$ordered[$lower] }
    $fraction = $position - $lower
    return [double]$ordered[$lower] +
        ([double]$ordered[$upper] - [double]$ordered[$lower]) * $fraction
}

function Distribution([double[]]$values) {
    if ($values.Count -eq 0) {
        return [ordered]@{ count = 0; mean = $null; median = $null; p95 = $null; p99 = $null; max = $null; min = $null }
    }
    return [ordered]@{
        count = $values.Count
        mean = ($values | Measure-Object -Average).Average
        median = Percentile $values 0.50
        p95 = Percentile $values 0.95
        p99 = Percentile $values 0.99
        max = ($values | Measure-Object -Maximum).Maximum
        min = ($values | Measure-Object -Minimum).Minimum
    }
}

$csv = @(Import-Csv -LiteralPath $CsvPath)
$packets = [Collections.Generic.List[object]]::new()
$currentPacket = $null
foreach ($line in Get-Content -LiteralPath $ReceiverLogPath) {
    if ($line.StartsWith('packet:')) {
        $fields = Read-Fields $line
        $currentPacket = [ordered]@{ Header = $fields; Timestamps = [Collections.Generic.List[UInt64]]::new() }
        $packets.Add($currentPacket)
    }
    elseif ($line.StartsWith('sample:') -and $null -ne $currentPacket) {
        $fields = Read-Fields $line
        $currentPacket.Timestamps.Add([UInt64]::Parse($fields.timestampNs, $culture))
    }
}

$accepted = @($packets | Where-Object { $_.Header.status -in @('baseline', 'in_order', 'gap') })
$results = [ordered]@{}

foreach ($sessionId in 1..4) {
    $session = [string]$sessionId
    $test = $testBySession[$session]
    $samples = @($csv | Where-Object sessionId -eq $session)
    $moveSamples = @($samples | Where-Object action -eq 'MOVE')
    $positiveIntervalsMs = [Collections.Generic.List[double]]::new()
    $zeroIntervals = 0
    $negativeIntervals = 0
    for ($i = 1; $i -lt $samples.Count; $i++) {
        $dt = [Int64]$samples[$i].eventTimeNs - [Int64]$samples[$i - 1].eventTimeNs
        if ($dt -gt 0) { $positiveIntervalsMs.Add($dt / 1e6) }
        elseif ($dt -eq 0) { $zeroIntervals++ }
        else { $negativeIntervals++ }
    }

    # Active movement excludes coordinate-identical transitions and gaps over 100 ms.
    # The fixed cutoff removes explicit held-still periods and touch-down hesitation,
    # without trimming ordinary ~4 ms touchscreen sampling intervals.
    $activeIntervalsMs = [Collections.Generic.List[double]]::new()
    for ($i = 1; $i -lt $moveSamples.Count; $i++) {
        $dtMs = ([Int64]$moveSamples[$i].eventTimeNs - [Int64]$moveSamples[$i - 1].eventTimeNs) / 1e6
        $moved = ([single]$moveSamples[$i].x -ne [single]$moveSamples[$i - 1].x) -or
            ([single]$moveSamples[$i].y -ne [single]$moveSamples[$i - 1].y)
        if ($dtMs -gt 0 -and $dtMs -le 100 -and $moved) { $activeIntervalsMs.Add($dtMs) }
    }
    $activeSeconds = ($activeIntervalsMs | Measure-Object -Sum).Sum / 1000.0
    $activeRateHz = if ($activeSeconds -gt 0) { $activeIntervalsMs.Count / $activeSeconds } else { $null }

    $sessionPackets = @($packets | Where-Object { $_.Header.sessionId -eq $session })
    $sessionAccepted = @($accepted | Where-Object { $_.Header.sessionId -eq $session })
    $movePackets = @($sessionAccepted | Where-Object { $_.Header.eventType -eq 'MOVE' })
    $moveCounts = [double[]]@($movePackets | ForEach-Object { [double]$_.Header.sampleCount })

    $arrivalIntervalsMs = [Collections.Generic.List[double]]::new()
    for ($i = 1; $i -lt $sessionAccepted.Count; $i++) {
        $arrivalIntervalsMs.Add([double]$sessionAccepted[$i].Header.receiveElapsedMs -
            [double]$sessionAccepted[$i - 1].Header.receiveElapsedMs)
    }

    $moveArrivalIntervalsMs = [Collections.Generic.List[double]]::new()
    $sourceIntervalsMs = [Collections.Generic.List[double]]::new()
    $jitterProxyMs = [Collections.Generic.List[double]]::new()
    $burstIntervals = 0
    $burstReleasePairs = 0
    $previousJitter = $null
    for ($i = 1; $i -lt $movePackets.Count; $i++) {
        $arrivalDt = [double]$movePackets[$i].Header.receiveElapsedMs -
            [double]$movePackets[$i - 1].Header.receiveElapsedMs
        $sourceDt = ([double]$movePackets[$i].Timestamps[-1] -
            [double]$movePackets[$i - 1].Timestamps[-1]) / 1e6
        $jitter = $arrivalDt - $sourceDt
        $moveArrivalIntervalsMs.Add($arrivalDt)
        $sourceIntervalsMs.Add($sourceDt)
        $jitterProxyMs.Add($jitter)
        if ($arrivalDt -lt 1.0 -and $sourceDt -ge 4.0) { $burstIntervals++ }
        if ($null -ne $previousJitter -and $previousJitter -gt 4.0 -and $jitter -lt -4.0) {
            $burstReleasePairs++
        }
        $previousJitter = $jitter
    }

    $packetFirstNs = [double]$sessionAccepted[0].Timestamps[-1]
    $packetLastNs = [double]$sessionAccepted[-1].Timestamps[-1]
    $logicalSpanSeconds = ($packetLastNs - $packetFirstNs) / 1e9
    $moveFirstNs = [double]$movePackets[0].Timestamps[-1]
    $moveLastNs = [double]$movePackets[-1].Timestamps[-1]
    $moveSpanSeconds = ($moveLastNs - $moveFirstNs) / 1e9
    $moveSampleSpanSeconds = ([double]$moveSamples[-1].eventTimeNs -
        [double]$moveSamples[0].eventTimeNs) / 1e9
    $activeMovePacketIntervalsMs = @($sourceIntervalsMs | Where-Object { $_ -le 100 })
    $activeMovePacketSeconds = ($activeMovePacketIntervalsMs | Measure-Object -Sum).Sum / 1000.0
    $sampleCountHistogram = [ordered]@{}
    foreach ($group in $moveCounts | Group-Object) {
        $sampleCountHistogram[[string][int]$group.Name] = $group.Count
    }

    $gapEstimate = 0L
    foreach ($packet in $sessionPackets | Where-Object { $_.Header.status -eq 'gap' }) {
        $gapEstimate += [long]$packet.Header.sequenceDelta - 1
    }

    $results[$test] = [ordered]@{
        sessionId = $sessionId
        android = [ordered]@{
            totalSamples = $samples.Count
            historical = @($samples | Where-Object source -eq 'historical').Count
            current = @($samples | Where-Object source -eq 'current').Count
            durationSeconds = ([Int64]$samples[-1].eventTimeNs - [Int64]$samples[0].eventTimeNs) / 1e9
            positiveIntervalMs = Distribution ([double[]]$positiveIntervalsMs)
            zeroIntervals = $zeroIntervals
            negativeIntervals = $negativeIntervals
            activeMovementIntervals = $activeIntervalsMs.Count
            activeMovementSeconds = $activeSeconds
            activeSampleRateHz = $activeRateHz
            moveWindowSeconds = $moveSampleSpanSeconds
            moveWindowSampleRateHz = if ($moveSampleSpanSeconds -gt 0) {
                ($moveSamples.Count - 1) / $moveSampleSpanSeconds
            } else { $null }
        }
        protocol = [ordered]@{
            logicalPackets = $sessionAccepted.Count
            movePackets = $movePackets.Count
            movePacketRateHz = if ($moveSpanSeconds -gt 0) { ($movePackets.Count - 1) / $moveSpanSeconds } else { $null }
            activeMovePacketRateHz = if ($activeMovePacketSeconds -gt 0) {
                $activeMovePacketIntervalsMs.Count / $activeMovePacketSeconds
            } else { $null }
            logicalPacketRateHz = if ($logicalSpanSeconds -gt 0) { ($sessionAccepted.Count - 1) / $logicalSpanSeconds } else { $null }
            moveSampleCount = Distribution $moveCounts
            moveSampleCountHistogram = $sampleCountHistogram
        }
        windows = [ordered]@{
            acceptedPackets = $sessionAccepted.Count
            acceptedReceiveIntervalMs = Distribution ([double[]]$arrivalIntervalsMs)
            moveReceiveIntervalMs = Distribution ([double[]]$moveArrivalIntervalsMs)
        }
        jitterProxy = [ordered]@{
            definition = 'adjacent accepted MOVE arrival interval minus source interval; not one-way latency'
            sourceIntervalMs = Distribution ([double[]]$sourceIntervalsMs)
            variationMs = Distribution ([double[]]$jitterProxyMs)
            minVariationMs = if ($jitterProxyMs.Count) { ($jitterProxyMs | Measure-Object -Minimum).Minimum } else { $null }
            absoluteVariationMs = Distribution ([double[]]@($jitterProxyMs | ForEach-Object { [math]::Abs($_) }))
            arrivalUnder1msWhileSourceAtLeast4ms = $burstIntervals
            delayedThenCatchupPairsOver4ms = $burstReleasePairs
        }
        reliability = [ordered]@{
            receivedDatagrams = $sessionPackets.Count
            sequenceGapEstimate = $gapEstimate
            duplicatePackets = @($sessionPackets | Where-Object { $_.Header.status -eq 'duplicate' }).Count
            oldPackets = @($sessionPackets | Where-Object { $_.Header.status -eq 'old' }).Count
            invalidPackets = 0
        }
    }
}

$fullLastStats = Read-Fields ((Get-Content -LiteralPath $ReceiverLogPath | Where-Object { $_.StartsWith('stats:') })[-1])
$output = [ordered]@{
    includedSessions = $testBySession
    excludedSessions = @(5, 6)
    tests = $results
    fullRunReceiver = $fullLastStats
}
$output | ConvertTo-Json -Depth 12
