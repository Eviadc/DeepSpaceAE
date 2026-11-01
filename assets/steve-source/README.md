# Steve portrait provenance

Created for DeepSpaceAE using the built-in image_gen tool on 2026-11-01. The original generated RGBA source is steve-source.png (1024x1536). The game asset is assets/game/Textures/Items/steve-portrait.png (256x384), nearest-neighbor downsampled with its alpha preserved and encoded through the existing stdlib PNG encoder. No original game/Minecraft image files were copied. This is a Steve-inspired generated character depiction; it is separate from AE2 artwork and is not represented as AE2-licensed or as public-domain Minecraft artwork.

## Final generation prompt

Use case: stylized-concept. Asset type: transparent NPC portrait sprite for a retro pixel-art pawn shop game. Subject: Minecraft Steve as a friendly travelling AE engineer and merchant, recognizable square dark-brown hair, tan face, short square beard, blue eyes, turquoise short-sleeve shirt and dark indigo trousers, simple brown utility belt with a small copper circuit board pouch. Composition: one solitary front-facing character, visible from the top of the hair to just below the knees, arms relaxed at sides, readable neutral friendly expression, complete head and arms with generous transparent margins, no props extending outside silhouette. Medium: crisp low-resolution 16-bit pixel art with chunky pixels, restrained detailed shading and clean dark outlines, no 3D rendering, no smooth airbrushing. Palette: muted teal, warm brown, slate blue; fits a slightly worn science-fiction trading counter. Genuine transparent background, no scenery, no labels, no text, no watermark. One static portrait, not a sprite sheet. The final sprite will be normalized to a 256 by 384 canvas while preserving transparency.

## Reproduce the resize

Use Pillow 12.3.0 to convert the committed source to RGBA and resize to (256,384) with Image.Resampling.NEAREST, then encode using png() in scripts/generate-game-assets.py. Standard builds use the committed PNG and do not call image generation.
