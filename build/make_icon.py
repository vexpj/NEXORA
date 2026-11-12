"""Генерирует assets/NEXORA.ico (16–256 px). Запуск: python build/make_icon.py"""
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((32, 32, S - 32, S - 32), radius=220, fill=(25, 25, 31, 255),
                    outline=(168, 85, 247, 255), width=28)
pts = [(.27, .75), (.27, .25), (.38, .25), (.63, .58), (.63, .25), (.73, .25),
       (.73, .75), (.62, .75), (.37, .42), (.37, .75)]
d.polygon([(x * S, y * S) for x, y in pts], fill=(168, 85, 247, 255))

out = Path(__file__).resolve().parent.parent / "assets" / "NEXORA.ico"
out.parent.mkdir(exist_ok=True)
img.save(out, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("OK", out)
