# OTH Screenshot Backdrop

A small standalone MelonLoader utility for taking clean Over The Hill screenshots.

It creates a full-screen, solid-color UI layer that covers the 3D game world while remaining underneath other UI canvases. This is intended to keep the game's HUD and OTH RoadMeter visible while hiding the scenery.

## Default controls

- `F9` — toggle the backdrop on/off.
- The backdrop starts disabled every time the game launches.
- Default color: warmer, slightly darker cream (`#E9DFCB`).

## Installation

1. Build the project against your Over The Hill installation.
2. Copy `OTH_ScreenshotBackdrop.dll` to the game's `Mods` folder.
3. Launch the game once to generate the configuration file.
4. Edit:
   `Over The Hill/UserData/OTH_ScreenshotBackdrop/OTH_ScreenshotBackdrop.cfg`

## Configuration

```ini
[General]
Color=#E9DFCB
ToggleKey=F9
```

`Color` accepts `#RRGGBB` and `#RRGGBBAA`.

Set `ToggleKey=None` to disable the hotkey.

Restart the game after editing the configuration.

## Build

Open a terminal in this project directory and run:

```powershell
dotnet build .\OTH_ScreenshotBackdrop.csproj -c Release
```

The project uses the same default game directory as the current OTH RoadMeter project:

`E:\Games\Over The Hill`

If the game is installed elsewhere, override it:

```powershell
dotnet build .\OTH_ScreenshotBackdrop.csproj -c Release -p:GameDir="D:\Games\Over The Hill"
```

The output DLL is created in:

`bin\Release\net6.0\OTH_ScreenshotBackdrop.dll`

## Rendering note

The backdrop uses a dedicated `Screen Space - Overlay` Canvas with the lowest available sorting order. This should place it above the 3D scene and below ordinary game UI canvases, without changing camera settings or disabling scene renderers.

The actual UI ordering must still be verified in-game, especially for any game UI that uses an unusually low sorting order or a non-standard rendering path.
