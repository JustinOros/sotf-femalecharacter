# sotf-femalecharacter

A client side RedLoader mod for Sons of the Forest that adds two female player characters by replacing two of the male race variants for players who have the mod.

| Race slot | Shows as |
|---|---|
| Latin (2) | Alyssa (blonde) |
| BlackB (6) | Rachel (brunette) |

Pick the race with [sotf-character-select](https://github.com/JustinOros/sotf-character-select). Players with this mod see you as the woman. Players without it see the normal male character, so nothing breaks for them.

## Limitations

- Your own view is unchanged. Others see you as female, you still see the normal first person arms.
- With `clothes own` (the default) game clothing is hidden and the women wear their own outfit.
- With `clothes game` only their head, hair and hands are shown on top of the normal game clothing, so every outfit you find in game works. The game clothes are cut for the male body.

## Commands

| Command | What it does |
|---|---|
| `femalecharacter` | Show status |
| `femalecharacter clothes own` | Women wear their own outfit (default) |
| `femalecharacter clothes game` | Women wear the game clothing you have equipped |
| `femalecharacter handoffset rachel 0.05` | In game clothes mode, raise a character's hands into the sleeves by this many meters (defaults: alyssa 0.02, rachel 0.05) |
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
