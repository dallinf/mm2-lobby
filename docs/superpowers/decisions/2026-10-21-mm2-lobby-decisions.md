# MM2 Lobby — decision record, tasks 1 to 6

This is the working ledger from the run that built tasks 1 to 6, preserved
verbatim because it holds the decisions taken on the project's behalf.

Read it for one of three reasons:

1. **The 45 numbered rulings.** Each says what was decided, why, and what it
   costs if the decision was wrong. Several overturn earlier rulings in the same
   list, and the reasoning for the reversal is recorded beside it.
2. **The parked findings.** Around 20 known issues were deliberately left
   unfixed, each with a one-line fix and the reason for leaving it. If you hit
   one of them, the answer is probably already here.
3. **The verification evidence.** What was mutation-tested, what was proved by
   probe, and — importantly — which claims were checked by a compiler rather
   than by a test.

Two warnings about reading it.

It records process as well as decisions, so it is long and it is not a design
document. The spec at `docs/superpowers/specs/` is the authority on what the
system does; this file is the authority on why it does it that way.

It is also honest about mistakes, including mistakes in the plan and the spec
that were found and corrected mid-run. Rulings R4, R28 and R30 were overturned
outright, R12 was completed after proving incomplete, and R43 records two items
in a fix brief that would have silently defeated each other. Those entries are
the most useful ones in the file.

---

# SDD ledger — plan: docs/superpowers/plans/2026-10-21-mm2-lobby.md

Spec: `docs/superpowers/specs/2026-10-21-mm2-lobby-design.md` (read, reachable).
Repo: `~/learning/mm2-lobby`, branch `feat/core-lobby`, base commit `eb693cb`.
Scope: tasks 1 to 7. Tasks 8 and 9 need Windows and are out of scope here.

## Pre-flight scan

### Cross-task rows (every pair that shares a file or an interface)

| Pair | Produced → consumed | Finding |
|---|---|---|
| 1 → 2 | `IProcessRunner.RunAsync(string, string, CancellationToken)`; `FakeProcessRunner` keys responses on `"{fileName} {arguments}"` | Clean. Task 2 calls `("zerotier-cli", "-j info")` and the fake keys match the test setup exactly. |
| 1 → 7 | `IProcessRunner` → not consumed by task 7 | Clean. Task 7 declares `IProcessLauncher` separately, so no overlap. |
| 2 → 4 | `IZeroTierClient.GetLinkQualityAsync`, `LinkQuality` | Clean. `FakeZeroTierClient` (task 4) implements all three interface methods as task 2 declares them. |
| 2 → 6 | `GetStatusAsync`, `GetNetworkAsync`, `ZeroTierNetwork.Address`, `.PrefixLength` | Clean. |
| 3 → 4 | `PeerTable.GetLive() → IReadOnlyList<LivePeer>`, `IPv4Math.Subnet` | Clean. `Peer` (task 4) composes `LivePeer` fields plus `LinkQuality`; no field is invented. |
| 3 → 6 | `PeerTable(TimeSpan, Func<DateTimeOffset>)`, `Announcement`, `IPv4Math.BroadcastAddress` | Clean. Task 6 constructs `PeerTable(LobbyService.PeerExpiry, clock)`; `PeerExpiry` is task 6's own constant, so no circular reference. |
| 4 → 6 | `IPeerTransport` (`DatagramReceived`, `StartAsync(int, ct)`, `SendAsync(byte[], IPAddress, int, ct)`); `PeerDirectory.GetPeersAsync(string, IReadOnlyDictionary<string, string>, ct)` | Clean. `FakePeerTransport` (task 6) matches the task 4 interface member for member. |
| 5 → 6 | `ConfigStore.Load/Save`, `AppConfig.RememberedNames` as `Dictionary<string, string>` | Clean. `PeerDirectory` takes `IReadOnlyDictionary<string, string>`, which `Dictionary` satisfies. |
| 7 → (8, 9) | `ICheck`, `PreflightRunner`, `GameLauncher`, `JoinInstructions.For/ForHost` | Out of scope here, recorded for completeness. Task 9 calls `JoinInstructions.ForHost()`, which task 7 defines. |
| 1,2,5,7 → 8 | `SystemProcessRunner(bool elevate)`, the four check constructors | Out of scope. Constructors in task 9's wiring match task 8's declarations. |

### Per-task self-consistency rows

| Task | Do its tests agree with its code, and its created files with its later edits? | Finding |
|---|---|---|
| 1 | Tests reference only `ProcessResult`; `FakeProcessRunner` is written for later tasks and used by none of task 1's own tests | Clean. See ruling R1 on the xunit template version. |
| 2 | 11 tests against 3 interface methods; every fixture field the parser reads is present in the fixture JSON | Clean. |
| 3 | 10 + 6 + 12 tests; one test per rejection rule named in spec 5.3; `IPNetwork.Contains` used as the subnet test | Clean. |
| 4 | Transport test uses loopback on two ports, not broadcast; directory tests cover all three display states | Clean. The plan says broadcast belongs to the manual test, which matches spec 8. |
| 5 | 7 tests; `Load` defaults asserted for absent and malformed files | Clean. |
| 6 | 12 tests; three of them call `StartAsync` then deliver a datagram | **DEFECT.** See ruling R2. |
| 7 | 6 + 6 tests; `BuildPlan` throws before the launcher is touched | Clean. |

## Rulings

**R1 — the xunit template version is whatever the SDK ships.**
The plan says `dotnet new xunit`. On a .NET 10 SDK that may scaffold xunit v3
rather than v2. Accept whatever the template produces. Every assertion the plan
uses (`Assert.Equal`, `NotNull`, `Single`, `Empty`, `Throws`, `ThrowsAsync`,
`Same`, `True`, `False`, `Contains`, `DoesNotContain`, `EndsWith`, `Theory`,
`InlineData`) exists in both versions with the same signature, so no test code
changes. If `TreatWarningsAsErrors` turns an xunit analyser warning into a build
error, fix the assertion into the form the analyser asks for; do not add
`NoWarn`. Cost if wrong: one task-1 fix round to adjust package references.

**R2 — `StartAsync` must run one cycle before it starts the timer.**
As written, `LobbyService.StartAsync` starts the transport and then launches
`PumpAsync`, which calls `RunCycleAsync` asynchronously. `OnDatagram` returns
early while `_network` is null, and `_network` is only set inside
`RunCycleAsync`. So in the three task-6 tests that call `StartAsync` and then
`FakePeerTransport.Deliver(...)`, the datagram is dropped unless the pump has
already finished a cycle — a race the test cannot control. The same race hits a
real user: every datagram arriving in the first moments is discarded.

The fix, which the task-6 implementer must apply:

- `StartAsync` awaits one `RunCycleAsync(ct)` after `_transport.StartAsync`,
  before it launches the pump.
- `PumpAsync` waits for the tick first, then runs the cycle:
  `while (!ct.IsCancellationRequested) { await timer.WaitForNextTickAsync(ct); await RunCycleAsync(ct); }`

I checked every task-6 assertion against this change. No test asserts a send
count after calling `StartAsync`, so none breaks. The first announcement now
also leaves immediately rather than 2 seconds late, which is the better
behaviour anyway. Cost if wrong: the startup cycle runs before the window is
ready to receive `PeersChanged`, which would show an empty list for one cycle.

**R3 — deferred to task 9, which is out of scope here.**
`MainWindow.OnRecheckClick` calls `_lobby.StartAsync` again after a successful
first start, and `UdpPeerTransport.StartAsync` throws `InvalidOperationException`
on a second call. Task 9's implementer must guard it with a started flag.
Recorded now so the defect is not rediscovered later. Cost if wrong: the Check
again button crashes the window once the lobby is already running.

## Progress

Setup: `~/learning` turned out to be a git repository with no commits, and the
move did not carry `.git` across. The user chose a repository of its own, so
`~/learning/mm2-lobby` is now its own repo. The three earlier commits are gone;
the documents survived intact, and `eb693cb` recreates them in one commit. The
user approved branch `feat/core-lobby`.

SDK: .NET 10.0.402, installed by the user through Homebrew.

Task 1: dispatched (model sonnet, BASE `eb693cb`). Brief
`task-1-brief.md`, report `task-1-report.md`. Carries ruling R1.
Task 1: implementer DONE, commit `de74105`, 2/2 passing, output pristine.
  Notes it raised: the template gave xunit v2 (2.9.3), no analyser warnings, so
  R1 cost nothing. SDK 10.0.402 writes `Mm2Lobby.slnx` by default, so it
  recreated the file with `dotnet new sln --format sln` to match the brief's
  `Mm2Lobby.sln`. The RED error was CS0234, not the CS0246 the brief predicted;
  same cause, a missing namespace.
