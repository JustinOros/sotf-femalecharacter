# sotf-femalecharacter

A client side RedLoader mod for Sons of the Forest that shows players who have the mod as female characters. Each race slot has its own woman, named after the slot number.

| Race slot | Woman | Hair |
|---|---|---|
| White (0) | Woman0 | blonde |
| Black (1) | Woman1 | black hair |
| Latin (2) | Woman2 | black hair |
| Asian (3) | Woman3 | black hair |
| BlackA (4) | Woman4 | Asian, blonde |
| WhiteA (5) | Woman5 | black hair |
| BlackB (6) | Woman6 | blonde |
| LatinA (7) | Woman7 | blonde |

Installing the mod is all you need. You are shown as the woman for your race slot, so pick the slot you want in the game or with [sotf-character-select](https://github.com/JustinOros/sotf-character-select). You can also switch with `femalecharacter woman2` and so on, or turn it off with `femalecharacter off`.

Players who turn it off with `femalecharacter off` still show as the normal male character for their slot. Players without the mod always see the normal male characters, so nothing breaks for them.

## How it works

When you load in or change character, the mod sends a short chat line such as `[mod] FemaleCharacter: BlackB`. Every player with one of these mods hides lines that start with `[mod]` and reads them instead. Players without the mod see these lines in chat.

`ModChat.cs` is a standalone file that any mod can include to send and receive `[mod]` lines.

## Limitations

- Your own view is unchanged. Others see you as female, you still see the normal first person arms.
- The outfits are look-alikes made from MakeHuman clothing, not the game's own models.
- The backpack is the game's own model and sits where it would on the male body. Other pieces you add on top (helmet, rebreather) are not shown on the women.

## Outfits

Each woman has her own version of every game outfit. The mod switches to it when you change clothes in your inventory.

| Game clothing | Woman wears |
|---|---|
| Nothing | Underwear |
| Tactical Jacket | Tactical jacket, cargo pants, boots |
| Hoodie, Old Jacket, Leather Jacket, Puffy Jacket | Matching jacket with cargo pants and boots |
| Tuxedo, Silk Pyjamas, Wetsuit, Space Suit, Priest Outfit, Flight Attendant Uniform, Golden Armour | Matching full outfit |

Hold one of Virginia's outfits (Camo Suit, Dress, Leather Suit, Tracksuit, Swimsuit) in your hand and your woman wears it. Changing your game clothing switches back. Other players with the mod see the same outfit.

## Commands

| Command | What it does |
|---|---|
| `femalecharacter woman2` | Play as Woman2 (saved). Works for any slot that has a model |
| `femalecharacter off` | Show as the normal male character to other players (saved) |
| `femalecharacter on` | Show as the woman for your race slot again (default) |
| `femalecharacter` | Show status |
| `femalecharacter wear` | Show the outfit you are wearing |
| `femalecharacter wear v_dress` | Wear an outfit by name: base, tactical, hoodie, oldjacket, leatherjacket, puffyjacket, tuxedo, pyjamas, wetsuit, spacesuit, priest, flightattendant, goldenarmour, v_camosuit, v_dress, v_leathersuit, v_tracksuit, v_swimsuit |
| `femalecharacter wear auto` | Follow your game clothing again |
| `femalecharacter backpack on` / `off` | Show or hide your backpack on your woman (saved, other players with the mod see your choice) |
| `femalecharacter hands on` / `off` | Her hands follow the player's real hand positions so they line up with held weapons and tools (default on) |
| `femalecharacter announce` | Send your character to other players again, for example after someone joins |
| `femalecharacter preview woman2` | Show Woman2 in front of you copying your movement and outfit, for testing without another player |
| `femalecharacter preview off` | Remove the preview |

## Building

1. Generate the women with `tools/build_woman.py` (Blender 4.2 or newer with the MakeHuman MPFB add-on and the MakeHuman asset packs).
2. Build the character bundle in Unity 2022.2.16f1 (the game's Unity version): open the project in `unity`, put `woman0.fbx` to `woman7.fbx` and the `textures` folder in `Assets/Models`, run FemaleCharacter > Build Bundle, and copy `Bundles/femalecharacter` to `assets/femalecharacter`.
3. Build and install the mod:

```
.\build.ps1 -Install
```

Bundles built before the rename with `alyssa.fbx` and `rachel.fbx` still work as Woman2 and Woman6.
