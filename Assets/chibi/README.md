# Trà My — Chibi

The approved chibi sheets supply eight key drawings for each motion. The build
script aligns transparent silhouettes and creates three optical-flow inbetweens
per interval, including the last-to-first interval. Each clip has 32 frames, not
32 independently hand-drawn poses. Interpolation can still deform details where
the source drawings disagree; this is not equivalent to a fully rigged animation.

The six atlases are 1792×1152 RGBA PNGs, eight columns and four rows. Each tile is
224×288; the ground anchor is (112, 280). Read tiles left-to-right, top-to-bottom.
Durations and dimensions are in manifest.json and ChibiAnimation.cs.

The renderer caches and freezes all frames, uses an elapsed-time clock, and never
blends two visible sprites. Walking/crawling phase follows travel speed during
acceleration and braking. Chibi images face right natively. Sleeping has its own
clip; the classic character remains selectable. Right-click → Động tác previews
individual movements. Transitions between different actions use the destination
pose; dedicated hand-drawn transition poses are not supplied by the source sheet.

Rebuild: `python tools/build_chibi.py approved-atlas.png approved-sleep.png`.
Dependencies: Pillow, numpy, scipy, opencv-python-headless. The two input images
are the user-approved sheets generated in the conversation. No AI API is needed
to render or animate this character inside BPet.
