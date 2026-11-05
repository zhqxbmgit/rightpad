param([Parameter(Mandatory=$true)][string]$CaptureDir,[int]$Seconds)
$ErrorActionPreference='Stop'
$round=Split-Path (Split-Path $CaptureDir -Parent) -Parent
& (Join-Path $round 'evidence\Analyze.ps1') -TaskDir $CaptureDir | Out-Null
$path=Join-Path $CaptureDir 'summary.json'
$summary=Get-Content $path -Raw|ConvertFrom-Json
$summary.capture_window_s=$Seconds
$summary|ConvertTo-Json -Depth 10|Set-Content $path -Encoding UTF8
$rows=@(Import-Csv (Join-Path $CaptureDir 'intervals.csv'))
$culture=[Globalization.CultureInfo]::InvariantCulture
$buckets=@{};$run=0;$longest=0
foreach($r in $rows){
 $t=[double]::Parse($r.time_s,$culture);$dt=[double]::Parse($r.interval_ms,$culture)
 $second=[int][Math]::Floor($t-$summary.first_event_s)
 if(!$buckets.ContainsKey($second)){$buckets[$second]=[Collections.Generic.List[double]]::new()}
 $buckets[$second].Add($dt)
 if($dt -ge 7 -and $dt -lt 10){$run++;$longest=[Math]::Max($longest,$run)}else{$run=0}
}
$perSecond=@(foreach($second in @($buckets.Keys|Sort-Object)){
 $v=@($buckets[$second]|Sort-Object);$n=$v.Count
 [pscustomobject]@{second=$second;intervals=$n;median_ms=$v[[Math]::Ceiling(.5*$n)-1];p95_ms=$v[[Math]::Ceiling(.95*$n)-1];p99_ms=$v[[Math]::Ceiling(.99*$n)-1];over10ms=@($v|Where-Object {$_ -gt 10}).Count;over20ms=@($v|Where-Object {$_ -gt 20}).Count;max_ms=$v[-1]}
})
$perSecond|Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $CaptureDir 'per-second.csv')
$before=Get-Content (Join-Path $CaptureDir 'receiver-before.json') -Raw|ConvertFrom-Json
$after=Get-Content (Join-Path $CaptureDir 'receiver-after.json') -Raw|ConvertFrom-Json
$result=[pscustomobject]@{name=(Split-Path $CaptureDir -Leaf);captureSeconds=$Seconds;reports=$summary.reports;contacts=$summary.contacts;contactSpanSeconds=($summary.last_event_s-$summary.first_event_s);intervals=$summary.all_move_intervals;histogram=$summary.histogram;longestConsecutive7to10ms=$longest;synDropped=$summary.syn_dropped;multiReports=$summary.multi_contact_reports;acceptedSamplesDelta=($after.acceptedSamples-$before.acceptedSamples);mouseFailures=$after.mouseOutputFailures;gamepadFailures=$after.gamepadFailures;perSecond=$perSecond}
$result|ConvertTo-Json -Depth 12|Set-Content (Join-Path $CaptureDir 'stability.json') -Encoding UTF8
[pscustomobject]@{name=$result.name;reports=$result.reports;contacts=$result.contacts;span=$result.contactSpanSeconds;median=$result.intervals.median_ms;p95=$result.intervals.p95_ms;p99=$result.intervals.p99_ms;max=$result.intervals.max_ms;histogram=$result.histogram;max7to10msRun=$longest}|ConvertTo-Json -Depth 5
