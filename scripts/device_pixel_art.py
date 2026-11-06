"""Device pixel artwork; provenance: assets/device-pixel-source/README.md.

All drawing happens on the final 32x32 or 48x48 RGBA canvas. No generated
high-resolution image is sampled, quantized, sharpened or resized into a sprite.
The interfaces and drive use authored geometry. The controller uses the exact
16x16 AE2 cell identities, placed on an explicitly authored integer-pixel grid.
"""
from pathlib import Path
from collections import Counter
import colorsys
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]

# Explicit material ramps, not a palette automatically fitted to an image.
# Plum outline and warm metal highlight follow extracted native machinery.
P = {
    'outline': (41,34,52), 'recess': (49,45,61), 'panel': (68,64,80),
    'metal_dark': (103,102,117), 'metal_mid': (139,137,147),
    'metal': (166,169,166), 'metal_light': (189,192,181),
    'metal_glint': (195,197,186),
    'blue_dark': (58,79,104), 'blue_field': (79,108,131),
    'blue_mid': (105,148,170), 'blue': (131,187,209),
    'blue_hi': (170,212,220), 'blue_shade': (88,149,175),
    'violet_dark': (59,43,82), 'violet': (130,84,174),
    'violet_shade': (95,64,139), 'pink': (217,164,224),
    'pink_shade': (180,123,199),
    'water': (123,196,186), 'water_shade': (105,175,172),
    'water_hi': (167,211,198),
    'trace_light': (183,232,121), 'trace_dark': (105,202,90),
}


def color(name):
    return P[name] + (255,)


def rect(draw, box, name):
    draw.rectangle(box, fill=color(name))


def line(draw, points, name, width=1):
    draw.line(points, fill=color(name), width=width)


def casing(size, margin):
    im = Image.new('RGBA', (size,size)); d = ImageDraw.Draw(im)
    a,b = margin,size-margin-1
    d.polygon([(a+2,a),(b-2,a),(b,a+2),(b,b-2),(b-2,b),(a+2,b),(a,b-2),(a,a+2)], fill=color('outline'))
    d.polygon([(a+2,a+1),(b-2,a+1),(b-1,a+2),(b-1,b-2),(b-2,b-1),(a+2,b-1),(a+1,b-2),(a+1,a+2)], fill=color('metal'))
    rect(d,(a+2,a+2,b-2,b-2),'metal')
    line(d,[(a+2,a+1),(b-2,a+1)],'metal_light')
    line(d,[(b-1,a+3),(b-1,b-2),(b-2,b-1),(a+3,b-1)],'metal_mid')
    return im


def interface(fluid=False):
    im = casing(32,2); d=ImageDraw.Draw(im)
    rect(d,(5,5,26,26),'recess')
    # Outer dark ring with broad, flat changes in its metal backing.
    rect(d,(6,6,25,25),'metal_light')
    rect(d,(7,7,24,24),'blue_field')
    # AE interface face: two square circuits and eight short edge contacts.
    rect(d,(9,9,22,22),'blue')
    rect(d,(12,12,19,19),'blue_field')
    for x in (10,20):
        rect(d,(x,7,x+1,9),'blue')
        rect(d,(x,22,x+1,24),'blue')
    for y in (10,20):
        rect(d,(7,y,9,y+1),'blue')
        rect(d,(22,y,24,y+1),'blue')
    rect(d,(12,12,19,19),'blue_field')
    rect(d,(13,13,18,18),'blue_mid')
    rect(d,(14,14,17,17),'blue_dark')
    if fluid:
        stamp=[
            '....O....',
            '...OLO...',
            '...OLO...',
            '..OHLSO..',
            '..OHLSO..',
            '.OHLLSSO.',
            'OHLLLSSSO',
            'OHLLLSSSO',
            'OLLLLSSSO',
            '.OLSSSSO.',
            '..OOOOO..',
        ]
        palette={'O':'outline','L':'water','S':'water_shade','H':'water_hi'}
        for y,row in enumerate(stamp):
            for x,p in enumerate(row):
                if p!='.':im.putpixel((20+x,18+y),color(palette[p]))
    return im


def drive():
    im=casing(32,2);d=ImageDraw.Draw(im)
    rect(d,(4,4,27,27),'metal_mid')
    rect(d,(14,4,17,27),'recess')
    for y in (4,9,14,19,24):
        for x in (5,18):
            # The same authored 9x4 bay repeated exactly, on a five-pixel pitch.
            rect(d,(x,y,x+8,y+3),'outline')
            rect(d,(x+1,y+1,x+7,y+2),'violet_dark')
            rect(d,(x+2,y+1,x+3,y+1),'pink')
            rect(d,(x+2,y+2,x+3,y+2),'pink')
            rect(d,(x+4,y+1,x+5,y+1),'violet')
            rect(d,(x+4,y+2,x+5,y+2),'violet')
            line(d,[(x,y+3),(x+8,y+3)],'metal_mid')
            im.putpixel((x,y+1),color('metal_dark'))
            im.putpixel((x,y+2),color('metal_dark'))
    return im


