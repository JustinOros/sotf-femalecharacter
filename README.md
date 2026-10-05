# sotf-femalecharacter

A client side RedLoader mod for Sons of the Forest that adds two female player characters by replacing two of the male race variants for players who have the mod.

| Race slot | Shows as |
|---|---|
| Latin (2) | Alyssa (blonde) |
| BlackB (6) | Rachel (brunette) |

Choose your character in the console with `femalecharacter alyssa`, `femalecharacter rachel` or `femalecharacter off`. The choice is saved and applied every time you load in. [sotf-character-select](https://github.com/JustinOros/sotf-character-select) also works, and the two mods stay in sync. Players with this mod see you as the woman. Players without it see the normal male character, so nothing breaks for them.

## Limitations

- Your own view is unchanged. Others see you as female, you still see the normal first person arms.
- With `clothes game` (the default) only their head, hair and hands are shown on top of the normal game clothing, so every outfit you find in game works. The game clothes are cut for the male body.
- With `clothes own` game clothing is hidden and the women wear their own outfit.

## Commands

| Command | What it does |
|---|---|
| `femalecharacter alyssa` | Play as Alyssa (saved) |
| `femalecharacter rachel` | Play as Rachel (saved) |
| `femalecharacter off` | Go back to the character you had before |
| `femalecharacter` | Show status |
| `femalecharacter clothes game` | Women wear the game clothing you have equipped (default) |
| `femalecharacter clothes own` | Women wear their own outfit |
| `femalecharacter handoffset rachel 0.05` | Fallback hand offset for outfits without their own value (defaults: alyssa 0.07, rachel 0.10) |
| `femalecharacter headoffset rachel 0.03` | Fallback head offset for outfits without their own value (defaults: alyssa 0, rachel 0.01) |
| `femalecharacter fillers on` / `off` | In game clothes mode, show the White male neck and forearms under the clothes to fill gaps at the collar and cuffs (default on) |
| `femalecharacter fillers neck on` / `off` | Turn only the neck filler on or off |
| `femalecharacter fillers arms on` / `off` | Turn only the forearm filler on or off |
| `femalecharacter skintone 1.1 1.05 1.0` | Tint the filler neck and forearms for the previewed woman (red green blue, 1 is unchanged). `skintone reset` restores it |
| `femalecharacter outfit` | Show your current outfit and the hand and head offsets each woman uses with it |
| `femalecharacter hand 0.07` | Set the hand offset for the previewed woman in your current outfit |
| `femalecharacter head 0.01` | Set the head offset for the previewed woman in your current outfit |
| `femalecharacter preview alyssa` | Show Alyssa in front of you copying your movement, for testing without another player |
| `femalecharacter preview rachel` | Same for Rachel |
| `femalecharacter preview off` | Remove the preview |

## Building

1. Build the character bundle in Unity 2022.2.16f1 (the game's Unity version): open the project in `unity`, put `alyssa.fbx` and `rachel.fbx` in `Assets/Models`, run FemaleCharacter > Build Bundle, and copy `Bundles/femalecharacter` to `assets/femalecharacter`.
2. Build and install the mod:

```
.\build.ps1 -Install
```

The models are Mixamo characters.
