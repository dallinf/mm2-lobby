# MM2 Lobby — Design

Date: 2026-10-21
Status: Approved for planning
Revision: 2 — peer discovery moved from HTTP polling to UDP broadcast. See
section 4.2 for the reason.

## 1. Purpose

Midtown Madness 2 has no working matchmaking service of its own. The MSN Gaming
Zone closed in 2006. GameRanger replaced it, but GameRanger is one company's
service, it has no status page, and when it fails a private group of friends
cannot race.

MM2 Lobby replaces GameRanger for one known group of friends. It lists who is
online, lets one player claim the host role, and starts the game. It runs no
server of its own, so it has nothing central that can fail.

## 2. Scope

### In scope

- One Windows desktop application.
- Midtown Madness 2 only.
- A fixed, private group of friends who trust each other.
- A room list, a host role, and a game launcher.
- A setup screen that finds and fixes the common blockers.

### Out of scope

The following are deliberate omissions. Do not add them without a new design.

| Omitted | Reason |
|---|---|
| Accounts, a central server, a database | The virtual network already carries who is online. A server would reintroduce the single point of failure this project exists to remove. |
| Text chat and voice chat | The group already uses Discord. |
| Rankings, statistics, player profiles | Nobody asked for them. |
| Support for a second game | Hard-code MM2. Generalise only when a second game arrives. |
| Router setup help, port forwarding | The virtual network removes the need. |
| macOS, Linux, Wine, virtual machines | Every player runs Windows 10 or 11. |
| A player cap | MM2 sets its own cap. The app displays the host's session but never enforces a limit. |
| Protection against a hostile group member | Every member of the ZeroTier network is a trusted friend. The app validates datagrams, but it does not defend against an insider. |

## 3. How MM2 multiplayer works

These facts drive the whole design. Verify each one during the first manual
test.

- MM2 uses DirectPlay. Its connection choices are IPX, TCP/IP, serial cable,
  and modem.
- The TCP/IP path asks the joining player to type the host's IP address. It
  does not rely on LAN broadcast discovery. Broadcast discovery is the part
  that usually breaks across a virtual network, so MM2 avoids the hardest
  problem for us.
- DirectPlay needs TCP 47624 and both TCP and UDP in the range 2300 to 2400.
  Microsoft KB Q240429 lists Midtown Madness 2 version 2.0 among the affected
  titles. A virtual network carries these ports without any router change, but
  the Windows firewall still inspects them on the local machine.
- Windows 10 and Windows 11 ship the DirectPlay component turned off. Each
  player must turn it on once.
- DirectPlay provides a lobby-launch interface. A lobby application can start
  the game and supply the session details, so the player never sees the address
  dialog. This is how GameRanger removes the typing. Phase two uses it.

## 4. Architecture

A single Windows program. No server. No database. Each copy of the app
announces itself on the virtual network and listens for the others.

```
  Your PC                                      Friend's PC
  ┌──────────────────────────┐                 ┌──────────────────────────┐
  │ MM2 Lobby                │                 │ MM2 Lobby                │
  │  ZeroTierClient ─────────┼─ reads local ───┼──── ZeroTierClient       │
  │  PeerTransport (UDP) ◀═══╪══ announcements ═╪═══▶ PeerTransport (UDP)  │
  │  PeerTable               │    every 2 s    │  PeerTable               │
  │  PeerDirectory           │                 │  PeerDirectory           │
  │  LobbyService            │                 │  LobbyService            │
  │  GameLauncher            │                 │  GameLauncher            │
  └───────────┬──────────────┘                 └───────────┬──────────────┘
              │                                            │
  ┌───────────▼──────────────┐                 ┌───────────▼──────────────┐
  │ ZeroTier virtual adapter │◀═══ encrypted ═▶│ ZeroTier virtual adapter │
  └───────────┬──────────────┘   direct, or    └───────────┬──────────────┘
              │                  relayed                   │
  ┌───────────▼──────────────┐                 ┌───────────▼──────────────┐
  │ midtown2.exe (DirectPlay)│                 │ midtown2.exe             │
  └──────────────────────────┘                 └──────────────────────────┘
```