# Six background/casing columns and rows are 2px rather than 3px, so every
# interior vertical/horizontal trace stays 3px within the 42px visible body.
# Cell connectivity and both source light-band identities remain unchanged.
CORE_COLUMNS = [3,2,3,2,3,2,3,2,3,2,3,3,3,3,2,3]
CORE_ROWS = [3,2,3,2,3,3,2,3,2,3,2,3,3,3,2,3]
CORE_MATERIALS = {
    (242,242,242): (207,209,202),
    (232,232,234): (190,193,191),
    (65,63,84): (63,57,82),
    (77,77,103): (75,72,94),
    (135,143,165): (132,136,150),
    (120,126,151): (119,120,139),
    (31,31,31): (35,31,44),
    (31,34,33): (39,35,47),
    (26,28,28): (35,31,44),
    (38,40,38): (44,40,54),
    (45,47,45): (44,40,54),
}


def core():
    base=Image.open(ROOT/'assets/ae2-source/controller_powered.png').convert('RGBA')
    lights=Image.open(ROOT/'assets/ae2-source/controller_lights.png').convert('RGBA').crop((0,0,16,16))
    im=Image.new('RGBA',(48,48));d=ImageDraw.Draw(im)
    assert sum(CORE_COLUMNS)==sum(CORE_ROWS)==42
    y=3
    for sy,height in enumerate(CORE_ROWS):
        x=3
        for sx,width in enumerate(CORE_COLUMNS):
            light=lights.getpixel((sx,sy))
            if light[3]:
                value=color('trace_light' if light[:3]==(179,248,109) else 'trace_dark')
            else:
                pixel=base.getpixel((sx,sy))
                if not pixel[3]:raise ValueError('Missing AE2 cell at '+str((sx,sy)))
                value=CORE_MATERIALS[pixel[:3]]+(255,)
            d.rectangle((x,y,x+width-1,y+height-1),fill=value)
            x+=width
        y+=height
    # Native-style one-pixel perimeter only on the white panels. Light bands
    # continue to the edge at their original ports; there is no raised bevel.
    trace={color('trace_light'),color('trace_dark')}
    for p in range(3,45):
        for xy in ((p,3),(p,44),(3,p),(44,p)):
            if im.getpixel(xy) not in trace:im.putpixel(xy,color('outline'))
    return im


def inactive(im):
    # Like native machinery: keep the physical casing; extinguish lit surfaces.
    off={
        'blue_dark':(45,60,72),'blue_field':(58,75,88),
        'blue_mid':(70,94,106),'blue':(82,110,120),
        'blue_hi':(103,131,137),'blue_shade':(59,85,96),
        'pink':(117,100,132),'pink_shade':(87,74,103),
        'violet':(86,68,112),'violet_shade':(65,52,88),
        'trace_light':(119,137,131), 'trace_dark':(89,111,105),
    }
    mapping={color(k):v+(255,) for k,v in off.items()}
    out=im.copy();out.putdata([mapping.get(p,p) for p in im.get_flattened_data()]);return out


def controller_frames(im):
    # Each authored shade is transformed as a whole, keeping native colour blocks.
    lights=Image.open(ROOT/'assets/ae2-source/controller_lights.png').convert('RGBA')
    pairs=[]
    for frame in range(12):
        count=Counter(p[:3] for p in lights.crop((0,frame*16,16,(frame+1)*16)).get_flattened_data() if p[3])
        pairs.append([colorsys.rgb_to_hsv(*(c/255 for c in p)) for p,n in count.most_common(2)])
    for frame in range(12):
        mapping={}
        for name,rgb in P.items():
            if not name.startswith('trace_'):continue
            band=0 if name.startswith('trace_light') else 1
            h,s,v=colorsys.rgb_to_hsv(*(c/255 for c in rgb));h0,s0,v0=pairs[0][band];ht,st,vt=pairs[frame][band]
            shifted=colorsys.hsv_to_rgb((h+ht-h0)%1,min(1,s*st/s0),min(1,v*vt/v0))
            mapping[color(name)]=tuple(round(c*255) for c in shifted)+(255,)
        out=im.copy();out.putdata([mapping.get(p,p) for p in im.get_flattened_data()]);yield frame,out



def assets():
    """Return the 20 final-size sprites; never inset or darken them again."""
    images={'item-interface':interface(),'fluid-interface':interface(True),'drive':drive(),'core':core()}
    result={}
    for name,sprite in images.items():
        result[name+'.png']=sprite
        result[name+'-off.png']=inactive(sprite)
        if name=='core':
            for frame,artwork in controller_frames(sprite):
                result['core-'+str(frame)+'.png']=artwork
    return result
