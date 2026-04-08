# BazaarLogic Mod

BazaarLogic Mod exports in-game board state to the BazaarLogic website so you can track runs, replay boards, and share builds. It also supports a "streamer mode" display name so your in-game username is not shown on the site or in-game banners.

See it in action: https://youtu.be/WVbbyau6Eeg

## Features
- Uploads current run state and encounter snapshots to bazaarlogic.quest (powers Follow + replays).
- Updates the current run state about every 2 seconds while you're in a run.
- Tracks runs and encounters for later replay.
- Opens your current board on the site with a single hotkey (B).
- Uses a configurable display name for privacy/streaming (also replaces in-game banner name).
- Checks for updates on launch (Windows installer builds).

## Requirements
- The Bazaar installed (Steam or Tempo Launcher).
- BepInEx 5.4.x (`BepInEx_win_x64_*.zip` is included in the release zip beside the installer).
- A BazaarLogic account to export your config.

## Install (Windows)
1. Download the latest release zip from:
   https://github.com/oceanseth/BazaarLogicMod/releases
2. Extract the zip to a folder.
3. Run `BazaarLogicModInstaller.exe`.
4. On the BazaarLogic site, open Settings (gear icon) and export `BazaarLogic.config`.
5. Put `BazaarLogic.config` in the same folder as the installer and run the installer again.
   - The installer will place the config in `BepInEx/config/BazaarLogic.cfg`.

If you use the Tempo Launcher and it blocks modded launches, use `customlauncher.bat` from the release zip. If you use Steam, launch the game normally.

## Manual Install (Windows)
1. Extract `BepInEx_win_x64_5.4.23.5.zip` into the folder that contains `TheBazaar.exe`.
2. Copy `BazaarLogicMod.dll` into `BepInEx/plugins`.
3. Create `BepInEx/config` if it does not exist.
4. Export `BazaarLogic.config` from the BazaarLogic site and place it in `BepInEx/config`.
5. Rename it to `BazaarLogic.cfg`.

## Usage
- Press `B` in game to open your board on bazaarlogic.quest.
- Click Follow on the site to view your current run state while you play.
- Runs are listed in the Runs tab and can be replayed after battles.

## Configuration
Config is stored at:
`BepInEx/config/BazaarLogic.cfg`

Key fields used by the mod:
- `Authentication.Uid`
- `Authentication.DisplayName`

Token fields in the config are obsolete and not required.
If `Authentication.Uid` is empty, the mod will not sync runs to the site.

## Development
- Update `GamePath` in `BazaarLogicMod.csproj` if your install is not auto-detected.
- Build with `dotnet build` or `dotnet publish -c Release`.
- The build target copies the DLL into `BepInEx/plugins` and deletes other DLLs in that folder. Adjust the target if you keep multiple plugins.

## Release Checklist
1. Update version in `BazaarLogicMod.csproj` and `BazaarLogicModInstaller/Program.cs`.
2. Replace `BazaarLogicModInstaller/BepInEx_win_x64_5.4.23.5.zip` with the BepInEx package you want to ship.
3. `dotnet publish -c Release`
4. Verify the publish folder contains `BazaarLogicModInstaller.exe`, `BazaarLogicMod.dll`, `Readme-what is this.txt`, `customlauncher.bat`, and the intended `BepInEx_win_x64_*.zip`.
5. Zip the publish folder as `BazaarLogicModInstaller-vX.Y.Z.zip`.
6. Tag the release and upload the zip to GitHub Releases.

## Contributing
Open an issue or share feedback in the community Discord.

## License
MIT. See `LICENSE`.