### 4.1 Network layer

Use ZeroTier. The free plan allows 25 nodes and sets no limit on the number of
people. A friend joins by entering one network ID. You approve the machine in
the ZeroTier console. ZeroTier replaces three hard problems that GameRanger
solved by hand: it traverses home routers, it falls back to its own relays when
a direct path fails, and it gives every player a stable private address.

### 4.2 Why peer discovery uses broadcast, not HTTP

Revision 1 of this spec had each app serve an HTTP endpoint and had the others
poll it. That cannot work. A ZeroTier node knows its own assigned address, but
it does not know the addresses the controller gave the other members.
`zerotier-cli listpeers` returns node IDs and path information only. The map
from member to address lives in the ZeroTier Central API, and reading it would
add both an API token and a central service to the path — the exact thing this
project removes.

ZeroTier gives a virtual Ethernet network, and it enables broadcast by default.
So each app sends a small UDP announcement to the subnet broadcast address
every 2 seconds, and the others listen. This needs no address list, no server,
and no token. It also removes the HTTP server and the poller from the design.

ZeroTier's layer-2 behaviour is therefore load-bearing. Tailscale is **not** a
drop-in substitute: it is IP-only, it carries no broadcast, and a Tailscale
build would have to read its coordination server's API to learn the peer list.
Its free plan also allows only 3 people. If you ever switch, expect to replace
peer discovery, not one class.

### 4.3 Technology

- .NET 10, C#. .NET 8 leaves long-term support in November 2026, so the
  project targets .NET 10, which Microsoft supports to November 2028. Every
  API here, including `System.Net.IPNetwork`, arrived in .NET 8 and remains
  in .NET 10.
- `Mm2Lobby.Core` targets `net10.0` and holds every piece of logic. It contains
  no WPF and no Windows-only API. It therefore builds and tests on macOS,
  which is where this project is being designed.
- `Mm2Lobby.Windows` targets `net10.0` and holds the registry, DISM, firewall,
  and process code. It does **not** target `net10.0-windows`, which would make
  it unbuildable on macOS for no gain: `Microsoft.Win32.Registry` compiles on
  any target framework and throws only at run time off Windows. Every Windows
  dependency here sits behind a seam — `IProcessRunner` for DISM and `netsh`,
  `IZeroTierClient` for ZeroTier, an injectable file test for DirectPlay, and an
  injectable registry read for the game path — so the four checks and their
  behaviour test on macOS. Only `SystemProcessRunner`, `SystemProcessLauncher`
  and the registry read itself cannot be verified there, and each is a few
  lines.
- `Mm2Lobby.App` targets `net10.0` and holds the window, built with **Avalonia**
  rather than WPF.
- `xunit` for tests. Every test lives in `Mm2Lobby.Core.Tests`, which targets
  `net10.0`.
- Published as one self-contained executable. A friend downloads one file.

**Why Avalonia and not WPF.** Revision 2 of this spec chose WPF. WPF cannot
build on macOS at all, because its targeting pack is Windows-only — so the
window, which is the largest and least testable part of the application, would
have been written and shipped without anybody ever running it. Avalonia builds
and runs on macOS and ships as an ordinary Windows application, which makes the
riskiest piece of this project verifiable before it reaches a player.

The cost is real and worth stating: Avalonia renders with its own engine, so the
window will not look pixel-identical on Windows to the way it looks in
development. A window that runs and looks slightly different beats a window
nobody could run.

Phase two must call the DirectPlay COM interfaces, and C# calls COM with the
least work. That is why the stack is .NET rather than Go or Rust. Phase two is
therefore Windows-only and will need a `net10.0-windows` target of its own,
confined to that one piece.

## 5. Components

Each component takes its dependencies through an interface, so each one tests
alone.

### 5.1 ZeroTierClient

**What it does.** Reads the local ZeroTier state.

