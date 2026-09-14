# 2Ship Discord Presence (`2ShipDiscordPresence.exe`)

Automatic Discord Rich Presence status detector for **2Ship2Harkinian** (the PC port of *The Legend of Zelda: Majora's Mask*).

---

## Features

- 🔍 **Automatic Detection**: Monitors the `2ship.exe` (or `2Ship2Harkinian.exe`) process.
- 🎮 **Custom Discord Rich Presence**:
  - **Main Title**: `Majora's Mask`
  - **Details**: `The Legend of Zelda: Majora's Mask`
  - **State**: `Playing 2Ship2Harkinian`
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

If you modify the source code [`Program.cs`](file:///d:/DEV/Projets/2ShipDiscordPresence/Program.cs), simply run [`build.bat`](file:///d:/DEV/Projets/2ShipDiscordPresence/build.bat) to regenerate `2ShipDiscordPresence.exe`.
