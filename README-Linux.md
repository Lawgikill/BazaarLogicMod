# Install on Linux
Credits to @yggraszill for the original guide.

This guide assumes you are installing the Windows version of The Bazaar through Steam/Proton.

## 1. Install the Tempo Launcher
Download the Windows installer from:
https://playthebazaar.com/download

## 2. Add the Installer to Steam
1. Open Steam.
2. Go to Games > Add a Non-Steam Game.
3. Browse and select the Tempo Launcher installer `.exe` you downloaded.

## 3. Set Proton Compatibility
1. In your Steam library, right-click the installer.
2. Choose Properties > Compatibility.
3. Check "Force the use of a specific Steam Play compatibility tool".
4. Select Proton 9.0-4 (or your preferred recent Proton version).

## 4. Install the Launcher
1. Run the installer through Steam.
2. Recommended install location: `~/Games/` (or anywhere you can find easily).

## 5. Sign in and Install the Game
1. Open the installed Tempo Launcher.
2. Log in and let it download and unpack the game.
3. Launch the game once, then close it.

## 6. Enable winhttp via Wine
Run:

```bash
winecfg
```

In the window:
1. Go to the Libraries tab.
2. Add `winhttp` and set it to `native, builtin`.
3. Click OK.

## 7. Add the Launcher to Steam
1. Add a Non-Steam Game again.
2. Browse to the installed launcher, for example:
   ```bash
   ~/Games/Tempo Launcher - Beta/Tempo Launcher - Beta.exe
   ```

## 8. Set Launch Options
1. Right-click the new Steam entry and open Properties.
2. Under Launch Options, set:
   ```bash
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```
3. Ensure Proton is still selected under Compatibility.

## 9. Install the Mod
You can either run the Windows installer through Wine or install manually.

### Option A: Wine Installer
1. Run `BazaarLogicModInstaller.exe` via Wine/Proton.
2. Point it at your game folder (the folder that contains `TheBazaar.exe`).

### Option B: Manual Install
1. Extract `BepInEx_win_x64_5.4.23.2.zip` into the folder that contains `TheBazaar.exe`.
2. Copy `BazaarLogicMod.dll` into `BepInEx/plugins`.
3. Create `BepInEx/config` if it does not exist.
4. Export `BazaarLogic.config` from the BazaarLogic site and place it in `BepInEx/config`.
5. Rename it to `BazaarLogic.cfg`.

## 10. Launch the Game
Start the game from Steam. Press `B` in game to open your board on bazaarlogic.quest and use Follow for live sync.