```csharp
public interface IZeroTierClient
{
    Task<ZeroTierStatus?> GetStatusAsync(CancellationToken ct);
    Task<ZeroTierNetwork?> GetNetworkAsync(string networkId, CancellationToken ct);
    Task<IReadOnlyDictionary<string, LinkQuality>> GetLinkQualityAsync(CancellationToken ct);
}
```

**How it works.** It shells out through `IProcessRunner` to three commands and
parses their JSON:

| Command | Gives |
|---|---|
| `zerotier-cli -j info` | this machine's node ID, and whether the service is online |
| `zerotier-cli -j listnetworks` | this machine's assigned address, its prefix length, and the network status |
| `zerotier-cli -j listpeers` | one entry per known node, with its paths |

`LinkQuality` for a node is `Direct` when `listpeers` shows at least one path
that is active and not expired, `Relayed` when it shows no such path, and
`Unknown` when the node does not appear in `listpeers` at all.

**It never throws on bad input.** Malformed or absent output returns `null` or
an empty dictionary. A missing `zerotier-cli` is an expected condition, not a
crash.

### 5.2 PeerTransport and the announcement

**What it does.** Carries announcements between the apps.

```csharp
public interface IPeerTransport : IAsyncDisposable
{
    event Action<byte[], IPAddress>? DatagramReceived;
    Task StartAsync(int port, CancellationToken ct);
    Task SendAsync(byte[] payload, IPAddress destination, int port, CancellationToken ct);
}
```

**The datagram.** UTF-8 JSON on UDP port 48200, sent to the subnet broadcast
address of the ZeroTier network, every 2 seconds:

```json
{
  "proto": 1,
  "appVersion": "1.0.0",
  "nodeId": "a09acf0233",
  "player": "dallin",
  "address": "10.147.17.42",
  "hosting": true,
  "startedAt": "2026-10-21T18:04:11Z"
}
```

`startedAt` is null when `hosting` is false.

**Binding.** The socket binds to `0.0.0.0` so it receives the directed
broadcast. That means the player's home network can also send to the port, so
`PeerTable` rejects every datagram whose source address falls outside the
ZeroTier subnet. The subnet membership test is the authorisation boundary.

### 5.3 PeerTable

**What it does.** Holds the peers heard recently, and rejects everything else.

```csharp
public sealed class PeerTable
{
    public PeerTable(TimeSpan expiry, Func<DateTimeOffset> clock);
    public bool Accept(ReadOnlySpan<byte> datagram, IPAddress source, IPNetwork allowed);
    public IReadOnlyList<LivePeer> GetLive();
}
```

`Accept` rejects a datagram when any of these holds. Each rule gets a test.

1. The source address falls outside `allowed`.
2. The payload is not valid JSON.
3. `proto` is not 1.
4. The payload's `address` does not equal the source address.
5. `player` is empty, or longer than 32 characters.
6. `nodeId` is empty.

`GetLive` returns the entries heard within `expiry`. The expiry is 6 seconds,
which is three announcement periods, so one lost datagram never drops a
player from the list.

### 5.4 PeerDirectory

**What it does.** Builds the room list from the table and the link quality.

```csharp
public sealed record Peer(
    string NodeId,
    string DisplayName,
    string AppVersion,
    IPAddress? Address,
    bool IsOnline,
    bool AppRunning,
    bool Hosting,
    DateTimeOffset? StartedAt,
    LinkQuality Link);
```

`AppVersion` carries each peer's reported version, and it exists for one reason:
section 7 requires the app to warn when two players run different versions.
Revision 2 of this spec demanded that warning without giving `Peer` a field to
carry it, so no task could have built it. A peer the app has never heard from
carries the empty string, never null.

A peer reaches the list by one of three routes, and the window must show all
three differently:

| Display state | Source | `IsOnline` | `AppRunning` |
|---|---|---|---|
| In the lobby | heard in `PeerTable` | true | true |
| Online, app not running | in `listpeers` as a `LEAF`, not heard | true | false |
| Offline | in the remembered-names list, neither of the above | false | false |

