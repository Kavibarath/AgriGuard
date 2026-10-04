# Image credits

Every photograph in the product. Each one is stored locally in `web/public/img/` as WebP under
200 KB, and none is hotlinked. The originals were supplied by the project team as reference
images (four on 30 Sept 2026, three more on 1 Oct 2026), and the files below were cropped,
resized and compressed from them.

**Before submission, fill in "Original source" and "Licence" for each row.** Use the Unsplash or
Pexels page each photo came from, and its licence ("Unsplash License" or "Pexels License": free
for commercial use, no attribution required). The report's references section needs them. If a
photo cannot be traced to a free licence, replace it. If an image was made with an AI image
generator, say so in "Original source" and name the tool.

## Video

| File | Made from | Shows | Used on | Original source | Licence |
|---|---|---|---|---|---|
| `web/public/video/hero-field.mp4` (1920×1080, 8.04 s, H.264 + AAC, 5,057,004 bytes = 4.82 MiB) | Pexels' own 1920×1080 rendition, downloaded 2 Oct 2026 and served unchanged (no ffmpeg on the build machine, so no trim, re-encode or WebM) | A slow pan across smallholder brassica plots (cabbage and kale) on bare soil; no people | Web home page hero, muted and looping; never played with reduced motion | [Pexels 7983392, "Green Plants on the Field", by Andi Farruku](https://www.pexels.com/video/green-plants-on-the-field-7983392/) | [Pexels License](https://www.pexels.com/license/): free for commercial use, no attribution required |
| `web/public/img/hero-field-poster.webp` (1440×810, 203 KB) | The video's first frame, scaled and compressed | The same plots | The video's poster, the still under reduced motion, and what the loop fades through | As above | As above |

The clip is a pan, so its last frame is not its first. The page fades the video out over its last
0.9 s onto the poster (its own first frame) and back in as it restarts, so the loop dissolves
instead of cutting (`HomeHero.tsx`). A clip trimmed to a seamless loop would need ffmpeg.

## Photographs in use

| File | Made from | Shows | Used on | Original source | Licence |
|---|---|---|---|---|---|
| `fields-aerial-portrait.webp` (620×1104, 186 KB) | Supplied 1 Oct (749×1334) | Fields from above: a dark row crop, a cut field, yellow-flowering and green fields, a line of trees | Web dashboard hero for the field agronomist and the administrator; home page role card; phone sign-in panel (under the spotlight) and home background for the agronomist | _to confirm_ | _to confirm_ |
| `harvest-lanes-portrait.webp` (620×1240, 187 KB) | Supplied 1 Oct (1000×2000) | A green harvester on a dirt lane between ripe and green grain. It looks like a digital illustration; confirm whether it is AI-generated | Web dashboard hero for the farmer; home page role card; phone welcome screen (under the spotlight) and home background for the farmer. (A mirrored crop, `harvest-lanes-wide.webp`, was the web home hero under the spotlight on 2 Oct until the video replaced it; deleted.) | _to confirm_ | _to confirm_ |
| `tractor-maize-portrait.webp` (720×900, 91 KB) | Supplied 1 Oct (1200×1500) | A tractor and trailer passing a maize field, motion-blurred; no driver is recognisable | Web dashboard hero for the agro-dealer; home page role card; phone home background for the dealer and administrator | _to confirm_ | _to confirm_ |
| `tractor-maize-square.webp` (1200×1200, 171 KB) | The same image, cropped square at full width | The same scene | Web sign-in panel, under the spotlight | _to confirm_ | _to confirm_ |
| `banner-hill-country.webp` (1600×400) | Reference 1 (2000×1499) | Wooded hills above terraced paddy, without the people | Home page role card for co-op administrators | _to confirm_ | _to confirm_ |

The phone carries three of these, copied to `mobile/assets/img/` (about 460 KB together): the
welcome and sign-in photos, and one full-screen home photo per role. Every other phone screen uses
drawn icons only.

## No longer used

These were replaced on 1 and 2 Oct 2026, when the dashboards, the home page and sign-in moved to
the photos above. They are still in `web/public/img/` and can be deleted, or brought back.

| File | Made from | Shows | Was used on | Original source | Licence |
|---|---|---|---|---|---|
| `hill-terraces-portrait.webp` (900×1200) | Reference 1 (2000×1499) | Terraced paddy below wooded hills; three people working the field | Web and phone sign-in panels, until 2 Oct | _to confirm_ | _to confirm_ |
| `banner-dry-zone-paddy.webp` (1600×400) | Reference 3 (1204×773) | Ripening paddy on flat land, tree line and a single figure | Dashboard banner for dry-zone districts | _to confirm_ | _to confirm_ |
| `banner-sigiriya-paddy.webp` (1600×400) | Reference 4 (1016×823) | Paddy fields with Sigiriya rock in the haze | Dashboard banner for Matale | _to confirm_ | _to confirm_ |
| `harvest-carry-portrait.webp` (450×600) | Reference 2 (563×750) | A person carrying a bundle of cut paddy on their head | Phone home header strip | _to confirm_ | _to confirm_ |

## How the photos are used

The photos are context, never data:

- They carry a canopy-green scrim wherever text sits over them, deepest where the text is.
- Their corners are rounded to the card they sit in.
- They have explicit width and height, so nothing shifts as they load.
- On the web dashboard the photo fills the right side of the opening card, and the live figures
  lie over its lower edge. On the phone home it fills the screen behind the cards, and stays put
  while they scroll.
- On sign-in (web and phone) and the phone's welcome screen, the photo is shown in canopy monochrome,
  and a soft spotlight that follows the cursor (or a finger) shows it in true colour.
- The web home page opens on the video under a canopy wash (`brand-900` at 55%) and a vertical
  gradient (80% → 40% → 85%). Measured over five frames, the lightest background pixel behind any
  hero text still gives 6.1:1 for the white headline; even a pure-white pixel under the thinnest
  point of the scrim would give 5.98:1 (white) and 5.04:1 (the `brand-100` labels).
- No person in a photo is presented as an AgriGuard user, or as endorsing the product. No one in the
  photos in use is recognisable, and no caption names or quotes anyone.

Empty states use a line illustration drawn in code (`EmptyState.tsx`), not a photo.
