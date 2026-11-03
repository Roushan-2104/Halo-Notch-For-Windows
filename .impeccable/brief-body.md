Scope: Shammy's open notch (ExpandedView: chips row + home card row + details) and the states around it (rest, peek,
activity + action circle). Visitor mode: Operate. Audience/job: students, developers and Mac switchers glancing at
and controlling what's happening (music, meetings, timers, weather, system, clipboard) without leaving their work.
Constraints: PRODUCT.md principles (never in the way, offline-first, keyboard+voice first, near-zero idle CPU).
Unresolved: exact display face licensing for WPF (must be an embeddable OFL font); whether peek shows the focused tile.

## Direction contract

THESIS: The open notch is one rail of artwork-first tiles; the tile you focus grows and explains itself while the rest
dim, so the screen always says "this one, now". It refuses the category default of a grey grid of equal widget cards.

OWN-WORLD: Deep neutral ground (#0B0B0D notch, #16161A tiles), colour comes only from each tile's artwork field
(album art, sky, date, ring), one accent (#FF8A3D amber-orange) reserved for the focused tile's 1.5px outline + glow and
the primary control; condensed display type for titles and big numerals, small tracked uppercase labels, tabular figures.

STORY: The user hovers or arrows across the rail, sees each activity as a picture first, reads details only on the
focused tile, acts with its one primary control or opens it in place; the pet stays the home key.

FIRST VIEWPORT: 1012-wide black notch hanging from the top edge; top row = pet-eyes home, tracked uppercase chips,
search field right; below, a single rail of 6:5 artwork tiles; the focused tile (default: the most relevant live
activity) at 1.15× with title, one line and its primary control; neighbours shift aside and dim to 55%; the action
circle bubbles off the notch's right edge for the live activity's one direct action.

FORM: Focus Wall (catalog challenger pop-culture-shelf-streaming-title-card-wall), chosen over the assigned Truck-Art
Night Shift; seed:c20fef8e9b50d475. Signature move: focus expansion with row dimming (spring scale + neighbour slide).

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
