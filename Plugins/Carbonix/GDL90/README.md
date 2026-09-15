# GDL 90 bridge

Streams the aircraft and the traffic it hears to an electronic flight bag over UDP,
in GDL 90 (Garmin 560-1058-00 Rev A). Built for AvPlan EFB on an iPad; anything that
listens on port 4000 for GDL 90 should work. Ticket SW-908.

Everything in this folder is written from the ICD. No GDL 90 reference implementation
was read while writing it, and the validation below depends on that staying true.

## Layers

Dependencies run one way, downward. Each layer is one or two files.

| Layer | Files | Holds |
| - | - | - |
| Wire | `Gdl90Frame`, `Gdl90Messages`, `PressureAltitude` | framing, CRC, the message encoders, ISA pressure altitude |
| Observation | `Gdl90VehicleState`, `Gdl90VehicleReader` | what the aircraft was last seen doing, read off MAVLink |
| Traffic | `Gdl90TrafficTracker` | the other aircraft, accumulated from an ADSB_VEHICLE subscription |
| Reporting | `Gdl90Ownship`, `Gdl90Traffic` | observation to report fields, pure |
| Assembly | `Gdl90FrameSet`, `Gdl90LogShape` | one second of output in order, and how a message is logged |
| Recording | `Gdl90FrameLog` | the frame log beside each tlog |
| Source | `Gdl90Source` | aircraft, simulator, replay, or not yet known |
| Transport | `Gdl90Service`, `Gdl90Configuration` | the 1 Hz UDP stream |
| Offline | `Gdl90TlogConverter` | a recording to a frame log, transmitting nothing |
| Operator | `Gdl90DeviceProbe`, `Gdl90TabPresenter`, `UI/EFBTab` | the EFB tab |

The wire layer is the one part with an external oracle behind it (see Validation). If it
looks wrong, report it rather than editing it.

## One second of output

Every second the service builds one frame set and hands each frame to the socket:

1. Heartbeat, carrying the ground station clock and the position-valid bit.
2. Ownship report, sent whether or not a position is available (ICD 3.4).
3. Ownship geometric altitude, only when one is available (ICD 3.8).
4. ForeFlight ID, so the receiver knows the geometric altitude is MSL.
5. Traffic reports with an alert first, then the rest nearest the ownship first
   (ICD 2.3 bandwidth management).

A target received and not forwarded produces a log record, not a frame, once per drop.

## Ownship

**Stale is null or NaN.** The reader reads the MAVLink messages directly, never
`CurrentState`, whose fields are written by several messages, carry display multipliers,
or go stale silently. A value older than its threshold is absent from the state and the
report says the field is unavailable.

| Threshold | Value | Applies to |
| - | - | - |
| `PositionStaleAfter` | 2 s | GLOBAL_POSITION_INT, with GPS_RAW_INT as the fallback position |
| `StaleAfter` | 3 s | everything else |

Two deliberate exceptions. A stale GPS_RAW_INT keeps its fix type and drops only its
accuracy figure, since the estimator is still GPS-backed. And `LandedState` is held for
the life of the connection, which is what the airborne bit depends on.

**Airborne is `landed_state != ON_GROUND`, and nothing else.** EXTENDED_SYS_STATE is
derived from the same `is_flying()` call ArduPlane feeds to its own ADS-B transponder,
so a mirror built on it agrees with the transponder by construction. The failure
directions are not symmetric: a false on-ground makes the aircraft disappear from other
pilots' displays, which de-clutter ground targets; a false airborne is a parked target,
visible and harmless. So a landed state not yet received reads as airborne, and the
message is never allowed to go stale into a guess. Four real flights showed it running at
2 Hz and never stopping on a healthy link; every gap coincided with a degraded link or
Mission Planner's own parameter and FTP bursts.

EXTENDED_SYS_STATE is in none of ArduPlane's stream groups, so the service leases it at
2 Hz through the rate manager. Everything else the report needs rides on streams Mission
Planner always requests.

**The stream does not start until every expected message is arriving.** The reader
names what is missing (GLOBAL_POSITION_INT, GPS_RAW_INT, SCALED_PRESSURE, ATTITUDE,
EXTENDED_SYS_STATE), the service logs the names on change, and the tab shows only
"not receiving all telemetry".

