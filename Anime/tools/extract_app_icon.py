"""Extract Lanerc 1.0.8 launcher artwork and generate Anime's platform icons (Pillow)."""
from io import BytesIO
import json
from pathlib import Path
import sys
from zipfile import ZipFile

from PIL import Image


ANIME = Path(__file__).resolve().parents[1]
APK = Path(sys.argv[1]) if len(sys.argv) > 1 else ANIME.parent / 'issues/anime/lanerc/lanerc.apk'
# AndroidManifest icon 0x7f0c0010; res/BW.xml references background 0x7f0c0011
# and foreground 0x7f0c0012. Paths below come from aapt2 dump resources.
LAYERS = {
    'mdpi': ('9w', '3z', 'QZ'),
    'hdpi': ('yn', 'I2', 'zr'),
    'xhdpi': ('FS', '6h', 'Em'),
    'xxhdpi': ('RJ', 'cV', 'Lf'),
    'xxxhdpi': ('o-', 'Gx', 'as'),
}


def read_image(apk, path):
    return Image.open(BytesIO(apk.read(path))).convert('RGBA')


with ZipFile(APK) as apk:
    for density, names in LAYERS.items():
        target = ANIME / 'MikoApp.Anime.Android/Resources' / f'mipmap-{density}'
        for name, source in zip(('appicon', 'appicon_background', 'appicon_foreground'), names):
            # Preserve the APK's exact pixels, density sizes and adaptive-icon safe area.
            (target / f'{name}.png').write_bytes(apk.read(f'res/{source}.png'))

    foreground = read_image(apk, 'res/as.png')
    background = read_image(apk, 'res/Gx.png')
    assert foreground.size == background.size == (432, 432)
    composed = Image.alpha_composite(background, foreground)
    # Adaptive layers are 108dp; the launcher viewport is the central 72dp.
    composed = composed.crop((72, 72, 360, 360))
    opaque = Image.new('RGBA', composed.size, 'white')
    opaque.alpha_composite(composed)
    master = opaque.convert('RGB').resize((1024, 1024), Image.Resampling.LANCZOS)

branding = ANIME / 'Branding'
branding.mkdir(exist_ok=True)
master.save(branding / 'appicon.png')
master.save(branding / 'appicon.ico', sizes=[(n, n) for n in (16, 24, 32, 48, 64, 128, 256)])

ios = ANIME / 'MikoApp.Anime.iOS/Assets.xcassets/AppIcon.appiconset'
contents = json.loads((ios / 'Contents.json').read_text(encoding='utf-8-sig'))
generated = set()
for entry in contents['images']:
    filename = entry['filename']
    size = round(float(entry['size'].split('x')[0]) * float(entry['scale'].rstrip('x')))
    if filename not in generated:
        master.resize((size, size), Image.Resampling.LANCZOS).save(ios / filename)
        generated.add(filename)
print(f'Extracted 15 Android layers; generated {len(generated)} iOS icons and desktop PNG/ICO.')
