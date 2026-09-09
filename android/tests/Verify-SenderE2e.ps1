param(
    [Parameter(Mandatory=$true)][string]$CsvPath,
    [Parameter(Mandatory=$true)][string]$LogcatPath,
    [Parameter(Mandatory=$true)][string]$ReceiverLogPath
)
$ErrorActionPreference = 'Stop'
function Assert-Equal($expected, $actual, [string]$label) {
    if ($expected -cne $actual) { throw "$label expected=$expected actual=$actual" }
}
function Read-Fields([string]$line) {
    $fields = @{}
    foreach ($match in [regex]::Matches($line, '(\w+)=([^\s]+)')) {
        $fields[$match.Groups[1].Value] = $match.Groups[2].Value
    }
    return $fields
}
function Float-Bits([string]$value) {
    $number = [single]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
    return [BitConverter]::ToInt32([BitConverter]::GetBytes($number), 0)
}
function Assert-Sample($csv, $other, [string]$timeField) {
    Assert-Equal $csv.eventTimeNs $other[$timeField] 'nanoseconds'
    Assert-Equal (Float-Bits $csv.x) (Float-Bits $other.x) 'x float32 bits'
    Assert-Equal (Float-Bits $csv.y) (Float-Bits $other.y) 'y float32 bits'
}

$csv = @(Import-Csv -LiteralPath $CsvPath)
if ($csv.Count -eq 0) { throw 'No captured samples' }
$logcat = @(Get-Content -LiteralPath $LogcatPath)
$touch = @($logcat | Where-Object { $_ -match 'RightpadTouch.* action=' } | ForEach-Object { Read-Fields $_ })
Assert-Equal $csv.Count $touch.Count 'CSV/Logcat sample count'
$expectedPackets = [Collections.Generic.List[object]]::new()
$group = [Collections.Generic.List[object]]::new()
for ($i = 0; $i -lt $csv.Count; $i++) {
    $row = $csv[$i]
    Assert-Equal ([string]$i) $row.sampleIndex 'CSV index'
    Assert-Equal $row.sessionId $touch[$i].session 'Logcat session'
    Assert-Equal $row.action $touch[$i].action 'Logcat action'
    Assert-Equal $row.source $touch[$i].source 'Logcat source'
    Assert-Sample $row $touch[$i] 'eventTimeNs'
    if ($row.action -eq 'CANCEL') { continue }
    $group.Add($row)
    if ($row.source -eq 'current') {
        if ($row.action -eq 'MOVE') {
            Assert-Equal ($group.Count - 1) ([int]$touch[$i].historySize) 'historical grouping'
        }
        $expectedPackets.Add(@{ Samples = $group.ToArray(); Action = $row.action; Session = $row.sessionId })
        $group.Clear()
    }
}
Assert-Equal 0 $group.Count 'no incomplete historical batch'

$packets = [Collections.Generic.List[object]]::new()
$current = $null
foreach ($line in Get-Content -LiteralPath $ReceiverLogPath) {
    if ($line.StartsWith('packet:')) {
        $current = @{ Header = (Read-Fields $line); Samples = [Collections.Generic.List[object]]::new() }
        $packets.Add($current)
    } elseif ($line.StartsWith('sample:')) {
        $current.Samples.Add((Read-Fields $line))
    } elseif ($line.StartsWith('stats:') -and $null -ne $current) {
        $current.Stats = Read-Fields $line
    }
}
$datagram = 0
$acceptedSamples = 0
for ($sequence = 0; $sequence -lt $expectedPackets.Count; $sequence++) {
    $expected = $expectedPackets[$sequence]
    $copies = if ($expected.Action -eq 'MOVE') { 1 } else { 3 }
    $acceptedSamples += $expected.Samples.Count
    for ($copy = 0; $copy -lt $copies; $copy++) {
        if ($datagram -ge $packets.Count) { throw 'Missing UDP datagram' }
        $packet = $packets[$datagram++]
        Assert-Equal '2' $packet.Header.version 'version'
        Assert-Equal ([string]$sequence) $packet.Header.sequence 'logical sequence'
        Assert-Equal $expected.Session $packet.Header.sessionId 'session'
        Assert-Equal $expected.Action $packet.Header.eventType 'event type'
        Assert-Equal $expected.Samples.Count ([int]$packet.Header.sampleCount) 'sampleCount'
        $status = if ($copy -gt 0) { 'duplicate' } elseif ($sequence -eq 0) { 'baseline' } else { 'in_order' }
        Assert-Equal $status $packet.Header.status 'receive status'
        Assert-Equal ($sequence + 1) ([int]$packet.Stats.acceptedPackets) 'duplicate packet accounting'
        Assert-Equal $acceptedSamples ([int]$packet.Stats.acceptedSamples) 'duplicate sample accounting'
        if ($copy -gt 0) {
            Assert-Equal 0 $packet.Samples.Count 'duplicate produces no samples'
            continue
        }
        Assert-Equal $expected.Samples.Count $packet.Samples.Count 'accepted sample count'
        for ($j = 0; $j -lt $packet.Samples.Count; $j++) {
            $sample = $packet.Samples[$j]
            Assert-Equal ([string]$j) $sample.sampleIndex 'packet sample index'
            Assert-Equal ([string]$sequence) $sample.sequence 'sample sequence'
            Assert-Equal $expected.Session $sample.sessionId 'sample session'
            Assert-Sample $expected.Samples[$j] $sample 'timestampNs'
        }
    }
}
Assert-Equal $packets.Count $datagram 'no extra datagrams'
$sent = @($logcat | Where-Object { $_ -match 'RightpadUdp.*packet_sent' } | ForEach-Object { Read-Fields $_ })
Assert-Equal $expectedPackets.Count $sent.Count 'sender logical packets'
for ($i = 0; $i -lt $sent.Count; $i++) {
    Assert-Equal ([string]$i) $sent[$i].sequence 'sender sequence'
    Assert-Equal (20 + 16 * $expectedPackets[$i].Samples.Count) ([int]$sent[$i].bytes) 'packet byte length'
    $copies = if ($expectedPackets[$i].Action -eq 'MOVE') { 1 } else { 3 }
    Assert-Equal $copies ([int]$sent[$i].copies) 'redundancy copies'
}
if ($logcat -match 'queue_overflow|send_failed|packet_rejected|sender_open_failed') { throw 'Sender error in capture' }
$historical = @($csv | Where-Object source -eq 'historical').Count
if ($historical -eq 0) { throw 'No historical samples observed; repeat with a swipe' }
$subMillisecond = @($csv | Where-Object { ([long]$_.eventTimeNs % 1000000) -ne 0 }).Count
if ($subMillisecond -eq 0) { throw 'No non-millisecond sample timestamps observed' }
Write-Output "PASS CSV/Logcat/Receiver content, float32 bits, nanoseconds, original order and packet grouping"
Write-Output "PASS sequence, DOWN/UP three copies, duplicate accounting and sender diagnostics"
Write-Output "samples=$($csv.Count) historical=$historical current=$($csv.Count - $historical) nonMillisecondTimestamps=$subMillisecond logicalPackets=$($expectedPackets.Count) datagrams=$($packets.Count) duplicates=$($packets.Count - $expectedPackets.Count)"
