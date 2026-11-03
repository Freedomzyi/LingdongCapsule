"""Regenerate the original geometric icon. Optional asset tooling: pip install pillow."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1] / "assets"
scale = 4
image = Image.new("RGBA", (256*scale, 256*scale))
draw = ImageDraw.Draw(image)
def box(coords): return tuple(int(v*scale) for v in coords)
draw.rounded_rectangle(box((8,8,248,248)), radius=56*scale, fill="#2474ED")
draw.rounded_rectangle(box((34,82,222,174)), radius=46*scale, fill="#10131C")
draw.ellipse(box((60,106,104,150)), fill="#5BE5BC")
draw.rounded_rectangle(box((120,111,187,121)), radius=5*scale, fill="white")
draw.rounded_rectangle(box((120,135,163,144)), radius=4*scale, fill="#AAB7D5")
image = image.resize((256,256), Image.Resampling.LANCZOS)
image.save(root / "icon.png")
image.save(root / "icon.ico", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
