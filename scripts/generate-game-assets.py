"""Reproduce pinned pixel assets (Pillow 12.3.0 for decoding; stdlib PNG encoder).
Provenance: assets/ae2-source/sources.json (CC BY-NC-SA 3.0), drawers-source (MIT).
"""
from pathlib import Path
import argparse
import hashlib
import json
import struct
import zlib
import math
from PIL import Image, ImageDraw, ImageOps, ImageChops
from device_pixel_art import assets as device_pixel_assets
from bus_pixel_art import assets as bus_pixel_assets

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

def inset_device(img, margin):
    """Uniform area sampling keeps source geometry at a noninteger pixel scale.

    Sample on a common integer grid first: BOX alone on 32->28 or 48->42
    unevenly averages source pixels. Only boundaries blend; flat areas stay flat.
    """
    width,height=img.width-2*margin,img.height-2*margin
    sx=width//math.gcd(img.width,width); sy=height//math.gcd(img.height,height)
    exact=img.resize((img.width*sx,img.height*sy),Image.Resampling.NEAREST)
    body=exact.resize((width,height),Image.Resampling.BOX)
    result=Image.new('RGBA',img.size)
    result.alpha_composite(body,(margin,margin))
    return result

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

def cell_sprite(source):
    # AE2's capacity textures already include the entire housing. Keep one
    # unscaled sprite, centered in the physical 2x1 footprint's canvas.
    result=Image.new('RGBA',(32,16))
    result.alpha_composite(image(source),(8,0))
    return result

def engineer_icon():
    """19px native-style symbol: one ink, transparent inside and outside.

    All twelve installed icons_* sprites are 19x19. Their common pixel circle
    has 56 pixels; the menu scales the source by 2 to its 38px display target.
    Reference extraction stays in artifacts, never in the distributed assets.
    """
    ink=(41,34,52,255)
    result=Image.new('RGBA',(19,19)); draw=ImageDraw.Draw(result)
    edge=(".......#####.......",
          ".....##.....##.....",
          "...##.........##...",
          "..##...........##..",
          "..#.............#..",
          ".#...............#.",
          ".#...............#.",
          "#.................#",
          "#.................#",
          "#.................#",
          "#.................#",
          "#.................#",
          ".#...............#.",
          ".#...............#.",
          "..#.............#..",
          "..##...........##..",
          "...##.........##...",
          ".....##.....##.....",
          ".......#####.......")
    for y,row in enumerate(edge):
        for x,pixel in enumerate(row):
            if pixel=='#': draw.point((x,y),fill=ink)
    # Native icons use a solid silhouette with a few transparent cutouts.
    # Keep the controller's etched circuit motif as negative space in its face.
    clear=(0,0,0,0)
    draw.rectangle((5,5,13,13),fill=ink)
    for x,y in [(5,5),(13,5),(5,13),(13,13)]: draw.point((x,y),fill=clear)
    for contact in [(8,3,9,5),(13,8,15,9),(9,13,10,15),(3,9,5,10)]:
        draw.rectangle(contact,fill=ink)
    for groove in [[(7,6),(7,8),(10,8),(10,6),(12,6)],
                   [(6,11),(8,11),(8,10),(11,10),(11,12)]]:
        draw.line(groove,fill=clear,width=1)
    return result

def steve_contact():
    """Original 32x16 contact slip; native cards/paper were extracted as references.

    One-pixel purple outline, flat paper blocks and printed ME circuit/6322.
    No native game pixels are copied into this distributed image.
    """
    result=Image.new('RGBA',(32,16)); draw=ImageDraw.Draw(result)
    ink=(41,34,52,255); paper=(196,196,176,255); shade=(168,174,165,255)
    draw.rectangle((2,1,29,14),fill=ink); draw.rectangle((1,2,30,13),fill=ink)
    draw.rectangle((2,2,29,13),fill=paper)
    draw.line((2,13,29,13),fill=shade); draw.line((29,2,29,12),fill=shade)
    # Solid controller face, small negative-space circuit and four contacts.
    draw.rectangle((4,4,10,10),fill=ink)
    for contact in ((6,3,7,4),(10,6,11,7),(7,10,8,11),(3,7,4,8)):
        draw.rectangle(contact,fill=ink)
    draw.line([(5,5),(7,5),(7,7),(9,7)],fill=paper,width=1)
    draw.line([(5,9),(8,9),(8,8)],fill=paper,width=1)
    digits={
        '6':('111','100','111','101','111'),
        '3':('111','001','111','001','111'),
        '2':('111','001','111','100','111'),
    }
    for index,char in enumerate('6322'):
        for y,row in enumerate(digits[char]):
            for x,value in enumerate(row):
                if value=='1': draw.point((13+index*4+x,5+y),fill=ink)
    draw.line((13,11,27,11),fill=shade)
    return result

