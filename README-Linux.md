# Install on Linux
Credits to @yggraszill for the guide!

## 1. Download the Installer
Download from [playthebazaar.com/download](https://playthebazaar.com/download)

## 2. Add the Installer to Steam
1. Open **Steam**
2. Go to **Games > Add a Non-Steam Game**
3. Click **Browse**, and select the `Tempo Launcher - Beta Setup 1.0.4.exe` installer you downloaded

## 3. Set Compatibility
1. In your Library, right-click the installer
2. Choose **Properties > Compatibility**
3. Check **"Force the use of a specific Steam Play compatibility tool"**
4. Select **Proton 9.0-4**

## 4. Install the Launcher
1. Run the `.exe` installer through Steam
2. Recommended install location: `~/Games/` (or somewhere easy to remember)

## 5. Sign in and Launch the Game
1. Open the installed **Tempo Launcher**
2. Log into your account
3. Let the launcher finish downloading and unpacking the game
4. Launch the game at least once to finalize setup

## 6. Close the Game
Once the game has launched successfully, **close it**.

## 7. Enable `winhttp` via Wine
Open a terminal and run:

```bash
winecfg
```

In the window that appears:
1. Go to the **Libraries** tab
2. From the drop-down, choose `winhttp` and click **Add**
3. Confirm `winhttp (native, builtin)` appears under existing overrides
4. Click **OK** to close

## 8. (Optional) Clean up Installer
You can remove the Installer from Steam if you'd like. It served its purpose.

## 9. Add the Actual Launcher to Steam
1. Go to **Add a Non-Steam Game** again
2. Browse to the installed launcher:
   ```bash
   ~/Games/Tempo Launcher - Beta/Tempo Launcher - Beta.exe
   ```

## 10. Set Launch Options
In the new Steam entry:
1. Right-click → **Properties**
2. Under **LAUNCH OPTIONS**, paste:
   ```bash
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```
3. Make sure Proton 9.0-4 is selected under Compatibility again

## 11. (Optional) Test the Game
Launch the game again via Steam and ensure it works correctly with these settings.

## 12. Install the Mod
### Option 1: Wine Installer
1. Run `BazaarLogicModInstaller.exe` via Wine
2. Installation path should be: `Z:/home/[your-username]/Games/Tempo Launcher - Beta/The Bazaar game_64/bazaarwinprodlatest/`

### Option 2: Manual Install
[Content missing]

## 13. Done!
Launch the game from your Steam library. The Mod should be working.