The remembered-names list maps a node ID to the last display name the app
heard from it, and it persists in the configuration file. Without it, a friend
whose app is not running would appear as a bare node ID.

### 5.5 LobbyService

**What it does.** Owns the one refresh cycle for the whole application.

```csharp
public sealed class LobbyService : IAsyncDisposable
{
    public IReadOnlyList<Peer> Peers { get; }
    public bool IsHosting { get; }
    public Peer? CurrentHost { get; }
    public string? MyAddress { get; }
    public Exception? LastCycleFault { get; }
    public event Action<IReadOnlyList<Peer>>? PeersChanged;

    public Task StartAsync(CancellationToken ct);
    public Task RunCycleAsync(CancellationToken ct);
    public void StartHosting();
    public void StopHosting();
}
```

The constructor takes the window's `AppConfig` instance. The service does not
load its own — see section 5.8 for why two copies corrupt the file.

One cycle sends this app's announcement, then rebuilds the peer list, then
raises `PeersChanged`, then saves any name it learned. `StartAsync` runs one
cycle itself and then runs `RunCycleAsync` every 2 seconds. Tests call
`RunCycleAsync` directly, so no test waits on a timer. No other component holds
a timer.

`CurrentHost` never returns this machine, because the peer list excludes it. The
window shows your own hosting through `IsHosting` and `MyAddress`.

`LastCycleFault` holds the exception from the most recent failed cycle, and a
cycle that completes clears it. The app absorbs an exception at three places —
the transport's subscriber, this service's event, and the pump — because a
frozen list with no message is worse than a stale one, and `Mm2Lobby.Core` has
no logger. Without this property no information about a failure would reach
anybody, ever. The window shows it so a player can say what it said.

`StartAsync` may be called once. On any failure the caller disposes the instance
and constructs a new one.

### 5.6 PreflightCheck

**What it does.** Finds and fixes the blockers before a player hits them
inside the game. Most support work lands here, so this screen deserves care.

```csharp
public enum CheckStatus { Pass, Fail, Unavailable }

public sealed record CheckResult(
    string Name, CheckStatus Status, string Message, string? FixDescription)
{
    public bool Blocks { get; init; } = true;
}

public interface ICheck
{
    string Name { get; }
    bool Blocks => true;
    Task<CheckResult> RunAsync(CancellationToken ct);
    Task<CheckResult> FixAsync(CancellationToken ct);
}
```

`PreflightRunner` runs every check even when an earlier one fails, so the
player sees the whole list at once rather than one problem per attempt.

| Check | Blocks | Test | Fix |
|---|---|---|---|
| ZeroTier | yes | `IZeroTierClient` reports the service online and the network status `OK` | Open the download page, or start the service, or show the network ID to join |
| DirectPlay | yes | `C:\Windows\SysWOW64\dplayx.dll` exists | Run `dism /online /enable-feature /featurename:DirectPlay /all`, with an elevation prompt |
| MM2 found | yes | Enumerate the values under `HKLM\SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Midtown Madness 2` and take the first that points at an existing `midtown2.exe`; otherwise use the saved path | Open a file picker and save the path |
| Firewall | yes | `netsh advfirewall firewall show rule name="MM2 Lobby - Midtown Madness 2"` finds the rule | Add inbound and outbound allow rules, with an elevation prompt |
| DirectPlay registration | no | The key `MM2 Lobby - Midtown Madness 2` under `HKLM\SOFTWARE\WOW6432Node\Microsoft\DirectPlay\Applications` holds the application GUID of the group, names this computer's `midtown2.exe`, starts its `File` value with a backslash, and holds no `Launcher` value | Import one registry script with `reg import`, with one elevation prompt. The script writes `Guid`, `Path`, `File`, `CommandLine` and `CurrentDirectory`, and it deletes `Launcher` |
| Compatibility mode | no | The value of `midtown2.exe` under `HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers` holds no legacy operating system token | Remove the operating system tokens and keep every other token. The key belongs to the account of the player, so this fix needs no elevation |

#### The app owns one DirectPlay key, and it touches no other

