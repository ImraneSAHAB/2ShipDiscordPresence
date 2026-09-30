# 2Ship Discord Presence (`2ShipDiscordPresence.exe`)

Automatic Discord Rich Presence status detector for **2Ship2Harkinian** (the PC port of *The Legend of Zelda: Majora's Mask*).

---

## Features

- 🔍 **Automatic Detection**: Monitors the `2ship.exe` (or `2Ship2Harkinian.exe`) process.
- 🎮 **Custom Discord Rich Presence**:
  - **Main Title**: `Majora's Mask`
  - **Details**: Current location, such as `South Clock Town`, `Termina Field`, or `Woodfall Temple (Odolwa)`.
  - **State**: In-game day, time, and hearts, such as `1st Day (06:00 AM) • ❤️ 3/3`.
  - **Small Icon Badge**: Profile picture badge with hover tooltip `"Made by Imashiro"`
  - **Icon**: Majora's Mask
  - **Playtime Counter**: Live playtime counter
- 🔔 **System Tray Integration**: Runs silently in the background with a Majora's Mask icon in the Windows notification area (System Tray). Double-click or right-click to toggle the console or exit.
- ⚙️ **`config.json` File**: Easily customize status text, images, or `client_id` without recompiling.
- ⚡ **Lightweight & Standalone**: Native Windows executable, no Python or Node.js required.

---

## Usage

1. Start **`2ShipDiscordPresence.exe`**.
2. Launch your game **`2ship.exe`**.
3. The app automatically detects the game and updates your Discord Presence!
4. The app runs in your Windows System Tray (near the clock). Right-click the Majora's Mask icon to view status or exit.
5. As soon as you exit the game, the Discord status is automatically cleared.

---

## Building from Source

Run [`build.bat`](build.bat) to regenerate the 64-bit `2ShipDiscordPresence.exe`. Run `powershell -ExecutionPolicy Bypass -File .\test.ps1` to check location decoding and day/time formatting.

## Live game locations

The monitor reads the running game's memory without modifying it and refreshes the presence every `check_interval_seconds` (3 seconds by default). It supports the 102 defined entrance areas, including interiors, boss rooms, seasonal areas, and inverted temples. Location names follow the existing English display.

On the title screen and file-selection menus, Discord displays `In menus`. Location, day, time, and hearts appear only after a save has entered gameplay. Exact menu detection uses the matching `debug/2ship.pdb` file distributed with the Windows build of 2Ship2Harkinian.

Locations use `Save.entrance` and the official [2Ship EntranceSceneId table](https://github.com/HarbourMasters/2ship2harkinian/blob/e8757c14a0fc8701461b0458c9ed72c118bcfc67/mm/include/z64scene.h#L636). These IDs differ from the game's `SceneId` values. The location reflects the area's entrance, not Link's exact position within a room, and may update during a loading transition.

When an entrance is unknown, the configured `details` text is shown while available hearts and game time remain visible. When game memory cannot be read, both configured texts are used. The elapsed session timer stays visible in either case. Memory detection depends on the port's save structure; future builds may require adjustments. Menu screens can retain the last loaded save data.

After rebuilding, exit any running copy from its system tray menu and relaunch the executable. To verify in game, travel from South Clock Town to Termina Field and enter a shop or dungeon: the location should change while the hearts, game clock, and session timer remain present.
