# Physics Bubble Test Branch

Branch: `physics-bubble-tests`

This branch is for testing a precision watchdog before any real physics-bubble handoff is attempted.

## Current direction

Map expansion offset work is on hold. The first physics precision work will live inside Railroader Stock Optimizer because this mod already tracks rolling stock and decides hot/warm/cold/frozen activity state.

## First test goal

Detect when live rolling stock is getting too far from its current physics bubble origin and recommend a better bubble.

This is currently dry-run only. It does not move trains, cars, terrain, track, or colliders.

## Current assumptions

- `MainBubble` starts at global origin `0,0,0`.
- Current Unity `transform.position` is treated as temporary global position.
- Bubbles are generated from a configurable grid size.
- Recommended bubble is the grid bubble that gives the smallest local coordinate magnitude.
- Whole consists should eventually move together. This first dry-run still evaluates per car because the existing optimizer currently tracks cars.

## UMM settings added

- Enable precision watchdog dry-run
- Dry-run only: log recommended bubble moves, do not move trains
- Show precision details in overlay
- Bubble Grid Size
- Precision Warning Distance
- Transfer Recommendation Distance
- Emergency Distance
- Better Bubble Margin

## Default thresholds

- Warning: 10 km
- Transfer recommendation: 20 km
- Emergency: 30 km
- Better bubble margin: 5 km
- Bubble grid size: 20 km

## Overlay output

The overlay reports the last processed batch:

- number of cars over warning threshold
- number of cars with a recommended bubble transfer
- number of cars over emergency threshold
- worst local distance
- estimated float step at the worst local distance
- last transfer recommendation

## What to test first

1. Build this branch.
2. Enable the mod in UMM.
3. Enable `Enable precision watchdog dry-run`.
4. Turn on debug logging if you want log entries.
5. Move around the map and confirm values stay sensible near origin.
6. Use a test map or moved stock far from origin and confirm it recommends a grid bubble.

Expected example:

```text
Car ABC:
    MainBubble -> GridBubble[20000,0]
    local 22500m -> 2500m
```

## Next stages

1. Add true double/global position storage instead of treating scene position as global.
2. Group cars into consists and evaluate the consist, not each car.
3. Add named/manual bubbles before grid bubbles.
4. Add a dry-run consist handoff plan.
5. Only after track/terrain/colliders are bubble-local too, test real handoff.
