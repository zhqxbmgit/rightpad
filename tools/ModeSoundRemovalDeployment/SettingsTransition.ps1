# Deployment-only configuration transition. Receiver has no retired-field logic.
function Assert-PreparedSettings {
    if((HashOf $plan.preparedSettings) -ne $plan.expectedSettingsSha256) {throw 'Prepared settings hash mismatch'}
    if(!$plan.settingsNeedsCleanup -and $plan.expectedSettingsSha256 -ne $plan.oldSettingsSha256) {throw 'Absent-field settings must remain byte-identical'}
}
function Apply-PreparedSettings {
    Assert-PreparedSettings
    $before=HashOf $plan.settingsTarget
    if($before -ne $plan.oldSettingsSha256) {throw 'Settings changed before the protected transition'}
    if($plan.settingsNeedsCleanup) {
        $temporary=$plan.settingsTarget+'.remove-retired-'+$PID+'.tmp'
        try {
            [IO.File]::WriteAllBytes($temporary,[IO.File]::ReadAllBytes($plan.preparedSettings))
            if((HashOf $temporary) -ne $plan.expectedSettingsSha256) {throw 'Temporary settings hash mismatch'}
            # Atomic replacement on the same NTFS directory; exact old bytes are
            # already in the independently verified full recovery package.
            [IO.File]::Replace($temporary,$plan.settingsTarget,[System.Management.Automation.Language.NullString]::Value)
        } finally {if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
    }
    $after=HashOf $plan.settingsTarget
    if($after -ne $plan.expectedSettingsSha256) {throw 'Settings transition verification failed'}
    Save 'settings-transition.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');modeSoundExisted=[bool]$plan.settingsNeedsCleanup;
        formalFileRewritten=[bool]$plan.settingsNeedsCleanup;beforeSha256=$before;afterSha256=$after;
        removedTopLevelFields=$(if($plan.settingsNeedsCleanup){@('modeSound')}else{@()});otherBytesPreserved=$true;atomicReplace=[bool]$plan.settingsNeedsCleanup})
}