The key name `MM2 Lobby - Midtown Madness 2` matches the firewall rules, so one
name marks every thing that this app owns. The check reads that key alone, and
the fix writes that key alone.

An installer of the game writes a key of its own. That key holds the
application GUID of the game, and another program such as GameRanger reads it.
A fix that wrote over that key would break the other program, and the player
would never connect the breakage to this app. Two keys that name the same
executable sit beside each other with no harm.

`LobbyApplicationReader.ReadFrom` reads our key and no other key. The launcher
and the setup check both call it, so the two cannot disagree.

**There is no fallback to another key, and there must never be one.** An
earlier draft read the first key whose name holds the word `midtown` when ours
was absent. Walk that case: a friend who installed the game has the installer's
key, holding the GUID of the game, and has not yet pressed Fix. The launcher
would start the game with a GUID that nobody in the group shares, and the game
would open into a session that **looks correct and that no friend can see**.
MM2 reports no error and no code, so no player can report it either. That is
the silent failure this section opens with, built deliberately.

The fallback helps only a player who has not pressed Fix, and that player is
already handled: the row reads `Optional`, one press writes our key, and the
reader finds it from then on. A computer with no key of ours reports `NoKey`,
the app uses the ordinary launch, and the player types the address of the host.
That is what every player has today, so nobody is worse off.

The reader also refuses a key of ours that holds the wrong GUID, and reports
`WrongGuid`. Someone may edit the key by hand. The check treats that as a
failure, and the launcher must not disagree with the check. Both rules live in
the one method that reads the registry, so a session that nobody can see is
**structurally impossible** and not merely unlikely.

#### The application GUID

`LobbyRegistrationCheck.ApplicationGuid` holds
`{42418287-74D9-4C2D-A8D0-1B5D4D811C21}`. A run on two computers on 2026-10-23
proved it: one player hosted, the other joined, and the player who joined typed
no address.

DirectPlay shows a player only the sessions that carry the same application
GUID. The value does not have to be the GUID of the game. It has to be the same
value on every computer of the group, and every player runs this app. The fix
writes this value every time, and the app never generates one.

**Do not change this value.** Every player already holds it. A player who holds
another value meets nobody, and MM2 then reports that it found no host and
gives no reason.

`LobbyApplicationReader.ApplicationGuid` holds the value, and
`LobbyRegistrationCheck.ApplicationGuid` names the same field. The reader
compares the key against it, so the check and the launcher read one source.

The DirectPlay check reads a file rather than asking DISM, because
`dism /online /get-featureinfo` needs administrator rights and the check must
run without them. Verify the file test during the first manual test. If it
proves wrong, fall back to the DISM query and accept the elevation prompt.

#### A check that blocks, and a check that gives advice

`ICheck.Blocks` is true for every check that names a condition MM2 needs.
`PreflightRunner.AllPassed` reads the blocking results only, and the window
gates **Host** and **Join** on that answer.

The last two checks set `Blocks` to false. The app raced before either one
existed: the window shows the address of the host, the player types it into
MM2, and the race happens. Each of these two checks buys the one click start
and nothing else. A block would take the race away from a player who cannot
write the registry, for example on a work computer with no administrator
rights. That is a loss of capability, and this app does not take a capability
away to add a feature.

**The window still owns every row.** It shows the name, the state, the reason
and the **Fix** button of an advisory check beside the others. The row reads
`Optional` and not `Problem`, so the player can see that the race goes on. The
setup panel opens while any check does not pass, blocking or not, so a player
always reads the offer.

The firewall check matters most. When the firewall blocks the game, MM2 fails
silently and reports only that it found no host.

### 5.7 GameLauncher

**Phase one.** It starts `midtown2.exe`, then shows a panel with the host's
address in a large font and the three steps the player must follow. It also
copies the address to the clipboard as a convenience.

> **Caution:** MM2 draws its own dialogs, so Ctrl+V may not paste into the
> address field. Assume the player types the address by hand. A ZeroTier
> address such as `10.147.17.42` is short enough to type. This limitation is
> the main reason phase two exists.

