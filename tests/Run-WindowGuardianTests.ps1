# Window guardian tests H2 / T7 / T8 (Windows PowerShell 5.1). Builds nothing: build Trayloom.App and tests\AmDummy in Release first.
#
#   powershell -ExecutionPolicy Bypass -File tests\Run-WindowGuardianTests.ps1 [-Scratch <dir>]
#
# Only AmDummy windows are touched. Trayloom.exe is started ONLY as "--window-guardian-launch" (through the AmDummy owner, like the real
# client does), never as the app, and with TRAYLOOM_DATA pointing at the scratch directory. No input is synthesised.
param(
    [string]$Scratch = (Join-Path ([IO.Path]::GetTempPath()) 'trayloom-guardian-tests'),
    [string[]]$Modes = @('kept', 'ghost', 'parked', 'nearoff', 'iconicoff', 'hiddenNoRecord', 'helperRecord', 'maximised', 'maximisedVisible')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$trayloom = (Get-ChildItem (Join-Path $root 'src\Trayloom.App\bin\Release') -Recurse -Filter Trayloom.exe | Select-Object -First 1).FullName
$dummy = (Get-ChildItem (Join-Path $root 'tests\AmDummy\bin\Release') -Recurse -Filter AmDummy.exe | Select-Object -First 1).FullName
if (-not $trayloom -or -not $dummy) { throw 'Build Trayloom.App and tests\AmDummy in Release first.' }
New-Item -ItemType Directory -Force $Scratch | Out-Null
$Scratch = (Resolve-Path $Scratch).Path
Write-Host "Trayloom : $trayloom"
Write-Host "AmDummy  : $dummy"
Write-Host "Scratch  : $Scratch"

# the heuristic pass of the guardian looks at AmDummy windows only (never at a real Apple Music that may be running)
$env:TRAYLOOM_GUARDIAN_PROCESSES = 'AmDummy'
$script:failures = 0
$script:started = New-Object System.Collections.Generic.List[int]
$report = @()

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Host "  PASS $name $detail" } else { Write-Host "  FAIL $name $detail" -ForegroundColor Red; $script:failures++ }
}

function Alive([int]$id) { return [bool](Get-Process -Id $id -ErrorAction SilentlyContinue) }

function Wait-File([string]$path, [int]$ms) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $path)) { if ($sw.ElapsedMilliseconds -gt $ms) { throw "Timed out waiting for $path" }; Start-Sleep -Milliseconds 20 }
}

function New-Case([string]$name) {
    $dir = Join-Path $Scratch $name
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $env:TRAYLOOM_DATA = $dir
    return $dir
}

function Start-Owner([string]$dir) {
    $go = Join-Path $dir 'go'
    $info = Join-Path $dir 'owner.txt'
    $p = Start-Process $dummy -ArgumentList "--role owner --exe `"$trayloom`" --go `"$go`" --info `"$info`"" -WindowStyle Hidden -PassThru
    $script:started.Add($p.Id)
    Wait-File $info 10000
    $parts = (Get-Content $info -Raw).Trim().Split(' ')
    return @{ Process = $p; Pid = [int]$parts[0]; Start = $parts[1]; Go = $go }
}

function Start-Guardian($owner) {
    New-Item -ItemType File -Force $owner.Go | Out-Null
    Wait-File ($owner.Go + '.guardian') 10000
    $gpid = [int](Get-Content ($owner.Go + '.guardian') -Raw)
    if ($gpid -gt 0) { $script:started.Add($gpid) }
    return $gpid
}

function Stop-Quietly([int]$id) { try { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue } catch { } }

function Tree-Kill([int]$id) {
    & taskkill.exe /T /F /PID $id | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ((Alive $id) -and $sw.ElapsedMilliseconds -lt 3000) { Start-Sleep -Milliseconds 20 }
}

