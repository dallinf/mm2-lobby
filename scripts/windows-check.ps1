<#
.SYNOPSIS
    Builds MM2 Lobby on Windows, then reports what this computer tells us.

.DESCRIPTION
    This script closes a gap. The developer wrote MM2 Lobby on a Mac, so no
    test could read a Windows registry, call netsh, or start the published
    program. This script runs those steps on Windows and writes one report
    file. You paste the report back, and the developer learns which
    assumptions hold.

    This script reads. It does not change a Windows setting. It does not add
    a firewall rule, it does not turn DirectPlay on, and it does not join a
    ZeroTier network. The build writes only into the bin and obj folders of
    this repository.

    Every step runs, even after one step fails. One run therefore reports the
    whole picture. A missing .NET SDK stops the build steps and the Windows
    steps still run.

    Warning: do not run this script as an administrator. The most important
    question is whether ZeroTier answers a standard user. An elevated run
    hides that answer.

.PARAMETER SkipInteractive
    Skips the two steps that need you. One step asks you to refuse a Windows
    permission prompt. The other step starts the program.

.PARAMETER ExpectedTests
    The number of tests the developer saw on the Mac. The report names a
    difference. A difference is not a failure, because the suite grows.

.EXAMPLE
    .\scripts\windows-check.ps1
#>

[CmdletBinding()]
param(
    [switch] $SkipInteractive,
    [int]    $ExpectedTests = 365
)

$ErrorActionPreference = 'Continue'

# ---------------------------------------------------------------------------
# The report
# ---------------------------------------------------------------------------

$script:Report = [System.Text.StringBuilder]::new()

function Add-Line {
    param([string] $Text = '')
    [void] $script:Report.AppendLine($Text)
    Write-Host $Text
}

function Add-Section {
    param([string] $Title)
    Add-Line ''
    Add-Line ('=' * 74)
    Add-Line $Title
    Add-Line ('=' * 74)
}

function Add-Result {
    param(
        [ValidateSet('PASS', 'FAIL', 'INFO', 'OPEN', 'SKIP')]
        [string] $Mark,
        [string] $Text
    )
    Add-Line ("[{0}] {1}" -f $Mark.PadRight(4), $Text)
}

# Runs a program and never throws. It returns the exit code, the output, and
# the error text. A missing program gives the code 9009, the code that cmd
# reports for a command it cannot find.
function Invoke-Probe {
    param(
        [string] $CommandLine,
        [int]    $TimeoutSeconds = 60
    )

    $stdout = Join-Path $env:TEMP ("mm2-out-" + [guid]::NewGuid().ToString('N') + ".txt")
    $stderr = Join-Path $env:TEMP ("mm2-err-" + [guid]::NewGuid().ToString('N') + ".txt")

    try {
        $process = Start-Process -FilePath $env:ComSpec `
            -ArgumentList ('/c ' + $CommandLine) `
            -NoNewWindow -PassThru `
            -RedirectStandardOutput $stdout `
            -RedirectStandardError $stderr

        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill() } catch { }
            return [pscustomobject]@{
                ExitCode = -1
                Output   = ''
                Error    = "The command did not finish in $TimeoutSeconds seconds."
                TimedOut = $true
            }
        }

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output   = (Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue)
            Error    = (Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue)
            TimedOut = $false
        }
    }
    catch {
        return [pscustomobject]@{
            ExitCode = -1
            Output   = ''
            Error    = $_.Exception.Message
            TimedOut = $false
        }
    }
    finally {
        Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
    }
}

# Writes the first lines of a command output into the report. A long output
# hides the answer, so this function shows a part and names the whole size.
function Add-Output {
    param(
        [string] $Label,
        [string] $Text,
        [int]    $Lines = 12
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        Add-Line ("    {0}: (empty)" -f $Label)
        return
    }

    $all = $Text -split "`r?`n" | Where-Object { $_.Trim() -ne '' }
    Add-Line ("    {0} ({1} lines):" -f $Label, $all.Count)
    foreach ($line in ($all | Select-Object -First $Lines)) {
        Add-Line ("      " + $line.Trim())
    }
    if ($all.Count -gt $Lines) {
        Add-Line ("      ... {0} more lines" -f ($all.Count - $Lines))
    }
}

