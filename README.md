# Time Bomb

A Bopl Battle ability. Hot potato, with a countdown.

<img width="800" height="450" alt="ezgif-3dbf5eab1ecf09d1" src="https://github.com/user-attachments/assets/bef38401-8a63-428c-9c5a-efb553ae7ee0" />

Use it and you are holding a live bomb with an eight second fuse. Touch another player and it
becomes theirs, and the fuse resets.
Whoever is holding it when the fuse runs out explodes, wherever they are and whatever they are
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

Private lobbies work; invite a friend through Steam as usual. **Both players need the same
mods installed**, or the ability lists won't line up. Thunderstore can export your whole
profile as a code for them to import.

Public matchmaking is disabled by the game whenever any mod is loaded. That's Bopl's rule, not
this mod's.


## License

MIT
