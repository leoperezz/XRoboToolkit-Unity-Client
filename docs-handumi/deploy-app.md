# Deploy XRoboToolkit as an APK (PICO)

This is a Unity XR client for **PICO 4 / PICO 4 Ultra**, not a regular phone app. The APK is built from the Unity Editor.

Target Unity version: **2022.3.16f1** (see `ProjectSettings/ProjectVersion.txt`). Use this exact version.

Package name: `com.xrobotoolkit.handumi`

---

## 1. Install Unity Hub (Ubuntu / Debian)

```bash
sudo apt install curl
sudo install -d /etc/apt/keyrings
curl -fsSL https://hub.unity3d.com/linux/keys/public | sudo gpg --dearmor -o /etc/apt/keyrings/unityhub.gpg
echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/unityhub.gpg] https://hub.unity3d.com/linux/repos/deb stable main" | sudo tee /etc/apt/sources.list.d/unityhub.list
sudo apt update
sudo apt install unityhub
```

Sign in with a Unity account. A **Personal** license is enough.

Optional Linux libraries if the Editor fails to start:

```bash
sudo apt install libgtk-3-0 libnss3 libasound2t64 libxss1 libglu1-mesa
```

Plan for **15–25 GB** free disk space (Editor + Android modules + project `Library/` cache).

---

## 2. Install Unity 2022.3.16f1 with Android modules

1. Open **Unity Hub**.
2. Go to **Installs → Install Editor**.
3. Select **2022.3.16f1**.
4. Enable these modules:
   - **Android Build Support**
   - **Android SDK & NDK Tools**
   - **OpenJDK**
5. Install and wait until it finishes.

Do **not** chase the README versions of Android SDK 29 / NDK 21.4. Those are outdated. Unity 2022.3 ships the correct toolchain (NDK r23b, OpenJDK, SDK tools). Leave **Edit → Preferences → External Tools** on the default “Installed with Unity” paths.

Android Studio is optional. You only need it if you want a separate SDK Manager or Device File Explorer.

The PICO Integration SDK **3.1.2** is already in this repo (`PICO Unity Integration SDK/`). You do not need to download it separately.

---

## 3. Open the project

1. In Unity Hub → **Projects → Add** → select this repository folder.
2. Open it with **2022.3.16f1**.
3. Wait for the first import (`Library/` generation). This can take several minutes.

---

## 4. Switch platform to Android

1. **File → Build Settings**.
2. Select **Android**.
3. Click **Switch Platform** if it is not already active.
4. Keep **Build App Bundle (Google Play)** unchecked. You want an `.apk`, not an `.aab`.
5. Scene in build should include `Assets/Main.unity`.

Project Android settings already in the repo:

| Setting | Value |
|---|---|
| Minimum API | 30 |
| Target API | 31 |
| Architecture | ARM64 |
| Scripting backend | IL2CPP |

---

## 5. Fix signing (“Can not sign the application”)

The repo ships with a Windows keystore path that does not exist on Linux:

`C:/Users/Admin/Documents/lyu20/user.keystore`

If you hit **Build** like this, Unity shows:

> Can not sign the application  
> Unable to sign the application; please provide passwords!

### Option A — Debug keystore (this is what worked)

Use this to sideload and test on the headset.

1. Close the error dialog (**Ok**).
2. In **Build Settings**, click **Player Settings...**.
3. Open the **Android** tab (robot icon).
4. Expand **Publishing Settings**.
5. **Uncheck Custom Keystore**.
6. Close Player Settings and click **Build** again.

Unity will sign the APK with its debug keystore.

### Option B — Your own keystore (better for repeated installs)

Use this if you will keep updating the same app on the Pico without uninstalling it every time.

1. **Player Settings → Android → Publishing Settings**.
2. Check **Custom Keystore**.
3. Open **Keystore Manager → Create New → Anywhere**.
4. Save it somewhere local, for example `~/keys/xrobotoolkit.keystore`.
5. Fill in keystore password, alias (e.g. `xrobotoolkit`), alias password, and dummy identity fields.
6. Confirm. Publishing Settings should now show the keystore path, alias, and both passwords.
7. Click **Build** again.

Save those passwords. If you lose them, you cannot update `com.xrobotoolkit.handumi` without uninstalling the old APK from the headset.

If Unity forgets the passwords between sessions, reopen **Publishing Settings** and type them again before building. You do not need to recreate the keystore.

---

## 6. Build the APK

From **File → Build Settings → Build**, choose an output path, for example:

`Builds/Android/XRoboToolkit.apk`

Or use the project one-click builder:

- Menu: **Build → One-Click Package**
- Shortcut: **Ctrl+Shift+B** (Linux / Windows)

One-click output:

```
Builds/Android/XRoboToolkit_<version>.apk
```

Current product version in Player Settings is `1.1.1`.

---

## 7. Install on the PICO headset

### Enable developer mode on the Pico

1. On the headset: **Settings → General → About**.
2. Tap **Software Version** about 7–12 times until **Developer** appears.
3. Open **Developer**.
4. Enable **USB Debugging** (and **Install via USB** if available).
5. Connect USB-C and accept the debugging prompt on the headset.

### Install with adb

`adb` is included with the Android SDK Unity installed, or install it from apt:

```bash
sudo apt install android-tools-adb
adb devices
adb install -r /path/to/XRoboToolkit.apk
```

Then open **XRoboToolkit** from the Pico launcher.

---

## 8. After install (runtime notes)

- Pico and robot PC must be on the **same Wi-Fi** for pose sync / teleoperation.
- The robot PC must be running **XRoboToolkit PC Service**.
- First launch may show **No entitlement info** if the headset is offline. Connect it to the public internet and run the app again.
- VST camera capture / remote stereo still needs **PICO Enterprise camera permission**. Without it the APK still installs and runs; camera features will not work.

To push a custom video source file:

```bash
adb pull /sdcard/Android/data/com.xrobotoolkit.handumi/files/video_source.yml
# edit the file
adb push video_source.yml /sdcard/Android/data/com.xrobotoolkit.handumi/files/video_source.yml
```

Revert to default:

```bash
adb shell rm /sdcard/Android/data/com.xrobotoolkit.handumi/files/video_source.yml
```

---

## Quick checklist

1. Install Unity Hub + **2022.3.16f1** + Android / SDK / NDK / OpenJDK modules.
2. Open this repo in that Editor.
3. **Build Settings → Android → Switch Platform**.
4. **Player Settings → Publishing Settings → uncheck Custom Keystore** (or create your own).
5. **Build** the `.apk`.
6. Enable Pico developer mode + USB debugging.
7. `adb install -r your.apk`.
