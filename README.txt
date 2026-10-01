Soundpack Switcher - one-click soundpack switching for GTA V
===================================================

Put Soundpack Switcher.exe anywhere (e.g. a "Tools" folder) and run it.
Needs nothing extra: it uses .NET Framework 4.8, which is built into Windows 10 and 11.

First run
- It finds GTA V automatically (Legacy, Steam, Epic, any drive). If it can't, click Change... and pick the GTA V folder.
- Each time it opens, it checks your desktop for its shortcut and offers to add it if it's missing
  (or to fix it if you moved the app). Tick "Don't ask again" to stop the check.
- Before the first switch it backs up your current RESIDENT.rpf and WEAPONS_PLAYER.rpf as "Original (backup)".
  If your game already has a soundpack installed, that pack becomes the backup. Verify the game files in the
  launcher first if you want a clean original.

Changing a soundpack
- Drag a soundpack onto the desktop shortcut (or onto the app itself): the two .rpf files, a folder that contains them
  (any depth), or a .zip/.oiv. It's saved to your library and switched in immediately.
- In the app: double-click a pack, or select it and click "Switch to this pack".
- "Desktop shortcut" makes a shortcut for one pack, so switching is a double-click.
- A pack with only RESIDENT.rpf (or only WEAPONS_PLAYER.rpf) uses the original for the missing file.
- Windows asks for admin permission when the game is in Program Files. That's needed to write there.
- Close GTA V / FiveM first; the game locks these files while it runs.

Backing up
- "Backup current..." zips the RESIDENT.rpf and WEAPONS_PLAYER.rpf that are in the game right now.
  You pick the name and the folder (it remembers the folder for next time).
- The files sit at the root of the zip, so you can drop that zip back onto the app or shortcut to switch to it.

Where things are
- Library and backup: %LOCALAPPDATA%\Soundpack Switcher  ("Open packs folder" in the app)

Windows SmartScreen
- The exe isn't code-signed, so Windows may say "Windows protected your PC". Click "More info" > "Run anyway",
  or build it yourself from SoundpackSwitcher.cs with build.bat (uses the compiler already on your PC).
