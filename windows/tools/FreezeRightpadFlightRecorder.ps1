$ErrorActionPreference = 'Continue'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root "test-results\failure-captures\$stamp"
$diagnostics = Join-Path $env:LOCALAPPDATA 'rightpad\diagnostics'
$null = New-Item -ItemType Directory -Path $destination -Force

foreach ($name in 'flight-recorder.log','flight-recorder.previous.log') {
    $source = Join-Path $diagnostics $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $destination }
}

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
public static class RightpadFreezeNative {
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr p, uint a, out IntPtr t);
  [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(IntPtr t, int c, IntPtr b, int n, out int needed);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint id);
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr h, int i, StringBuilder b, int n, out int needed);
  public static uint ForegroundPid() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
  public static string Integrity(IntPtr process) { IntPtr token; if(!OpenProcessToken(process,8,out token)) throw new Win32Exception(); try { int n; GetTokenInformation(token,25,IntPtr.Zero,0,out n); IntPtr b=Marshal.AllocHGlobal(n); try { if(!GetTokenInformation(token,25,b,n,out n)) throw new Win32Exception(); return new SecurityIdentifier(Marshal.ReadIntPtr(b)).Value; } finally { Marshal.FreeHGlobal(b); } } finally { CloseHandle(token); } }
  public static string Desktop(uint thread) { int n; var b=new StringBuilder(512); if(!GetUserObjectInformation(GetThreadDesktop(thread),2,b,1024,out n)) throw new Win32Exception(); return b.ToString(); }
}
'@

$receivers = @(Get-Process -Name 'Rightpad.Receiver' -ErrorAction SilentlyContinue | ForEach-Object {
    $p = $_; $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)"
    $owner = Invoke-CimMethod -InputObject $cim -MethodName GetOwner
    [pscustomobject]@{ Pid=$p.Id; Path=$p.Path; StartTime=$p.StartTime; SessionId=$p.SessionId
        User="$($owner.Domain)\$($owner.User)"; IntegritySid=[RightpadFreezeNative]::Integrity($p.Handle)
        Desktop=[RightpadFreezeNative]::Desktop($p.Threads[0].Id); CommandLine=$cim.CommandLine }
})
$foregroundPid = [RightpadFreezeNative]::ForegroundPid()
$foreground = Get-Process -Id $foregroundPid -ErrorAction SilentlyContinue
$system = [pscustomobject]@{
    CapturedAt = [DateTimeOffset]::Now.ToString('o')
    Receiver = $receivers
    Udp50000 = @(Get-NetUDPEndpoint -ErrorAction SilentlyContinue | Where-Object LocalPort -eq 50000 |
        Select-Object LocalAddress,LocalPort,OwningProcess)
    Foreground = if ($foreground) { [pscustomobject]@{ Pid=$foreground.Id; Name=$foreground.ProcessName; Path=$foreground.Path } }
    MouseDevices = @(Get-PnpDevice -Class Mouse -ErrorAction SilentlyContinue | Select-Object Status,FriendlyName,InstanceId)
    HidDevices = @(Get-PnpDevice -Class HIDClass -ErrorAction SilentlyContinue | Select-Object Status,FriendlyName,InstanceId)
    SunshineProcesses = @(Get-Process -Name 'sunshine' -ErrorAction SilentlyContinue | Select-Object Id,Path,StartTime,SessionId)
    SunshineServices = @(Get-Service -Name '*sunshine*' -ErrorAction SilentlyContinue | Select-Object Name,Status,StartType)
}
$system | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $destination 'system.json') -Encoding UTF8

$adb = Get-Command adb -ErrorAction SilentlyContinue
if ($adb) {
    & adb devices -l 2>&1 | Set-Content -LiteralPath (Join-Path $destination 'adb-devices.txt') -Encoding UTF8
    & adb shell dumpsys activity activities 2>&1 | Set-Content -LiteralPath (Join-Path $destination 'android-activity.txt') -Encoding UTF8
    & adb logcat -d -t 2000 2>&1 | Set-Content -LiteralPath (Join-Path $destination 'android-logcat.txt') -Encoding UTF8
}

[pscustomobject]@{ CaptureDirectory=$destination; FrozenFiles=@(Get-ChildItem -LiteralPath $destination | Select-Object -ExpandProperty Name) }