**Phase two.** It implements a DirectPlay lobby client. It creates
`IDirectPlayLobby3`, builds a `DPLCONNECTION` that names the TCP/IP service
provider and carries the host's address, and calls `RunApplication`. The game
then joins with no dialog. Phase two is Windows-only and is the hardest piece
of the project. It ships behind the same user interface, so phase one keeps
working if phase two stalls.

#### The window chooses the path, and the steps follow the path

The launch button takes one of two paths. The window takes the lobby path when
every advisory check passed in the last run and `ILobbyLauncher.CanLaunch`
answers true. In every other case, and after a launch that fails for any
reason, it takes the ordinary path of phase one.

**The steps must follow the path that the window took.** After a lobby launch
the game holds the address already, so the step `Type the host address` would
be false. After an ordinary launch that step is the one the player needs most.
`JoinInstructions` therefore holds four lists, and one method of the window
chooses between them. The window calls that method after each press and on
every refresh, so a list cannot go stale two seconds later.

The window shows the address in both cases. The address is information and not
an instruction: a friend whose own lobby path is shut still needs it.

**The app publishes as `win-x86`.** Windows 11 ships a 32 bit `dplayx.dll`
only, so a 64 bit process reports `WrongProcessBitness` and never reaches
DirectPlay.

**The window holds one session.** `ILobbySession` owns the DirectPlay
interface and the memory that MM2 reads about three seconds after the launch,
so the window holds it for as long as the game may run and disposes it when
the player closes the window. The launch button takes no guard against a
second press, because MM2 stops and the player must start it again without a
new **Host**. The bound therefore sits on the session and not on the button:
each press disposes the session that the window holds, and a session releases
the interface only after the game process ends. The press that follows a crash
finds no game, so the old session closes and one new session takes its place.
A press while the game still runs closes nothing, and the window then takes
the ordinary path.

### 5.8 Configuration

One file at `%APPDATA%\Mm2Lobby\config.json`.

```json
{
  "displayName": "dallin",
  "gamePath": "C:\\Program Files (x86)\\Microsoft Games\\Midtown Madness 2\\midtown2.exe",
  "zeroTierNetworkId": "0123456789abcdef",
  "rememberedNames": { "bb11223344": "chris" }
}
```

`displayName` defaults to the Windows computer name.

**The window owns the configuration object, and it is the only owner.** It loads
one `AppConfig`, passes that instance to `LobbyService`, and hands the same
instance to the preflight checks. The window is therefore where the player
changes the display name: it sets the property, and the next cycle announces the
new name.

Revision 2 said only "The player can change it", which gave the requirement no
owner, and it let `LobbyService` load a second copy of the configuration. Those
two mistakes are the same mistake. With two copies, the sequence below destroys
the player's game path on a two-second timer:

1. The window builds `LobbyService`, whose copy holds an empty `gamePath`.
2. The player uses the preflight fix to choose the game, which writes the path
   to the file.
3. A friend's name arrives, and the lobby saves its stale copy. The path is
   gone.

One object, one owner, passed in.

A display name longer than 32 characters must never be announced: `PeerTable`
rejects such an announcement, so the player would become invisible to every
friend while their own list looked normal, because the app excludes itself from
its own list. The window limits the text box, and `LobbyService` truncates
before it announces.
An empty display name is the same failure: `PeerTable` rejects an announcement
that carries no name. `ConfigStore.Load` puts the computer name in the place of
a blank name at the start, and the window does the same at run time. So a
player who clears the box keeps a name.
A missing or malformed file loads the defaults rather than failing.

**The window also owns the network ID.** Revision 2 named
`zeroTierNetworkId` in the file and gave it no writer, so a fresh install had
no way to reach a lobby: the ZeroTier check failed, the app blocked Host and
Join, and the only advice named a settings screen that does not exist. The
window therefore holds a second box for the ID. The box takes 16 hexadecimal
characters, it removes the spaces at the ends of a pasted ID, and it accepts
both letter cases. A value that breaks the rule does not reach the
configuration, and the advice then names the rule and ZeroTier Central at
`https://my.zerotier.com`. The checks read the ID from the one `AppConfig` on
every run, so **Check again** starts the lobby with the new ID and the player
does not restart the app.

