# Image credits

Every photograph in the product. Each one is stored locally in `web/public/img/` as WebP under
200 KB, and none is hotlinked. The four originals were supplied by the project team on 30 Sept 2026
as reference images, and the files below were cropped and compressed from them.

**Before submission, fill in "Original source" and "Licence" for each row.** Use the Unsplash or
Pexels page each photo came from, and its licence ("Unsplash License" or "Pexels License": free
for commercial use, no attribution required). The report's references section needs them. If a
photo cannot be traced to a free licence, replace it.

| File | Cropped from | Shows | Used on | Original source | Licence |
|---|---|---|---|---|---|
| `hill-terraces-portrait.webp` (900×1200) | Reference 1 (2000×1499) | Terraced paddy below wooded hills; three people working the field | Web login panel; phone login panel (cropped to the hills and terraces, people out of frame) | _to confirm_ | _to confirm_ |
| `banner-hill-country.webp` (1600×400) | Reference 1 | The same hillside and terraces, without the people | Dashboard banner for hill-country districts (Nuwara Eliya) | _to confirm_ | _to confirm_ |
| `banner-dry-zone-paddy.webp` (1600×400) | Reference 3 (1204×773) | Ripening paddy on flat land, tree line and a single figure | Dashboard banner for dry-zone districts (Anuradhapura), and the default | _to confirm_ | _to confirm_ |
| `banner-sigiriya-paddy.webp` (1600×400) | Reference 4 (1016×823) | Paddy fields with Sigiriya rock in the haze | Dashboard banner for Matale, and for roles without a district | _to confirm_ | _to confirm_ |
| `harvest-carry-portrait.webp` (450×600) | Reference 2 (563×750) | A person carrying a bundle of cut paddy on their head | Phone home header strip | _to confirm_ | _to confirm_ |

The phone carries only the last two, copied unchanged to `mobile/assets/img/` (205 KB together),
because photos cost bytes on field connections. Every other phone screen uses drawn icons only.

## How the photos are used

The photos are context, never data:

- They carry a canopy-green scrim wherever text sits over them.
- Their corners are rounded to 12px.
- They have explicit width and height, so nothing shifts as they load.
- No person in a photo is presented as an AgriGuard user, or as endorsing the product. The people
  are small figures in a landscape, and no caption names or quotes them. On the phone's home
  screen the signed-in user's name sits in a card below the photo, never over the person in it.

Empty states use a line illustration drawn in code (`EmptyState.tsx`), not a photo.
