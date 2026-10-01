"""Portway 로고·파비콘·OS 아이콘을 하나의 도형 정의에서 생성합니다. Pillow가 필요합니다.

마크는 파일이 드나드는 관문(아치) 안에 양방향 전송 화살표를 둔 형태입니다.
작은 크기(32px 이하)는 선을 굵게 하고 화살표를 단순화한 별도 도형을 사용합니다.

    python scripts/make-icons.py
"""
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

REPO = Path(__file__).resolve().parent.parent
WEB = REPO / 'src' / 'Portway.Desktop' / 'wwwroot'
ASSETS = REPO / 'assets' / 'Portway.Artifact' / 'assets'
DOC_IMAGES = REPO / 'assets' / 'Portway.Document' / 'docs' / 'images'

# Tabler 1.4.0 기본 primary(#066fd1)를 중심으로 한 대각선 그라데이션입니다.
TOP = '#2a8cf0'
BOTTOM = '#0559b3'
INK = '#ffffff'

# 64×64 격자 기준 도형입니다. arch=(중심 x, 중심 y, 반지름, 기둥 아래 y, 선 두께)
# arrows는 (시작점, 끝점, 머리 길이, 양쪽 머리 여부)입니다.
FULL = {
    'radius': 15,
    'arch': (32, 30, 17, 52, 5),
    'arrows': [((24, 34), (40, 34), 5, False), ((40, 45), (24, 45), 5, False)],
    'arrow_width': 4,
}
# 32px 크기에서는 선을 굵게 하고 화살표 간격을 넓힙니다.
SMALL = {
    'radius': 14,
    'arch': (32, 30, 19, 55, 7),
    'arrows': [((23, 33), (41, 33), 6, False), ((41, 47), (23, 47), 6, False)],
    'arrow_width': 5.5,
}
# 24px 이하에서는 관문을 통과하는 화살표 하나로 줄입니다.
TINY = {
    'radius': 13,
    'arch': (32, 30, 20, 56, 8.5),
    'arrows': [((20, 41), (42, 41), 9, False)],
    'arrow_width': 7,
}


def arrow_strokes(spec):
    """화살표마다 몸통 선과 머리 꺾은선을 반환합니다."""
    strokes = []
    for (x1, y1), (x2, y2), head, both in spec['arrows']:
        direction = 1 if x2 > x1 else -1
        strokes.append([(x1, y1), (x2, y2)])
        strokes.append([(x2 - direction * head, y2 - head), (x2, y2), (x2 - direction * head, y2 + head)])
        if both:
            strokes.append([(x1 + direction * head, y1 - head), (x1, y1), (x1 + direction * head, y1 + head)])
    return strokes


def fmt(value):
    return f'{value:.2f}'.rstrip('0').rstrip('.')


def path_data(points):
    return 'M' + ' L'.join(f'{fmt(x)} {fmt(y)}' for x, y in points)


def svg_mark(spec, gradient_id, x=0, y=0):
    cx, cy, r, bottom, width = spec['arch']
    arch = f'M{fmt(cx - r)} {fmt(bottom)}V{fmt(cy)}a{fmt(r)} {fmt(r)} 0 0 1 {fmt(2 * r)} 0V{fmt(bottom)}'
    arrows = ''.join(path_data(s) for s in arrow_strokes(spec))
    return (
        f'<g transform="translate({x} {y})">'
        f'<defs><linearGradient id="{gradient_id}" x1="0" y1="0" x2="1" y2="1">'
        f'<stop offset="0" stop-color="{TOP}"/><stop offset="1" stop-color="{BOTTOM}"/></linearGradient></defs>'
        f'<rect width="64" height="64" rx="{spec["radius"]}" fill="url(#{gradient_id})"/>'
        f'<g fill="none" stroke="{INK}" stroke-linecap="round" stroke-linejoin="round">'
        f'<path d="{arch}" stroke-width="{fmt(width)}"/>'
        f'<path d="{arrows}" stroke-width="{fmt(spec["arrow_width"])}"/></g></g>'
    )


def svg_document(spec, title):
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" role="img">'
        f'<title>{title}</title>{svg_mark(spec, "portway-bg")}</svg>\n'
    )


