"""Normalize the generated bust to the verified native portrait canvas and scale.

Source and native-reference observations: assets/steve-source/README.md.
This only fits/resizes the authored source; it does not paint or copy native pixels.
"""
from pathlib import Path
from PIL import Image, ImageOps
from importlib.util import spec_from_file_location, module_from_spec

root=Path(__file__).resolve().parents[1]
spec=spec_from_file_location('ae_png',root/'scripts/generate-game-assets.py')
encoder=module_from_spec(spec); spec.loader.exec_module(encoder)
source=Image.open(root/'assets/steve-source/fluix-researcher-block-source.png').convert('RGBA')
# Ignore the generator's nearly transparent stray pixels when fitting the bust.
# The source alpha inside the chosen crop is retained without repainting it.
bounds=source.getchannel('A').point(lambda alpha:255 if alpha>=8 else 0).getbbox()
body=source.crop(bounds)
body=ImageOps.contain(body,(150,160),Image.Resampling.NEAREST)
result=Image.new('RGBA',(288,211))
result.alpha_composite(body,((288-body.width)//2,211-body.height))
output=root/'assets/game/Textures/Items/steve-portrait.png'
output.write_bytes(encoder.png(result))
print(output,result.size,'bounds',result.getbbox())