**Pressure altitude** comes from SCALED_PRESSURE through the ISA formula on the
1013.25 hPa datum. **Geometric altitude** is MSL, with the ForeFlight capability bit
saying so. Nothing downstream checks the datum, and an ellipsoidal height would render
20 to 25 m high in eastern Australia.

**NACp** is derived, not hardcoded. GPS_RAW_INT `h_acc` survives the ZED-F9P to DroneCAN
to autopilot chain intact. EPU is 2.45 times the reported one-sigma figure, the
conservative reading of an inconsistent ArduPilot definition, then binned by the ICD
table. `h_acc == 0` means unknown, not perfect: it is what a MAVLink 1 message or an
unpopulated backend leaves behind, so it maps to NACp 0. NIC is 0; nothing in the chain
produces a protection level. Emitter category is 14, UAV.

**Track source** switches from heading to true track above 6 m/s and back below 3 m/s,
with ATTITUDE yaw referenced to true north.

## Traffic

**Subscribe; never poll.** `getPacketLast` is keyed by message ID alone, so polling
ADSB_VEHICLE returns whichever aircraft arrived most recently and discards the rest of
the sky. The tracker subscribes with sysid 0 and filters on the selected vehicle, and
rebinds when the redundant link manager swaps `MainV2.comPort` for another interface.
`MainV2.adsbPlanes` is not used: its projection drops the validity flags, emitter
category and vertical velocity.

**Every validity flag is honoured.** An unhonoured flag turns data the receiver said it
did not have into a confident claim on the wire. Only `PRESSURE_QNH` altitudes are
forwarded, and that name is a misnomer for plain pressure altitude; a geometric altitude
is reported as unavailable rather than converted. Zero velocity with the flag set is a
stationary target, not missing data.

**SIMULATED is not read.** Over 10,277 ADSB_VEHICLE messages from four flights, 94
carried the bit across 17 aircraft, every one of which also flew as an ordinary target
in the same session. The PingRX Pro also sets flag bits 9 to 14 that MAVLink does not
define, and the values look like fill patterns. Honouring the bit only ever hid real
aircraft; a simulator's targets are kept off the wire by refusing to transmit from a
simulated source instead.

**Age and extrapolation.** A target's age is the receiver's own `tslc` plus the time
since the message arrived. Reports older than 1 s carry the extrapolated bit; the
position is carried forward along the track for the actual age, capped at 10 km, which
only wrong inputs can reach. Targets are dropped after 5 s, deliberately looser than the
ownship position's 2 s. Measured cadence is about one message per target per second.

| Drop reason | Meaning |
| - | - |
| `ownship` | our own transponder heard back over the link |
| `invalid_address` | zero, or wider than 24 bits |
| `stale` | older than `DropAfter` |

**Alerts** come from the autopilot's COLLISION message, source ADS-B only, since for
other sources its id is a system id rather than an ICAO address. LOW and HIGH both alert,
matching Mission Planner's own map. ArduPilot streams COLLISION at 1 Hz while a threat
exists and for 5 s after, so the tracker holds the last threat until NONE arrives or the
target is dropped. No COLLISION message appears in any of the four validation flights.

**Airborne** is true for everything except the two surface-vehicle emitter categories.
ADSB_VEHICLE has no air/ground state, the bit has no unknown value, and a ground target
drawn at its real level is a lesser error than an airborne one a receiver suppresses. A
point obstacle stays airborne for the same reason.

Traffic NIC and NACp are 0, unknown, because ADSB_VEHICLE carries no integrity field.

## Source policy

| Source | Transmits | Frame log |
| - | - | - |
| Live aircraft | yes | yes |
| Simulator | only unlocked and attested to, this session | follows transmit |
| Replay | never | never |
| Unknown | no | no |

**SIMSTATE is the only simulator signal.** A `SIM_` parameter scan and `MainV2.sitl`
were dropped: the latter reads a UdpClient's `Connected`, which stays true forever once
Mission Planner has launched SITL and would mark the next real aircraft a simulator.
Stickiness is free: `packetsLast` is only cleared when a link opens, so the verdict
lasts one connection and resets on the next. Detection must not run on a closed link,
because closing leaves the packet list populated and the next connection would inherit
the previous session's verdict.