# ---------------------------------------------------------------------------
# Start
# ---------------------------------------------------------------------------

Add-Line "MM2 Lobby - Windows report"
Add-Line ("Written {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Add-Line ''
Add-Line 'This script reads. It changes no Windows setting.'
Add-Line 'Paste the whole report file back into the conversation.'

# ---------------------------------------------------------------------------
Add-Section '1. This computer'
# ---------------------------------------------------------------------------

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdmin   = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Add-Line ("Windows:     {0}" -f [Environment]::OSVersion.VersionString)
Add-Line ("Build:       {0}" -f (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue).DisplayVersion)
Add-Line ("Process:     {0}" -f (& { if ([Environment]::Is64BitProcess) { '64 bit' } else { '32 bit' } }))
Add-Line ("PowerShell:  {0}" -f $PSVersionTable.PSVersion)
Add-Line ''

if ($isAdmin) {
    Add-Result 'FAIL' 'This script runs as an administrator.'
    Add-Line '     The ZeroTier answer in section 5 is then worthless, because the app'
    Add-Line '     runs as a standard user. Please close this window. Then open a normal'
    Add-Line '     PowerShell window and run the script again.'
}
else {
    Add-Result 'PASS' 'This script runs as a standard user. That is correct.'
}

# ---------------------------------------------------------------------------
Add-Section '2. The tools this script needs'
# ---------------------------------------------------------------------------

$repoRoot = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { (Get-Location).Path }
$solution = Join-Path $repoRoot 'Mm2Lobby.sln'

if (Test-Path -LiteralPath $solution) {
    Add-Result 'PASS' ("The script found the repository at {0}" -f $repoRoot)
}
else {
    Add-Result 'FAIL' ("The script found no Mm2Lobby.sln at {0}" -f $repoRoot)
    Add-Line '     Put this script in the scripts folder of the repository. Then run it'
    Add-Line '     again. The build steps cannot run without the repository.'
}

$sdkProbe   = Invoke-Probe 'dotnet --list-sdks'
$haveDotnet = $sdkProbe.ExitCode -eq 0
$haveNet10  = $false

if ($haveDotnet) {
    Add-Result 'PASS' 'The dotnet command answers.'
    Add-Output 'The SDKs on this computer' $sdkProbe.Output 20

    $haveNet10 = ($sdkProbe.Output -split "`r?`n" | Where-Object { $_ -match '^\s*10\.' }).Count -gt 0
    if ($haveNet10) {
        Add-Result 'PASS' 'This computer has a .NET 10 SDK. The projects need it.'
    }
    else {
        Add-Result 'FAIL' 'This computer has no .NET 10 SDK.'
        Add-Line '     Install the .NET 10 SDK from https://dotnet.microsoft.com/download'
        Add-Line '     Then run this script again. Section 5 still runs without it.'
    }
}
else {
    Add-Result 'FAIL' 'The dotnet command does not answer.'
    Add-Output 'The error' ($sdkProbe.Output + $sdkProbe.Error)
    Add-Line '     Install the .NET 10 SDK from https://dotnet.microsoft.com/download'
    Add-Line '     Section 5 still runs without it.'
}

$canBuild = $haveNet10 -and (Test-Path -LiteralPath $solution)

# ---------------------------------------------------------------------------
Add-Section '3. The build and the tests'
# ---------------------------------------------------------------------------

if (-not $canBuild) {
    Add-Result 'SKIP' 'The script skipped the build, because section 2 failed.'
}
else {
    Push-Location $repoRoot

    Add-Line 'The script builds the solution. This takes a few minutes the first time.'
    $build = Invoke-Probe 'dotnet build -c Release --no-incremental' 900

    if ($build.ExitCode -eq 0) {
        Add-Result 'PASS' 'The build succeeded.'
    }
    else {
        Add-Result 'FAIL' ("The build failed with the code {0}." -f $build.ExitCode)
    }

    $warnings = @($build.Output -split "`r?`n" | Where-Object { $_ -match ':\s*warning\s+\w+' })
    $errors   = @($build.Output -split "`r?`n" | Where-Object { $_ -match ':\s*error\s+\w+' })

    if ($warnings.Count -eq 0) {
        Add-Result 'PASS' 'The build printed no warning. The Mac build prints none either.'
    }
    else {
        Add-Result 'FAIL' ("The build printed {0} warnings. The Mac build prints none." -f $warnings.Count)
        foreach ($line in ($warnings | Select-Object -First 15)) {
            Add-Line ("      " + $line.Trim())
        }
    }

    if ($errors.Count -gt 0) {
        Add-Line ''
        Add-Line '    The errors:'
        foreach ($line in ($errors | Select-Object -First 20)) {
            Add-Line ("      " + $line.Trim())
        }
    }

    Add-Line ''
    Add-Line 'The script runs the tests.'
    $test = Invoke-Probe 'dotnet test -c Release --no-build' 900

    $summary = @($test.Output -split "`r?`n" | Where-Object { $_ -match 'Passed!|Failed!|Passed:|Failed:|total:' })
    foreach ($line in $summary) { Add-Line ("      " + $line.Trim()) }

    if ($test.ExitCode -eq 0) {
        Add-Result 'PASS' 'Every test passed.'
    }
    else {
        Add-Result 'FAIL' ("The tests failed with the code {0}." -f $test.ExitCode)
        $failed = @($test.Output -split "`r?`n" | Where-Object { $_ -match '^\s*(Failed|Error Message|Assert)' })
        foreach ($line in ($failed | Select-Object -First 25)) {
            Add-Line ("      " + $line.Trim())
        }
    }

    # The count comes from the summary line of the test runner.
    $match = [regex]::Match(($test.Output -join "`n"), 'total:\s*(\d+)')
    if ($match.Success) {
        $count = [int] $match.Groups[1].Value
        if ($count -eq $ExpectedTests) {
            Add-Result 'PASS' ("The run holds {0} tests. The Mac run holds the same number." -f $count)
        }
        else {
            Add-Result 'INFO' ("The run holds {0} tests. The Mac run held {1}. The suite grows, so a difference is not a failure." -f $count, $ExpectedTests)
        }
    }
    else {
        Add-Result 'INFO' 'The script could not read the test count from the output.'
    }

    Pop-Location
}

# ---------------------------------------------------------------------------
Add-Section '4. The published program'
# ---------------------------------------------------------------------------

$publishDir = Join-Path $repoRoot 'src\Mm2Lobby.App\bin\Release\net10.0\win-x64\publish'
$exePath    = Join-Path $publishDir 'Mm2Lobby.App.exe'

if (-not $canBuild) {
    Add-Result 'SKIP' 'The script skipped the publish, because section 2 failed.'
}
else {
    Push-Location $repoRoot

    # The project file sets the runtime under a condition. A publish without
    # the two switches below falls back to the runtime of this computer and
    # says nothing. The switches therefore repeat what the project file sets.
    $command = 'dotnet publish src\Mm2Lobby.App\Mm2Lobby.App.csproj -c Release -r win-x64 --self-contained true'
    Add-Line ("The script runs: {0}" -f $command)
    $publish = Invoke-Probe $command 900

    if ($publish.ExitCode -eq 0) {
        Add-Result 'PASS' 'The publish succeeded.'
    }
    else {
        Add-Result 'FAIL' ("The publish failed with the code {0}." -f $publish.ExitCode)
        Add-Output 'The output' $publish.Output 25
    }

    $publishWarnings = @($publish.Output -split "`r?`n" | Where-Object { $_ -match ':\s*warning\s+\w+' })
    if ($publishWarnings.Count -gt 0) {
        Add-Result 'INFO' ("The publish printed {0} warnings." -f $publishWarnings.Count)
        foreach ($line in ($publishWarnings | Select-Object -First 10)) {
            Add-Line ("      " + $line.Trim())
        }
    }

    Pop-Location
}

if (Test-Path -LiteralPath $exePath) {
    $exe = Get-Item -LiteralPath $exePath
    Add-Result 'PASS' ("The publish folder holds Mm2Lobby.App.exe, {0:N1} MB." -f ($exe.Length / 1MB))

    # A player copies one file. Any other program file beside the exe would
    # therefore break the copy. The pdb files are large and a player needs none.
    $others = Get-ChildItem -LiteralPath $publishDir -File |
        Where-Object { $_.Name -ne 'Mm2Lobby.App.exe' }

    if ($others.Count -eq 0) {
        Add-Result 'PASS' 'No other file sits beside the exe.'
    }
    else {
        $dlls = @($others | Where-Object { $_.Extension -eq '.dll' })
        if ($dlls.Count -eq 0) {
            Add-Result 'PASS' 'No DLL sits beside the exe. One copied file therefore starts.'
        }
        else {
            Add-Result 'FAIL' ("{0} DLL files sit beside the exe. A player who copies one file gets a program that does not start." -f $dlls.Count)
        }

        Add-Line '    The other files in the folder:'
        foreach ($file in ($others | Sort-Object -Property Length -Descending)) {
            Add-Line ("      {0,10:N1} MB  {1}" -f ($file.Length / 1MB), $file.Name)
        }
        Add-Line '    Copy only Mm2Lobby.App.exe to another computer.'
    }
}
else {
    Add-Result 'FAIL' ("The script found no exe at {0}" -f $exePath)
}

# ---------------------------------------------------------------------------
Add-Section '5. The Windows answers the Mac could not give'
# ---------------------------------------------------------------------------

Add-Line 'No test on the Mac could reach a Windows registry, netsh, or DISM.'
Add-Line 'Every answer below is new information.'

# --- 5a ZeroTier ---------------------------------------------------------

Add-Line ''
Add-Line '--- 5a. ZeroTier, the most important question ---'
Add-Line ''
Add-Line 'The app calls zerotier-cli as the player, with no administrator rights.'
Add-Line 'The command needs a token file. If only an administrator can read that'
Add-Line 'file, then every ZeroTier call in the app fails, and the app is unusable.'
Add-Line ''

$zeroTierExe = Join-Path ${env:ProgramFiles(x86)} 'ZeroTier\One\zerotier-cli.bat'
$zeroTierAlt = Join-Path $env:ProgramFiles 'ZeroTier\One\zerotier-cli.bat'

foreach ($path in @($zeroTierExe, $zeroTierAlt)) {
    if (Test-Path -LiteralPath $path) {
        Add-Result 'INFO' ("A ZeroTier command file sits at {0}" -f $path)
    }
}

$onPath = Invoke-Probe 'where zerotier-cli'
if ($onPath.ExitCode -eq 0) {
    Add-Result 'PASS' 'The name zerotier-cli works without a folder.'
    Add-Output 'where zerotier-cli' $onPath.Output 5
}
else {
    Add-Result 'FAIL' 'The name zerotier-cli does not work without a folder.'
    Add-Line '     The app runs zerotier-cli by name. The app therefore needs the name'
    Add-Line '     on the PATH, or the app needs the full path instead.'
}

$authToken = Join-Path $env:ProgramData 'ZeroTier\One\authtoken.secret'
if (Test-Path -LiteralPath $authToken) {
    Add-Result 'INFO' 'The token file exists.'
    try {
        $null = Get-Content -LiteralPath $authToken -TotalCount 1 -ErrorAction Stop
        Add-Result 'PASS' 'This standard user can read the token file.'
    }
    catch {
        Add-Result 'FAIL' 'This standard user cannot read the token file.'
        Add-Line ("     The reason: {0}" -f $_.Exception.Message)
        Add-Line '     This is the serious case. The app must then run the commands another'
        Add-Line '     way, or the app must ask for the rights once and say why.'
    }
}
else {
    Add-Result 'INFO' 'The script found no token file. ZeroTier may not be installed.'
}

# The app reads these three commands. Each one must answer with JSON.
foreach ($argument in @('info', 'listnetworks', 'listpeers')) {
    $probe = Invoke-Probe ("zerotier-cli -j " + $argument) 30
    Add-Line ''
    Add-Line ("    zerotier-cli -j {0}" -f $argument)
    Add-Line ("      exit code: {0}" -f $probe.ExitCode)

    if ($probe.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($probe.Output)) {
        try {
            $null = $probe.Output | ConvertFrom-Json -ErrorAction Stop
            Add-Result 'PASS' ("The command {0} answered with JSON that reads." -f $argument)
        }
        catch {
            Add-Result 'FAIL' ("The command {0} answered, but the answer is not JSON." -f $argument)
            Add-Output 'The answer' $probe.Output 6
        }
    }
    else {
        Add-Result 'FAIL' ("The command {0} failed." -f $argument)
        Add-Output 'The output' $probe.Output 6
        Add-Output 'The error'  $probe.Error 6
    }
}

# --- 5b DirectPlay -------------------------------------------------------

Add-Line ''
Add-Line '--- 5b. DirectPlay ---'
Add-Line ''
Add-Line 'The check reads one file, because DISM needs administrator rights to'
Add-Line 'report a feature. The question is whether that file is the right signal.'
Add-Line ''

$dplay32 = 'C:\Windows\SysWOW64\dplayx.dll'
$dplay64 = 'C:\Windows\System32\dplayx.dll'

foreach ($pair in @(@{ Path = $dplay32; Note = 'The check reads this file.' },
                    @{ Path = $dplay64; Note = 'The check does not read this file.' })) {
    if (Test-Path -LiteralPath $pair.Path) {
        $file = Get-Item -LiteralPath $pair.Path
        Add-Result 'INFO' ("{0} exists, version {1}. {2}" -f $pair.Path, $file.VersionInfo.FileVersion, $pair.Note)
    }
    else {
        Add-Result 'INFO' ("{0} does not exist. {1}" -f $pair.Path, $pair.Note)
    }
}

Add-Line ''
Add-Line '    Please open Windows Features and read the DirectPlay state by eye:'
Add-Line '      1. Press the Windows key.'
Add-Line '      2. Type: Turn Windows features on or off'
Add-Line '      3. Open Legacy Components.'
Add-Line '      4. Read the DirectPlay box.'
Add-Line '    Then tell the developer whether the box agrees with the file above.'
Add-Line '    A file that exists while the box is empty would break the check.'

# --- 5c The game ---------------------------------------------------------

Add-Line ''
Add-Line '--- 5c. The Midtown Madness 2 registry key ---'
Add-Line ''

$gameKey = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Midtown Madness 2'
Add-Line ("    The app reads: {0}" -f $gameKey)

if (Test-Path -LiteralPath $gameKey) {
    Add-Result 'PASS' 'The key exists.'
    $key = Get-Item -LiteralPath $gameKey
    foreach ($name in $key.GetValueNames()) {
        $value = $key.GetValue($name)
        Add-Line ("      {0} = {1}" -f ($name -replace '^$', '(default)'), $value)
    }

    # The app accepts a value that names the file, and a value that names the
    # folder that holds the file.
    $found = $false
    foreach ($name in $key.GetValueNames()) {
        $value = $key.GetValue($name)
        if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) { continue }
        foreach ($candidate in @($value, (Join-Path $value 'midtown2.exe'))) {
            if ((Split-Path -Leaf $candidate) -eq 'midtown2.exe' -and (Test-Path -LiteralPath $candidate)) {
                Add-Result 'PASS' ("The app finds the game at {0}" -f $candidate)
                $found = $true
                break
            }
        }
        if ($found) { break }
    }
    if (-not $found) {
        Add-Result 'FAIL' 'No value under the key names an existing midtown2.exe.'
        Add-Line '     The app then asks the player for the path. Please tell the developer'
        Add-Line '     where midtown2.exe really sits on this computer.'
    }
}
else {
    Add-Result 'FAIL' 'The key does not exist.'
    Add-Line '     The app then asks the player for the path, which still works. Please'
    Add-Line '     tell the developer where midtown2.exe sits, and search the registry'
    Add-Line '     for a better key.'
}