# ------------------------------------------------------------------------------------------------ H2: one bad state per mode
foreach ($mode in $Modes) {
    Write-Host "== H2 mode '$mode'"
    $dir = New-Case $mode
    $env:TRAYLOOM_GUARDIAN_IDLE_MS = '120000'
    $owner = Start-Owner $dir
    $ready = Join-Path $dir 'ready'
    $status = Join-Path $dir 'status.txt'
    $w = Start-Process $dummy -ArgumentList "--role windows --mode $mode --owner $($owner.Pid) $($owner.Start) --ready `"$ready`" --status `"$status`"" -WindowStyle Hidden -PassThru
    $script:started.Add($w.Id)
    Wait-File $ready 10000
    $first = (Get-Content $status | Select-Object -First 1)
    Check 'window starts in the bad state' ($first -match 'good=0') $first
    $gpid = Start-Guardian $owner
    Check 'launcher returned the guardian pid' ($gpid -gt 0) "pid $gpid"
    Start-Sleep -Milliseconds 2500
    Check 'guardian runs while its owner lives' (Alive $gpid)
    $g = Get-Process -Id $gpid -ErrorAction SilentlyContinue
    $memory = $null
    if ($g) {
        $wpf = @($g.Modules | Where-Object { $_.ModuleName -like 'PresentationFramework*' -or $_.ModuleName -like 'PresentationCore*' -or $_.ModuleName -like 'WindowsBase*' })
        Check 'guardian did not load WPF' ($wpf.Count -eq 0) (($wpf | ForEach-Object { $_.ModuleName }) -join ',')
        $memory = [pscustomobject]@{ Mode = $mode; WorkingSetMB = [math]::Round($g.WorkingSet64 / 1MB, 1); PrivateMB = [math]::Round($g.PrivateMemorySize64 / 1MB, 1); PagedMB = [math]::Round($g.PagedMemorySize64 / 1MB, 1); Threads = $g.Threads.Count; Handles = $g.HandleCount; Modules = $g.Modules.Count; CpuSec = [math]::Round($g.TotalProcessorTime.TotalSeconds, 3) }
    }
    $parent = (Get-CimInstance Win32_Process -Filter "ProcessId=$gpid" -ErrorAction SilentlyContinue).ParentProcessId
    Check 'guardian is an orphan (its parent, the launcher, is gone)' (-not (Alive $parent)) "parent pid $parent"

    $killTicks = [DateTime]::UtcNow.Ticks
    Tree-Kill $owner.Pid
    Check 'owner (and its tree) is dead' (-not (Alive $owner.Pid))
    Start-Sleep -Milliseconds 700
    Check 'guardian survived the tree kill' (Alive $gpid)

    $goodMs = $null
    $exitMs = $null
    $fileGoneMs = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 9000) {
        $now = ([DateTime]::UtcNow.Ticks - $killTicks) / 10000
        if ($null -eq $goodMs) {
            $last = Get-Content $status -ErrorAction SilentlyContinue | Select-Object -Last 1
            if ($last -match '^(\d+) .*good=1') { $goodMs = [math]::Round(([int64]$Matches[1] - $killTicks) / 10000) }
        }
        if ($null -eq $fileGoneMs -and -not (Test-Path (Join-Path $dir 'window-guardian.json'))) { $fileGoneMs = [math]::Round($now) }
        if ($null -eq $exitMs -and -not (Alive $gpid)) { $exitMs = [math]::Round($now) }
        if ($null -ne $goodMs -and $null -ne $fileGoneMs -and $null -ne $exitMs) { break }
        Start-Sleep -Milliseconds 25
    }
    $lines = Get-Content $status
    $lastLine = $lines | Select-Object -Last 1
    Check 'window rescued within 6 s of the kill' ($null -ne $goodMs -and $goodMs -le 6000) "rescue time $goodMs ms; last: $lastLine"
    if ($lastLine -match 'visible=1') {
        Check 'window is visible' $true
    } else { Check 'window is visible' $false $lastLine }
    if ($mode -notin @('nearoff', 'helperRecord', 'maximisedVisible')) {
        # spec: visible + iconic (a window that was already shown on screen, like nearoff, only has to be on screen)
        Check 'window is minimised' ($lastLine -match 'iconic=1') $lastLine
    }
    if ($mode -eq 'helperRecord') {
        # D: the untitled helper window is never shown, whatever the records say; its records are gone with the rest
        Check 'untitled helper window stays hidden' ($lastLine -match 'helperShown=0') $lastLine
    }
    if ($mode -eq 'maximised') {
        # C: the person's maximised window comes back minimised and is still a maximised window
        Check 'window is still maximised' ($lastLine -match 'zoomed=1') $lastLine
    }
    if ($mode -eq 'maximisedVisible') {
        # C: a maximised window on screen is not moved or resized (its rectangle reaches past the screen edges by its frame)
        $firstRect = if ($first -match 'rect=(\[[^\]]*\])') { $Matches[1] } else { 'none' }
        $lastRect = if ($lastLine -match 'rect=(\[[^\]]*\])') { $Matches[1] } else { 'none' }
        Check 'maximised window keeps its rectangle' ($firstRect -eq $lastRect -and $firstRect -ne 'none') "$firstRect -> $lastRect"
        Check 'maximised window is still maximised and not minimised' ($lastLine -match 'zoomed=1' -and $lastLine -match 'iconic=0') $lastLine
    }
    Check 'no layered / transparent / tool-window style left' ($lastLine -match 'exBits=0') $lastLine
    Check 'rectangle accepted by Sanitize (comfortably visible)' ($lastLine -match 'comfortable=1') $lastLine
    Check 'transparent popup (no record) made opaque by the heuristic pass' ($lastLine -match 'popupGhost=0') $lastLine
    Check 'state file removed' ($null -ne $fileGoneMs -and -not (Test-Path (Join-Path $dir 'window-guardian.json'))) "after $fileGoneMs ms"
    Check 'guardian exited by itself' ($null -ne $exitMs) "after $exitMs ms"
    $log = Join-Path $dir 'logs\window-guardian.log'
    $logText = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
    if ($mode -eq 'hiddenNoRecord') {
        Check 'log shows the heuristic rescue' ($logText -match 'heuristic rescue')
    } else {
        Check 'log shows the rescue (before/after rectangles)' ($logText -match 'rescue 0x' -and $logText -match 'after ')
    }
    $report += [pscustomobject]@{ Mode = $mode; RescueMs = $goodMs; StateFileGoneMs = $fileGoneMs; GuardianExitMs = $exitMs; Memory = $memory }
    Stop-Quietly $w.Id
    Stop-Quietly $gpid
    Get-Content $log -ErrorAction SilentlyContinue | Select-Object -Last 14 | ForEach-Object { Write-Host "    | $_" }
}

