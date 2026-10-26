# SNAPTIC Demo Autosplitter

A LiveSplit autosplitter component for `SNAPTIC Demo` that reads the game's
Godot state directly from memory.

## Activate It in LiveSplit

1. Open Livesplit
2. Right-Click Livesplit and Select **Edit Splits**
3. Type "SNAPTIC Demo" into the Game Name
4. Press the "Activate" Button on the Autosplitter Row when it pops up below with the correct game name.

### Additionally if its not on Livepslit Autosplitters
1. Copy `Components/LiveSplit.SNAPTICDemo.dll` into LiveSplit's `Components`
   folder.
2. Restart LiveSplit.
3. Right-click LiveSplit and select **Edit Layout**.
4. Click **+**, open **Control**, and add **SNAPTIC Demo Autosplitter**.
5. Open **Layout Settings** and select the autosplitter tab to configure it.

## Features
All Features are Based off of the Leaderboard Rules from https://www.speedrun.com/snaptic


- Starts after the second opening cutscene finishes or is skipped and the
  player gains control.
- Splits when the active player body touches the final platform or either pot
  at the ending location.
- Supports the character, marble, and other active player-body forms through
  the same position reader.
- Keeps a detected end contact pending until LiveSplit successfully splits.
- Prevents a successful end split from queuing another split.
- Resets LiveSplit when the game returns to the main menu containing the Play
  button.
- Does not pause Game Time or perform load removal.

## Settings

### Start

Starts LiveSplit when control is gained after the second opening cutscene.

### Split

Enables or disables automatic splitting. The indented **End Split** option
controls the final location split.

The end split uses contact-sized world bounds around:

- the surface of the final `UpperPlatform`;
- the upright `UpperPot2`;
- the inverted `SkyPot` beneath it.

### Reset

Resets LiveSplit when SNAPTIC returns to its main menu.

Start, Split, End Split, and Reset are enabled by default.

## Supported Game

- Game version: `0.84.0.0`
- Steam build: `25857588`
- Steam App ID: `5273300`
- Godot version: `4.7.1.stable.custom_build`
- Platform: `Windows x64`

## How It Works

The component attaches to the 64-bit `snaptic.exe` process and locates the
game's Godot `LevelLoader`, cutscene, and player data.

Start is detected when the second cutscene releases its request and returns
control to the player. Reset is detected when the active level returns to the
main menu. For the end split, the component follows the player to the current
active body and compares its world position against the final platform and pot
collision regions. Fast movement between memory samples is also checked so a
brief contact is not missed.

The autosplitter reads process memory only. It does not modify SNAPTIC Demo or
its files.

## Files

- `LiveSplit.SNAPTICDemo/` - component source code
- `Components/LiveSplit.SNAPTICDemo.dll` - compiled LiveSplit component

## License

MIT License. See [LICENSE](./LICENSE).