def svg_wordmark():
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 64" role="img">'
        '<title>Portway</title>'
        # 문서 뷰어의 테마와 OS 테마가 다를 수 있어 밝은·어두운 배경 모두에서 읽히는 primary 색을 씁니다.
        '<style>text{fill:#066fd1;font:650 42px "Noto Sans KR Variable","Noto Sans KR","Segoe UI",system-ui,sans-serif;letter-spacing:-1.2px}</style>'
        f'{svg_mark(FULL, "portway-wordmark-bg")}'
        '<text x="80" y="46">Portway</text></svg>\n'
    )


def render(spec, size, padding=0):
    """4배 슈퍼샘플링으로 그린 뒤 축소합니다. padding은 64 격자 밖 여백(비율)입니다."""
    scale = 4
    canvas = size * scale
    inner = round(canvas * (1 - 2 * padding))
    offset = (canvas - inner) // 2
    unit = inner / 64

    vertical = Image.linear_gradient('L')
    gradient = ImageChops.add(vertical, vertical.transpose(Image.Transpose.ROTATE_90), scale=2)
    gradient = gradient.resize((inner, inner), Image.Resampling.BILINEAR)
    fill = Image.composite(Image.new('RGBA', (inner, inner), BOTTOM), Image.new('RGBA', (inner, inner), TOP), gradient)
    mask = Image.new('L', (inner, inner), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, inner - 1, inner - 1), round(spec['radius'] * unit), fill=255)

    image = Image.new('RGBA', (canvas, canvas), (0, 0, 0, 0))
    image.paste(fill, (offset, offset), mask)
    draw = ImageDraw.Draw(image)

    def stroke(points, width):
        scaled = [(offset + x * unit, offset + y * unit) for x, y in points]
        w = width * unit
        draw.line(scaled, fill=INK, width=round(w), joint='curve')
        for x, y in (scaled[0], scaled[-1]):
            draw.ellipse((x - w / 2, y - w / 2, x + w / 2, y + w / 2), fill=INK)

    cx, cy, r, bottom, width = spec['arch']
    outer = (r + width / 2) * unit
    draw.arc((offset + cx * unit - outer, offset + cy * unit - outer, offset + cx * unit + outer, offset + cy * unit + outer), 180, 360, fill=INK, width=round(width * unit))
    stroke([(cx - r, cy), (cx - r, bottom)], width)
    stroke([(cx + r, cy), (cx + r, bottom)], width)
    for points in arrow_strokes(spec):
        stroke(points, spec['arrow_width'])
    return image.resize((size, size), Image.Resampling.LANCZOS)


def icon_for(size, padding=0):
    return render(TINY if size <= 24 else SMALL if size <= 32 else FULL, size, padding)


def main():
    (WEB / 'logo.svg').write_text(svg_document(FULL, 'Portway'), encoding='utf-8', newline='\n')
    (WEB / 'favicon.svg').write_text(svg_document(TINY, 'Portway'), encoding='utf-8', newline='\n')
    DOC_IMAGES.mkdir(parents=True, exist_ok=True)
    (DOC_IMAGES / 'portway-logo.svg').write_text(svg_wordmark(), encoding='utf-8', newline='\n')

    ico_sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [icon_for(size) for size in ico_sizes]
    frames[-1].save(ASSETS / 'portway.ico', format='ICO', sizes=[(s, s) for s in ico_sizes], append_images=frames[:-1])
    # Pillow는 기준 이미지보다 큰 크기를 버리므로 가장 큰 프레임을 기준으로 저장합니다.
    icon_for(48).save(WEB / 'favicon.ico', format='ICO', sizes=[(16, 16), (32, 32), (48, 48)], append_images=[icon_for(16), icon_for(32)])

    icon_for(512).save(ASSETS / 'portway.png', optimize=True)
    icon_for(180).save(WEB / 'apple-touch-icon.png', optimize=True)

    # macOS 아이콘은 1024 캔버스 안 824 격자에 두고 둘레를 비웁니다.
    mac = render(FULL, 1024, padding=100 / 1024)
    mac.save(ASSETS / 'portway.icns', format='ICNS', append_images=[render(FULL, s, padding=100 / 1024) for s in (16, 32, 64, 128, 256, 512)])
    print('Portway 로고, 파비콘, OS 아이콘을 생성했습니다.')


if __name__ == '__main__':
    main()
