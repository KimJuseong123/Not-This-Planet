# Multiplayer tool throw charge gauge

Report: holding Q with a bat or welding gun throws successfully on release, but
the charge gauge near the crosshair is missing in multiplayer.

Static evidence: GameHud reads PlayerRig.ChargingTool/ToolCharge, which previously
exposed only local simulation state. NetPlayer replicated teammate toss charge
but omitted tool charge. A participant's empty local hands also reset the local
tool fields in DriveArms, so copying into those fields alone is insufficient.
This explains the remote-client symptom; the reported live session was not reproduced.

Fix: replicate the server's active tool charge through a SyncVar and apply it only
to the owning non-server body. PlayerRig keeps the received presentation value
separate from local hand simulation. Host/solo getters retain authoritative state;
server release/cancellation publishes zero. Existing HUD layout is unchanged.

Validation: Unity 6000.5.4f1 compiled and passed all 4 ToolChargeTests EditMode
cases (client charge survives empty-hand ticks, server release clears the gauge,
authority rejects remote writes, received range is clamped). Results are in
METAGAME/Logs/tool-charge-tests.xml. Existing-source warnings were emitted;
there were no compilation errors. Unity startup's unrelated rendering-setting
rewrites were reverted after testing.

Pending manual verification: use the same updated build for host and participant.
For each player, equip a bat and then a welding gun, hold Q for about two seconds,
check that the crosshair gauge fills, then release Q and check that the tool throws
and the gauge disappears. Repeat pickup/throw, short-tap drop, and charge interruption
by dropping/downing. Check that another player's charging never displays on the
observer's HUD. No two-peer visual or player-build validation was performed here.