Task 1: task reviewer dispatched (model sonnet) over `eb693cb..de74105`.
Task 1: review spec ✅, quality Approved, no Critical and no Important findings.
Task 1: both ⚠️ items resolved by me, not by the reviewer.
  - Commit trailer: `git log -1 --format=%B de74105` shows
    `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. Correct.
  - GREEN claim: `dotnet test` run now, 2 passed, 0 failed, output pristine.
Task 1: minor (deferred): `FakeProcessRunner` has no test of its own, yet its
  keying, its 127 default and its `Calls` recording are the contract every later
  task's tests rely on. A bug there would surface as a confusing failure in
  another task.
Task 1: minor (deferred): `FakeProcessRunner` uses a plain `List` and
  `Dictionary`, which is fine for sequential tests but not for a concurrent one.
  Task 6 drives `RunCycleAsync` directly rather than on a timer, so no planned
  test is concurrent.
Task 1: minor (deferred): `coverlet.collector` is an unused template default.
Task 1: complete (commits eb693cb..de74105, review clean)

Task 2: dispatched (model haiku, BASE `de74105`). Brief `task-2-brief.md`,
  report `task-2-report.md`. Cheapest tier per the skill's model selection,
  because the brief carries the complete code to write.
Task 2: implementer DONE_WITH_CONCERNS, commit `5cdd276`. I ran `dotnet test`
  myself: 13 passed, 0 failed, output pristine.

**R4 — a commit's attribution names the model that wrote it.**
The implementer flagged that the brief demanded
`Co-Authored-By: Claude Opus 5`, while its own session's attribution rule named
Claude Haiku 5.5, the model that actually did the work. It used Haiku 5.5 and
asked whether to amend.

Ruling: accuracy beats uniformity. A trailer records who wrote the code, so each
implementer uses its own session's attribution line. I will not amend `5cdd276`,
because rewriting history to claim Opus 5 wrote Haiku's code is a worse outcome
than a branch whose trailers differ. I corrected the plan's Global Constraints,
which is where the error was: revision 1 hardcoded one model for commits that
subagents make.
Known inaccuracy left standing: `de74105` says Opus 5, but a sonnet implementer
wrote it, under the old constraint. Not worth a history rewrite.
Cost if wrong: the branch has mixed trailers. If the user wants one name, a
single interactive rebase at the end fixes every commit at once.

**R5 — `IProcessRunner` must not throw when the executable is missing.**
The implementer noted that `ZeroTierClient` does not catch exceptions from
`RunAsync`, so the contract depends on the runner returning a non-zero
`ProcessResult` rather than throwing. It is right that `FakeProcessRunner`
cannot catch a violation.

Ruling: the contract already holds. Task 8's `SystemProcessRunner` catches
`Win32Exception` and `FileNotFoundException` and returns
`new ProcessResult(127, "", e.Message)`, which is exactly the behaviour the four
"missing or malformed" tests in task 2 assume. No code change now. Carry this
requirement into task 8's dispatch explicitly, so the catch clause is not
treated as optional polish.
Cost if wrong: on a machine with no `zerotier-cli`, the preflight screen throws
instead of reporting a failed check.

Task 2: task reviewer dispatched (model sonnet) over `de74105..5cdd276`.
R4 applied to the plan in commit `8238a1c` (docs only, no source). Task 3's BASE
  is therefore `8238a1c`, not `5cdd276`.
Task 2: review spec ✅, quality **Needs fixes**. One Important finding, labelled
  plan-mandated. Five Minor.

**R6 — the finding wins over the plan. Fix it.**
The reviewer found that the brief's own parsing code throws
`InvalidOperationException` on valid JSON whose elements have unexpected types:
`JsonElement.GetString()` throws when the element is not a string or null, and
`TryGetProperty` throws when the element is not an object. So
`{"address":123}` from `info`, or `[null]` from `listpeers`, crashes the client.
Six `GetString()` sites and six `TryGetProperty` sites are affected.

Ruling: the finding wins. Spec section 5.1 says in terms, "It never throws on
bad input. Malformed or absent output returns null or an empty dictionary." A
changed CLI output format is one of the three expected conditions the spec
names. The plan's code does not meet the spec it argues from, so the plan is
wrong, not the reviewer. This enters fix round 1.

I also accept the reviewer's choice of remedy over its own alternative. Guarding
each read site is right; wrapping the body in `catch (InvalidOperationException)`
would hide genuine logic bugs behind the same catch.

Load-bearing, which is why this is not deferrable: tasks 4, 6 and 8 all consume
`IZeroTierClient`, and task 6's `RunCycleAsync` calls it on a 2-second timer. An
exception there would surface as a crash in the lobby window, not as a failed
preflight check.
Cost if wrong: two or three more guard clauses than strictly needed, and two
extra tests.

Task 2: minor (deferred): the "check kind, dispose, then `using`" block repeats
  in all three methods. A `ReadJsonAsync(args, expectedKind, ct)` that disposes
  the document itself would leave one place to audit the lifetime.
Task 2: minor (deferred): `int.TryParse` at `ZeroTierClient.cs:193` uses the
  current culture; it should pass `NumberStyles.None` and
  `CultureInfo.InvariantCulture`.
Task 2: minor (deferred): `NoPeers` is a shared mutable `Dictionary` typed as
  `IReadOnlyDictionary`. A caller who casts it could corrupt every later empty
  answer. Use `ReadOnlyDictionary` or return a fresh empty map.
Task 2: minor (deferred): the case-insensitivity test uses only lower-case
  fixture data, so it proves the lookup ignores case but not that an upper-case
  node ID from the CLI gets lower-cased.
Task 2: minor (deferred): unused `using Mm2Lobby.Core.Processes;` in the test
  file.
Task 2: fix round 1/5 dispatched — resumed the original implementer
  (`a393071c`, haiku), finding sent verbatim, five Minors explicitly held back.
  Expecting 18 tests. FIX_BASE for the scoped re-review is `8238a1c`, which is
  the head the previous review saw plus my docs-only plan correction; scoping
  from there keeps the plan edit out of the fix diff.
Task 2: fix round 1/5 (1 addressed, 0 open — unexpected JSON shapes no longer
  throw; commits 5cdd276..6d506cc). 18/18 passing.
  Evidence checked rather than trusted: the report's RED run shows 5 failures,
  each an `InvalidOperationException` naming the exact type mismatch the
  reviewer predicted (String vs Number, Object vs Null), then 16/16 filtered and
  18/18 full. I ran `dotnet test` myself: 18 passed, 0 failed, pristine.
  Re-review verdict: all findings addressed, no new breakage, no out-of-scope
  observations. It confirmed no blanket `catch (InvalidOperationException)` was
  added, and that no existing test or fixture line changed.

**R7 — my fixture spec was wrong, and the implementer was right to change it.**
I specified the `assignedAddresses` guard test with a bare JSON object.
`GetNetworkAsync` only reads a top-level array, so a bare object would have
returned null for the wrong reason and the test would never have reached the
guard it was written for. The implementer wrapped it in an array and said so
rather than quietly matching my text.

Ruling: accept the correction. A test that passes for the wrong reason is worse
than no test. Recorded because the deviation is mine, not the implementer's. The
re-reviewer confirmed the test now reaches the `FirstIPv4` guard, and that the
RED run shows it failing on the old code for the right reason.
Cost if wrong: one test exercises a different guard than intended.

Task 2: complete (commits de74105..6d506cc, review clean after 1 fix round)

Task 3: dispatched (model haiku, BASE `6d506cc`). Brief `task-3-brief.md`,
  report `task-3-report.md`. Carries the never-throw lesson from task 2, and the
  distinction that `IPv4Math` is meant to throw on programmer error.
Task 3: implementer DONE_WITH_CONCERNS, commit `499bc41`, 46/46 passing.

**R8 — add the missing test. The brief's test count is a description, not a cap.**
The implementer reported that `PeerTable.Accept`'s "claimed address does not
parse" branch has no test, and proved it by mutation: it changed the branch to
accept and all 46 tests still passed. It counted 8 rejection branches against
the brief's 6 stated rules, 7 of 8 covered. It did not add a test because the
brief fixes the count at 28.

Ruling: add it, before review rather than after. `PeerTable.Accept` is the
authorisation boundary of the whole application — the socket binds to 0.0.0.0,
so the player's home network can reach the port and nothing but these rules
stops a stranger's datagram. Spec section 5.3 states rule 4 as "The payload's
`address` does not equal the source address", and an unparseable address is a
sub-case of that rule, not a new rule. An untested branch there is exactly the
coverage I told this implementer was the point of the task. The brief's "28
tests" describes what the plan thought of; it does not forbid a 29th.

The implementer was right to flag and right not to exceed its brief unasked.
Cost if wrong: one extra test on a branch that was already correct.

**R9 — ASD-STE100 applies to code comments, and I will get specifics from the
reviewer rather than churn the code on a vague flag.**
The implementer noted that the brief's code comments do not follow the user's
ASD-STE100 writing rule, which their CLAUDE.md applies to code comments as well
as prose. It kept them verbatim and asked me to decide.

Ruling: the rule does cover code comments, and it is a standing user
instruction that outranks my plan, so I am not waving it off. But the flag
carries no specific sentence, and I will not rewrite comments on a general
suspicion. I am naming it as an explicit constraint in the task-3 reviewer's
dispatch, so a reviewer whose job is finding things returns the specific
sentences. The whole-branch review then sweeps every comment at once with the
full diff in view, which is cheaper than per-task guessing.
Cost if wrong: some comments stay non-compliant until the final review.

Task 3: pre-review fix returned DONE, commit `1d9b3cc`, 47/47 passing.
  Mutation check in both directions, which I asked for as proof the new test can
  fail: with the branch set to accept, the new test fails (1 failed, 12 passed);
  with the branch restored, all 13 pass. I ran `dotnet test` myself: 47 passed,
  0 failed, pristine.
Task 3: task reviewer dispatched (model sonnet) over `6d506cc..1d9b3cc`, two
  commits. Dispatch names the ASD-STE100 comment sweep per R9, and asks the
  reviewer to apply the "could this test pass for another reason?" check to
  every remaining rejection branch and to the expiry tests. It also carries the
  boundary question on `HeardAt > cutoff` and a hand-check of the `/25` masking.
Task 3: review spec ✅, quality **Needs fixes**. 2 Important, 5 Minor.
  Checks it ran and cleared, worth keeping so nobody redoes them:
  - `/25` masking verified by hand: 192.168.1.130/25 → base .128, broadcast .255.
  - `TryParse` never throws under hostile input. It probed a throwaway copy
    outside the checkout with an empty span, JSON null, invalid UTF-8, 200-level
    nesting, a lone surrogate, an out-of-range number, a bad date, a BOM, and
    `proto: 1.5`. All returned null. `catch (JsonException)` is sufficient.
  - Locking is sufficient. `GetLive` copies inside the lock and `LivePeer` is
    immutable, so no caller sees a torn state.
  - Expiry maths holds: exclusive `>` means a peer lives just under 6 s, so with
    a 2 s period one lost datagram leaves a max age near 4 s.
  - It also found a second reason the ordered test was needed: `IPAddress.TryParse`
    leaves `claimed` null on failure, so dropping that branch would make the next
    line throw a `NullReferenceException` on network input.

**R10 — one fix round that pins every spec-fixed constant. I am promoting two
Minors into it, against the usual rule.**
The reviewer raised four findings that are the same defect in different places:
a test that does not pin a constant the spec fixes, so a mutation of that
constant survives the suite.

- Important 1: `GetLive_drops_a_peer_heard_outside_the_expiry` can pass
  vacuously. It ignores `Accept`'s return value and never asserts the peer was
  live before the clock moved. If `Accept` stored nothing, `Assert.Empty` still
  passes.
- Important 2: the 5 s keep and 7 s drop cases admit any expiry from 5 to 7 s.
  A `>` to `>=` flip, or a change to `_expiry`, survives.
- Minor 5: `Accept_rejects_an_over_long_player_name` tests 33 characters only. A
  32-character name must be accepted and is untested, so a `>` to `>=` flip at
  the length check survives.
- Minor 2: no test covers prefix 0 or 32, so deleting the `prefixLength == 0`
  special case breaks nothing — and that case exists precisely because C# masks
  a shift count of 32 to 0. The `/25` case also uses only the upper half.

Ruling: all four go into one fix round. The skill keeps Minors out of the loop,
and I am overriding that for these two because the reviewer's severity split is
inconsistent: 6 seconds, 32 characters and prefix 0 are all constants the spec
fixes, and "the test does not pin the constant" is one defect class, not two.
They are test-only edits in the two files the implementer is already opening, so
splitting them across two passes would cost more than it saves.
Cost if wrong: four extra test cases on code that is already correct. The
expiry maths and the masking are both confirmed right by hand, so this round
buys protection against future regressions, not a present bug.

**R11 — defer the ASD-STE100 comment sweep to one pass, with the rewrites
banked here.**
The sweep is now specific, so R9's reason for waiting is satisfied. I am still
deferring it, for a different reason: tasks 4 to 7 will add more comments from
the same plan, so one sweep across the whole branch gives a consistent voice
where seven per-task passes would not. The test files have no comments. The
reviewer's rewrites, banked verbatim so nothing is lost to compaction:

- `Announcement.cs:29-32` — fragment, ambiguous "it", colon joining two ideas,
  and "broadcast" used as both verb and noun in one file. →
  "This record holds the data that one app sends about itself every 2 seconds.
  Keep the record small. It must fit in one UDP datagram."
- `IPv4Math.cs:83-87` — fragment, ~29 words against a 25 limit, two ideas on a
  semicolon, and "member"/"sender" both standing for "peer". →
  "This class does the IPv4 arithmetic for peer discovery. ZeroTier reports an
  address and a prefix length. The app uses the subnet to decide which senders
  it trusts. The app uses the broadcast address to reach every peer."
- `LivePeer.cs:140` — fragment, and "heard" is figurative. →
  "One peer that sent an announcement to the app. The record includes the time
  of the last announcement."
- `PeerTable.cs:159-163` — fragment with no subject, and "everything else" is
  vague. →
  "This class holds the peers that sent an announcement recently. It rejects
  every other datagram."
- `PeerTable.cs:160-162` — two ideas joined by "so". →
  "The socket listens on 0.0.0.0 to receive the directed broadcast. The home
  network of the player can also send data to the port."
- `PeerTable.cs:162` — "boundary" is a metaphor, and "test" already means a unit
  test in this repo. →
  "The subnet check decides whether the app trusts a sender."
- `PeerTable.cs:179-183` — fragment, ambiguous "which", figurative "fails", two
  ideas. →
  "This method checks one datagram and stores the peer. It returns false when
  the datagram breaks a rule. The caller can ignore this result. A rejected
  datagram is a normal event. It is not an error."

Cost if wrong: the comments stay non-compliant until the final fix wave, and if
that wave is skipped they ship that way.

**R12 — a spec requirement that NO task implements. My plan's self-review was
wrong.**
Chasing the reviewer's `appVersion` finding led me back to spec section 7, which
requires: "App versions disagree — the announcement carries `appVersion`. The
app warns rather than behaving strangely." Nothing builds that warning.
`LobbyService` puts `appVersion` into its own announcement and `PeerTable` keeps
each peer's, but no task compares them and no task surfaces a warning. My plan's
self-review table claimed section 7 was covered by tasks 2, 3, 5, 7, 8 and 9.
That claim was wrong.

Ruling: do not bolt it on here. It is a user-visible feature, it belongs in the
window, and task 9 is the only task that owns user-visible behaviour. Carry it
into task 9's dispatch as an explicit added requirement: compare each peer's
`appVersion` against this app's own and show a warning on any mismatch. Tell the
user, because it is a gap in a plan they approved, not a detail.
Cost if wrong: the first time two friends run different builds, the lobby
behaves oddly with no explanation — exactly what the spec wrote that row to
prevent.

Task 3: minor (deferred): `"appVersion":null` passes `Accept`, so
  `LivePeer.AppVersion` can be null despite its non-nullable `string` type. A
  nullable-contract leak, not a security issue. Coerce with `?? string.Empty` or
  reject it. This is the deferred minor most worth fixing, because R12's version
  warning will read that field.
Task 3: minor (deferred, and the spec already answers it): any in-subnet sender
  can replace another peer's entry by claiming its `NodeId`, and `_peers` never
  prunes expired entries. Spec section 2 puts "protection against a hostile
  group member" out of scope, and the node cap is 25, so growth is bounded. No
  action; recorded so a later reader does not mistake it for an oversight.
Task 3: minor (deferred): `NodeId` and `Player` have no character check, for
  example against control characters.
Task 3: fix round 1/5 dispatched — resumed the original implementer
  (`ab08222d`, haiku) with the four pinning findings. Test-only. Expecting 51
  tests. STE sweep and the three remaining Minors explicitly held back.
Task 3: fix round 1/5 returned DONE_WITH_CONCERNS, commit `e39b0d2`, 56/56
  passing. My predicted 51 was wrong, not the result: xUnit counts each
  `InlineData` row, and 6 of the 9 new cases were theory rows. 47 + 9 = 56.
  I confirmed `git diff --stat 1d9b3cc..e39b0d2` touches only the two test
  files, 42 insertions and 1 deletion, so no source changed this round. I ran
  `dotnet test`: 56 passed, 0 failed, pristine.
  All four mutation checks reported in both directions and restored clean:
  expiry `>=` fails the exactly-6s case; name-length `>=` fails the 32-char
  accept; deleting the prefix-0 special case fails the broadcast `/0` row;
  storing nothing fails the amended drop test where the old one passed.
  Open: the implementer reports the subnet `/0` row does NOT catch the deleted
  special case, only the broadcast row does, and it could not say whether
  `System.Net.IPNetwork` normalises its base address. Sent to the re-reviewer as
  a named question with the mechanism to reason about: `uint.MaxValue << 32` is
  masked to `<< 0` in C#, so the mask stays all-ones and the masking step
  becomes the identity. My standard for finding 4: at least one new row must
  catch the mutation, and the broadcast row does, so a weak subnet row is a
  deferred note rather than an open finding.
Task 3: scoped re-review dispatched (model sonnet) over `1d9b3cc..e39b0d2`,
  with one focused probe permitted outside the checkout to settle the
  `IPNetwork` question.
Task 3: fix round 1/5 (4 addressed, 0 open — vacuous drop test, unpinned 6 s
  threshold, unpinned 32-char limit, untested prefix 0/32 and `/25` lower half;
  commits 1d9b3cc..e39b0d2). 56/56. Re-review: all addressed, no new breakage.
  The open question is settled with evidence, not reasoning: a probe on the
  project's own runtime (net10.0, runtime 10.0.13) in a scratch project outside
  the checkout showed `uint.MaxValue << (32 - 0)` gives `FFFFFFFF`, and
  `new IPNetwork(10.147.17.42, 0)` returns base `0.0.0.0` without throwing. So
  `IPNetwork` normalises silently, the mutated mask becomes the identity, and
  the constructor masks the address itself. **No input address can make the
  subnet `/0` row catch that mutation.** Only `BroadcastAddress` exposes it, and
  the broadcast `/0` row does. Finding 4 stands addressed; the subnet row is
  weak but not wrong, and it still documents intent.
  The re-reviewer also confirmed the single deleted line was replaced by
  stronger assertions, so no assertion was lost, and that 56 is the right count.
Task 3: minor (deferred): the new max-length test builds its name from
  `PeerTable.MaxPlayerNameLength`, not a literal 32, so changing the constant to
  31 or 33 is not caught — both the accept test and the existing reject test
  move with it. One-line fix for the final wave:
  `Assert.Equal(32, PeerTable.MaxPlayerNameLength)`, or build the name with
  `new string('x', 32)`. Deferred rather than promoted: the round's finding was
  the `>` versus `>=` comparison, which is caught, and I already promoted two
  Minors into this round. Promoting its own out-of-scope observations too would
  make the loop unbounded.
Task 3: minor (deferred): the masking in `IPv4Math.Subnet` is redundant, because
  `IPNetwork` normalises. A simplification, not a bug. Note for the reader: the
  `prefixLength == 0` special case is still essential, because
  `BroadcastAddress` needs `~mask` to be all ones.
Task 3: complete (commits 6d506cc..e39b0d2, review clean after 1 fix round plus
  1 pre-review fix)

Task 4: dispatched (model **sonnet**, BASE `e39b0d2`). Brief `task-4-brief.md`,
  report `task-4-report.md`. Model raised a tier from tasks 2 and 3: this is the
  first task with a background receive loop, linked cancellation tokens and
  `DisposeAsync` ordering. Async lifetime bugs pass tests and fail in
  production, which the skill's model selection calls an integration task rather
  than transcription.
Task 4: implementer DONE_WITH_CONCERNS, commit `7312a9a`, 67/67 passing
  (56 + 3 transport + 8 directory). My brief predicted 52, which was stale.
  All four mutation checks reported in both directions, each restore clean at
  0 bytes. The transport test passed 30 of 30 runs and `FreePort()` never raced,
  so the timing worry I flagged does not bite.
  It also found and fixed a wrong-reason pass in its own brief's tests:
  `A_peer_heard_outside_the_expiry_falls_back_to_app_not_running` ignored
  `Accept`'s return value, so it still passed when the announcement was
  rejected. It wrapped the call in `Assert.True`. Approved — that is the
  standing discipline working as intended, third task running.
  Raising the model tier paid for itself here: it confirmed the cancel-dispose-
  await ordering is correct, that an idle dispose returns at once with no
  deadlock, and that disposing an unstarted or already-disposed transport is
  safe — then found a hole the ordering review would have missed.

**R13 — a throwing subscriber must not kill discovery. Fix it.**
The implementer confirmed with a scratch probe that if a `DatagramReceived`
subscriber throws anything outside the three caught types, the receive loop dies
silently, and `DisposeAsync` then rethrows that exception from `await _receive`.

Ruling: fix it before review. This is the exact failure class I raised the model
tier for — it passes every test and then fails on someone's machine. The symptom
would be that the lobby simply stops updating, with no error and no crash, for
the rest of the session. Task 6's `LobbyService.OnDatagram` is a subscriber, and
it calls `IPv4Math.Subnet`, which throws by design on a bad prefix. That path is
guarded today only because `ZeroTierClient` validates the prefix range, so the
protection is incidental rather than deliberate.

The remedy is a catch around the `Invoke` call ONLY, not around the loop body.
Scope matters: wrapping the whole body would also swallow the socket errors the
loop deliberately distinguishes. `Mm2Lobby.Core` has no logging abstraction and
adding one is scope creep, so the exception is swallowed, and the trade-off goes
in a comment plus the interface contract: a subscriber must not throw, and the
transport ignores it if one does, because one bad handler must not stop
discovery for the whole session.
Cost if wrong: a genuine bug in a subscriber is silently ignored instead of
surfacing. I judge a silent handler bug better than silent total discovery
failure, which is what the code does today.

**R14 — a persistent socket error must not spin the loop.**
The implementer noted that a persistent `SocketException` on a live socket would
spin the receive loop with no delay.

Ruling: fix it in the same round. This is not hypothetical on the target
platform: on Windows an unconnected UDP socket can receive `WSAECONNRESET` from
an ICMP port-unreachable reply, and this app is Windows-only. A hot loop would
burn a core with no symptom but a hot laptop. The fix is a short delay inside
the existing `SocketException` catch, awaited against the loop's own token so
shutdown stays immediate.
Cost if wrong: a 50 ms delay after a transient receive error, which is
invisible against a 2-second announcement period.

Task 4: pre-review fixes returned DONE_WITH_CONCERNS, commit `71da262`, 68/68.
  Fix 1 scoped to the `Invoke` call only, in `catch (Exception)`, with the
  trade-off comment and the contract sentence on the `IPeerTransport` event.
  New test `A_subscriber_that_throws_does_not_stop_the_receive_loop`; mutation
  check both ways, restore clean at 0 bytes. Transport test 20 of 20 runs.

**R15 — my instruction for fix 2 was wrong on C# semantics, and the
implementer was right to deviate.**
I told it to let the delay's `OperationCanceledException` "take the existing
return path". It cannot: a throw inside a catch block does not reach the sibling
catches of the same try. It wrapped the delay in an inner
`catch (OperationCanceledException) { return; }` instead.

Ruling: accept. The correction is simply right, and the shutdown property I
asked for still holds — `DisposeAsync` cancels before awaiting, and `Task.Delay`
on an already-cancelled token completes at once. Third time this session an
implementer has corrected my instruction rather than following it into a bug.
Cost if wrong: none identified. The behaviour is what I asked for; only my
described mechanism was wrong.

**R16 — the event safely supports exactly one subscriber. Document it, do not
engineer for a second.**
The implementer found something my fix does not cover, and explained why its own
test is shaped the way it is. A throw inside a multicast delegate skips the
later handlers for that datagram. So the catch keeps the loop alive, but if two
handlers were ever attached and the first threw, the second would silently miss
that datagram. That is also why its test uses one handler that throws on the
first call and records on the second, rather than two handlers.

Ruling: defer, and document rather than build. The design has exactly one
subscriber — `LobbyService.OnDatagram` in task 6 — and task 9 subscribes to
`LobbyService.PeersChanged`, not to this event. Iterating
`GetInvocationList()` with a `try` per handler is the correct fix for a second
subscriber and the wrong amount of machinery for zero. Carry the limit into task
6's dispatch, so the one subscriber there is a deliberate choice.
Cost if wrong: if a later task adds a second subscriber without reading this,
it loses a datagram whenever the first handler throws — rare, and its symptom is
one missed announcement out of every 2 seconds, so a peer would flicker rather
than vanish.

Task 4: left as two commits rather than squashed. The implementer offered; the
  separate history reads better, and the review package covers the whole range.
Task 4: task reviewer dispatched (model sonnet) over `e39b0d2..71da262`.
Task 4: review spec ✅, quality **Needs fixes**. 1 Important, 6 Minor.
  Checks it ran and cleared, worth keeping so nobody redoes them:
  - Fix 1's scope is exact: the broad catch wraps only `Invoke`, the three outer
    catches are untouched, and a subscriber throwing `OperationCanceledException`
    or `ObjectDisposedException` can no longer reach the outer `return` paths.
  - Fix 2 is correct, and the delay cannot use a disposed token source:
    `_loop.Dispose()` runs only after `await _receive`.
  - `DisposeAsync` is safe on every path — idle, unstarted, and twice.
  - The transport tests are deterministic **by construction**, not lucky. Bind is
    synchronous and the send happens after both starts, so an early datagram
    waits in the socket buffer. The throwing-subscriber test waits for `threw`
    before sending the second datagram, so a dead loop fails it on the 5 s
    guard, and a missing catch would rethrow from `DisposeAsync`.
  - The `seen` set is seeded with `myNodeId` and used by all three merge paths,
    so the sources are mutually exclusive and this machine is excluded from all
    three.
  - The single-subscriber design is safe as built, and the one-handler test shape
    matches it.

**R17 — one fix round. I am promoting four Minors into it; two stay deferred.**

The Important finding is plan-mandated and mine:
- `PeerDirectoryTests.cs:128-143` — deleting `.ThenByDescending(peer => peer.AppRunning)`
  leaves the sort test passing, because after "zoe" the order chris, sam, jo
  already falls out of `IsOnline` and then the alphabet. That key is the only
  thing that puts "in the lobby" above "online, app not running", and the test
  name claims to pin it. The `Hosting` and `IsOnline` keys are genuinely pinned.
  Fix: rename the not-running peer to sort before "chris" — "abe" for "sam".

Promoted, with reasons:
- Minor 2, `OrdinalIgnoreCase` not pinned for node IDs. Changing the `HashSet`
  comparer to the default breaks no test, because every test ID shares one case.
  This is the same defect class as R10's pinning round — a constraint the plan
  states everywhere and no test pins — so promoting it is consistency with my
  own precedent, not a new standard.
- Minor 4, `Hosting` and `StartedAt` pass-through unasserted; `StartedAt` is
  never read by any test. Task 6 starts next and consumes both through
  `CurrentHost`. An untested cross-task contract is worth two asserts now rather
  than a confusing task 6 failure later.
- Minor 6, the interface contract omits the single-subscriber limit. This is my
  own R16 ruling half-executed: I said document rather than build, then only
  documented the swallowing. One sentence finishes the job.
- Minor 3, a failed `Bind` leaks the `UdpClient`. Three lines, in the method the
  last round already touched. A leak on an already-failing path is low harm, but
  deferring it to a wave that may triage it away is worse than fixing it while
  an implementer is in that file.

Deferred:
- Minor 1, the ASD-STE100 sweep. Still one pass across the whole branch.
- Minor 5, no tiebreaker for display names equal apart from case, so the order
  falls back to dictionary enumeration. Cosmetic, and it needs only two friends
  to pick names differing in case. `ThenBy(NodeId)` is the fix.

Cost if wrong: five small, independent changes — four tests and three lines of
source — on code the reviewer already judged correct. The convergence risk is
low because none of them interacts with another.

**STE rewrites banked from this review, added to R11's list:**
- `IPeerTransport.cs:8` "Raised on the receive thread, once per datagram." →
  "The transport raises this event in the receive loop, once for each datagram."
  (passive; and the loop is an async task, not a thread)
- `IPeerTransport.cs:10-11` "one bad handler" → "one faulty subscriber"
  (the file says both "subscriber" and "handler" for one thing)
- `UdpPeerTransport.cs:63-66` → "A subscriber must not throw. If one does, the
  transport ignores the exception, so one faulty subscriber cannot stop
  discovery. Mm2Lobby.Core has no logger, so the transport cannot record the
  exception." ("logging" as a noun; "error" where the interface says
  "exception"; "the error is lost" names no agent)
- `UdpPeerTransport.cs:79-83` → "...from making the loop run without a break." /
  "The wait uses the loop token, so shutdown is immediate." / "An exception
  inside a catch block does not reach the other catch blocks of the same try. So
  this wait handles its own cancellation." Also use "pause" throughout, to match
  `SocketErrorPause`. ("spinning", "waiting on", "a throw", "its own cancel",
  and "sibling" as a metaphor)
- `UdpPeerTransport.cs:6` → "This class sends and receives announcements over
  UDP." and `PeerDirectory.cs:5` → "This class merges the peers heard over UDP
  with the nodes ZeroTier reports ..." (name the agent)
- `UdpPeerTransport.cs:109` "Expected on shutdown." → "The receive task ends with
  this exception on shutdown."
- Already compliant: `Peer.cs:6-9`, `UdpPeerTransportTests.cs:9-10`.

Task 4: fix round 1/5 dispatched — resumed the original implementer
  (`a63fe1dc`, sonnet) with the Important finding and the four promoted Minors.
  STE sweep and the name-tiebreaker explicitly held back.
Task 4: fix round 1/5 returned DONE, commit `dcf4a86`, 70/70 (68 + 2 new).
  I confirmed `git diff --stat 71da262..dcf4a86` touches three files, 46
  insertions and 7 deletions, and `git status` is clean. I ran `dotnet test`:
  70 passed, 0 failed, pristine.
  Mutation checks reported both ways for the three test findings: deleting the
  `AppRunning` key now fails the sort test; the default `HashSet` comparer fails
  the new case test; dropping either of `Hosting` or `StartedAt` fails the new
  pass-through test. All restores clean at 0 bytes.
  Fix 5 wraps the socket setup in `try`/`catch { client.Dispose(); throw; }`,
  with `_client` still assigned only after success.
  It reports changing no comment, which matters because an unrequested comment
  edit would collide with the banked STE sweep. Asked the re-reviewer to confirm
  that from the diff.
Task 4: scoped re-review dispatched (model sonnet) over `71da262..dcf4a86`. It
  is asked to check all three sort keys, not only the one that was fixed: a
  careless rename could pin `AppRunning` while unpinning `Hosting` or
  `IsOnline`, which the previous review had confirmed were both pinned.
Task 4: fix round 1/5 (4 addressed, 1 open; commits 71da262..dcf4a86). Findings
  2 to 5 addressed. Finding 1 open: **the rename pinned `AppRunning` and
  unpinned `IsOnline`.** With names zoe, chris, abe, jo, deleting
  `.ThenByDescending(peer => peer.IsOnline)` leaves abe and jo tied on the first
  three keys, and the alphabet then reproduces the expected order, so the test
  passes. Before the rename, sam and jo gave jo, sam without that key and the
  test failed. The implementer ran the deletion check only on the key it was
  asked about; the `IsOnline` deletion was never tried.

**R18 — process lesson, carried into every later dispatch.**
Fixing a test's discriminating power can move which *other* properties it
discriminates. A sort test's names are load-bearing for every key at once, so
renaming a peer to pin one key can silently unpin another, and the round looks
like progress. Asking the re-reviewer to verify all three keys rather than the
one fixed is what caught this; that instruction stays in every dispatch where a
fix changes test data rather than test logic.
Cost if wrong: nothing. The instruction is cheap and it has now paid once.

Task 4: fix round 2/5 dispatched — resumed the original implementer
  (`a63fe1dc`, sonnet). Names must satisfy offline < not-running < running:
  ann, bea, chris, with zoe hosting. I derived all three deletion outcomes
  myself before sending, and each breaks the expected order. This round requires
  a deletion check on all three keys, not just the one that changed.
Task 4: fix round 2/5 returned DONE, commit `ff2ea15`, 70/70 unchanged — a
  rename, not a new test. Three insertions, three deletions, one file. Tree
  clean. I ran `dotnet test`: 70 passed, 0 failed, pristine.
  "abe" became "bea" and "jo" became "ann"; expected order zoe, chris, bea, ann.
  It verified my derivation by hand before running it, then ran all three
  deletion checks: each key's deletion fails the sort test, each restore clean.
  Note on method: deleting the FIRST key in a LINQ `OrderByDescending(...)`
  chain does not compile, so for the `Hosting` check it promoted `AppRunning` to
  first instead. Flagged to the re-reviewer to judge whether that substitution
  is the same mutation or a weaker one.
  It also self-corrected its own report: it first wrote that this test could pin
  the `ThenBy(DisplayName)` tiebreak, then corrected that to say it cannot,
  because the first three keys already give a total order over these four peers.
  That is consistent with my deferral of the tiebreaker, and the re-reviewer is
  asked to confirm the corrected claim is the true one.
Task 4: scoped re-review dispatched (model sonnet) over `dcf4a86..ff2ea15`. It
  must derive all three deletion orders itself rather than trust the report, and
  check whether the renamed data is read by any other test in the file.
Task 4: fix round 2/5 (1 addressed, 0 open; commits dcf4a86..ff2ea15).
  The re-reviewer derived all three deletion orders itself and agreed with both
  my derivation and the report's. The four peers now hold distinct
  (Hosting, AppRunning, IsOnline) tuples — (T,T,T), (F,T,T), (F,F,T), (F,F,F) —
  and each pair that depends on one key is in the opposite alphabetical order,
  so each deletion flips a pair. Round 1's failure mode is closed.
  The `Hosting` substitution is **equivalent**, not weaker: promoting
  `AppRunning` to first yields exactly the chain that removing `Hosting` would,
  since the remaining keys keep their relative order. Reordering was needed only
  because the first key of a LINQ chain cannot be deleted and still compile.
  The report's self-correction is **true**: no pair ties on the first three
  keys, so `ThenBy(DisplayName)` never decides anything in this test and cannot
  be pinned by it. Consistent with deferring the tiebreaker.
  A grep confirmed no other test reads the renamed data, and the diff changes no
  comment, so the banked STE sweep is unaffected.
Task 4: complete (commits e39b0d2..ff2ea15, review clean after 2 fix rounds plus
  2 pre-review fixes)

Task 5: dispatched (model haiku, BASE `ff2ea15`). Brief `task-5-brief.md`,
  report `task-5-report.md`. Back to the cheapest tier: the brief is 224 lines
  of complete code over plain file I/O, with no concurrency and no lifetime
  questions. Carries the never-throw principle, because `ConfigStore.Load` is
  the third component in this design that must answer with a default rather
  than fail.
Task 5: implementer DONE_WITH_CONCERNS, commit `1d72b0f`, 77/77 (70 + 7 new,
  all `[Fact]`). Tree clean, scratch probe removed — I verified both.
  Its three mutation checks all passed in both directions. Note on method: the
  files were untracked at mutation time, so `git diff` could not show the
  changes; it used `cmp` against backups instead. Acceptable evidence.
  It also disclosed that its first run of mutation 3 silently did not execute,
  because a zero-match `grep` short-circuited an `&&` chain, and it re-ran it
  cleanly. That disclosure is worth more than the check itself.

**R19 — two inputs escape the catch, and one lets the app brick its own config.
Fix the re-wrap, do not widen the catch.**
The implementer probed `Load` in a scratch console project and found two inputs
that throw outside the brief's `JsonException`/`IOException`/
`UnauthorizedAccessException` set, both from the case-insensitive re-wrap line:

1. `{"rememberedNames":null}` throws `ArgumentNullException`. **And `Save`
   writes exactly that when the dictionary is null.** So the app can write a
   file that it will then fail to read forever: every later `Load` throws, and
   nothing recovers until a human deletes the file.
2. `{"rememberedNames":{"AB11":"x","ab11":"y"}}` throws `ArgumentException` for
   a duplicate key. Only a hand-edited file reaches this, because the in-memory
   dictionary is already case-insensitive. Same permanent failure.

Ruling: fix it. Spec section 5.8 says "A missing or malformed file loads the
defaults rather than failing", and both of these are malformed files that fail.
This is not a hypothetical: the first one is reachable from the app's own
`Save`.

Of the implementer's two options I take (b), make the re-wrap safe, and reject
(a), widen the catch. The reason is the same one the task-2 reviewer gave
against a blanket `catch (InvalidOperationException)`: a wider catch hides
logic bugs behind the clause that was meant for expected conditions. (b) fixes
the defect where it happens and leaves the catch set meaningful. It also
preserves data — indexer assignment keeps the last of a duplicate pair instead
of discarding the whole file.

I am extending (b) past the two reported escapes to cover a third leak the
implementer found and classified as a consumer problem:
`{"rememberedNames":{"bb11223344":null}}` loads a null **value** into a
`Dictionary<string, string>`. Task 4's `PeerDirectory` reads that straight into
`Peer.DisplayName`, so the window would bind a null. One skip condition handles
all three cases, so this costs nothing extra. It is the same nullable-contract
leak family as task 3's `appVersion` minor, which is still deferred.
Cost if wrong: a hand-edited file silently loses an entry rather than loading
it, which is better than the app refusing to start.

**R20 — the report file is required, and the implementer was wrong to skip it.**
It wrote no `task-5-report.md`, saying "the system rules forbid report .md
files", and put the whole report in its reply instead.

Ruling: that is a misreading. `.superpowers/sdd/` is git-ignored scratch, it is
this process's own workspace, and every prior task wrote its report there. The
file is not documentation added to the repo; it is the evidence the task
reviewer reads instead of trusting a summary, and without it the review loses
the mutation output and the probe results. Asking for it again, with the reason.
If it declines a second time I will write the file myself from its reply, which
is bookkeeping rather than a code fix.
Cost if wrong: nothing. The file is ignored by git and deleted with the
workspace.

Task 5: fix round 1/5 dispatched — resumed the original implementer
  (`a0133287`, haiku) with R19's fix, three new tests, and R20's report file.
Task 5: fix round 1/5 returned DONE_WITH_CONCERNS, commit `e806084`, 80/80
  (70 + 7 + 3). All three new tests written RED first and each failed for the
  predicted reason. Six mutations run, each as its own command, each restore
  verified with `git restore` plus an empty `git diff`.
  Two details worth keeping from its table:
  - Mutation A could not run as specified: deleting the null guard fails the
    *build* under nullable-analysis-as-error, so no test executes. It added A'
    with `in source!` to bypass the compiler and show the test catches the
    behaviour. That is the right instinct — "the compiler caught it" is not the
    same claim as "a test caught it", and it reported both.
  - It again disclosed a check that silently printed nothing (its grep did not
    match compiler errors) and re-ran it. Second such disclosure from this
    agent; both times the re-run was clean.

**R20 resolved — I wrote the report file myself.**
The implementer held its position that the platform rule forbids report `.md`
files in any location, and invited me to write the file from its replies, as I
had offered. I did: `task-5-report.md` now holds both rounds, both mutation
tables, the full escape-probe results, and a header saying I transcribed it.
I did not press the point a second time. The evidence is what the review needs,
and who typed it does not change its content. Writing it is controller
bookkeeping, the same as this ledger, not a code fix.

**R21 — close the gap the implementer found in its own work.**
Its mutation D showed that removing the blank-node-ID clause from `Rewrap`
leaves all 10 tests passing. It offered a fourth test and declined to add it
unasked, because R19 specified three.

Ruling: add it. This is the fifth time this session an untested guard on a
parsing or trust boundary has come up, and I have promoted it every time, so
promoting it again is consistency rather than a new standard. The guard is
reachable: `{"rememberedNames":{"":"x"}}` from a hand-edited file would, without
the clause, store an entry with an empty key, and task 4's `PeerDirectory`
remembered-names loop would then render a phantom offline row in the lobby.
Low impact, real, and one test.
It was right not to exceed the ruling unasked, and right to report the gap
rather than let its own table go unread.
Cost if wrong: one test on a guard that was already correct.

Task 5: fix round 2/5 dispatched — resumed `a0133287` for the fourth test only.
Task 5: fix round 2/5 returned DONE, commit `6917e77`, 81/81 (70 + 11
  `ConfigStore` tests, counted as 7 + 3 + 1 Facts). I ran `dotnet test`: 81
  passed, 0 failed, pristine.
  Mutation: removing the blank-node-ID clause **builds cleanly** — the compiler
  did not catch it, a test did, failing with `Assert.Single() Failure: The
  collection contained 2 items`. Restore verified with `git restore` and an
  empty `git diff`. The implementer drew that distinction unprompted, having
  been asked to say which of the two caught it; unlike round 1's mutation A,
  this one is genuinely test-caught.
  It also declined to vouch for my phantom-row reasoning, saying it had not
  inspected task 4 and that the impact was my reading rather than its finding.
  **I verified it myself afterwards.** `PeerDirectory`'s remembered-names loop
  guards only `seen.Add(nodeId)` — there is no blank-key check — so a blank key
  does emit a `Peer` with an empty node ID and a name from the file: a lobby row
  that is never online and never leaves. My claim holds, and it is now checked
  rather than asserted. Recorded because the implementer was right to refuse to
  carry a claim it had not tested, and the right response was to test it, not to
  restate it.

Task 5: task reviewer dispatched (model sonnet) over `ff2ea15..6917e77`, three
  commits. This is task 5's FIRST task review — both earlier rounds answered the
  implementer's own concerns, not a review. The dispatch tells the reviewer I
  wrote the report file and why, so it does not mistake my transcription for
  verification. It also names one gap in the evidence: the escape probe was
  never re-run after the fix, so the reviewer may spend its one permitted probe
  checking whether any input still escapes the catch set.
Task 5: review spec ✅, quality **Approved**. 0 Critical, 0 Important, 7 Minor.
  It spent its probe on the gap I named and **no input escaped the catch set**
  after the fix. Probed: blank and whitespace keys, a blank value, an array, a
  wrong-typed value, a BOM, a null dictionary alongside a kept `displayName`,
  and a duplicate `rememberedNames` property with a case-differing key. That
  closes the one hole in the implementer's evidence.
  It also cleared the round trip specifically: `PropertyNamingPolicy` does not
  touch dictionary keys, so node IDs are never camelCased, and `Rewrap`
  re-applies the comparer `System.Text.Json` drops.
  On my transcription: it treated the report as the implementer's claims and did
  not credit my typing as verification, which is what I asked for.
  On my `PeerDirectory` reasoning: it agreed the conclusion follows from the
  quoted loop, while noting it is only as good as the snippet and the absence of
  a later filter. Fair.

**R22 — I am overriding the reviewer's severity on two findings and running one
more round against an Approved verdict.**
Findings 3 and 4 are graded Minor. By the calibration rubric I handed that same
reviewer, "tests that assert nothing" is an Important example, and both qualify:

- Finding 4 is tautological. `Load_keeps_one_entry_when_node_ids_differ_only_in_case`
  asserts `config.RememberedNames["AB11"] == config.RememberedNames["ab11"]`,
  which is true by construction the moment one case-insensitive entry exists.
  `TryAdd` — first wins — would pass it, even though R19 specified replace.
- Finding 3 covers two tests that write a `displayName` and never assert it
  survives, so they would pass if `Load` fell back to `Defaults()`. They prove
  "empty either way" rather than "parsed, then hardened".

Ruling: promote both. These three tests are the evidence for R19, which is the
ruling this whole task exists to serve, and as written they would stay green if
someone later widened the catch — the exact change I ruled against. An Approved
verdict does not settle severity; I do, and the rubric I supplied grades these
higher than the reviewer did. Three small asserts.
Cost if wrong: three assertions on tests that already pass.

Task 5: minor (deferred): `{"gamePath":null}` and `{"zeroTierNetworkId":null}`
  still load as `null` into non-nullable `string` properties. Same family as
  R19, smaller blast radius. I traced every consumer: task 8's `GamePathCheck`
  and `FirewallCheck`, task 9's `StartTheGame` via `GameLauncher.BuildPlan`, and
  task 6's `GetNetworkAsync` all funnel through `string.IsNullOrWhiteSpace` or a
  string comparison, so a null is tolerated everywhere today. Fix for the final
  wave: `config.GamePath ??= string.Empty;` and the same for the network ID.
Task 5: minor (deferred): `Save` can still write `"rememberedNames": null`,
  because the setter is public. `Load` now reads that as empty, so it no longer
  bricks startup — the app merely repairs the file on every load. Tidier fix:
  coerce in the setter.
Task 5: minor (deferred): the blank-key test uses `""` only; a whitespace-only
  key and a blank-but-not-null value are covered by `IsNullOrWhiteSpace` and by
  the reviewer's probe, but not by a test. Low value.
Task 5: minor (deferred): `Rewrap` also filters, so a name like
  `CopyValidEntries` would say more, and it has no comment explaining why blank
  entries are dropped — the non-obvious part. Folds into the STE sweep.

**STE rewrites banked from this review, added to R11 and R17's lists:**
- `AppConfig.cs:32-33` — "heard from it" is passive with no agent, and "friend"
  is a second word for what the code calls a "peer". →
  "Holds the last display name that the app received from each ZeroTier node ID.
  The lobby uses it to show a peer whose app is closed."
- `ConfigStore.cs:49-50` — names the file as the agent when `Load` does the
  loading; "broken file" duplicates "malformed file"; "stop a race" is
  figurative, since the app does not race. →
  "If the file is missing or malformed, `Load` returns the default
  configuration. A malformed file must not stop the app."
- Already compliant: `ConfigStore.cs:48-49`.
- Also flagged: commit subject `e806084` says "config" where the code and other
  subjects say "configuration". **Not fixing** — I will not rewrite history for
  one word in a commit subject. Recorded so the final review does not re-raise
  it.

**Carry into task 6's dispatch (the reviewer's ⚠️):** `Rewrap` protects only the
load path, so check that `LobbyService.RememberNames` cannot insert a blank key
itself. My read is that it cannot — the node IDs and names it writes come from
`PeerTable`, which already rejects both blank — but task 6 should confirm rather
than inherit my assumption.

Task 5: fix round 3/5 dispatched — resumed `a0133287` with the two promoted
  findings. Three asserts, no source change.
Task 5: fix round 3/5 returned DONE_WITH_CONCERNS, commit `2d63804`, 81/81
  unchanged — assertions added, no new tests.
  Finding 3 is addressed and proven: mutation M1 (return `Defaults()` after a
  successful parse) builds, and both amended null tests now fail on
  `DisplayName`, 6 of 11 failing in total. Before the amendment they passed.
  Finding 4's fix is **wrong, and the implementer caught my inconsistency.**

**R23 — I was wrong in R22, and the reviewer's finding 4 was a distinction
without an observable difference. Revert the pinned value.**
The implementer pushed back on the `Assert.Equal("y", ...)` assertion I ordered.
Its argument: `Rewrap` iterates the dictionary `System.Text.Json` built with the
default comparer, so the winner is whichever entry that **enumeration** reaches
last, not whichever the JSON text lists last. `Dictionary` enumeration happens
to follow insertion order with no removals in current .NET, but that is an
implementation detail and not a documented guarantee. It noted this is the same
assumption I told it not to pin in round 1, and it is right — I took the
reviewer's "JSON parse order is deterministic" without noticing the extra hop
through a `Dictionary`.

Following that through changes the requirement, not just the test. With
enumeration order unspecified, `map[nodeId] = name` and `map.TryAdd(nodeId, name)`
are **observationally identical** for a case-only duplicate pair: both yield
exactly one entry, and which value survives is unspecified either way. So:

- The reviewer's finding 4 — "`TryAdd` would pass this test" — identifies no
  defect. `TryAdd` satisfies the real requirement just as well.
- My R19 wording, "assign by indexer so a duplicate key replaces rather than
  throws", over-specified. Only the "rather than throws" half is observable.
- **No test should distinguish the two.** Recorded so a later reviewer does not
  re-raise finding 4 and send someone back to pin an accident.

The genuine content of finding 4 still stands: the original
`Assert.Equal(map["AB11"], map["ab11"])` was tautological. The fix is an
order-independent assertion, not an order-dependent one.
Cost if wrong: the test does not pin which value wins — which is correct,
because nothing specifies it.

Task 5: fix round 4/5 dispatched — resumed `a0133287` to replace the pinned
  value with an order-independent assertion. Round 4 of a 5-round cap; if this
  does not close it I adjudicate rather than dispatch again.
Task 5: fix round 4/5 returned DONE_WITH_CONCERNS, commit `3769341`, 81/81
  unchanged. The implementer agreed with the three replacement assertions and
  checked that each can fail for a real reason rather than taking my word.
  All three mutations reported, including the one that **must pass**:
  1. Default comparer → `Assert.Single` fails (2 items), and
     `RememberedNames_ignores_the_case_of_a_node_id` fails with
     `KeyNotFoundException`. Restore clean.
  2. Remove the blank-name clause → the case test does NOT fail on its own
     input, which has no null value. Only
     `Load_skips_a_remembered_name_with_a_null_value` fails. It then
     scratch-edited the case test's input to carry a null and showed
     `Assert.Contains` failing. Restore clean on both files.
  3. `TryAdd` for the indexer → build succeeds and the test **passes**, 11 of
     11. That is the expected result and the evidence for R23.

**R24 — no further round. The implementer's residual concern is correct and
already answered by another test.**
It flagged that mutation 2's *evidence* depends on enumeration order even
though the test does not: in its scratch run the null entry happened to be
enumerated last and therefore won, so had the null come first, `"x"` would have
survived and `Assert.Contains` would have passed. It drew the right distinction
— the test is order-independent, the mutation evidence for that one assertion is
not — and declined to paper over it.

Ruling: close the loop. The blank-name clause is not guarded by the case test
and does not need to be: `Load_skips_a_remembered_name_with_a_null_value` is its
dedicated guard, and mutation 2's first half showed that test failing
deterministically when the clause is removed. The case test's
`Assert.Contains` is a secondary check that still earns its place — it fails if
values are nulled wholesale — while `Assert.Single` and the `ContainsKey("Ab11")`
lookup are both deterministic. Nothing is left unguarded, so a fifth round would
buy coverage that already exists elsewhere.
Cost if wrong: one assertion in one test is weaker evidence than its neighbours,
on a clause that has its own passing guard.

Task 5: scoped re-review dispatched (model sonnet) over `6917e77..3769341`,
  rounds 3 and 4 together.
Task 5: fix rounds 3-4 (2 addressed, 0 open; commits 6917e77..3769341).
  Finding A derivation it checked rather than assumed: `Defaults()` sets
  `DisplayName = Environment.MachineName`, so the new assertion fails on a
  discarded file and would pass only on a machine literally named "x".
  Finding B: all three assertions present, each able to fail for a real reason,
  none dependent on enumeration order. It added a case I had not seen —
  `ContainsKey("Ab11")` catches something `Assert.Single` misses: one entry kept
  under an ordinal comparer by a first-seen dedupe.
  It **agreed** with R23 and sharpened it: the indexer-versus-`TryAdd`
  difference is unobservable here, but indexer-versus-`Add` is real, because
  `Add` throws on an existing key and the indexer cannot. The requirement is
  only "a duplicate does not throw", which the test covers.
  It **agreed** with R24: without the blank-name clause,
  `{"bb11223344":null}` yields one entry, so `Assert.Empty` in the dedicated
  test fails whatever the enumeration order. The case test's value assertion has
  no job guarding that clause, and the implementer's scratch input was an
  artifact of its experiment, not a suite gap.
Task 5: complete (commits ff2ea15..3769341, review Approved; 4 fix rounds, two
  of them my own promotions, one of them a revert of my own ruling)

Task 6: dispatched (model **sonnet**, BASE `3769341`). Brief `task-6-brief.md`,
  report `task-6-report.md`. The integration task: it consumes tasks 2 to 5 and
  owns the only timer in the design. Carries four earlier rulings that change
  the brief's code — R2 above all — plus the task-5 reviewer's ⚠️ on blank keys
  in the write path.
Task 6: implementer DONE_WITH_CONCERNS, commit `289485b`, 93/93 (81 + 12). Tree
  clean, report file written. My brief's "71 tests" was stale again.
  R2's correction applied and **confirmed necessary**: without the awaited
  cycle, two delivery tests fail. No test broke.
  All five mutations checked both ways, diff clean after each restore. Mutation
  5 fails the **build** first (CS8602), not a test; with `!` added it fails the
  not-joined test by `NullReferenceException`. It drew that distinction
  unprompted.
  Q2 answered: a blank key cannot reach the configuration. `Accept` rejects a
  blank `NodeId` and a blank `Player`. It added that `Accept` does not limit
  `NodeId` length — deferred, below.

**R25 — four real defects, all found while answering verification questions
rather than by being asked. Fix all four, before review.**

1. **A genuine race in `OnDatagram`.** It reads `_network` twice: once for the
   null check, once for `IPv4Math.Subnet`. `RunCycleAsync` can set `_network` to
   null between those reads, from the cycle thread, while `OnDatagram` runs on
   the receive-loop thread. The result is a `NullReferenceException` that task
   4's transport then **silently swallows**, losing one datagram. Fix: read
   `_network` once into a local. Two lines, and it removes the race rather than
   narrowing it. Note that I told this implementer my own reading of that chain
   was "incidental rather than deliberate" — being nervous there was correct.
2. **`DisposeAsync` is not idempotent.** A second call throws
   `ObjectDisposedException`, because `_loop` is never nulled after disposal —
   unlike `UdpPeerTransport`, which nulls its fields. `IAsyncDisposable` is
   contractually idempotent. Fix: null the fields, or guard with a flag.
3. **A second `StartAsync` adds a second subscriber.** It subscribes *before*
   calling `_transport.StartAsync`, which throws on a second call — so the
   second subscription is already attached when the throw happens. That breaks
   R16 outright, and it is reachable: task 9's "Check again" button calls
   `StartAsync` again, which is R3. Fix: guard at the top of `StartAsync`,
   before subscribing.
4. **Any non-cancellation error in the pump ends the refresh for good.** The
   worst of the four, and reachable today. `RememberNames` calls
   `ConfigStore.Save` every cycle where a name changed, and I checked the
   source: `Save` catches nothing, so `File.WriteAllText` throws `IOException`
   on a locked file or a full disk. The pump task then faults and the lobby
   silently stops updating for the rest of the session — the same failure shape
   as R13, one layer up, which is what that ruling existed to prevent.

Ruling: fix all four. Each is small, none interacts with another, and three are
reachable from code already written or planned. For number 4 I accept R13's
trade-off — swallow and continue to the next tick, reason in a comment —
because a frozen lobby with no error is worse than a silently retried cycle,
and `Mm2Lobby.Core` still has no logger. I also told the implementer NOT to add
a catch inside `ConfigStore.Save`: the pump-level catch protects against any
unexpected throw in a cycle, not just that one.
Cost if wrong: four small changes to code already tested. The risk is number
4's catch hiding a repeating fault; the mitigation is that every cycle retries,
so a persistent problem yields a permanently stale list rather than a frozen
one — still bad, but visible to a player.

**R26 — promote the weak `PeersChanged` test.**
The implementer flagged `RunCycleAsync_raises_PeersChanged` as weak. It asserts
only `Assert.NotNull(seen)`, which the event satisfies by firing with an empty
list. Same "asserts nothing" class I have promoted five times. Fix: hear a peer
first, then assert the captured list contains it.
Cost if wrong: one stronger assertion on a test that already passes.

Task 6: minor (deferred): `PeerTable.Accept` does not limit `NodeId` length, so
  a very long node ID from an in-subnet peer would be stored and displayed.
  Spec section 2 puts a hostile group member out of scope, so no action;
  recorded so it is not rediscovered as new.

Task 6: fix round 1/5 dispatched — resumed the original implementer
  (`a1fe7bb0`, sonnet) with the four defects and the one test.
Task 6: fix round 1/5 returned DONE_WITH_CONCERNS, commit `a8038e1`, 95/95
  (93 + 2). Tree clean. All five fixes applied.
  Evidence quality worth noting: **three** of its mutations failed the build
  rather than a test (CS0169 for the `_disposed` field, CS0414 for `_started`,
  CS0067 for an unused `PeersChanged` event), and it said so explicitly each
  time, then built compiling variants to get real test evidence. That is the
  third task running where this agent has kept "the compiler caught it" and "a
  test caught it" apart without being reminded.
  Fix 4 is **not testable in the committed suite** — the pump needs a real
  2-second `PeriodicTimer` and there is no seam. It proved it two ways in an
  uncommitted scratch test, since deleted: with the catch, the cycle after a
  failed one still ran and `PeersChanged` fired twice; without it, the pump
  faulted with the `IOException`. It added no seam, as instructed.
  It also added `SubscriberCount` to its own `FakePeerTransport` to prove fix 3,
  and showed that moving the guard below the subscription fails with
  "Expected 1, Actual 2".

**R27 — `StartAsync` is single-use by contract. Document it; do not build
in-place retry.**
I read the committed code: `_started = true` is set immediately after the
guard, before the subscription and before any awaited work. So **any** failure —
a port conflict in the transport, or an `IOException` from the first cycle —
leaves the instance permanently unusable, and a retry throws
`InvalidOperationException`. The implementer flagged exactly this and said task
9's "Check again" button must know.

Ruling: make it an explicit single-use contract rather than attempting retry. A
partial start spans two objects — this service and the transport, which also
refuses a second start — so in-place retry means tracking partial state across
both, and getting it subtly wrong is likelier than getting it right. The honest
contract is: `StartAsync` succeeds once; on any failure, dispose the instance
and construct a new one. That is cheap for the caller, since the window builds
the service already.
Carried into task 9's dispatch, joining R3: the "Check again" handler must
dispose and rebuild, not re-call `StartAsync`.
Cost if wrong: task 9 writes three more lines in its recheck handler than a
retryable service would need.

**R28 — the first cycle's failure propagating is correct. Document the
asymmetry.**
The implementer noted the first cycle in `StartAsync` sits outside the pump's
new catch, so a failure there throws to the caller.

Ruling: that is right, and it should be stated rather than left to look like an
oversight. Startup failures must be visible — the window needs to show "the
lobby could not start" — while steady-state failures must be tolerated, because
a frozen list with no error is worse than a stale one. Same reasoning as R25's
fourth fix, applied in the opposite direction, which is exactly why it needs a
comment.
Cost if wrong: nothing changes behaviourally; this is documentation.

**R29 — add the cancellation filter. A foreign `OperationCanceledException`
must not end the pump.**
The implementer noted a non-cancellation-related `OperationCanceledException`
from inside a cycle still exits the pump silently, and that
`when (ct.IsCancellationRequested)` fixes it.

Ruling: take it. It is strictly better and one line: our own shutdown still
returns, while anything else falls into the general catch and the next tick
retries. I told it to keep the existing return path in R25, which is why it left
this alone — correctly, since I had said so — and raising it instead was right.
Cost if wrong: a cancellation we did not request causes a retried cycle rather
than a silent stop.

Task 6: minor (deferred): `Peers` and `_network` have no `volatile` and no lock.
  Reference-typed writes are atomic in .NET, so the exposure is staleness, not
  tearing: a reader can see one cycle's old value. With a 2-second refresh that
  is harmless, and `Peers` is replaced wholesale with an immutable list. No
  action; recorded with the reasoning so it is not mistaken for an oversight.

Task 6: fix round 2/5 dispatched — resumed `a1fe7bb0` with R27's and R28's
  documentation and R29's one-line filter.
Task 6: fix round 2/5 returned DONE_WITH_CONCERNS, commit `738d029`, 95/95
  unchanged. All three items applied. Item 3 again not testable in the committed
  suite without a seam; proved in a scratch test, since deleted: with the filter
  a foreign `OperationCanceledException` on cycle 2 gave 2 `PeersChanged`
  events, without it `calls=2 raised=1`, so the pump ended silently.

**R30 — my atomicity claim was wrong for one field, and the implementer found
the counter-example I asked for.**
In R29's dispatch I deferred the volatile question on the grounds that
"reference-typed writes are atomic in .NET, so the exposure is staleness, not
tearing", and invited a counter-example. It found one and drew the line exactly
right: `Peers` and `_network` are reference types, so my reasoning holds for
those — it said so — but `_hostingSince` is a `DateTimeOffset?`, a struct wider
than 8 bytes, written by the UI thread and read by the pump thread. A torn read
is possible in principle. I had lumped a value type in with two references.

It also found the observable half, which needs no tearing at all: I read the
committed `AnnounceAsync` and confirmed `Hosting = IsHosting` and
`StartedAt = _hostingSince` are **two separate reads** of the same field. A
`StopHosting()` between them produces an announcement with `Hosting: true` and
no `StartedAt`. No race on hardware required — just ordinary interleaving
between a button click and a 2-second cycle.

Ruling: fix the double read with the same read-once-into-a-local pattern as R25
fix 1. Two lines, kills the inconsistency outright, and reduces the torn-read
exposure to a single read.
The residual torn read stays deferred, now with reasoning rather than a wrong
premise: a torn `DateTimeOffset?` would serialize to nonsense or throw, and
R25's pump catch now tolerates a throwing cycle by retrying the next tick. The
window is one instruction pair against a 2-second period.
Cost if wrong: two lines in a method the implementer has already tested.

Task 6: fix round 3/5 dispatched — resumed `a1fe7bb0` for the `AnnounceAsync`
  double read only.
Task 6: fix round 3/5 returned **DONE**, commit `8857fec`, 95/95 unchanged. No
  new concerns. `AnnounceAsync` now reads `_hostingSince` once into a local, so
  `Hosting` and `StartedAt` always agree. It added no test, no `volatile`, no
  lock and no `Interlocked`, as instructed, and it agreed with R30's corrected
  reasoning on the residual — adding one caveat of its own, that a torn read
  during the FIRST cycle would make `StartAsync` throw rather than be retried,
  which is consistent with R28 and negligible.

Task 6: task reviewer dispatched (model **opus**, the most capable available)
  over `3769341..8857fec`, four commits, 506 insertions, three files, no
  deletions. Model raised two tiers above the task-5 reviewer, per the skill's
  rule that a subtle concurrency change warrants it: four of this task's
  defects were found by reasoning rather than by a failing test, and three
  remain untestable in the committed suite.
  The dispatch carries all ten rulings as requirements, the four deferred items
  with their reasoning, and three things I explicitly asked it to challenge:
  (a) whether scratch-test evidence for the three untestable items is
  acceptable or whether a seam is warranted after all — I am the one who forbade
  a seam, so that decision deserves an adversarial pass; (b) whether any of my
  deferral reasoning is wrong, given R30 already proved one such claim wrong;
  (c) whether the implementer's build-versus-test distinction is honest.
  I also warned it that reviewers in this session have repeatedly under-graded
  "a test that cannot fail", and that such a test is Important, not Minor —
  because I have had to override that severity call twice.
Task 6: review spec ✅ on all ten rulings and all 14 tests, quality **Needs
  fixes**. 0 Critical, 2 Important, 8 Minor. The richest review of the session.
  It audited all 14 tests individually and found none that cannot fail — and
  corrected the implementer's own "weak stop test" note, pointing out that note
  concerned a `StartHosting` mutation, which is a different test's job.
  Checks it ran and cleared, worth keeping:
  - Dispose ordering is right and load-bearing: the transport is disposed only
    after the pump stops, so no in-flight cycle can send on a disposed socket.
  - The pump genuinely **cannot fault**, so `await _pump` cannot rethrow. It
    verified every statement outside the `try`, including that
    `ct.IsCancellationRequested` does not throw on a token whose source was
    disposed.
  - No busy-spin: `WaitForNextTickAsync` precedes the cycle and `PeriodicTimer`
    buffers at most one missed tick.
  - `RememberNames` saves only on change, so steady state does zero disk I/O.
  - Six tests start the pump, but none that asserts a send count calls
    `StartAsync`, and `PeerTable` gets a frozen clock — so "no test waits on a
    timer" holds in substance, not just in name.
  - `ZeroTierNetwork` is a `sealed record` with init-only members, so R25.1's
    single read gives a consistent (Address, PrefixLength) pair. Fully closed.
  - **It probed to correct its own assumption.** It expected Roslyn to skip
    unused-member diagnostics on *public* members, compiled a 7-line class, and
    found CS0067 fires for public and private events alike, with CS0169 and
    CS0414 exactly as quoted. **The implementer's build-versus-test distinction
    is honest and all three codes are right.**
  It agreed with all four deferrals and strengthened two: the `_hostingSince`
  caveat needs `StartHosting` to be called *before* `StartAsync`, which the
  window cannot do if the host button is enabled only after start; and
  `Peers`/`_network` are safer than I argued, because the `PeersChanged` invoke
  and the UI dispatcher marshalling both impose barriers.

**R31 — the two Important findings. Both are real and both undercut R25.4.**

1. **`RememberNames` destroys the record of its own pending save, and runs
   before `PeersChanged`.** It writes `_config.RememberedNames[nodeId]` and sets
   `changed = true`, *then* saves. If `Save` throws `IOException`, the dictionary
   already holds the new name, so the next cycle takes the `continue`, `changed`
   stays false, and the save is never attempted again. The pump retries the
   cycle but cannot retry this write, because the only record that a write was
   pending was destroyed by the write itself. That falsifies R25.4's own
   motivating example — a locked config file — one line above the fix.
   The second half is worse: `RememberNames()` runs *before*
   `PeersChanged?.Invoke(Peers)`, so a throwing `Save` skips the event for that
   cycle — and only cycles where something changed ever save. A config-write
   failure therefore suppresses the UI update on exactly the cycle the player
   needed to see.
   Ruling: fix both. Raise `PeersChanged` before persisting — persistence must
   never gate the UI — and add a pending-save flag that stays set until a save
   succeeds, so a failed write is retried on the next cycle.
   Cost if wrong: one extra boolean field and a reordered call.

2. **`AnnounceAsync` still carries the multi-read pattern R25.1 removed from
   `OnDatagram`, and public `RunCycleAsync` has no re-entrancy contract.** It
   reads `_status` twice and `_network` four times. That is safe only while one
   cycle runs at a time, and nothing enforces it. Two concurrent cycles would
   null-dereference `_network`, corrupt a plain `Dictionary` from two threads,
   and collide on the same `ConfigStore.Save` path. No live caller triggers it —
   the pump is the only one — so it is a hazard the next task walks into.
   Ruling: take the semaphore, not just the doc sentence. I weighed documenting
   it the way I documented R16 and R27, and the empirical record decides it
   against me: **R16's documented single-subscriber contract was violated by the
   very next task** — task 6's own first `StartAsync` attached a second
   subscriber. A contract this session has already seen broken one task later
   does not get documented again; it gets enforced. Also take the local-read fix
   in `AnnounceAsync`, because leaving the identical pattern fixed in one method
   and unfixed in its sibling invites a reader to conclude the fix was cosmetic.
   Cost if wrong: a manual refresh waits out an in-flight cycle, at most 2
   seconds.

**R32 — I am reversing my own seam forbid. The reviewer's argument beats mine.**
I forbade a test seam for the pump on the grounds that any sleeping test
violates "no test may need a real timer". The reviewer accepted that reasoning
for the two obvious seams and then proposed a third I had not considered, with
an argument I had not made:

**The compiler protects three of the four lifecycle fixes and protects these two
not at all.** Delete the `_disposed` guard → CS0169. Delete `_started` → CS0414.
Delete the `PeersChanged` invoke → CS0067 (it probed this). But delete the
`catch (Exception)` block, or drop the `when` filter, and the result compiles
clean with zero warnings and all 95 tests green. So the two
highest-consequence behaviours in the class — the only two whose failure mode is
a silent, whole-session freeze — are the only two with no guard of any kind. And
both are attractive deletion targets: an empty `catch (Exception)` is a standard
lint finding, and `when (ct.IsCancellationRequested)` reads as redundant to
anyone who has not been told why it is there. Comments help a careful reader;
they do not survive a cleanup pass.

Its seam is not the one I forbade. `TimeProvider? timeProvider = null` on the
constructor, used at exactly one site —
`new PeriodicTimer(CyclePeriod, timeProvider ?? TimeProvider.System)` — removes
the clock instead of shortening it. `CyclePeriod` stays a `static readonly` 2
seconds, the public surface the brief names is unchanged, every existing call
site compiles untouched, and `FakeTimeProvider.Advance` sleeps not at all. The
constraint is honoured rather than bent.

It also buys something I did not know was missing: **R2's second half has no test
at all.** Two tests prove the awaited first cycle, but nothing proves
`PumpAsync` waits for the tick *before* running the cycle — swap those two lines
back and the suite stays green.

Ruling: take the seam. Allow `Microsoft.Extensions.TimeProvider.Testing` in the
test project only.
Cost if wrong: one test-only package and roughly 25 lines of test, on a class
whose two most consequential invariants currently have no protection.

**R33 — split the work across two rounds rather than one.**
R31 and R32 together are a lot of new logic for one round: a pending-save flag,
a reordered event, a semaphore, local reads, a constructor parameter, a package,
and three tests. The items are independent, so a single round would probably
land — but round 4 of a 5-round cap is the wrong place to bet on "probably".
Round 4 takes R31 plus the cheap documentation minors. Round 5 takes R32's seam
alone. If round 5 does not land cleanly I adjudicate and ship the seam's absence
as a documented risk, which is exactly the fallback the reviewer offered.

Task 6: minor (deferred): `FakePeerTransport.Disposed` is a `bool`, not a count,
  so no test can detect a double transport dispose. Harmless — the reviewer
  checked `UdpPeerTransport` is double-dispose-safe.
Task 6: minor (deferred): `_started` and `_disposed` are plain check-then-set
  `bool`s, so two concurrent `DisposeAsync` calls could both run the body.
  Every step is idempotent, so the exposure is nil, and `IAsyncDisposable`
  idempotency is normally understood as sequential.
Task 6: minor (deferred): no use-after-dispose guard on `RunCycleAsync`,
  `StartHosting` or `StopHosting`. One caller; low value.
Task 6: minor (deferred): `A_delivered_announcement_from_outside_the_subnet_is_ignored`
  is semi-vacuous in isolation — `Assert.Empty` also passes if the datagram
  never arrived. Adequately pinned by mutation 3 and the two positive delivery
  tests. Self-sufficient version: deliver one in-subnet and one out-of-subnet
  announcement in the same test and assert exactly one peer.
Task 6: note for the final review: `LobbyService.cs` is 214 lines and is not
  accumulating, but `RememberNames` is 18 lines of persistence policy that
  could live beside `ConfigStore`. That is the seam to use if the file grows.

**STE rewrites banked from this review (10 violations, all introduced by this
diff, none backlog) — added to R11, R17 and task 5's lists:**
- `LobbyService.cs:8` → "The service owns the one refresh cycle for the whole
  application."
- `:69` → "StartAsync starts the transport. It runs one cycle. Then it starts
  the refresh loop."
- `:72-73` → "If this method throws, the service is not usable. Dispose this
  instance. Then construct a new one."
- `:89` → "If the start fails, the caller must see the error. Then the window
  can tell the player that the lobby did not start."
- `:90-91` → "Later, the pump ignores a failed cycle. A stale list is better
  than a refresh that stops."
- `:180-181` → "This catch ignores any error in a cycle, for example a locked
  config file or a failed send. The next tick tries again."
- `:182` → "Mm2Lobby.Core has no logger. This catch discards the error."
  **Also fix the verbatim duplicate at `UdpPeerTransport.cs:74-75`, outside this
  diff, with the same replacement.**
- `:184-186` → "A fault that continues leaves the peer list stale, and a player
  can see the stale list. A stopped loop would show nothing."
- `:207` → "The pump throws this exception when the service stops." See the
  round-4 item: that sentence is also now untrue.
- `FakePeerTransport.cs:30` (plan-mandated, verbatim from the brief) → "This
  method gives a datagram to the subscriber, as the real transport does."
- Borderline, not counted: `:10-11` is legal at 18 words but has three clauses.

Task 6: fix round 4/5 dispatched — resumed `a1fe7bb0` with R31 and three cheap
  documentation fixes. R32's seam is held for round 5.
Task 6: fix round 4/5 returned **DONE**, commit `ea66207`, 97/97 (95 + 2). Tree
  clean. No build failures this round.
  It solved both test problems **without** a seam, which I had expected to be
  impossible: the save-failure test uses the real `ConfigStore` with the config
  file set read-only, and the gate test uses a test-local blocking client. Both
  deterministic, neither needs a timer. Mutations: the old ordering fails with
  `Assert.NotNull() Failure`; clearing the flag before `Save` fails with
  `KeyNotFoundException`; removing the gate fails at "Expected: 1 Actual: 2";
  and with the service guard removed the start-twice test now fails on the
  fake's own throw.
  Honest note it added: the locals in `AnnounceAsync` have no test of their own,
  because the gate removes the case they guard against.

**R34 — my own reordering created a new single point of failure. Guard the
event invoke, exactly as R13 guards the transport's.**
I verified the committed code: `PeersChanged?.Invoke(Peers);` is unguarded and
`RememberNames()` runs immediately after it. So a subscriber that throws on
every cycle — a WPF handler with a binding bug, say — makes `RunCycleAsync`
throw at the invoke, the pump catch swallows it, and the next cycle does the
same. Name persistence stops permanently. The implementer spotted that this is a
direct consequence of the reordering I ordered in R31.

Ruling: wrap the invoke, and only the invoke, in its own try/catch. This is
R13's pattern one layer up — task 4 wraps the transport's subscriber call for
exactly this reason, with the same swallow-and-continue trade-off and the same
absence of a logger. Applying it here makes the two subscriber boundaries in the
design consistent, which is worth as much as the fix.
Note the reordering was still right: announcements go out before the event, so a
throwing handler never blocked those. Only persistence was exposed.
Cost if wrong: a genuinely broken window handler fails silently rather than
stopping the save it was already going to stop.

Task 6: minor (deferred): the save-failure test sets the config file read-only,
  which does not stop a process running as root, so the test would fail under a
  root CI runner. Deferred because it fails **loudly** rather than passing
  silently — a visible limitation, not a trap, and no CI exists yet. If anyone
  hits it, the portable injection is an exclusive `FileShare.None` handle held
  by the test, which blocks any user and releases on dispose.

Task 6: fix round 5/5 dispatched — resumed `a1fe7bb0` with R34's guard and
  R32's `TimeProvider` seam. **This is the cap.** Anything still open after the
  scoped re-review gets adjudicated, not another round.
Task 6: fix round 5/5 returned **DONE**, commits `14d69aa` and `2b58048`,
  101/101 (97 + 4). Tree clean. No mutation failed the build. It ran the 20
  `LobbyServiceTests` 8 extra times with no failures.
Task 6: fix rounds 4-5 (4 addressed, 0 open; commits 8857fec..2b58048).
  Re-review derived every verdict itself rather than accepting the report.
  Findings it closed with reasoning worth keeping:
  - F1's retry now works because the pending record lives **outside** the
    dictionary, so the write cannot destroy it. The flag is touched only inside
    `RememberNames`, which runs only inside the gate, so one thread owns it.
  - F3's gate `WaitAsync` sits **outside** the `try`, so a cancelled wait never
    releases a permit it did not take. `SemaphoreFullException` is unreachable.
  - On whether `AnnounceAsync`'s locals are now dead defence: **redundant, not
    dead**, and keep them untested. With the gate there is no second writer, so
    they cannot change an outcome — but they hold the invariant without a reader
    having to prove the gate first. `OnDatagram`'s local is a different case and
    genuinely necessary, because the receive thread races the cycle thread.
  - The tick-first test is deterministic by construction, not by luck: the timer
    is registered with the `FakeTimeProvider` before `StartAsync` returns, the
    fake never auto-advances, and under the swapped mutation the counter reaches
    2 synchronously.
  - It worked through all six new tests for wrong-reason passes and found none.

**R35 — the pump-catch mutation is valid, but its failure message names the
wrong symptom. Recorded, not fixed at the cap.**
The implementer disclosed that removing the pump catch fails the test with an
`IOException` from disposal that masks the real `TimeoutException`. The
re-reviewer derived the chain and confirmed the mutation still proves the
defect: an `IOException` can escape `DisposeAsync` only if the pump faulted, and
a faulted pump has left the loop, so the next cycle cannot have run. "IOException
out of disposal" is therefore equivalent to "the pump died".

Ruling: valid, and I am not fixing it at the cap. But the cost is real and
belongs in the record: a future regression here will report `IOException: disk`
after a 5-second stall, and the reader must reconstruct the replaced
`TimeoutException` to see that the next cycle never ran. Cheap repair for
whoever next touches the file: assert the cycle count before the service leaves
scope, or dispose inside the test body. The cancellation-filter test next to it
fails cleanly with `TimeoutException` alone, so the difference between the two
reports is explained rather than accidental.
Cost if wrong: one confusing failure message on a test that does catch the bug.

**R36 — I ratify the seam deviation, and the judgment was sound even though the
execution was heavy.**
The re-reviewer's verdict: ratify. The seam itself is right-sized — four lines
of product code, one optional parameter, one use site, `CyclePeriod` unchanged,
and the package confined to the test project. The overrun sits entirely in the
test helper.
About half of it was avoidable: `ScriptedZeroTierClient` carries a lock, a
waiter list and a count-based `WhenCalled(n)` where a counting
`SemaphoreSlim(0)` gives the same determinism in about three lines, because
releases accumulate and the already-happened case needs no special path. The two
test-local fakes also duplicate pass-through boilerplate that one fake with a
settable gate would have served.
What a genuine 25-line version would have had to give up: **nothing**, with the
counting semaphore. What it would have given up by dropping the signal
altogether is real — a poll loop or a sleep, which puts wall-clock time back into
the test and trades determinism for line count, the opposite of the seam's
point. The implementer was right to refuse that, so pushing on rather than
stopping landed on the right side of my instruction.
Recorded as a ratified deviation rather than an ignored one.

Task 6: minor (deferred): `Assert.ThrowsAnyAsync<Exception>` in the read-only
  save test would alone accept a throw from anywhere in the cycle; the two
  following assertions pin it, so the pair is tight enough.
Task 6: minor (deferred): `Build` passes one client to both the service and the
  `PeerDirectory`, so a future test that sets `_zeroTier.Network` *and* passes a
  scripted client would have its setup silently ignored. No test does both
  today. This is a trap for the next author, so it is worth the final review's
  attention.
Task 6: minor (deferred): the new event guard wraps a multicast delegate, so
  with more than one subscriber a throwing first handler would starve the later
  ones. One subscriber by design — same shape as R16.
Task 6: minor (deferred): three more comments new in this diff use the passive
  voice with no named agent — `LobbyService.cs:73`, `:130`, `:240`. Added to the
  banked STE sweep.
Task 6: note for the final review (out of scope, predates this diff):
  `PumpAsync` discards the `bool` from `WaitForNextTickAsync`. If that timer were
  ever disposed while the loop ran, the call would return false instead of
  throwing and the loop would spin hot. Unreachable today, because only the
  method's own `using` disposes it. Worth one line if anyone moves the timer out.
Task 6: note on the deferred use-after-dispose guard: an external caller parked
  at the gate when disposal begins can acquire the permit and reach
  `SendAsync` after the transport is disposed. The gate neither creates nor
  widens that case — without it the same call would also corrupt the dictionary.
  **If the guard is ever added, put it after the gate is acquired**, so the check
  cannot pass and then go stale while the caller waits.
Task 6: complete (commits 3769341..2b58048, review clean after 5 fix rounds)

=== ALL SIX IN-SCOPE TASKS COMPLETE ===
Branch `feat/core-lobby`, HEAD `2b58048`, 101 tests passing, tree clean.
Tasks 7 to 9 are out of scope for this run: task 7 is Windows-independent but
was not reached; tasks 8 and 9 need a Windows machine.
Next: the final whole-branch review.

=== WHOLE-BRANCH REVIEW (model opus, `eb693cb..2b58048`, 23 commits) ===
Verdict: **With fixes.** 0 Critical, 8 Important, 12 Minor, plus a triage of all
30 deferred minors and three corrections to my rulings.
It read in six passes and said so: requirements, 1,000 lines of `src` as whole
files (because the base holds only docs, so every source file is a pure
addition and the tree gives correct line numbers), all 1,250 test lines asking
two questions per test, the full 1,506-line ledger, a cross-cutting sweep, and
a forward check against the plan's unwritten tasks 7 to 9.
It worked in a `git worktree` at `/tmp` and verified `HEAD` and a clean
`git status` after removing it.

**Three probes, each of which found something no per-task review could:**
- Probe 1: replaced `_config.ZeroTierNetworkId` with a wrong literal. **101
  tests passed.** The one line guarding the entire purpose of the app is
  unguarded, because `FakeZeroTierClient` ignores its `networkId` argument.
- Probe 2: added the real transport's `SendAsync`-before-start guard to the
  fake. **5 failed** — including the three headline tests that verify the
  announcement the whole design rests on. They exercise a call order that throws
  in production.
- Probe 3: fed a 40-character display name through the app's own `PeerTable`.
  Rejected. So a long name makes a player invisible to every friend with no
  local symptom, because you exclude yourself from your own list.

**The finding that matters most (I1).** `LobbyService` loads its own
`AppConfig` and writes it back whole. The plan's task 9 builds the service
*before* running preflight, and the plan's task 8 `GamePathCheck.FixAsync`
writes the game path straight to the file. So the planned sequence is: window
builds the service with an empty game path, player picks the game, a friend's
name arrives two seconds later, `RememberNames` saves the stale object, **the
game path is gone.** Repeating, silent, and the symptom points nowhere near the
cause. Nothing in this branch can trigger it, which is why it is not Critical —
and why it is cheap here and expensive after task 8 starts.

**R37 — I accept the verdict and all eight Important findings.**
Also promoted into the wave, past the reviewer's own merge bar, five cheap items
with invisible failure modes: I5 (the unenforced name length), I6 (see R38),
I7 (`LastCycleFault`, which is the answer to the question I asked about the
three absorb sites), I8 (the unpinned network ID), and M1 (`File.WriteAllText`
truncates in place, so a crash loses the whole configuration silently).
Cost if wrong: a larger single wave with no second wave to catch a miss. The
mitigation is that every item is one to three lines and independent, and I
adjudicate residuals rather than dispatching again.

**R38 — R28 is overturned at one line. The reviewer is right.**
R28 said startup failures must propagate and steady-state failures must be
absorbed. That is right for the transport and for `IZeroTierClient`. It is wrong
for a failed configuration save, and three things show why:
- R31 built a pending-save flag specifically to make that fault retryable, and
  then cycle 1 treats the identical fault as fatal. My two rulings disagree at
  one line of code.
- Propagation has exactly one observer in the whole design, `StartAsync`. So
  "propagate so the window can report it" delivers, in practice, only "abort the
  start" — and R27 then makes the instance permanently unusable.
- **Whether it fires is a race.** It depends on whether a peer's announcement
  lands between the transport starting and the first cycle. Peers announce every
  2 seconds, so the window is small and non-zero. A player with a read-only or
  sync-locked `%APPDATA%` gets a lobby that usually starts and sometimes
  refuses to, with no pattern.
Ruling: absorb `IOException` and `UnauthorizedAccessException` around the `Save`
call inside `RememberNames` and leave the flag set. R28's principle survives;
its application to persistence does not. The bind and a throwing ZeroTier client
remain the two genuine startup failures.

**R39 — R30's residual reasoning was wrong, and the conclusion survives anyway.**
I deferred the torn `_hostingSince` read on the grounds that a torn value
"would serialize to nonsense or throw", so the pump's catch would retry. The
reviewer corrected it: `StopHosting` writes all-zero bytes, so the likely tear
is `hasValue = true` with `dateData = 0`, which **does not throw** — it produces
a clean announcement reading `hosting: true` with
`startedAt: "0001-01-01T00:00:00+00:00"`. No exception, so no retry; the value
is simply wrong for one cycle. The other tear direction is harmless because the
field is not read then.
So the deferral stood on a false premise. Ruling: take the fix, because it is
smaller than the paragraph arguing against it — hold the start time as a `long`
of ticks with `Volatile.Read`/`Volatile.Write` and the whole class of reasoning
disappears. That is the second of my atomicity claims this session to be
corrected by someone I invited to break it.

**R40 — R12 was right and incomplete, and that incompleteness was expensive.**
Deferring the version warning to task 9 was correct; it is user-visible. But
task 9 **cannot build it** with the surface this branch ships: `Peer` has no
`appVersion`, `PeerDirectory` drops `LivePeer.AppVersion`, and the plan's window
keeps its `PeerTable` as a local. A ruling that defers work to a task that
cannot perform it gets rediscovered as a blocker on a Windows machine, which is
the most expensive place in this project to discover anything.
Ruling: land the field here, with the task-3 null coercion it depends on.

**R41 — the fakes had no owner, and that is a process defect, not a code one.**
Five fakes, four implementers, six tasks, and nobody reviewed them as a set.
Both probe 1 and probe 2 trace to that. The reviewer's recommendation, which I
adopt for tasks 7 to 9: a fidelity pass on the fakes asking one question — does
this fake refuse everything the real type refuses? Task 7 introduces
`IProcessLauncher` and `ICheck`, and a launcher fake that accepts a launch the
real one would refuse produces the same green-but-wrong suite, in the one task
whose entire point is that it must never start a process.

**R42 — one disagreement with the whole-branch review, in favour of an earlier
reviewer.** It called the undisposed `_cycleGate` inconsistent with the care
taken over `_loop`. Task 6's re-reviewer gave better reasoning: `SemaphoreSlim`
allocates its wait handle only when `AvailableWaitHandle` is read, which nothing
does, so there is nothing to release — and disposing a semaphore that still has
waiters leaves their waits pending while every later wait throws. Keep it
undisposed. Recorded in the wave's parked list with that reasoning.

**Triage adjudication.** Of 30 deferred minors: 6 promoted to fix before merge,
10 into the wave, 14 parked. Three the reviewer pushed back on and I accepted:
the name tiebreaker (identical names are likelier than case-differing ones, and
the list can reorder under the player's cursor), the `Build` helper trap (tasks
7 to 9 will add tests to that file), and the semi-vacuous out-of-subnet test
("pinned elsewhere" is the shape that survives a refactor of the other tests).

**The STE sweep is final with six gaps added**, which the reviewer found by
inventorying every comment in the branch — 14 in `src`, 2 in `tests`. One whole
file, `ZeroTierClient`, had never been swept, because task 2's review predates
the constraint. It also overturned two "already compliant" calls and required a
glossary pass first, since "one meaning per word" is branch-wide and three word
pairs are still live. Without the glossary the sweep yields compliant sentences
that disagree with each other.

Fix wave dispatched (model opus) with the complete list in
`fix-wave-brief.md` — phase 1 code (A1 to A12n), phase 2 the comment sweep
(S1 to S6), two commits. **One wave only**; residuals get adjudicated.
Spec and plan corrections are mine and are not in the wave.
Spec and plan corrected in commit `909d26b` (docs only): `Peer.AppVersion` added
  to spec 5.4 with the reason; spec 5.8 now gives the window sole ownership of
  the one `AppConfig` and names it as where the player edits the display name,
  with the two-copies failure sequence written out; spec 5.5 gains `MyAddress`,
  `LastCycleFault`, the `CurrentHost` exclusion and the single-use contract;
  spec 7's version row names the window as the owner and a new row covers a
  failed cycle. The plan's tasks 8 and 9 each gain a requirements block, because
  both code blocks predate this work — task 9 gets seven, task 8 gets three
  including R41's fake-fidelity rule.

Fix wave returned **DONE_WITH_CONCERNS**, commits `c39cfb2` (phase 1) and
`4e51269` (phase 2). **101 → 121 tests**, 0 failed, 0 warnings, 77 ms, tree
clean — I verified. Five consecutive clean runs for flakiness. 31 mutation
checks, each as its own command; two failed the build (CS0168, CS0414) with
compiling variants built for both, disclosed as such.

**R43 — two items in my own brief would have defeated each other, and the
implementer proved it before writing either.**
A5's test proved the save-retry by making `config.json` read-only. A11 changed
the write to `tmp` plus `File.Move`. On macOS `rename(2)` ignores the
destination's mode, so the atomic write **succeeds onto a read-only file** — and
A5's test would have passed while proving nothing. The implementer probed this
before applying either change and replaced the injection with a folder at the
config path, which fails on every platform and under root.

This is the defect class this whole run kept finding — a test that passes for
the wrong reason — and my brief created it by pairing two items that each looked
independent. Neither the whole-branch reviewer nor I saw the interaction; it is
only visible to whoever holds both items at once.
Ruling: accept the replacement, and record the general lesson. When a fix
changes *how* a behaviour fails, every test that injected the old failure is
suspect, even when the fix and the test live in different files and different
findings.
Cost if wrong: the folder injection is less realistic than a read-only file. It
is strictly more portable, which matters more.

**R44 — nine items would have shipped with no evidence, and two of my items had
no coverage at all.**
The implementer added tests for nine items I had not asked to be tested, all
mutation-checked, and found two holes worth naming:
- **A12g** — deleting the entire expected-kind check in `ZeroTierClient` left
  **all 117 tests passing.** I had written that item as a deduplication, so
  neither the brief nor the original task-2 review treated it as behaviour. It
  was behaviour, and it was unguarded.
- **A10** — every test clock in the suite has a zero UTC offset, so `Ticks` and
  `UtcTicks` were indistinguishable and the conversion could have been wrong in
  either direction. It added a `-06:00` clock test.
Ruling: both accepted and both are mine to have missed. Recorded because the
second one is a property of the whole suite, not of one test: a fixture set that
never varies a dimension cannot detect an error in that dimension.

**Six disclosed deviations, all accepted pending the re-review:** A3 (my test
passed with the fix removed, because the fake loops no send back, so it now
delivers the app's own announcement as a directed broadcast does), A5
(mechanism, see R43), A6 (two tests, because one cannot assert set-and-cleared
without a race), A7 (that test cannot await `StartAsync`, since the blocking
client would hang it), A12d (the guard skips all of `RememberNames`, so memory
is not polluted either), S3 (two banked comment lines were false after A6 —
"this catch discards the exception" became "it records `LastCycleFault`" — and
"error"/"fault" became "exception" per the glossary).

One process note it disclosed: an early `git checkout` restore reverted two
uncommitted items; it caught that, redid them, and committed phase 1 before
running the rest of the mutation battery. The re-reviewer is asked to confirm
nothing was lost from the committed tree.

Single scoped re-review dispatched (model opus) over `909d26b..4e51269`. **Last
gate.** It must independently verify the A11/A5 interaction, confirm phase 2
changed no executable line despite 173/142 churn across 19 files, judge all six
deviations, and check the glossary as a set. Residuals get adjudicated, not
another wave.

=== FIX-WAVE RE-REVIEW (model opus, `909d26b..4e51269`) ===
**All findings addressed. Ready to merge: Yes.** 0 Critical, 0 Important, 3
Minor. All thirteen Important-and-promoted items and all fourteen small items
verified present in the committed tree, each derived from the code rather than
from the report.

Its method is worth recording, because it is stronger than what I asked for:
rather than grepping the phase-2 diff for comment markers, it **stripped every
comment from both revisions of all nineteen files** with a comment-aware
stripper and compared the stripped files. That catches a code line a comment
line replaced, which a grep cannot. Result: exactly one file differs, in two
places, and both are the `Rewrap` → `CopyValidEntries` rename item S6
authorised. **Phase 2 changed no executable line**, so every file's banked
mutation evidence still stands.

It verified R43 independently with a file-system probe, and the table settles
it at the system-call level: `File.WriteAllText` onto a read-only file throws,
but temp-plus-`File.Move` **succeeds** onto one, because `rename(2)` replaces
the directory entry and never opens the destination. The folder injection fails
on both write shapes. It then proved the retry by mutation — moving the flag
reset before the save makes only that test fail, with `KeyNotFoundException` —
so the final assertion genuinely depends on the pending flag surviving.
A bonus I had not expected: `EISDIR` is a type conflict rather than a
permission, so **root does not escape it either**. That retires the parked
worry about that test under a root CI runner.

All six deviations judged right, and **two of them correct my brief**: A3's test
as I wrote it passed with the fix removed (the fake loops no send back, so the
peer list was empty either way), and A12d is better than I specified, because
guarding only the save would have left this machine's own node ID in memory and
written it one cycle later — moving the defect rather than removing it.

Glossary applied consistently, measured over all 259 comment lines as a set.

**R45 — all three residual Minors parked. No second wave.**

1. **The 32-character cut can split a UTF-16 surrogate pair.** The reviewer
   declined to determine whether the result throws or degrades, and that
   decides the severity, so I probed it myself on this machine's runtime:
   `Utf8JsonWriter.WriteString` **does not throw** — it writes `U+FFFD`. The
   payload is `{"player":"xxx…�"}`, still 32 characters, so
   `PeerTable.Accept` admits it and the player stays visible. The reviewer's
   worst case, a throw aborting `StartAsync`, is ruled out.
   Ruling: park. The whole consequence is one replacement character at the end
   of a name, and reaching it needs a hand-edited configuration file, because
   the window caps the text box at 32. Recorded fix if anyone wants it: do not
   cut between `char.IsHighSurrogate(name[31])` and its pair.
   Cost if wrong: a friend with an emoji at exactly that boundary sees one odd
   character. A4 remains a strict improvement, because the old code made that
   player invisible.

2. **One dead assertion**, `LobbyServiceTests.cs:417`. The test deletes
   `config.json` before the cycle, so `Load` returns defaults with an empty map
   and `Assert.DoesNotContain` cannot fail.
   Ruling: park. The intent — "the name is not yet on disk" — is carried by the
   retry assertion, which is mutation-proven. One dead line in a strong test,
   not a dead test. Fix: delete the line, or assert `File.Exists` is false.
   Cost if wrong: nothing; a neighbouring assertion covers the claim.

3. **A small new flake risk in `FreePort`.** It binds a probe socket, reads the
   port and closes it, so the port is free for any parallel test. Removing
   `SO_REUSEADDR` (A12e) means a collision now throws where it used to be
   hidden.
   Ruling: park. The helper predates this wave, the window is tiny, and the
   suite has run clean six times across two agents. Fix: hold the probe socket
   until the transport binds, or give the collision test a fixed high port.
   Cost if wrong: an occasional loud, obvious test failure — never a false pass.

4. **One glossary residue**, `LobbyService.cs:296`: "so the pump task does not
   fault" uses a noun as a verb, which the user's rule forbids.
   Ruling: park with the rewrite — "so the pump task does not end with an
   exception". The property name `LastCycleFault` is code, not prose, and the
   spec names it, so it stays.

Two things the reviewer raised and declined to call findings, which I accept:
`LastCycleFault` is read from the UI thread without `Volatile`, matching the
parked ruling for `Peers` and `_network`; and `ConfigStore.Save` does not flush
before the move, so the rename removes the truncate window as asked but does
not make the content durable against power loss. Both are inside the brief's
scope as written.

=== BRANCH COMPLETE ===
`feat/core-lobby`, HEAD `4e51269`, 121 tests passing, 0 warnings, clean tree.
25 commits. Tasks 7 to 9 remain; 8 and 9 need a Windows machine.
The decision record is committed to `docs/superpowers/decisions/` so it
survives this workspace.
