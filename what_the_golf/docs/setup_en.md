# WHAT THE GOLF? Multiworld Setup Guide

> **Status: work in progress.** The apworld (seed generation) exists; the
> in-game mod that actually connects WHAT THE GOLF? to a multiworld does not
> yet. This guide is a placeholder describing the intended flow.

## Required software

- [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases) (the generator + server).
- The `what_the_golf` apworld installed into your Archipelago `worlds/` folder
  (or double-click a packaged `.apworld`).
- WHAT THE GOLF? on PC (Steam), plus the game mod (**not built yet** — see the
  project README for the plan).

## Generating a game

1. Create a YAML options file for WHAT THE GOLF? (Goal, Include Crowns, etc.).
2. Generate a seed with your YAML included.
3. Host the resulting game (locally or on archipelago.gg).

## Connecting (planned)

Once the mod exists, you will launch WHAT THE GOLF?, open the mod's connection
menu, and enter the server address, port, your slot name, and password. The mod
will then report level clears as checks and unlock chambers as it receives
Access items from the server.
