# The Windows report script

## Why this script exists

The developer wrote MM2 Lobby on a Mac. The test suite there holds 365 tests,
and not one of them can read a Windows registry, call `netsh`, ask DISM, or
start the published program. Those answers exist only on Windows.

This script runs those steps on your Windows computer and writes one report
file. You paste the report back, and the developer learns which assumptions
hold and which ones fail.

## How to run it

Warning: an administrator window gives a false answer to the most important
question in this report. Read "Run it as a standard user" below before step 3.

1. Install the .NET 10 SDK from <https://dotnet.microsoft.com/download>.
2. Copy this repository to the Windows computer.
3. Open a normal PowerShell window.
4. Go to the folder of the repository.
5. Run this one command:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows-check.ps1
```

Windows refuses to run a script file until you allow it. The
`-ExecutionPolicy Bypass` part allows this one run. It changes no setting on
your computer.

The script prints its work and writes the report to your Desktop as
`mm2-lobby-report.txt`. Open that file, copy everything, and paste it back.

## Run it as a standard user

Do not run this script as an administrator. The most serious open question is
whether `zerotier-cli` answers a user who holds no administrator rights. The
app runs as the player, so an elevated run would give a false answer and hide
a defect that makes the app unusable. The script reads its own rights and
tells you to start again if they are too high.

## What the script does, and what it does not do

The script reads. It changes no Windows setting. It adds no firewall rule, it
does not turn DirectPlay on, and it joins no ZeroTier network. The build
writes only into the `bin` and `obj` folders of this repository.

The script runs every step, even after one step fails. One run therefore
gives the whole picture. A missing .NET SDK stops the build and the Windows
questions still get answers.

## Three steps need you

The script cannot answer these alone:

- It asks you to refuse a Windows permission prompt, so it can learn what the
  app sees when a player presses No.
- It starts the program and asks you four questions about the window.
- It asks you to read the DirectPlay box in Windows Features by eye, and to
  press Fix in the app once and count the permission prompts.

Add your answers under the report before you paste it back. To skip the two
steps that need you, add `-SkipInteractive` to the command.

## Warning: Windows blocks the program the first time

Nobody has signed this program, and it is 97 MB. Windows SmartScreen therefore
shows a blue box that says "Windows protected your PC", and it hides the Run
button. Take these steps:

1. Choose **More info**.
2. Choose **Run anyway**.

Tell your friends this before you send them the program. Without the warning,
a friend reads the blue box as a virus report and stops.

## Two tests this script cannot run

A host test and a join test each need two computers on one ZeroTier network.
One player hosts a race, and the other player joins it. The script says so at
the end.

## A note on the firewall commands

The report shows `netsh` commands. Those commands are for the real `netsh`
program. The test suite on the Mac uses a stand-in that accepts a smaller set
of words. Never judge the app by the stand-in, and never run a command from
the test suite on a real computer.