def quartz_wrench():
    """Reference-guided artwork, reduced to native pixels and a compact palette."""
    source=Image.open(REPO/'assets/wrench-source/wrench-generated.png').convert('RGBA')
    result=source.resize((16,48),Image.Resampling.BOX)
    palette=[(41,34,52),(41,64,87),(60,95,128),(107,144,179),
             (138,187,239),(172,233,255),(218,255,255)]
    for y in range(48):
        for x in range(16):
            r,g,b,a=result.getpixel((x,y))
            if a<128:
                result.putpixel((x,y),(0,0,0,0)); continue
            color=min(palette,key=lambda c:(c[0]-r)**2+(c[1]-g)**2+(c[2]-b)**2)
            result.putpixel((x,y),color+(255,))
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
    # AE2 storage_bus_base up faces with Minecraft's default up-face UVs.
    # Keep the thin ribbed face and rear stem: no hand-drawn arrow or center badge.
    face = project_top('storage_bus_base.json', image('storage_bus_sides.png'))
    # The multipart bus ends at z=4; connect it to the cable center at z=6.
    # Only the internal 4..6 segment is rendered, not an external transmissive port.
    connector = project_top('glass_cable_connection.json', image('glass_cable_connection.png'))
    face.alpha_composite(connector.crop((6,4,10,6)), (6,4))
    faces = [face, face.transpose(Image.Transpose.ROTATE_270), face.transpose(Image.Transpose.ROTATE_180), face.transpose(Image.Transpose.ROTATE_90)]
    for side, sprite in enumerate(faces): result[f'bus-face-{side}.png'] = sprite
    # Bus icons render directly at native resolution from each item model.
    result.update(bus_pixel_assets())
    # Installed faces keep the shared thin AE2 storage-bus mounting geometry,
    # preserving four-face hit regions.
    for kind in ('import','export'):
        profile=project_top('storage_bus_base.json', image(f'{kind}_bus_sides.png'))
        profile.alpha_composite(connector.crop((6,4,10,6)), (6,4))
        rotations=[profile,profile.transpose(Image.Transpose.ROTATE_270),profile.transpose(Image.Transpose.ROTATE_180),profile.transpose(Image.Transpose.ROTATE_90)]
        for side,sprite in enumerate(rotations): result[f'{kind}-face-{side}.png']=sprite
    terminal=Image.new('RGBA',(16,16))
    # AE2 model north-face UV is [2,2,14,14]; each mask needs its own cable-color tint.
    for name,color in [('terminal_dark.png',(24,44,33)),('terminal_medium.png',(76,139,86)),('terminal_bright.png',(151,252,132))]:
        layer=image(name)
        colored=ImageChops.multiply(layer.convert('RGB'),Image.new('RGB',(16,16),color)).convert('RGBA')
        colored.putalpha(layer.getchannel('A')); terminal.alpha_composite(colored)
    terminal.alpha_composite(image('monitor_front.png'))
    result['terminal.png']=terminal.resize((32,32),Image.Resampling.NEAREST)
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
    result['energy-receiver.png']=image('energy_acceptor.png').resize((32,32),Image.Resampling.NEAREST)
    for name,img in list(result.items()):
        if name in ('item-storage-bus.png','import-bus.png','export-bus.png'): continue
        if not name.endswith('-off.png') and name[:-4]+'-off.png' not in result: result[name[:-4]+'-off.png']=inactive(img)
    result['capacity-upgrade.png']=image('card_capacity.png')
    result['tag-upgrade.png']=image('card_fuzzy.png')
    result['wrench.png']=quartz_wrench()
    for size,source in [('small','item_storage_cell_1k.png'),('large','item_storage_cell_4k.png')]:
        disk=cell_sprite(source); disk.alpha_composite(image('storage_cell_led.png'),(8,0))
        result[f'disk-{size}.png']=disk
    result['disk-ghost.png']=cell_sprite('item_cell_housing.png')
    result['engineer-start.png']=engineer_icon()
    result['steve-contact.png']=steve_contact()
    # Remaining legacy devices keep their existing inset transform. The four
    # Pixel-authored devices already have their final pixels, alpha and off states.
    for name,img in list(result.items()):
        if name.startswith(('energy-receiver','terminal')):
            margin=max(1,img.width//16)
            bounds=img.getbbox()
            if bounds and min(bounds[0],bounds[1],img.width-bounds[2],img.height-bounds[3])<margin:
                result[name]=inset_device(img,margin)
    result.update(device_pixel_assets())
    # Fill only the transparent margin, stopping immediately before the body:
    # 2px devices, 3px controller, 4px terminal. Never cover their frame/circuitry.
    for depth in (2,3,4):
        prefix='device-port-' if depth==2 else f'device-port-{depth}-'
        port=Image.new('RGBA',(16,16)); port.alpha_composite(north.crop((5,0,11,depth)),(5,0))
        for side,img in enumerate([port,port.transpose(Image.Transpose.ROTATE_270),port.transpose(Image.Transpose.ROTATE_180),port.transpose(Image.Transpose.ROTATE_90)]):
            result[f'{prefix}{side}.png']=img
            result[f'{prefix}{side}-off.png']=inactive(img)
    drawer_source=REPO/'assets/drawers-source'
    drawer_meta=json.loads((drawer_source/'sources.json').read_text())
    for entry in drawer_meta['files']:
        content=(drawer_source/entry['file']).read_bytes()
        if Path(entry['file']).suffix!='.png': content=content.replace(b'\r\n',b'\n')
        assert hashlib.sha256(content).hexdigest()==entry['sha256'],entry['file']
    drawer=Image.open(drawer_source/'drawers_oak_front_1.png').convert('RGBA').resize((28,28),Image.Resampling.NEAREST)
    result['drawer.png']=Image.new('RGBA',(32,32)); result['drawer.png'].alpha_composite(drawer,(2,2))
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
