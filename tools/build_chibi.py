"""Build aligned RGBA clips from the approved chibi sheets.

Usage: python tools/build_chibi.py atlas.png sleep.png
Requires Pillow, numpy, scipy and opencv-python-headless.
Interpolation is baked once, never performed on the WPF UI thread.
"""
from pathlib import Path
import sys, json
import numpy as np
import cv2
from PIL import Image
from scipy.ndimage import label, find_objects, binary_dilation

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/chibi'
OUT.mkdir(parents=True, exist_ok=True)
W, H = 224, 288
BASE = 280

def extract(path, minimum):
    rgba = np.array(Image.open(path).convert('RGBA'))
    labels, _ = label(rgba[:, :, 3] > 160)
    result = []
    for idx, box in enumerate(find_objects(labels), 1):
        if box is None: continue
        mask = labels[box] == idx
        if mask.sum() < minimum: continue
        y, x = box
        y0, y1 = max(0, y.start-2), min(rgba.shape[0], y.stop+2)
        x0, x1 = max(0, x.start-2), min(rgba.shape[1], x.stop+2)
        sprite = rgba[y0:y1, x0:x1].copy()
        keep = binary_dilation(labels[y0:y1, x0:x1] == idx, iterations=2)
        alpha = sprite[:, :, 3].astype(float)
        sprite[:, :, 3] = np.where(keep, np.clip((alpha-8)*255/245, 0, 255), 0)
        sprite[sprite[:, :, 3] == 0, :3] = 0
        result.append((y.start, x.start, Image.fromarray(sprite)))
    return result

def canvas(sprite, name):
    if name == 'idle':
        # Keep the same head size as walk; shorten only the tall standing legs.
        head = sprite.crop((0, 0, sprite.width, 124))
        body = sprite.crop((0, 124, sprite.width, sprite.height))
        body = body.resize((body.width, 98), Image.Resampling.LANCZOS)
        sprite = Image.new('RGBA', (head.width, 222))
        sprite.alpha_composite(head)
        sprite.alpha_composite(body, (0,124))
    elif name == 'sleep':
        sprite = sprite.resize((208, round(sprite.height*208/sprite.width)), Image.Resampling.LANCZOS)
    frame = Image.new('RGBA', (W,H))
    frame.alpha_composite(sprite, ((W-sprite.width)//2, BASE-sprite.height))
    return np.array(frame)

def flow_pair(a, b, steps=4):
    # Compute on a neutral matte; interpolate premultiplied RGBA to avoid fringes.
    def gray(x):
        alpha = x[:,:,3:4].astype(np.float32)/255
        rgb = x[:,:,:3]*alpha + 235*(1-alpha)
        return cv2.cvtColor(rgb.astype(np.uint8), cv2.COLOR_RGB2GRAY)
    ga, gb = gray(a), gray(b)
    estimator = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
    estimator.setFinestScale(0)
    forward = estimator.calc(ga,gb,None)
    backward = estimator.calc(gb,ga,None)
    xx, yy = np.meshgrid(np.arange(W,dtype=np.float32), np.arange(H,dtype=np.float32))
    def premul(x):
        p = x.astype(np.float32)/255
        p[:,:,:3] *= p[:,:,3:4]
        return p
    pa,pb = premul(a),premul(b)
    frames = []
    for i in range(steps):
        if i == 0: frames.append(a); continue
        t=i/steps
        wa=cv2.remap(pa,xx-t*forward[:,:,0],yy-t*forward[:,:,1],cv2.INTER_LINEAR)
        wb=cv2.remap(pb,xx-(1-t)*backward[:,:,0],yy-(1-t)*backward[:,:,1],cv2.INTER_LINEAR)
        # Select one warped drawing instead of dissolving two silhouettes.
        # This avoids transparent double hands/feet at occlusion boundaries.
        mixed=wa if t < .5 else wb
        mixed[:,:,:3]/=np.maximum(mixed[:,:,3:4],1/255)
        frame=np.clip(mixed*255,0,255).astype(np.uint8)
        frame[frame[:,:,3]<5]=0
        frames.append(frame)
    return frames

parts=extract(sys.argv[1],8000)
assert len(parts)==40, f'Expected 40 atlas sprites, found {len(parts)}'
names=['walk','climb','crawl','play','idle']
keys={}
parts.sort(key=lambda item:item[0])
for row,name in enumerate(names):
    sprites=sorted(parts[row*8:(row+1)*8],key=lambda item:item[1])
    keys[name]=[canvas(sprite,name) for _,_,sprite in sprites]
sleep=extract(sys.argv[2],30000)
assert len(sleep)==8, f'Expected 8 sleeping sprites, found {len(sleep)}'
sleep.sort(key=lambda item:item[0])
sleep=sorted(sleep[:4],key=lambda item:item[1])+sorted(sleep[4:],key=lambda item:item[1])
keys['sleep']=[canvas(sprite,'sleep') for _,_,sprite in sleep]
# A relaxed idle with a blink; omit the unrelated folded-hands drawing.
keys['idle']=[keys['idle'][i] for i in [0,0,0,0,0,5,0,0]]
clips={}
durations={'walk':.88,'climb':1.25,'crawl':1.5,'play':.8,'idle':4.8,'sleep':3.6}
for name,frames in keys.items():
    baked=[]
    for i,a in enumerate(frames): baked.extend(flow_pair(a,frames[(i+1)%len(frames)]))
    sheet=Image.new('RGBA',(W*8,H*4))
    for i,f in enumerate(baked): sheet.paste(Image.fromarray(f),((i%8)*W,(i//8)*H))
    packed = sheet.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
    temp = OUT/f'{name}.tmp.png'
    packed.save(temp,optimize=True)
    with Image.open(temp) as check: check.load()
    temp.replace(OUT/f'{name}.png')
    clips[name]={'frames':32,'seconds':durations[name]}
    # Visual inspection artifact (not a production dependency).
    previews=[]
    for f in baked:
        bg=Image.new('RGBA',(W,H),'#e8edf3');bg.alpha_composite(Image.fromarray(f))
        previews.append(bg.convert('RGB'))
    previews[0].save(OUT/f'{name}-preview.gif',save_all=True,append_images=previews[1:],duration=round(durations[name]*1000/32),loop=0)
(OUT/'manifest.json').write_text(json.dumps({'frameWidth':W,'frameHeight':H,'columns':8,'anchor':[W//2,BASE],'clips':clips},indent=2))
print(json.dumps({'clips':list(clips),'frames':sum(c['frames'] for c in clips.values())}))
