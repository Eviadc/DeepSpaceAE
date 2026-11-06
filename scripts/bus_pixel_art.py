"""Standalone bus icons; provenance: assets/bus-pixel-source/README.md.

Render the pinned AE2 geometry and GUI transform directly at 16px. Only material
colors and raster-grid alignment change; model vertices and UVs remain intact.
"""
from pathlib import Path
import json,math
from PIL import Image
SOURCE=Path(__file__).resolve().parents[1]/'assets/ae2-source'

def rotate(p):
    # Pinned AE2 part_item_base GUI rotation [30,225,0].
    x,y,z=p; a=math.radians(225);b=math.radians(30)
    x,z=x*math.cos(a)+z*math.sin(a),-x*math.sin(a)+z*math.cos(a)
    y,z=y*math.cos(b)-z*math.sin(b),y*math.sin(b)+z*math.cos(b)
    return x,-y,z

def icon(kind,shift=None):
    # Subpixel alignment selects the native pixel grid, not a model deformation.
    # Keep the original planes and use one uniform scalar for both screen axes.
    if shift is None:shift={'storage':(.25,0),'import':(0,.25),'export':(0,-.25)}[kind]
    model=json.loads((SOURCE/(kind+'_bus_item.json')).read_text())
    faces=[];allpts=[]
    for e in model['elements']:
        a,b,c=e['from'];x,y,z=e['to']
        quads={'north':[(x,y,c),(a,y,c),(a,b,c),(x,b,c)],
               'south':[(a,y,z),(x,y,z),(x,b,z),(a,b,z)],
               'east':[(x,y,z),(x,y,c),(x,b,c),(x,b,z)],
               'west':[(a,y,c),(a,y,z),(a,b,z),(a,b,c)],
               'up':[(a,y,c),(x,y,c),(x,y,z),(a,y,z)],
               'down':[(a,b,z),(x,b,z),(x,b,c),(a,b,c)]}
        for face,coords in quads.items():
            pts=[rotate(tuple(v-8 for v in p)) for p in coords];allpts.extend(pts)
            tex=e['faces'][face];u,v,u1,v1=tex['uv'];uv=[(u,v),(u1,v),(u1,v1),(u,v1)]
            turns=tex.get('rotation',0)//90;uv=uv[turns:]+uv[:turns]
            key=tex['texture'].lstrip('#');name=model['textures'][key].split('/')[-1]+'.png'
            faces.append((face,pts,uv,Image.open(SOURCE/name).convert('RGBA')))
    lo=[min(p[i] for p in allpts) for i in (0,1)];hi=[max(p[i] for p in allpts) for i in (0,1)]
    scale=14/max(hi[i]-lo[i] for i in (0,1));offset=[(16-(hi[i]-lo[i])*scale)/2+shift[i] for i in (0,1)]
    im=Image.new('RGBA',(16,16));depths=[-1e9]*256
    def material(c,face):
        r,g,b,a=c
        # AE2 materials regrouped into the native-device color ramps.
        # Geometry, source UVs and GUI transform are unchanged.
        if g>r*1.25 and g>b*1.1:
            result=(143,194,119) if g>240 else ((93,151,91) if g>200 else (60,112,70))
        elif b>r*1.2 and b>g*1.1 and b>130:
            result=(131,187,209) if b>240 else ((79,108,131) if b>195 else (58,79,104))
        else:
            v=(r+g+b)/3
            result=(195,197,186) if v>237 else (189,192,181) if v>222 else (166,169,166) if v>203 else (139,137,147) if v>180 else (103,102,117) if v>135 else (68,64,80) if v>70 else (41,34,52)
        if face in ('north','south'):
            if result in ((195,197,186),(189,192,181)):result=(139,137,147)
            elif result==(166,169,166):result=(103,102,117)
            elif result==(139,137,147):result=(103,102,117)
        elif face in ('east','west'):
            if result in ((195,197,186),(189,192,181)):result=(166,169,166)
            elif result==(68,64,80):result=(49,45,61)
        return result+(a,)
    for face,raw,uv,texture in faces:
        pts=[(offset[0]+(p[0]-lo[0])*scale,offset[1]+(p[1]-lo[1])*scale,p[2]) for p in raw]
        for ids in [(0,1,2),(0,2,3)]:
            a,b,c=[pts[i] for i in ids];den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(den)<1e-8:continue
            for py in range(16):
                for px in range(16):
                    w0=((b[1]-c[1])*(px+.5-c[0])+(c[0]-b[0])*(py+.5-c[1]))/den
                    w1=((c[1]-a[1])*(px+.5-c[0])+(a[0]-c[0])*(py+.5-c[1]))/den
                    w=[w0,w1,1-w0-w1]
                    if min(w)<-1e-7:continue
                    z=sum(w[j]*pts[k][2] for j,k in enumerate(ids))
                    if z<depths[py*16+px]:continue
                    u=sum(w[j]*uv[k][0] for j,k in enumerate(ids));v=sum(w[j]*uv[k][1] for j,k in enumerate(ids))
                    rgba=texture.getpixel((min(15,max(0,int(u))),min(15,max(0,int(v)))))
                    if rgba[3]:
                        im.putpixel((px,py),material(rgba,face));depths[py*16+px]=z
    return im

def assets():
    result={}
    for kind in ['storage','import','export']:
        name='item-storage-bus' if kind=='storage' else kind+'-bus'
        result[name+'.png']=icon(kind)
    return result
