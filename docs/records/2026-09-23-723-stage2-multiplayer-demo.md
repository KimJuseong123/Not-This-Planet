# 723: Stage2 multiplayer demo replication

## Scope and baseline

- Branch: `S15P21B202-723-qa-2stage`, base `cbe14923`.
- Worktree: `C:/Users/SSAFY/.codex/worktrees/723-stage2-multiplayer/S15P21B202`.
- Unity 6000.5.4f1, existing Mirror/KCP networking. No direct screen interaction.
- Reported: the host sees the Stage2 cinematic and final card; guests remain waiting for teammates.
- Expected: all connected players follow the server timeline and see `-- 데모 완 --`.

## Cause and correction

`Stage2Bridge.Update` sent complete JSON snapshots on `Channels.Unreliable`.
The real KCP transport allows 1,184 message bytes on that channel. Mirror's
`NetworkServer.SendToAll` rejects larger messages before sending to any connection.
The host reads the authoritative yard directly, so only remote presentation stalls.
The old network harness used Telepathy, whose larger limit concealed this failure.

Evidence:

- The initial empty-cast fixture measured 1,162 bytes, inside the old limit. It did
  not reproduce multiplayer conditions (`before.xml`).
- With two actual cast entries, the unchanged production sender emitted the
  explicit dropped-message error: 1,393 bytes versus 1,184 (`before-two-players.xml`).
- After the fix, the same regression sent 1,407 bytes on reliable channel 0 and
  passed (`after.xml`). Small size differences come from live numeric state.

The production change only selects `Channels.Reliable`, allowing KCP fragmentation
and retransmission of the complete snapshot. It retains authenticated inputs,
server authority, ready-only recipients, snapshot sequence checks, the 20 Hz send
cadence, shared clock/cast, late-viewer seeking, and puzzle clear semantics.

The network harness now uses KCP. Its runner rejects oversize/drop errors and
checks the late observer's terminal demo state in addition to host/client states.

## Automated verification

Evidence directory: `artifacts/723-validation/` (local outputs).

Unity EditMode/PlayMode-transition tests: **45 passed, 0 failed** (`after.xml`):

| Fixture | Passed |
| --- | ---: |
| Stage2DemoTests | 4 |
| Stage2Tests | 12 |
| Stage2PressureTests | 6 |
| Stage2ContaminationTests | 5 |
| EndingSceneTests | 7 |
| CutsceneSkipTests | 8 |
| TeamRetryTests | 3 |

Includes full-snapshot KCP limits; movement/camera/assembly/terminal-state handling;
ESC close retaining input lock; late replica timeline/cast; demo opt-out; existing
crane, wrench, ladder, contamination and pressure behavior; ending/skip/retry rules.

Initial worktree import failed because LFS files were still pointers. Restoring
already-cached LFS objects resolved the missing Cecil/BouncyCastle compile errors.
Existing BouncyCastle resolver/deprecated-API warnings remain visible in logs.

## Runtime verification

Windows x64 development test-player build succeeded, Unity exit 0 (`build.log`).
Build method: `Metagame.EditorTools.Stage2Validation.BuildNetworkSmoke`.

- Three-peer KCP demo: **6/6 checks passed** (`demo/network-smoke.json`). Host,
  client, and late observer all reached cinematic and terminal demo states. Each
  kept a nonempty stable cast, input lock, and an uncleared/unlaunched game state.
- Three-peer KCP yard regression: **5/5 checks passed** (`yard/network-smoke.json`).
  Authenticated host/client container interaction, remote crane motion, late full
  state replication, and pilot cleanup after disconnect all passed.
- All six player processes exited successfully. The runner found no runtime
  exceptions, duplicate-component failures, oversize messages or dropped-message
  errors. These are headless runtime checks, not visual screen inspection.
- Commands: `tools/validation/stage2-pressure-smoke.ps1 -Mode demo -OutputFolder
  artifacts/723-validation/demo`, then the same runner with `-Mode yard` and
  `artifacts/723-validation/yard`.
- Unity-generated settings/material/image changes were restored. Final source
  changes are the bridge, the smoke harness, its runner, the regression test and
  this record. No scenes or prefabs changed.

## Limits and review

- No production scene, prefab, package, settings, save format or backend changes.
- Live internet/relay, artificial packet loss, host migration, dedicated Linux,
  and the full backend Stage1-to-Stage2 room flow are not validated here.
- Reliable snapshots can queue under poor connectivity; WAN latency/bandwidth
  behavior has not been measured. Client presentation continues using the common
  network clock after the start snapshot, rather than waiting for each new frame.
- Manual acceptance: with 2–4 players enter Stage2 from the Stage1 ending, verify
  the same intro/assembly/final card on every peer, ESC open/close, and lobby exit.
## 725-first merge preparation (2026-09-23)

- Integrated the current 725 tip `2700f409` into 723 without rewriting published history.
  This also includes its develop baseline `af143761`.
- 725 was revised upstream: it now contains replica diagnostics and size tests only;
  the reliable-channel correction remains in 723. Both contributions are retained.
- The merge was conflict-free. A temporary Git-tree simulation of 725 squashed onto
  develop followed by 723 also completed without conflicts; no target branch was
  changed by the simulation. This result applies to the above inspected tips.
- Combined Unity batch run: Stage2DemoTests 4/4 and Stage2SnapshotSizeTests 2/2,
  **6 passed / 0 failed** (`artifacts/723-validation/merge-725.xml`).
- Relative to 725, the 723 change remains its original five files, including this
  record. Generated Unity settings changes were restored before committing.
- Intended integration order: merge 725 into develop, then merge 723 into develop.
  Demo-specific bandwidth optimization is a separate change and is not included.