# --- 5d netsh ------------------------------------------------------------

Add-Line ''
Add-Line '--- 5d. The firewall commands ---'
Add-Line ''
Add-Line 'Warning: the commands below are for the real netsh program. The test'
Add-Line 'suite on the Mac uses a stand-in that accepts a smaller set of words.'
Add-Line 'Never judge the app by the stand-in, and never run a command from the'
Add-Line 'test suite here.'
Add-Line ''
Add-Line 'The app reads the rules with no administrator rights. This section runs'
Add-Line 'only the read, so it changes nothing.'
Add-Line ''

$inboundRule  = 'MM2 Lobby - Midtown Madness 2 (inbound)'
$outboundRule = 'MM2 Lobby - Midtown Madness 2 (outbound)'
$absentRule   = 'MM2 Lobby - a rule that cannot exist 8f2a'

foreach ($rule in @($inboundRule, $outboundRule, $absentRule)) {
    # This string repeats the command line that FirewallCheck builds.
    $line  = 'netsh.exe advfirewall firewall show rule name="' + $rule + '"'
    $probe = Invoke-Probe $line 30

    Add-Line ''
    Add-Line ("    {0}" -f $line)
    Add-Line ("      exit code: {0}" -f $probe.ExitCode)
    Add-Output 'The output' $probe.Output 8

    if ($rule -eq $absentRule) {
        if ($probe.ExitCode -eq 0) {
            Add-Result 'FAIL' 'A missing rule gives the code 0. The app reads the code and would call a missing rule present.'
        }
        else {
            Add-Result 'PASS' ("A missing rule gives the code {0}. The app can therefore read the code alone." -f $probe.ExitCode)
        }
    }
}

