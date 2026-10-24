# Settlement recipes

One JSON file per PC painting in `Assets/Art/Hexes/PCs`. When that painting would show on the board, `City3D` builds the settlement in 3D from this recipe instead (see `Assets/Scripts/City3D/CityRecipes.cs`). Paintings with no recipe fall back to the generic generator (`CityLayout`).

**File name:** the painting's name in lower case, letters and digits only: `Helm Deep.png` → `helmdeep.json`, `Barad-dur.png` → `baraddur.json`.

## Units and frames

Everything is in **hex widths** (1 = the width of a hex, about 1.09 world units).

- `at: [x, y]` puts a building on the hex. It's a **screen offset from the hex centre**: x to the right, y up the screen (farther back). The hex spans about x ∈ [-0.5, 0.5], y ∈ [-0.5, 0.5].
- A part's `pos: [x, y, z]` is in the **building's own frame**: x right, y up (height above the ground), z back (away from the viewer). The board is seen at an angle, so a depth z shows on screen as about `0.42 * z` up.
- `yaw` (degrees) turns a building, or a single part about its own `pos`. +yaw turns the +x side toward the viewer, so a ridge running down-right on screen has a positive yaw.

### Reading positions off a painting

The paintings are 974×1300 px. The hex's ground face is centred at about pixel (487, 690), and **765 px = 1 hex width**:

- `x = (px − 487) / 765`
- `y = (690 − py) / 765`, measured at the building's **base**, where it meets the ground.
- A height measured in pixels on the painting is `h = px_height / 765 / 0.9`.
- A ground depth seen in the painting is `z_extent ≈ px_depth / 765 / 0.42`.

Keep z extents modest (under ~0.25), or deep buildings look stretched.

Units stand at the hex centre and are always drawn in front of buildings, so the centre can hold buildings. Keep the very middle low anyway (plazas, paths, low walls) so it reads well.

## Recipe

```json
{
  "name": "Imladris",
  "description": "What the painting shows, in a sentence or two.",
  "style": "Elven",
  "colors":   ["#BDB49C", "#4C6070", "", "#A8A08A", "#B89A5C", "", "#8CAEB4", "", "#3E5530"],
  "surfaces": ["Ashlar",  "Slate",   "", "",        "Plain",   "", "Plain",   "", ""],
  "buildings": [ { "note": "great hall", "at": [0.0, 0.18], "yaw": 35, "parts": [ ... ] } ]
}
```

- **`style`** sets the default colours and surfaces: `Mannish`, `Elven`, `Dwarven`, `Hobbit`, `Orcish`, `Southron`, `Northman`.
- **`colors` / `surfaces`** take one entry per **cell**, in this order: `wall, roof, wood, stone, trim, dark, accent, nation, foliage`. `""` keeps the style's own.
  - `nation` is always the owner's colour (flags), so leave it `""`.
  - Use `accent` for anything special: water, lit windows, gold, banners in the painting's colour.
- **Surfaces:** `Plaster, HalfTimber, Ashlar, Fieldstone, Tiles, Thatch, Slate, Turf, Planks, Plain, Cloth, Leaves`.

## Parts

Every part has a `shape`, a `pos` (default [0,0,0]) and a `cell` (default `wall`). `top` (a cell) caps boxes and cylinders.

| shape | fields | notes |
|---|---|---|
| `box` | `size: [w, h, d]` | stands on pos, centred on x/z |
| `gable` | `size: [w, rise, d]` | gable roof, ridge along x; `top` = gable-end cell |
| `pyramid` | `radius, height` | 4-sided roof or spire, squared to the view |
| `cone` | `radius, height, sides` | round roof, spire, tent |
| `cylinder` | `radius, height, sides, taper, twist` | tower or drum; `taper` = top/bottom radius; `sides: 4, twist: 0.125` = square tower |
| `dome` | `radius, height, sides` | dome, hill, mound (a negative height hangs down) |
| `battlements` | `radius, count` | merlons round a tower top (pos = top centre) |
| `wallBattlements` | `length, thickness` | merlons along x (pos = wall top centre) |
| `flag` | `height, radius` | pole and flag in the owner's colour |
| `tree` | `kind: pine/broadleaf/poplar, height, radius, cell` | `cell` = foliage colour (e.g. `accent` for autumn gold) |
| `rock` | `radius, height, cell` | boulder, crag or cliff (big ones make mountain holds) |
| `wall` | `to: [x, z], height, thickness, count` | straight wall from pos to `to` on the ground; `count > 0` adds merlons |

Typical sizes (hex widths):

- **Houses:** cottage walls 0.06–0.1 wide and 0.05–0.08 high, roof rise 0.04–0.06.
- **Defences:** towers r 0.03–0.06 and 0.2–0.45 high; great halls 0.3–0.45 long. Town walls are about 0.025 thick and 0.04–0.08 high.
- **Trees:** 0.1–0.25 tall.
- **Most paintings** need 10–40 parts. Capture what makes the place recognisable: its silhouette, main buildings, towers, walls, trees, rocks and water.
