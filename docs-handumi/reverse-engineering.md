# How XRoboToolkit works

Single-scene Unity XR app (`Assets/Main.unity`) on PICO. It does three jobs:

1. **Pose sync** — send headset tracking to a robot PC.
2. **Remote vision** — receive a stereo stream from another Pico / ZED / PC camera.
3. **Local record** — dump VST video + pose JSON to `/sdcard/Download/`.

UI is driven by `UIOperate` (main panel) and `UICameraCtrl` (camera / record). Boot is `Main.cs`.

---

## Boot

`Main.Awake` / `OnEnable`:

- bump eye texture scale
- enable video see-through (`PXR_Manager`)
- disable the security fence (Enterprise only)
- open the VST camera (`PXR_Enterprise.OpenVSTCamera`)

`UIOperate.Awake`:

- bind enterprise service → SN (or `"TestDevice"` in editor)
- load `video_source.yml` into the video-source dropdown
- wire toggles to `TrackingData` / `TcpHandler`

If the headset is not Enterprise, SN / USB tethering / VST capture degrade; pose TCP still works.

---

## Two independent networks

Do not mix these. Pose and video use different ports and different packet formats.

```
PC Service (discovery)          UDP 29888  → popup IP list
Headset  →  PC Service          TCP 63901  pose / control  (legacy binary packets)
Headset  ↔  video peer          TCP 13579  OPEN_CAMERA cmd (v2 string protocol)
Video stream into headset       port 12345  RemoteCameraWindow + MediaDecoder
```

### Pose channel (PC Service)

1. `UIUdpReceiver` listens on **UDP 29888**.
2. PC broadcasts a packet with cmd `PACKET_CMD_TCPIP` (`0x7E`) and its IP as payload.
3. Headset shows the IP dialog. Click (or type IP) → `UIOperate.TcpConnect(ip)`.
4. `TcpHandler` opens **TCP 63901** (`NoDelay`, 15 s send timeout).
5. Handshake:
   - `0x19 CONNECT` → `"<SN>|-1"`
   - `0x6C VERSION` → `"<SN>|1.0|<apkVersion>"`
6. After VERSION is sent successfully, `_connectInited = true` and the UI can show WORKING.
7. Heartbeat every 10 s: `0x23` + SN. Auto-reconnect every 2 s on drop.

Packet layout (`PackageHandle`):

```
[head 1][cmd 1][len 4][body len][timestamp 8][end 1]
send head 0x3F   recv head 0xCF   end 0xA5
```

### Video channel (another headset / Orin / PC cam)

Separate from PC Service. `TcpManager` default port **13579**. Commands are `NetworkDataProtocol` (`command` + `byte[]`), not `NetCMD` bytes.

---

## Pose pipeline (the core of teleop)

```
UI toggles (UIOperate)
    Head / Controller / Hand / Body|Motion dropdown / HighAccuracy
        ↓ flags
TrackingData.Get()   ← main thread, every Update while Send is on
        ↓ JSON
TcpHandler send thread
        ↓ wrap as { "functionName": "Tracking", "value": <json> }
packet cmd 0x6D  PACKET_CCMD_TO_CONTROLLER_FUNCTION
        ↓ TCP 63901
PC Service
```

**Send** toggle sets `TcpHandler.SendTrackingData`. Nothing is streamed until that is on **and** TCP is inited.

**A button** (`AcontrolerTog` + `SendDataAction`) just flips the Send toggle.

`TrackingData.Get()` always writes:

- `predictTime` — Pico predicted display time (µs), meant to line up with camera frames
- `timeStampNs`
- `appState.focus`
- `Input` — active input device (hands vs controllers)

Then, only if the matching toggle / dropdown is on:

| UI | JSON key | Source |
|---|---|---|
| Head | `Head` | `PXR_System.GetPredictedMainSensorStateNew` |
| Controller | `Controller` | XR `InputDevice` left/right pose + buttons |
| Hand | `Hand` | PICO hand tracking |
| Body mode | `Body` | full-body joints (needs calibrated trackers) |
| Motion mode | `Motion` | raw tracker 6DoF + SN |

Coords are treated as right-handed: **X right, Y up, Z in**.

Body vs Motion:

- **Body** → `StartBodyTracking` (LOW or HIGH if HighAccuracy). PC Service will report **24 body joints**, not tracker count.
- **Motion** → object tracking. PC Service tracker count only updates in this mode.

