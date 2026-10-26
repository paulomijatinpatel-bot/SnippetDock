# SnippetDock

A small Windows desktop text snippet manager. It stores data locally and does not use AutoHotkey.

## Requirements
- Windows 10 or 11
- .NET 8 SDK (official Microsoft download): https://dotnet.microsoft.com/en-us/download/dotnet/8.0

## Run from source
1. Extract the ZIP.
2. Open the extracted `SnippetDock` folder.
3. Click the File Explorer address bar, type `powershell`, and press Enter.
4. Run:
   ```powershell
   dotnet run
   ```

## Use
- Click **New**, enter a name and text, then **Save snippet**.
- Select a snippet and assign it to one of the 10 slots.
- Choose the modifier/key combination and click **Assign slot**.
- Focus the destination application and press the assigned shortcut.
- Default shortcuts are Ctrl+Alt+1 through Ctrl+Alt+9, and Ctrl+Alt+0 for slot 10.

## Local data
Saved data is at `%LOCALAPPDATA%\SnippetDock\snippets.json`.

## Build a self-contained executable (optional)
From this folder:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```
The executable will be under `bin\Release\net8.0-windows\win-x64\publish\`.

## Safety
- Review the source code before running it.
- This project is unsigned and may prompt security software to assess it. Do not disable Defender, restore quarantined files, or add exclusions to force it to run.
- Global hotkeys may fail if another app or Windows already uses the chosen combination. Use a different modifier/key combination in that case.
- Pasting uses the clipboard, so it replaces the clipboard contents.

