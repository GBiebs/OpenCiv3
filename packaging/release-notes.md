A build of OpenCiv3, the Civilization III remake, from GBiebs's "Civ3 Complete" branch: Civ3's rules filled in (diplomacy, culture, golden ages, air and naval warfare, victories and more) and multiplayer (hotseat, play by email, and online).

## What you need

- Windows 10 or 11 (64-bit).
- **Civilization III Complete installed** (Steam or disc). The game uses Civ3's own art and finds it by itself.

## Install

1. Download **OpenCiv3-Setup-{VERSION}.exe** below and run it.
2. Windows will probably say "Windows protected your PC", because the installer isn't signed. Click **More info**, then **Run anyway**.
3. It installs for your account only (no administrator needed), with a Start menu shortcut and a desktop shortcut.

Saved games go in Civ3's `Conquests\Saves` folder. To update, run a newer installer over this one. To remove it, use Windows Settings > Apps.

## Found a bug?

Please report it at https://github.com/GBiebs/OpenCiv3/issues (a free GitHub account is needed). It helps a lot to include:

- what you did and what happened;
- the version, shown on the main menu under the title;
- the game's log: `log.txt` in `%LOCALAPPDATA%\Programs\OpenCiv3` (paste that path into File Explorer's address bar). Each start of the game begins a new one and keeps the one before as `log-previous.txt`, so if you've restarted since, send that one;
- a save from just before it happened: the game saves itself every turn in Civ3's `Conquests\Saves\Auto` folder.

## Playing together online

- Everyone needs **the same version**: when there's a new one, everyone updates.
- One player hosts: **Multiplayer > Host a Game Online**. They send the others the invite code and the game password shown in the lobby.
- The others choose **Multiplayer > Join a Game Online** and paste them in.
- The host's computer has to be reachable. The easiest way is [Tailscale](https://tailscale.com/): the host shares their computer with the other players.

This is an early build of a fan project. Expect rough edges.
