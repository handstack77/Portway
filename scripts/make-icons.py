"""Generate the repository's geometric application icon. Requires Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw
root = Path(__file__).resolve().parent.parent / 'assets'
root.mkdir(exist_ok=True)
im = Image.new('RGBA', (1024, 1024))
d = ImageDraw.Draw(im)
d.rounded_rectangle((0, 0, 1023, 1023), 245, fill='#066fd1')
for points in [[(250, 370), (775, 370)], [(635, 225), (780, 370), (635, 515)], [(775, 654), (250, 654)], [(390, 509), (245, 654), (390, 799)]]:
    d.line(points, fill='#ffffff', width=73, joint='curve')
    for x, y in points: d.ellipse((x-36,y-36,x+36,y+36),fill='#ffffff')
im.save(root / 'portway.icns')
im.resize((256,256),Image.Resampling.LANCZOS).save(root / 'portway.png')
im.save(root / 'portway.ico',sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)])
