# sotf-femalecharacter

A client side RedLoader mod for Sons of the Forest that lets players opt in to be shown as a female character. Each race slot has its own woman, named after the slot number.

| Race slot | Woman | Model |
|---|---|---|
| White (0) | Woman0 | not made yet |
| Black (1) | Woman1 | not made yet |
| Latin (2) | Woman2 | ready |
| Asian (3) | Woman3 | not made yet |
| BlackA (4) | Woman4 | not made yet |
| WhiteA (5) | Woman5 | not made yet |
| BlackB (6) | Woman6 | ready |
| LatinA (7) | Woman7 | not made yet |

Choose your character in the console with `femalecharacter woman2`, `femalecharacter woman6` or `femalecharacter off`. The choice is saved and applied every time you load in. [sotf-character-select](https://github.com/JustinOros/sotf-character-select) also works, and the two mods stay in sync.

Only players who opt in with a `femalecharacter woman` command are shown as women. Anyone else using the same race slot still shows as the normal male character. Players without the mod always see the normal male characters, so nothing breaks for them.

## How opt in works

When you load in or change character, the mod sends a short chat line such as `[mod] FemaleCharacter: BlackB`. Every player with one of these mods hides lines that start with `[mod]` and reads them instead. Players without the mod see these lines in chat.

`ModChat.cs` is a standalone file that any mod can include to send and receive `[mod]` lines.

## Limitations

- Your own view is unchanged. Others see you as female, you still see the normal first person arms.
- The outfits are look-alikes made from MakeHuman clothing, not the game's own models.
- Clothing pieces you add on top (backpack, helmet, rebreather) are not shown on the women.

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
| `femalecharacter off` | Go back to the character you had before |
| `femalecharacter` | Show status |
| `femalecharacter wear` | Show the outfit you are wearing |
| `femalecharacter wear v_dress` | Wear an outfit by name: base, tactical, hoodie, oldjacket, leatherjacket, puffyjacket, tuxedo, pyjamas, wetsuit, spacesuit, priest, flightattendant, goldenarmour, v_camosuit, v_dress, v_leathersuit, v_tracksuit, v_swimsuit |
| `femalecharacter wear auto` | Follow your game clothing again |
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