## 6. Data flow

The complete phase-one race:

1. Each player starts the app. Each app begins to announce itself every 2
   seconds and to listen for the others.
2. You click **Host a race**. Your announcement now carries `hosting: true`
   and your ZeroTier address.
3. Within a few seconds every other app shows your name, your address, and
   your link quality.
4. You start MM2 and choose Multiplayer, then TCP/IP, then Host.
5. A friend clicks **Join**. Their app starts MM2 and shows your address.
6. They choose Multiplayer, then TCP/IP, then Join, and enter the address.

## 7. Error handling

These are the failures the group will actually meet.

| Failure | What the app does |
|---|---|
| ZeroTier is not running | `PreflightCheck` blocks the lobby and offers to start the service |
| A friend is online but their app is not running | Show the remembered name and "app not running", which the user must be able to tell apart from "offline" |
| DirectPlay is turned off | `PreflightCheck` blocks and runs the `dism` command |
| The firewall blocks the game | `PreflightCheck` adds the rule before the player ever starts MM2 |
| The DirectPlay key of this app is missing, or it holds another application GUID, or it names another folder, or it holds a `Launcher` value | **The window shows the row and does not block.** The app offers to write its own key with one elevation prompt. It reads and writes that key only, so a key that an installer wrote stays as it is, and the launcher never reads one. Without the key the player still races: the window shows the address and the player types it into MM2. A block would take that race away from a player who holds no administrator rights. The window gates **Host** and **Join** on the blocking checks only |
| MM2 carries a legacy compatibility mode | **The window shows the row and does not block**, for the same reason. The app offers to remove the mode. The fix writes `HKCU`, so Windows asks for no permission. The fix removes the operating system tokens only, because a player can carry other tokens that the game needs |
| The host's link is relayed, not direct | Show the link quality beside the host's name, the way GameRanger shows a ping estimate, so a player understands a slow race |
| App versions disagree | The announcement carries `appVersion`, `PeerTable` stores it, and `Peer.AppVersion` carries it to the window. **The window owns the warning** — it compares each peer's version against its own and marks any mismatch. Revision 2 demanded this warning without giving `Peer` a field to carry it, so no task could have built it |
| A cycle fails for any reason | The pump absorbs it and the next tick tries again, so the list goes stale rather than frozen. `LobbyService.LastCycleFault` holds the exception and the window shows it, because otherwise no information about the failure reaches anybody |
| A stray datagram arrives from the home network | `PeerTable` rejects it on the subnet test |
| `zerotier-cli` is missing or its output changes | `ZeroTierClient` returns null or an empty result and the preflight screen explains it. The app never crashes on unexpected output |
| Two players claim the host role | Both appear as hosting. The group sorts it out in Discord. The app does not arbitrate |
| The lobby launch fails for any reason | **The window starts the game the ordinary way**, shows the address, and shows the three steps. The status line names the game first, then the reason and the step that `LobbyLaunchMessages` gives. A panel holds the trail of the DirectPlay calls, which the player can send to the person who runs the group. No player is worse off than he was before the lobby path existed |
| The lobby launch never opens | The advisory checks did not pass, or `CanLaunch` answers false. **The window says nothing about the lobby path** and starts the game the ordinary way. An advisory row already names its own reason and offers the **Fix**, and a second message would say it twice |
| The player presses the launch button again | The window starts a game every time. It disposes the session that it holds first, and it takes a new session only when the old one closed. So the window holds one session and never more |

## 8. Testing

### Unit and integration tests

Every test lives in `Mm2Lobby.Core.Tests`, targets `net10.0`, and runs on
macOS. No test needs Windows, a network, or a real ZeroTier install.

- **ZeroTierClient.** Parse recorded `zerotier-cli` output. Cover a direct
  peer, a relayed peer, a `PLANET` root that must be excluded, an empty
  network list, a network the machine has not joined, and malformed output.
