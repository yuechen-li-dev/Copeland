from pathlib import Path
from PIL import Image, ImageChops
import json, hashlib
root = Path.cwd()
art = root / 'src/TinyFarm/TinyFarm.Native/Assets/GateA'
output = root / 'artifacts/tinyfarm-gate-a'
provenance = json.loads((art / 'provenance.json').read_text(encoding='utf-8-sig'))
source_audit = []
heights = {'gardener.png': 64, 'slime.png': 50, 'mara.png': 64, 'turnip.png': 32}
for row in provenance['assets']:
    path = art / row['file']
    image = Image.open(path).convert('RGBA')
    width, height = image.size
    row['sourceDimensions'] = [width, height]
    row['sourceFileBytes'] = path.stat().st_size
    row['sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
    columns, rows = row['grid']
    poses = []
    for y in range(rows):
        for x in range(columns):
            rect = (x * width // columns, y * height // rows, (x+1) * width // columns, (y+1) * height // rows)
            cell = image.crop(rect)
            box = cell.getchannel('A').point(lambda alpha: 255 if alpha > 16 else 0).getbbox()
            assert box is not None
            poses.append({'trimmedSourceDimensions': [box[2]-box[0], box[3]-box[1]]})
    max_height = max(pose['trimmedSourceDimensions'][1] for pose in poses)
    if row['file'] in heights or row['file'] == 'props.png':
        for index, pose in enumerate(poses):
            source_w, source_h = pose['trimmedSourceDimensions']
            factor = heights[row['file']] / max_height if row['file'] != 'props.png' else [72,52,48,44][index] / source_h
            pose['presentationHeightWorldMetres'] = source_h * factor / 48
            pose['display1080p'] = [round(source_w * factor * (1080/11)/48,2), round(source_h * factor * (1080/11)/48,2)]
            pose['sourcePixelsPerDisplayedPixel1080pAxis'] = round(48 / (factor*(1080/11)),3)
            pose['display1440p'] = [round(value * 4/3,2) for value in pose['display1080p']]
    source_audit.append({'file': row['file'], 'sourceDimensions':[width,height], 'fileBytes': row['sourceFileBytes'], 'rgbaPayloadBytes': width*height*4, 'sampling':'Linear sRGB straight alpha', 'poses':poses})
(art / 'provenance.json').write_text(json.dumps(provenance,indent=2)+'\n',encoding='utf-8',newline='\n')
(output / 'asset-audit.json').write_text(json.dumps({'metric':'source pixels per displayed pixel along an axis, including trimmed alpha bounds; evidence, not a quality score','runtimeMipmaps':False,'assets':source_audit},indent=2)+'\n',encoding='utf-8')
reused=[]
for file in ['M24/meadow-slab.png','M24/tree.png','M25/farmhouse-three-quarter.png','M11/tinyfarm-sprite-atlas-source.png']:
    path=art.parent/file
    image=Image.open(path)
    reused.append({'file':file,'width':image.width,'height':image.height,'rgbaPayloadBytes':image.width*image.height*4})
brushes=[{'id':name,'width':w*64,'height':h*64,'rgbaPayloadBytes':w*h*64*64*4} for name,w,h in [('farm-path',18,12),('wood-path',22,14),('bank',22,14),('river',22,14)]]
total=sum(row['rgbaPayloadBytes'] for row in source_audit+reused+brushes)
(output/'texture-memory.json').write_text(json.dumps({'measurement':'RGBA8 payload accounting from actual source PNG dimensions and cold brush raster dimensions; not measured Vulkan allocator residency','spriteTexturesResident':14,'spriteUploadsAfterColdAttach':14,'newGeneratedTextures':source_audit,'reusedTextures':reused,'brushes':brushes,'spriteRgbaPayloadBytes':total,'spriteRgbaPayloadMiB':round(total/1048576,2),'excluded':'Image alignment/driver overhead, field image, font/profile atlases, portrait, compositor targets, readback and CPU copies','runtimeDownsampleVariants':False},indent=2)+'\n',encoding='utf-8')
hud=Image.open(output/'garden-hud.png').convert('RGBA')
world=Image.open(output/'garden-world.png').convert('RGBA')
region=(400,220,1920,980)
difference=ImageChops.difference(hud.crop(region),world.crop(region))
(output/'hud-world-proof.json').write_text(json.dumps({'semanticHashUnchanged':'native proof F9 assertion','comparisonRectangle':region,'comparedPixels':(region[2]-region[0])*(region[3]-region[1]),'worldPixelsExactlyEqualAwayFromHud':difference.getbbox() is None,'scope':'same start position, zero elapsed capture; excludes HUD area'},indent=2)+'\n',encoding='utf-8')
print('Sprite RGBA payload MiB:',round(total/1048576,2))
print('World-only unchanged region:',difference.getbbox())
print('Source dimensions:',[(row['file'],row['sourceDimensions']) for row in source_audit])