If trackers are missing / uncalibrated, the dropdown snaps back to None.

---

## Camera listen (remote stereo)

Operator headset (H2), source is another Pico / ZED / PC:

1. Pick source in dropdown (`PICO4U`, `ZEDMINI`, …) from `video_source.yml`.
2. **Listen** → `UICameraCtrl.OnListenCameraBtn`.
3. `VideoSourceManager.UpdateVideoSource` applies shader ratios / rect size.
4. IP dialog → `RequestCameraStream(ip)`.
5. Open `RemoteCameraWindow` listening on **12345** (width/height/fps/bitrate from YAML).
6. `TcpManager.StartClient(ip)` on **13579**.
7. Send `OPEN_CAMERA` with `CameraRequestSerializer` (resolution, 3D render mode, camera type, local IP, port 12345).
8. Peer starts encoding; H2 decodes via `MediaDecoder` / AAR `com.picovr.robotassistantlib.MediaDecoder`.
9. Controller **B** switches SBS vs stereo-3D (`SetLERE`).

H1 (robot-eye Pico) mostly just sits there with VST permission; it is the encoder side.

YAML (`Assets/StreamingAssets/video_source.yml`, also overridable on device under `Android/data/com.xrobotoolkit.handumi/files/`):

- `CamWidth` / `CamHeight` / `CamFPS` / `CamBitrate`
- `visibleRatio` / `contentRatio` / `heightCompressionFactor` / `RawImageRectSize` — stereo shader

---

## Local record

`UICameraCtrl` Record button. Must pick **Tracking** and/or **Vision**.

| Mode | What is written |
|---|---|
| Vision | `/sdcard/Download/CameraRecord_<ts>.mp4` via `CameraHandle` (Pico capture AAR) |
| Tracking | `/sdcard/Download/trackingData_<ts>.txt` — first line camera in/extrinsics, then one `TrackingData` JSON per frame |
| Both | both, started together after the resolution dialog |

Vision record only works on **Pico 4 Ultra Enterprise** with camera entitlement. Status text is the native capture state enum (idle → opened → recording ≈ 6).

Save Camera Parameters writes intrinsics/extrinsics to `persistentDataPath`.

---

## UI → code map

| Panel item | Code |
|---|---|
| SN / IP / FPS / Status | `UIOperate`, `TcpState`, `FPSDisplay` |
| PC Service IP / Enter | `UIUdpReceiver`, `IpInputDialog`, `TcpHandler.Connect` |
| Head / Controller / Hand | `TrackingData.Set*On` |
| PICO Motion Tracker mode + TrackerNum | `UIOperate.OnBodyModeDrop` / `UpdateBodyTracking` |
| Send / Switch w/ A | `TcpHandler.SendTrackingData`, `SendDataAction` |
| Video source + Listen | `VideoSourceConfigManager`, `UICameraCtrl.OnListenCameraBtn` |
| Record Tracking / Vision | `UICameraCtrl.OpenRecord` |
| Log | `LogView` / `LogWindow` (also hooked from `Main` log callback) |

---

## Native bridge

C# talks to Java AARs through JNI:

- `com.picovr.robotassistantlib.*` — TCP helpers, `MediaDecoder`, camera UDP handle
- `com.pxr.capturelib.PXRCameraCallBack` — `CameraHandle` capture state machine

`AndroidProxy` is the Unity object Java calls back into (`key|value` strings). Permissions (camera / mic / storage) are requested from `UIOperate.OnOpenCameraOperate`.

Manifest marks the app as VR (`pvr.app.type = vr`) and requests `INTERNET`, `CAMERA`, `ACCESS_NETWORK_STATE`.

---

## What to change for HandUMI-style teleop

Keep the pose channel intact unless the PC service changes too:

1. Payload shape → `TrackingData.Get`
2. When / how it is sent → `TcpHandler` send thread (`functionName = "Tracking"`)
3. Enable flags / A-button → `UIOperate`
4. Ports / handshake → `TcpHandler.TCP_PORT` (63901), `UIUdpReceiver` (29888), `NetCMD`

Vision is optional and separate. Do not reuse `TcpHandler` packets for video; that path is `TcpManager` + `NetworkCommander.OPEN_CAMERA` + port 12345.
