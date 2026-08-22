# Time Bomb

A Bopl Battle ability. Hot potato, with a countdown.

Use it and you are holding a live bomb with an eight second fuse. Touch another player and it
becomes theirs, and the fuse resets — so it can change hands right up to the last moment.
Whoever is holding it when the fuse runs out dies, wherever they are and whatever they are
doing.

The carrier visibly holds the bomb, its fuse burning faster and its beat growing harder as the
timer runs down, with the seconds counted above their head. There is a short grace period after
each pass so it can't ping-pong between two players standing together.

- **Fuse:** 8 seconds, reset on every pass
- **Uses:** one per round
- **Passing:** on contact, with a brief immunity after each hand-off
- **Dependencies:** BepInEx only

## Installing

Install with Thunderstore Mod Manager or r2modman, then launch the game from the mod manager.
The ability appears in the ability-select grid.

By hand: drop `TimeBomb.dll` into `BepInEx/plugins/TimeBomb/`.

## Playing online

Private lobbies work — invite a friend through Steam as usual. **Both players need the same
mods installed**, or the ability lists won't line up. Thunderstore can export your whole
profile as a code for them to import.

Public matchmaking is disabled by the game whenever any mod is loaded. That's Bopl's rule, not
this mod's.

## Building from source

Requires the .NET SDK and a Steam copy of Bopl Battle.

```bash
dotnet build -c Release
```

The paths to the game and to your mod profile are set at the top of `TimeBomb.csproj` — change
them to match your machine. **Close the game before building**; Windows locks the DLL while it
is loaded.

Artwork lives in `Resources/` and is compiled into the DLL, so PNG edits only take effect
after a rebuild.

## License

MIT