Add-Line ''
Add-Line '    These firewall questions stay open, because an answer needs a change:'
Add-Line '      1. Does "set rule" accept the words the app sends?'
Add-Line '      2. Does "set rule" give 0 on a match and another code on no match?'
Add-Line '      3. Does "set rule" change every rule of the same name?'
Add-Line '      4. Does "set rule" leave a disabled rule disabled?'
Add-Line '      5. Does each elevated call raise one permission prompt, so the fix'
Add-Line '         raises two prompts and no more?'
Add-Line '    Press Fix in the app once, count the prompts, and then read the two'
Add-Line '    rules in Windows Defender Firewall. Tell the developer what you saw.'

# --- 5e The permission prompt --------------------------------------------

Add-Line ''
Add-Line '--- 5e. A refused permission prompt ---'
Add-Line ''

if ($SkipInteractive) {
    Add-Result 'SKIP' 'You asked the script to skip this step.'
}
else {
    Add-Line 'The app expects one thing when you refuse a permission prompt: the start'
    Add-Line 'of the program throws, and the app turns that into a clear message.'
    Add-Line ''
    Add-Line 'Windows will now ask for permission. Please choose No.'
    Read-Host '    Press Enter when you are ready'

    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName        = $env:ComSpec
    $info.Arguments       = '/c exit 0'
    $info.UseShellExecute = $true
    $info.Verb            = 'runas'

    try {
        $process = [System.Diagnostics.Process]::Start($info)
        $process.WaitForExit()
        Add-Result 'INFO' ("The start succeeded and gave the code {0}." -f $process.ExitCode)
        Add-Line '     You chose Yes, or this computer asked for nothing. Run the step'
        Add-Line '     again and choose No, so the developer learns the refusal case.'
    }
    catch {
        Add-Result 'PASS' ("The start threw {0}. The app turns this into the code 127." -f $_.Exception.GetType().Name)
        Add-Line ("     The message: {0}" -f $_.Exception.Message)
    }
}