# ------------------------------------------------------------------------------------------------ T9: a newer Trayloom takes the window over during the rescue passes
Write-Host "== T9 newer owner takes over while the guardian is still rescuing"
$dir = New-Case 't9'
$env:TRAYLOOM_GUARDIAN_IDLE_MS = '120000'
$owner = Start-Owner $dir
$ready = Join-Path $dir 'ready'
$status = Join-Path $dir 'status.txt'
$signal = Join-Path $dir 'rehide'
$w = Start-Process $dummy -ArgumentList "--role windows --mode kept --owner $($owner.Pid) $($owner.Start) --ready `"$ready`" --status `"$status`" --rehide `"$signal`"" -WindowStyle Hidden -PassThru
$script:started.Add($w.Id)
Wait-File $ready 10000
$gpid = Start-Guardian $owner
Tree-Kill $owner.Pid
$sawGood = $false
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.ElapsedMilliseconds -lt 8000 -and -not $sawGood) {
    $l = Get-Content $status -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($l -match 'good=1') { $sawGood = $true } else { Start-Sleep -Milliseconds 20 }
}
Check 'first pass shows the window' $sawGood
# the new Trayloom hides it again and records it under its own pid; the guardian's later passes must leave it alone
New-Item -ItemType File -Force $signal | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
while ((Alive $gpid) -and $sw.ElapsedMilliseconds -lt 9000) { Start-Sleep -Milliseconds 25 }
Start-Sleep -Milliseconds 300
$lastLine = Get-Content $status | Select-Object -Last 1
$stateFile = Join-Path $dir 'window-guardian.json'
Check 'guardian exited' (-not (Alive $gpid)) "after $($sw.ElapsedMilliseconds) ms"
Check 'the newer owner hid the window and the guardian left it hidden' ($lastLine -match ' visible=0 ') $lastLine
$ownerNow = if (Test-Path $stateFile) { (Get-Content $stateFile -Raw | ConvertFrom-Json).ownerPid } else { $null }
Check 'state file still belongs to the newer owner' ($ownerNow -eq $w.Id) "ownerPid $ownerNow (newer owner $($w.Id))"
$log = Join-Path $dir 'logs\window-guardian.log'
$logText = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
Check 'log says the file belongs to a newer Trayloom' ($logText -match 'newer Trayloom')
Stop-Quietly $w.Id
Stop-Quietly $gpid

