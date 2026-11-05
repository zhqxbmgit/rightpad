. (Join-Path $PSScriptRoot 'Common.ps1')
function Original-Health {
    $s=Health $true
    $appPid=Adb 'shell pidof com.rightpad.capture'
    $phoneLog=Adb ('logcat -d --pid='+$appPid+' -t 350 -v threadtime')
    if($phoneLog -notmatch ('(?i)senderRunId=0*'+([string]$s.senderRunId).TrimStart('0'))){throw 'Original phone and Receiver Sender mismatch'}
    return $s
}
function Restore-Original([string]$reason) {
    Save 'restore-started.json' @{at=[DateTime]::UtcNow.ToString('o');reason=$reason}
    With-DeviceLock {
        Assert-Device
        foreach ($package in $plan.experiments) { $null=Adb ('shell am force-stop '+$package) }
        # The original APK/data were never replaced. Do not reinstall or rewrite them.
        Assert-PackageSet (PhonePackages) $plan.oldPackages
        if ((LayoutHash) -ne $plan.layoutHash) { throw 'Original layout changed unexpectedly' }
        $null=Adb 'shell am start -S -W -n com.rightpad.capture/.MainActivity' 20000
    } 60000
    $until=[DateTime]::UtcNow.AddSeconds(60);$healthy=$null
    while ([DateTime]::UtcNow -lt $until) {
        try { $healthy=With-DeviceLock { Original-Health }; break }
        catch { Log ('Restore health pending: '+$_); Start-Sleep -Seconds 2 }
    }
    if (!$healthy) { throw 'Original foreground/connection recovery not verified' }
    Save 'restored.json' @{at=[DateTime]::UtcNow.ToString('o');reason=$reason;snapshot=$healthy;receiverUntouched=$true;originalApkAndLayoutUnchanged=$true}
}
$armed=$false
try {
    $context=Guard-Context
    $request=ReadRecord 'guard-start-request.json'
    if ($request.task -ne $plan.task) { throw 'Guard request mismatch' }
    $checks=With-DeviceLock {
        Assert-Backups
        $s=Original-Health
        foreach ($package in $plan.experiments) {
            if($plan.experimentApks){
                $expected=@($plan.experimentApks|Where-Object package -eq $package)
                if($expected.Count -ne 1){throw 'Missing retained APK identity'}
                $paths=Adb ('shell pm path '+$package)
                $devicePath=($paths -split '\r?\n'|Where-Object {$_ -like 'package:*'}|Select-Object -First 1).Substring(8)
                $actual=((Adb ('shell sha256sum "'+$devicePath+'"')) -split '\s+')[0]
                if($actual -ne $expected[0].sha256){throw ('Retained experiment APK mismatch: '+$package)}
            } elseif ((Adb ('shell pm list packages '+$package)) -match ('(?m)^package:'+ [regex]::Escape($package)+'$')) { throw ('Experiment identity already installed: '+$package) }
        }
        @{fullRealHealth=$true;snapshot=$s;originalVerified=$true;experimentIdentitiesVerified=$true}
    } 60000
    Save 'guard-ready.json' @{nonce=$request.nonce;context=$context;checks=$checks;at=[DateTime]::UtcNow.ToString('o')}
    $idleUntil=[DateTime]::UtcNow.AddMinutes(5)
    while ($true) {
        Save 'guard-heartbeat.json' @{nonce=$request.nonce;pid=$PID;at=[DateTime]::UtcNow.ToString('o')}
        if (!$armed -and (Test-Path (Join-Path $evidence 'experiment-armed.json'))) {
            $arm=ReadRecord 'experiment-armed.json'
            if ($arm.nonce -ne $request.nonce -or $arm.guardPid -ne $PID -or $arm.guardCreatedUtc -ne $context.createdUtc) { throw 'Arming identity mismatch' }
            $armed=$true
            Save 'guard-armed.json' @{nonce=$request.nonce;context=$context;at=[DateTime]::UtcNow.ToString('o');deadline=$arm.deadline}
        }
        if ($armed) {
            if ((Test-Path (Join-Path $evidence 'experiment-finished.json')) -or
                (Test-Path (Join-Path $evidence 'experiment-failed.json')) -or
                [DateTime]::UtcNow -ge [DateTime]::Parse($arm.deadline).ToUniversalTime()) {
                Restore-Original 'experiment completion/failure/deadline'
                exit 0
            }
        } elseif ([DateTime]::UtcNow -ge $idleUntil) { exit 0 }
        Start-Sleep -Seconds 2
    }
} catch {
    Save 'guard-error.json' @{at=[DateTime]::UtcNow.ToString('o');error=$_.Exception.Message;armed=$armed}
    if ($armed) { try { Restore-Original 'unexpected guard failure' } catch { Save 'restore-error.json' @{error=$_.Exception.Message} } }
    exit 1
}