**Unknown** is the 2 s settling state after a link opens. Live is an absence of
evidence and SIMSTATE arrives with the streams, so Start waits it out and the tab says
"initializing". A stream is never started through Unknown, so the service does not
transmit through it either.

**Every refusal ends the run.** No link, a simulator without the unlock, and a replay
all disable the stream and log why, rather than leaving it switched on and silent. A
link that comes back does not resume the stream; Start is a deliberate preflight act. A
telemetry dropout on an open link is different: the stream keeps reporting with the
position marked unavailable, because an EFB with no ownship source falls back to its own
GPS, which is wherever the tablet is.

## Frame log

One JSON Lines file beside every tlog, `X.tlog` to `X.gdl90.jsonl`, one row per frame
handed to the socket. Its purpose is acceptance: the row carries the transmitted hex
beside the encoder's inputs, **before quantization**, so an independent parser can
disagree with us. Never decode the hex into the log and never quantize the values; either
makes the two columns agree by construction. A logged 1013 ft beside hex that decodes to
1000 ft is the field's 25 ft quantum, not a bug.

| Line | Shape |
| - | - |
| `meta` | schema, Mission Planner and plugin versions, the tlog path, a note on the values |
| `cols` | the column names for one message type, declared before its first row and again if they change |
| row | `[msg, t, seq, msg_id, hex, ...]` zipped against that message's `cols` |
| `source` | the service's source kind and, for a simulator, the signal; the converter writes `tlog`, `transmitted: false`, and whether the recording was live or sitl |
| `destination`, `link` | where frames go and which link is selected, replayed into any log opened later |
| `drop` | the traffic report a dropped target would have made, plus `reason` |
| `gap` | converter only: a stretch of recording longer than 60 s with no frames |
| `close` | frame and discard counts |

Column names come from the `JsonProperty` attributes on the message classes, so a field
renamed or reordered is absorbed by readers; the schema number moves only when a field
keeps its name and changes meaning. `seq` runs across the service, not per file, so the
logs of two redundant links can be joined and a log opened mid-flight visibly starts
partway. Writes go through a bounded queue on their own thread; a full queue discards, a
failing file closes the log, and nothing in here can delay or break the transmit loop.
The file is opened with `FileShare.Delete` because Mission Planner sorts finished logs by
moving every file with the tlog's stem, and a locked companion would abandon the tlog
too. A frame's row is written after the socket accepts it; a row is a promise that the
bytes reached the network stack.

## Converter

`Gdl90TlogConverter.Convert(tlog, config, overwrite)` steps a recording through the
same reader, tracker, reports and log writer as the live path, driven by tlog time,
emitting one frame set per recorded second and opening no socket. Gaps up to 60 s are
filled with frame sets built from the aging state, because a real dropout is where the
staleness handling earns its keep; a longer gap inside a tlog is a corrupt timestamp and
is recorded as a `gap` instead. The ownship identity is passed in, because an aircraft's
receiver does not report the aircraft. A recording of a simulator converts in full and
is labelled `sitl`; converting transmits nothing.

## EFB tab

One address box taking `address[:port]`, port 4000 when absent. The parser is stricter
than `IPAddress.TryParse` on purpose: that accepts `192.16` as 192.0.0.16, so a half-typed
address would have streamed to a real host that nobody chose.

**Liveness** is a ping first, then a unicast mDNS query to port 5353 while a name is still
wanted, every 5 s while the tab is visible. Unicast reaches a dozing iPad radio where a
multicast browse does not; a real iPad answered in 31 ms with its name and model. Answers
age out after 20 s, and the label distinguishes "last answered N ago" from "nothing
answered". Green means a device answered, never that AvPlan is receiving; GDL 90 is
one-way and iOS sends no rejection. A MAC address cannot identify an iPad: iOS
randomises it.

| State | Status line |
| - | - |
| destination or identity unusable | names the field at fault |
| replay | replaying a log |
| no link | not connected to the aircraft |
| simulator | refused, or asks for the attestation if unlocked |
| settling | Idle - initializing... |
| telemetry incomplete | Idle - not receiving all telemetry |
| ready | Idle - ready |
| running | send failed, starting, stalled, or transmitting with the frame count |

The top state says transmitting, never connected. Inputs are disabled while the stream
runs, and the counters are per session so a fixed address is not shown last run's error.