# ------------------------------------------------------------------------------------------------ T7: owner alive, state emptied -> idle exit
Write-Host "== T7 idle exit while the owner lives"
$dir = New-Case 't7'
$env:TRAYLOOM_GUARDIAN_IDLE_MS = '1500'
$owner = Start-Owner $dir
$stateFile = Join-Path $dir 'window-guardian.json'
function Write-State($owner, [bool]$withWindow) {
    $win = if ($withWindow) { '{"hwnd":1,"pid":1,"process":"AmDummy","cls":"WinUIDesktopWin32WindowClass","kind":"kept","exStyle":null,"home":null}' } else { '' }
    $json = '{"v":1,"ownerPid":' + $owner.Pid + ',"ownerStart":' + $owner.Start + ',"updated":"' + [DateTime]::UtcNow.ToString('O') + '","windows":[' + $win + ']}'
    [IO.File]::WriteAllText($stateFile, $json, (New-Object Text.UTF8Encoding($false)))
}
Write-State $owner $true
$gpid = Start-Guardian $owner
Start-Sleep -Milliseconds 3500
Check 'guardian stays while a window is tracked (longer than the idle time)' (Alive $gpid)
$g = Get-Process -Id $gpid -ErrorAction SilentlyContinue
$t8 = if ($g) { [pscustomobject]@{ WorkingSetMB = [math]::Round($g.WorkingSet64 / 1MB, 1); PrivateMB = [math]::Round($g.PrivateMemorySize64 / 1MB, 1); Threads = $g.Threads.Count; CpuSec = [math]::Round($g.TotalProcessorTime.TotalSeconds, 3) } } else { $null }
Write-State $owner $false
$event = [Threading.EventWaitHandle]::OpenExisting('Local\Trayloom.WindowGuardian.Changed.' + $owner.Pid)
$null = $event.Set()
$event.Dispose()
$sw = [Diagnostics.Stopwatch]::StartNew()
while ((Alive $gpid) -and $sw.ElapsedMilliseconds -lt 6000) { Start-Sleep -Milliseconds 25 }
$exit = $sw.ElapsedMilliseconds
Check 'guardian exits after the idle time once the state is empty' (-not (Alive $gpid)) "after $exit ms (idle 1500 ms)"
Check 'idle exit is not early' ($exit -ge 1000) "after $exit ms"
Check 'owner was untouched' (Alive $owner.Pid)
Tree-Kill $owner.Pid

# ------------------------------------------------------------------------------------------------ T8: idle footprint over time
Write-Host "== T8 guardian footprint (owner alive, one window tracked, default idle)"
$dir = New-Case 't8'
$env:TRAYLOOM_GUARDIAN_IDLE_MS = '120000'
$owner = Start-Owner $dir
$stateFile = Join-Path $dir 'window-guardian.json'
Write-State $owner $true
$gpid = Start-Guardian $owner
$samples = @()
$previous = 0
foreach ($t in 1, 3, 6, 10) {
    Start-Sleep -Seconds ($t - $previous)
    $previous = $t
    $g = Get-Process -Id $gpid -ErrorAction SilentlyContinue
    if ($g) { $samples += [pscustomobject]@{ AfterSec = $t; WorkingSetMB = [math]::Round($g.WorkingSet64 / 1MB, 1); PeakMB = [math]::Round($g.PeakWorkingSet64 / 1MB, 1); PrivateMB = [math]::Round($g.PrivateMemorySize64 / 1MB, 1); Threads = $g.Threads.Count; Handles = $g.HandleCount; CpuSec = [math]::Round($g.TotalProcessorTime.TotalSeconds, 3) } }
}
$samples | Format-Table | Out-String | Write-Host
if ($samples.Count -gt 0) {
    $lastSample = $samples[-1]
    Check 'guardian working set under 30 MB' ($lastSample.WorkingSetMB -lt 30) "$($lastSample.WorkingSetMB) MB"
    Check 'guardian uses (almost) no CPU' ($lastSample.CpuSec -lt 1.0) "$($lastSample.CpuSec) s"
}
Tree-Kill $owner.Pid
Start-Sleep -Seconds 1
Stop-Quietly $gpid

# ------------------------------------------------------------------------------------------------ summary
Write-Host ''
Write-Host '== Summary'
$report | ForEach-Object { Write-Host ("  {0,-15} rescue {1,5} ms   state file gone {2,5} ms   guardian exit {3,5} ms" -f $_.Mode, $_.RescueMs, $_.StateFileGoneMs, $_.GuardianExitMs) }
$report | Where-Object { $_.Memory } | ForEach-Object { Write-Host ("  {0,-15} WS {1} MB  private {2} MB  paged {3} MB  threads {4}  handles {5}  modules {6}  cpu {7}s" -f $_.Mode, $_.Memory.WorkingSetMB, $_.Memory.PrivateMB, $_.Memory.PagedMB, $_.Memory.Threads, $_.Memory.Handles, $_.Memory.Modules, $_.Memory.CpuSec) }
if ($t8) { Write-Host ("  T7 (idle, tracked 3.5 s) WS {0} MB private {1} MB threads {2}" -f $t8.WorkingSetMB, $t8.PrivateMB, $t8.Threads) }
foreach ($id in $script:started) { Stop-Quietly $id }
Remove-Item Env:TRAYLOOM_DATA -ErrorAction SilentlyContinue
Remove-Item Env:TRAYLOOM_GUARDIAN_IDLE_MS -ErrorAction SilentlyContinue
Remove-Item Env:TRAYLOOM_GUARDIAN_PROCESSES -ErrorAction SilentlyContinue
if ($script:failures -eq 0) { Write-Host 'ALL PASSED'; exit 0 } else { Write-Host "$($script:failures) FAILED" -ForegroundColor Red; exit 1 }
