# Installing Trayloom on another PC

Trayloom runs on any Windows 10 or Windows 11 PC that meets the requirements below. Nothing in it is tied to the PC it was
built on: settings, logs and data are created fresh in the new user's own profile, and the install needs no administrator
password.

## 1. Check the laptop

| Check | Where to look | Needed |
| --- | --- | --- |
| Windows version | Press **Win+R**, type `winver`, press Enter | Windows 11, or Windows 10 version **2004 (build 19041)** or newer |
| 64-bit | Settings > System > **About** > *System type* | "64-bit operating system". Windows 11 on ARM (Snapdragon) runs it too. Windows 10 on ARM does not |
| Smart App Control (Windows 11 only) | Windows Security > App & browser control > **Smart App Control settings** | *Off* or *Evaluation*. See step 4 if it says *On* |
| Free space | File Explorer > This PC | About 200 MB |

Nothing else needs to be installed first: the .NET runtime is inside the download.

## 2. Get the files

Use **one** of these:

* **From GitHub Releases** (easiest for other people): open
  [github.com/Naveen022006/Trayloom/releases](https://github.com/Naveen022006/Trayloom/releases) and download
  `Trayloom-win-x64.zip` (about 75 MB). If the page is empty, no release has been published yet; see
  [Publishing a release](#publishing-a-release-for-the-maintainer) below.
* **Shared directly:** copy `Trayloom-win-x64.zip` and `Trayloom-Setup.cmd` from the `dist` folder of a PC that built it
  (USB drive, Google Drive, OneDrive…). Keep both files in the same folder.
* **Build it on that laptop:** see [Build from source](../README.md#build-from-source). A build made on the laptop itself is
  not blocked by Smart App Control.

## 3. Unblock and unzip

Windows marks files downloaded from the internet. Removing that mark first avoids a warning for every file:

1. Right-click `Trayloom-win-x64.zip` > **Properties**.
2. At the bottom of the *General* tab, tick **Unblock** (if it is there) > **OK**.
3. Right-click the zip > **Extract All…** > choose a folder, for example `Downloads\Trayloom`.

## 4. Start the installer

1. Open the extracted folder and double-click **`Trayloom.exe`**. (Or double-click `Trayloom-Setup.cmd` next to the zip: it
   unzips and starts the installer for you.)
2. If a blue **"Windows protected your PC"** window appears: click **More info** > **Run anyway**. Trayloom is not
   code-signed yet, so Windows does not recognise the publisher.
3. If Windows says it **blocked** the app and offers no *Run anyway*, Smart App Control is on. Trayloom shows a short
   explanation. The options are:
   * build Trayloom on that laptop (see step 2), or
   * sign the files with your own code-signing certificate, or
   * turn Smart App Control off in Windows Security. That is the laptop owner's decision: once it is off, Windows can only
     turn it back on by resetting or reinstalling Windows.

   Trayloom itself never changes this setting.
4. Choose **Install Trayloom**. It installs for this Windows account only:
   * files go to `%LOCALAPPDATA%\Programs\Trayloom`,
   * a Start menu shortcut is added (a Desktop shortcut and *Start when I sign in* are optional),
   * it is listed in Settings > Apps > **Installed apps**.

   **Just run it** starts it without installing (a portable copy).
5. The first start after copying can take around 10 seconds while Windows scans the files. Later starts take about a second.

## 5. First run

1. A short introduction asks for a starting bundle. **Essentials** (Volume mixer, System monitor and Keep awake) is a good
   start; every other tool is one click away in the Features hub.
2. Find the Trayloom icon next to the clock. If it is hidden behind the **^** arrow, the introduction has a button to keep it
   visible (or: Settings > Personalization > Taskbar > *Other system tray icons*).
3. Click the icon for the panel, right-click it for the menu, press **Ctrl+Alt+Space** for the Command Bar.

Each Windows account on a PC that signs in and wants Trayloom installs it for itself; accounts do not share settings.

## What depends on that laptop

| Feature | Needs |
| --- | --- |
| Dynamic Island music controls, lyrics | Any player that shows in Windows' media controls (Spotify, Apple Music, a browser…) |
| Island Apple Music library, search and "play this song" | **Apple Music for Windows** from the Microsoft Store, signed in. Picking a song from the library works with Apple Music set to **English**; in other languages Trayloom does not find the menu items and plays nothing rather than guess. Play, pause, next and seeking work in any language |
| AI Agents | The Claude desktop app, Claude Code or Codex signed in on that laptop. Each person sees their own usage |
| External monitor brightness | A monitor with DDC/CI switched on in its own menu |
| Packages and App updates | `winget` (App Installer from the Microsoft Store; present on most PCs) |
| Screen capture, microphone, camera, calendar, notifications | Windows asks for permission the first time; the Features hub lists them first |
| Tools that act on other windows | They cannot affect apps running as administrator unless Trayloom also runs as administrator |

Some keyboard shortcuts may already be used by that laptop (for example Intel graphics drivers use Ctrl+Alt+Arrow to rotate
the screen). Trayloom picks a fallback and shows it in Settings > Keyboard shortcuts, where every shortcut can be changed.

## Updating

Download the new zip, unzip it and run `Trayloom.exe`. The installer sees the installed copy and offers **Update**, which
replaces it and keeps the settings.

## Uninstalling

Settings > Apps > Installed apps > **Trayloom** > Uninstall. Trayloom quits, removes its files, shortcuts and start-up entry,
and asks whether to keep the settings (`%APPDATA%\Trayloom`).

## If something goes wrong

* Right-click the tray icon > **Report a problem…** opens the log folder (`%APPDATA%\Trayloom\logs\trayloom.log`).
* Nothing happens when you start it: look for Trayloom in Task Manager. It may already be running in the tray.
* An antivirus warning: Trayloom is unsigned and its input tools use keyboard and mouse hooks, which some antivirus programs
  flag. Only use a zip from the GitHub Releases page or from someone you trust, or build it yourself. What goes online is
  listed under [Privacy](../README.md#privacy).

## Publishing a release (for the maintainer)

Do this once per version so other people can download Trayloom from GitHub:

1. Build the package on your PC:

   ```bash
   powershell -ExecutionPolicy Bypass -File scripts\package.ps1
   ```

   It writes `dist\Trayloom-win-x64.zip` and `dist\Trayloom-Setup.cmd`.
2. On GitHub, open the repository > **Releases** > **Draft a new release**.
3. **Choose a tag** > type the version from `Directory.Build.props` with a `v` in front (for example `v0.2.0`) > *Create new
   tag*.
4. Title it, for example *Trayloom 0.2.0*, and add a few lines about what changed.
5. Drag `Trayloom-win-x64.zip` and `Trayloom-Setup.cmd` into the *Attach binaries* box and wait for both uploads.
6. Click **Publish release**. The download link in the README now works.

With the GitHub CLI signed in (`gh auth login`), steps 2 to 6 are one command:

```bash
gh release create v0.2.0 dist/Trayloom-win-x64.zip dist/Trayloom-Setup.cmd --title "Trayloom 0.2.0" --notes "First public build"
```
