# Portrait & player-image drop-ins

Drop your art here, **beside the exe** at `data\ams2\portraits\`. Everything in this folder is
optional — a slot with no image shows a framed placeholder (a person-glyph) so you can see where the
art will go. Files are matched by **key**, case-insensitively, with a `.jpg`, `.jpeg`, `.png`, or
`.webp` extension. Square images look best (the driver portraits ship square ~1254×1254).

## 1. Driver portraits — `driver.<id>.jpg`

The face for a specific driver, shown on the Season's Grid cards and the rival dossier. Keyed by the
pack **driver id**, e.g. `driver.gilberto_ceara.jpg`, `driver.bernie_miller.jpg`. (The car preview
beneath each card is extracted automatically from the skin — you don't supply those.)

## 2. Player images — `player.<team>.jpg`  ← the team-coloured "YOU"

A **different player image per team** — the team-coloured helmet/driver that represents **YOU** while
you drive for that team. It replaces the AI driver's face on your own ("YOU") card in the Season's
Grid, and is shown front-and-centre on the character screen. When you **switch teams** (or pick a
different car), the app automatically swaps to that team's player image.

Name each one `player.<team>.jpg`, where `<team>` is the team's short name (its team id without the
`team.` prefix). For the Super Monaco GP roster that is:

```
player.madonna.jpg      player.firenze.jpg      player.millions.jpg     player.bestowal.jpg
player.iris.jpg         player.azalea.jpg       player.blanche.jpg      player.tyrant.jpg
player.losel.jpg        player.may.jpg          player.joke.jpg         player.bullets.jpg
player.dardan.jpg       player.linden.jpg       player.minarae.jpg      player.lares.jpg
player.feet.jpg         player.serga.jpg        player.rigel.jpg        player.cool.jpg
player.comet.jpg        player.orchis.jpg       player.moon.jpg         player.zeroforce.jpg
```

Example: the yellow-helmet Minarae driver goes at **`player.minarae.jpg`**.

> Coming next: once the per-team car stats (machine name, engine, max power, the ENG./T.M./SUS./
> TIRE/BRA. bars — like the classic Super Monaco GP car screen) are provided, they'll be shown on the
> character screen and the rival screen beside the player image and the car.
