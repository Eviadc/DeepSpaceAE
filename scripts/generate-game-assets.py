"""Reproduce pinned pixel assets (Pillow 12.3.0 for decoding; stdlib PNG encoder).
Provenance: assets/ae2-source/sources.json (CC BY-NC-SA 3.0), drawers-source (MIT).
"""
from pathlib import Path
import argparse
import hashlib
import json
import struct
import zlib
from PIL import Image, ImageDraw, ImageOps, ImageChops

REPO = Path(__file__).resolve().parents[1]
SOURCE = REPO / 'assets/ae2-source'
ROOT = REPO / 'assets/game/Textures/Items'

def png(image):
    image = image.convert('RGBA'); width, height = image.size; pixels = image.tobytes()
    raw = b''.join(b'\0' + pixels[y*width*4:(y+1)*width*4] for y in range(height))
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind+data))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB',width,height,8,6,0,0,0)) + chunk(b'IDAT',zlib.compress(raw,9)) + chunk(b'IEND',b'')

def image(name): return Image.open(SOURCE / name).convert('RGBA')
def inactive(img):
    result = ImageOps.grayscale(img).point(lambda x: x*2//5).convert('RGBA'); result.putalpha(img.getchannel('A')); return result

def project_top(model_name, texture):
    """Orthographic x/z up faces with the AE2 model's own UVs; no drawn cable arms."""
    result = Image.new('RGBA', (16, 16))
    model = json.loads((SOURCE/model_name).read_text())
    for element in sorted(model['elements'], key=lambda e: e['to'][1]):
        if 'up' not in element['faces']: continue
        x0, _, z0 = map(int, element['from']); x1, _, z1 = map(int, element['to'])
        u0, v0, u1, v1 = element['faces']['up'].get('uv', [x0, z0, x1, z1])
        for z in range(z0, z1):
            for x in range(x0, x1):
                u = min(15, max(0, int(u0+(x-x0+0.5)*(u1-u0)/(x1-x0))))
                v = min(15, max(0, int(v0+(z-z0+0.5)*(v1-v0)/(z1-z0))))
                result.putpixel((x, z), texture.getpixel((u, v)))
    return result

def assets():
    metadata = json.loads((SOURCE/'sources.json').read_text())
    for entry in metadata['files']:
        data = (SOURCE/entry['file']).read_bytes()
        # Git for Windows may check out text models with CRLF; binary assets stay exact.
        valid = hashlib.sha256(data).hexdigest() == entry['sha256']
        if not valid and Path(entry['file']).suffix in ('.json', '.mcmeta'):
            valid = hashlib.sha256(data.replace(b'\r\n', b'\n')).hexdigest() == entry['sha256']
        assert valid, entry['file']
    result = {}
    core = image('controller_powered.png'); core.alpha_composite(image('controller_lights.png').crop((0,0,16,16)))
    result['core.png'] = core.resize((48,48), Image.Resampling.NEAREST)
    result['core-off.png'] = inactive(image('controller.png')).resize((48,48), Image.Resampling.NEAREST)
    # AE2 storage_bus_base up faces with Minecraft's default up-face UVs.
    # Keep the thin ribbed face and rear stem: no hand-drawn arrow or center badge.
    face = project_top('storage_bus_base.json', image('storage_bus_sides.png'))
    raw_icon = face.transpose(Image.Transpose.ROTATE_270)
    # The multipart bus ends at z=4; connect it to the cable center at z=6.
    # Only the internal 4..6 segment is rendered, not an external transmissive port.
    connector = project_top('glass_cable_connection.json', image('glass_cable_connection.png'))
    face.alpha_composite(connector.crop((6,4,10,6)), (6,4))
    faces = [face, face.transpose(Image.Transpose.ROTATE_270), face.transpose(Image.Transpose.ROTATE_180), face.transpose(Image.Transpose.ROTATE_90)]
    for side, sprite in enumerate(faces): result[f'bus-face-{side}.png'] = sprite
    result['item-storage-bus.png'] = raw_icon
    # Standalone icons use their own AE2 model. Installed bus faces keep the
    # shared thin AE2 storage-bus mounting geometry, preserving four-face hits.
    for kind in ('import','export'):
        icon=project_top(f'{kind}_bus_base.json', image(f'{kind}_bus_sides.png'))
        profile=project_top('storage_bus_base.json', image(f'{kind}_bus_sides.png'))
        profile.alpha_composite(connector.crop((6,4,10,6)), (6,4))
        result[f'{kind}-bus.png']=icon.transpose(Image.Transpose.ROTATE_270)
        rotations=[profile,profile.transpose(Image.Transpose.ROTATE_270),profile.transpose(Image.Transpose.ROTATE_180),profile.transpose(Image.Transpose.ROTATE_90)]
        for side,sprite in enumerate(rotations): result[f'{kind}-face-{side}.png']=sprite
    terminal=Image.new('RGBA',(16,16))
    # AE2 model north-face UV is [2,2,14,14]; each mask needs its own cable-color tint.
    for name,color in [('terminal_dark.png',(24,44,33)),('terminal_medium.png',(76,139,86)),('terminal_bright.png',(151,252,132))]:
        layer=image(name)
        colored=ImageChops.multiply(layer.convert('RGB'),Image.new('RGB',(16,16),color)).convert('RGBA')
        colored.putalpha(layer.getchannel('A')); terminal.alpha_composite(colored)
    terminal.alpha_composite(image('monitor_front.png'))
    result['terminal.png']=terminal
    # AE2 glass cable top projection, including all connection pixels and model UVs.
    cable=image('glass_cable_connection.png')
    center=project_top('glass_cable_center.json', cable)
    north=project_top('glass_cable_connection.json', cable)
    straight=project_top('glass_cable_straight.json', cable)
    arms=[north, north.transpose(Image.Transpose.ROTATE_270), north.transpose(Image.Transpose.ROTATE_180), north.transpose(Image.Transpose.ROTATE_90)]
    for mask in range(16):
        img=center.copy()
        for bit, arm in enumerate(arms):
            if mask & (1<<bit): img.alpha_composite(arm)
        if mask==5: img=straight.copy()
        elif mask==10: img=straight.transpose(Image.Transpose.ROTATE_90)
        result[f'pipeline-{mask}.png']=img
    result['pipeline.png']=result['pipeline-15.png']
    rack=image('drive_inside.png'); rack.alpha_composite(image('drive_front.png'))
    result['drive.png']=rack.resize((32,32),Image.Resampling.NEAREST)
    result['energy-receiver.png']=image('energy_acceptor.png').resize((32,32),Image.Resampling.NEAREST)
    for name,img in list(result.items()):
        if not name.endswith('-off.png') and name[:-4]+'-off.png' not in result: result[name[:-4]+'-off.png']=inactive(img)
    lights=image('controller_lights.png')
    for frame in range(lights.height//16):
        controller=image('controller_powered.png')
        controller.alpha_composite(lights.crop((0,frame*16,16,(frame+1)*16)))
        result[f'core-{frame}.png']=controller.resize((48,48),Image.Resampling.NEAREST)
    result['capacity-upgrade.png']=image('card_capacity.png')
    result['tag-upgrade.png']=image('card_fuzzy.png')
    result['wrench.png']=image('certus_quartz_wrench.png')
    for size,source in [('small','item_storage_cell_1k.png'),('large','item_storage_cell_4k.png')]:
        cell=image('item_cell_housing.png'); cell.alpha_composite(image(source)); cell.alpha_composite(image('storage_cell_led.png'))
        disk=Image.new('RGBA',(16,32)); disk.alpha_composite(cell,(0,8))
        result[f'disk-{size}.png']=disk
    drawer_source=REPO/'assets/drawers-source'
    drawer_meta=json.loads((drawer_source/'sources.json').read_text())
    for entry in drawer_meta['files']:
        content=(drawer_source/entry['file']).read_bytes()
        if Path(entry['file']).suffix!='.png': content=content.replace(b'\r\n',b'\n')
        assert hashlib.sha256(content).hexdigest()==entry['sha256'],entry['file']
    result['drawer.png']=Image.open(drawer_source/'drawers_oak_front_1.png').convert('RGBA').resize((32,32),Image.Resampling.NEAREST)
    return {k:png(v) for k,v in result.items()}

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--check',action='store_true');args=parser.parse_args()
    generated=assets()
    for name, content in generated.items():
        path=ROOT/name
        if args.check:
            if not path.is_file() or path.read_bytes()!=content: raise SystemExit('Asset mismatch: '+name)
        else: path.write_bytes(content)
    print(('Verified' if args.check else 'Rendered'),len(generated),'licensed PNGs.')