**Simulator attestation.** A checkbox, "I have disabled AvPlan Live", shown only when
`Host.config` carries `IUnderstandTheRisksOfEFBWithSITL`, a link is open, and it is a
simulator. The key is a decision about the install; the tick is a claim about the
session, never persisted, cleared when the box leaves the screen.

| Setting | Lives in |
| - | - |
| ICAO and callsign lists | `CarbonixSettings.json`, `gdl90_icaos` and `gdl90_callsigns` |
| destination | `Host.config`, `cbx_gdl90_destination`, written on Start |
| selected ICAO, callsign, the enable | nowhere; chosen each session |

The plugin loop calls `UpdatePort` each second, reports a stalled transmit loop, and
disposes the service at exit. A stall is not restarted: the loop catches every
exception, so a late tick is a blocked call, and a second loop would block in it too.

## Validation

**Encoder.** Written blind from the ICD, then checked three ways: the ICD's own worked
examples (`Gdl90MessageTests`), 79 traffic-report vectors generated by Stratux's own
encoding code copied byte for byte from `stratux/stratux` (`Gdl90TrafficVectorTests`; 66
exact matches, 13 documented deliberate deviations; never `cyoung/stratux`, whose
callsign filter is wrong), and a phase-5 acceptance run: 387,645 frames from four real
flights fed to `NathanVaughn/gdl90py`, unread, its API found by introspection, and
joined field by field against the logged pre-quantization values with the ICD's
quantization applied independently and no tolerance. Zero discrepancies were ours. The
one real finding was theirs: it reads the ForeFlight capabilities mask little-endian.

**Regression net.** Four recorded Ottano flights, converted and compared line for line
against a baseline kept beside them. This lives in tree: `Gdl90TlogValidationTests` in
the test project does the work and `Plugins/Carbonix.Tests/GDL90/validate-tlogs.ps1`
runs it. Run it after any change in this folder:

```plaintext
validate-tlogs.ps1 -Root <folder>            build, convert, compare
validate-tlogs.ps1 -Root <folder> -Refresh   rewrite the baseline for an intended change
```

The folder holds the tlogs and a `baseline` subfolder of frame logs whose `report.txt`
names the commit that produced them; `-Root` defaults to the `CBX_GDL90_VALIDATION`
environment variable. The converter is deterministic once its one wall-clock value is
dropped, so the comparison is exact, and a difference is either a regression or an
intended change: the report groups differing lines by message with examples, the
developer reviews it, and refreshes the baseline only for the latter. Four flights take
about two minutes. The tlogs and the baseline are large and static, so they live on
Dropbox rather than in git, under
`Carbonix Software\04_Project\30_MissionPlanner_GDL90\tlog-validation`, which the
script uses when nothing else is given. The test is skipped when no folder is named.

The encoder fixtures, the vector generator and the acceptance harness stay outside the
repository too, in `reference` beside that folder, so an implementer cannot read the
generator and contaminate the comparison. Its `README.md` indexes them, and the ICD
text is in `spec` beside it.

## Receiver behaviour observed on AvPlan

- The ownship position, track, and geometric altitude drive the display; the
  barometric altitude shown is the iPad's own.
- The ForeFlight ID device name is shown, and the all-ones serial renders as `^^^^^^`.
- The MSL capability bit makes no visible difference.
- The extrapolated bit makes no visible difference.
- Its ADSB Status page has a Software Version field that nothing in GDL 90 or the
  ForeFlight extension can fill. A uAvionix identification message (0x25) made AvPlan
  name the bridge a SkyEcho and blanked the serial without filling the version, so it
  is not sent. AvPlan has been asked what does fill it.

## Open

- What feeds AvPlan's Software Version field.
- Traffic alt displayed as pressure alt, but can't find a place to read our pressure alt in AvPlan.
  It might show up on our ownship ghost when we turn on AvPlan live.
- ForeFlight capability flags seem to be ignored, but it appears that AvPlan hard-codes an MSL
  assumption on the geometric alt anyway (not the spec's ellipsoidal alt)
- Once the transponder is driven by the autopilot over UCP, its own reported air/ground
  state could replace the `landed_state` mirror; ArduPilot currently decodes and
  discards it.