# --- 5f The program ------------------------------------------------------

Add-Line ''
Add-Line '--- 5f. Does the published program start? ---'
Add-Line ''

if ($SkipInteractive) {
    Add-Result 'SKIP' 'You asked the script to skip this step.'
}
elseif (-not (Test-Path -LiteralPath $exePath)) {
    Add-Result 'SKIP' 'The script found no exe to start.'
}
else {
    Add-Line 'The script starts the program. A window should open. No test on the Mac'
    Add-Line 'could prove that, because the one file carries its own runtime and'
    Add-Line 'unpacks itself on the first start.'
    Add-Line ''

    try {
        $app = Start-Process -FilePath $exePath -PassThru -ErrorAction Stop
        Start-Sleep -Seconds 12

        if ($app.HasExited) {
            Add-Result 'FAIL' ("The program stopped on its own with the code {0}." -f $app.ExitCode)
        }
        else {
            Add-Result 'PASS' 'The program still runs after twelve seconds.'
            Add-Line ''
            Add-Line '    Please look at the window and answer these questions:'
            Add-Line '      1. Does the window show a Setup panel with four rows?'
            Add-Line '      2. Does each row carry a short reason?'
            Add-Line '      3. Drag the right edge of the window as far left as it goes.'
            Add-Line '         Do the lobby list and the headers still line up?'
            Add-Line '      4. Does a console window open as well? A console window is a defect.'
            Add-Line ''
            Read-Host '    Close the program, then press Enter'
        }

        if (-not $app.HasExited) { try { $app.CloseMainWindow() | Out-Null } catch { } }
    }
    catch {
        Add-Result 'FAIL' ("The program did not start: {0}" -f $_.Exception.Message)
    }

    # The app writes its settings here. The file proves the save path works.
    $configPath = Join-Path $env:APPDATA 'Mm2Lobby\config.json'
    if (Test-Path -LiteralPath $configPath) {
        Add-Result 'PASS' ("The app wrote its settings to {0}" -f $configPath)
        Add-Output 'The settings' (Get-Content -LiteralPath $configPath -Raw) 20
    }
    else {
        Add-Result 'INFO' ("The script found no settings file at {0}" -f $configPath)
    }
}

# ---------------------------------------------------------------------------
Add-Section '6. What to do next'
# ---------------------------------------------------------------------------

$reportPath = $null
foreach ($folder in @((Join-Path $env:USERPROFILE 'Desktop'), $env:USERPROFILE, $repoRoot)) {
    if ($folder -and (Test-Path -LiteralPath $folder)) {
        $reportPath = Join-Path $folder 'mm2-lobby-report.txt'
        break
    }
}

Add-Line 'Please send the whole report file back to the developer.'
Add-Line 'Please also answer the questions in sections 5b, 5d, and 5f by hand.'
Add-Line ''
Add-Line 'Two tests need two computers on one ZeroTier network. This script cannot'
Add-Line 'run them. One player hosts a race, and the other player joins it.'

try {
    $script:Report.ToString() | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host ''
    Write-Host ('The report file: ' + $reportPath) -ForegroundColor Green
    Write-Host 'Open that file, copy everything, and paste it into the conversation.'
}
catch {
    Write-Host ''
    Write-Host ('The script could not write the report file: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Copy the text above from this window instead.'
}