- **PeerTable.** One test per rejection rule in section 5.3. Plus expiry: a
  peer heard 5 seconds ago is live, one heard 7 seconds ago is not.
- **PeerDirectory.** Assert the three display states, using a fake
  `IZeroTierClient` and a hand-filled `PeerTable`.
- **PeerTransport.** Start two transports on different loopback ports and
  send a unicast datagram between them. Broadcast itself needs a real ZeroTier
  network, so it belongs to the manual test.
- **LobbyService.** Call `RunCycleAsync` directly. Assert the announcement it
  sends, assert `PeersChanged` fires, and assert `CurrentHost` follows
  `StartHosting` and `StopHosting`.
- **PreflightRunner.** Use fake checks. Assert every check runs even when the
  first one fails.
- **ConfigStore.** Round-trip a config through a temporary directory. Assert
  that a missing file and a malformed file both load the defaults.
- **GameLauncher.** Assert the launch plan it builds. It must never start a
  process during a test.

### Manual test

No test can prove MM2 connects. Keep this checklist and run it with one friend
before each release:

1. Broadcast reaches both machines: each lobby lists the other player.
2. The link quality column shows `Direct` or `Relayed`, not `Unknown`.
3. The host starts a TCP/IP session in MM2.
4. The joining player enters the host's address and reaches the lobby screen
   inside MM2.
5. A race starts and finishes.
6. The host leaves, and the joining player sees the lobby return to idle.
7. One player closes the app. The other shows "app not running" within 10
   seconds, not "offline".

## 9. Risks

| Risk | Response |
|---|---|
| ZeroTier's broadcast is disabled on the network, or its multicast recipient limit drops datagrams | Confirm "Enable Broadcast" in the ZeroTier console. The default limit of 32 recipients far exceeds this group. Step 1 of the manual test proves it |
| DirectPlay binds to the wrong network adapter when a machine has several | Verify in the first manual test. If it happens, disable the unused adapters during a race, or move the fix to phase two, where the lobby client names the address |
| The player cannot paste into MM2's address dialog | Expected. Show the address in a large font and plan phase two |
| The DirectPlay file test is wrong | Fall back to `dism /online /get-featureinfo` and accept the elevation prompt |
| ZeroTier changes its command-line output | `ZeroTierClient` isolates the parse. Switch to the local HTTP service API if the CLI proves unstable |
| ZeroTier's own service fails | The same failure class as GameRanger, but ZeroTier's record is far better, and existing direct paths keep working. Self-host the controller later if this ever matters |
| MM2 version mismatch between players | Out of scope for the app. The group agrees on one game version |

## 10. Build order

1. `ZeroTierClient`. This proves the app can read the virtual network.
2. `PeerTable`, `PeerTransport`, and `PeerDirectory`. This gives the room list.
3. `LobbyService`. This gives the host role.
4. `ConfigStore` and `PreflightRunner`. This makes the app usable by a friend
   rather than by you.
5. `GameLauncher`, phase one. The first real race.
6. `GameLauncher`, phase two. One-click join through the DirectPlay lobby
   interface.

Steps 1 to 5 are the deliverable. Step 6 is a separate piece of work and gets
its own plan.

## 11. Sources

- [Midtown Madness 2 FAQ, Racing Madness wiki](https://racingmadness.fandom.com/wiki/Midtown_Madness_2_FAQ)
- [Multiplayer (Midtown Madness), Racing Madness wiki](https://racingmadness.fandom.com/wiki/Multiplayer_(Midtown_Madness))
- [Microsoft KB Q240429, DirectX ports required to play on a network](https://jeffpar.github.io/kbarchive/kb/240/Q240429/)
- [Midtown Madness 2 eXtreme FAQ](https://www.mm2x.com/modules.php?name=FAQ&file=index&myfaq=yes&id_cat=3)
- [GameRanger, Wikipedia](https://en.wikipedia.org/wiki/GameRanger)
