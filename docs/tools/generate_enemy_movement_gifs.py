"""Generate the enemy movement GIFs used by the game creation manual.

Requires Pillow. Run this file from the repository root.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


WIDTH = 384
HEIGHT = 432
FRAME_COUNT = 55
FRAME_DURATION_MS = 100
ORIGIN_X = WIDTH // 2
AMPLITUDE = 82
FREQUENCY = 0.36
OUTPUT_DIRECTORY = Path("docs/assets/patterns")


def movement_offset(pattern: str, elapsed: float) -> float:
    phase = 2 * math.pi * FREQUENCY * elapsed
    if pattern == "sine":
        return AMPLITUDE * math.sin(phase)
    if pattern == "zigzag":
        return AMPLITUDE * (2 / math.pi) * math.asin(math.sin(phase))
    return 0


def load_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    try:
        return ImageFont.truetype("C:/Windows/Fonts/consola.ttf", size)
    except OSError:
        return ImageFont.load_default()


def draw_frame(pattern: str, frame_index: int) -> Image.Image:
    image = Image.new("RGB", (WIDTH, HEIGHT), "#030814")
    draw = ImageDraw.Draw(image)
    font = load_font(14)

    draw.rectangle((12, 13, 172, 22), fill="#57d7ef")
    draw.text((237, 10), "SCORE 00000000", font=font, fill="#fff24a")

    elapsed = frame_index * FRAME_DURATION_MS / 1000
    progress = frame_index / (FRAME_COUNT - 1)
    enemy_y = 62 + (238 * progress)
    enemy_x = ORIGIN_X + movement_offset(pattern, elapsed)

    trail_points: list[tuple[float, float]] = []
    for trail_index in range(frame_index + 1):
        trail_elapsed = trail_index * FRAME_DURATION_MS / 1000
        trail_progress = trail_index / (FRAME_COUNT - 1)
        trail_points.append(
            (
                ORIGIN_X + movement_offset(pattern, trail_elapsed),
                62 + (238 * trail_progress),
            )
        )
    if len(trail_points) > 1:
        draw.line(trail_points, fill="#5c2838", width=2)

    enemy_half_size = 14
    draw.rectangle(
        (
            enemy_x - enemy_half_size,
            enemy_y - enemy_half_size,
            enemy_x + enemy_half_size,
            enemy_y + enemy_half_size,
        ),
        fill="#ff5d66",
        outline="#ff9aa0",
        width=2,
    )

    player_x = ORIGIN_X
    player_y = 392
    draw.polygon(
        (
            (player_x, player_y - 12),
            (player_x + 10, player_y + 10),
            (player_x, player_y + 5),
            (player_x - 10, player_y + 10),
        ),
        fill="#4ddcf0",
    )
    return image


def generate(pattern: str) -> None:
    frames = [draw_frame(pattern, frame_index) for frame_index in range(FRAME_COUNT)]
    output_path = OUTPUT_DIRECTORY / f"enemy-{pattern}.gif"
    frames[0].save(
        output_path,
        save_all=True,
        append_images=frames[1:],
        duration=FRAME_DURATION_MS,
        loop=0,
        optimize=True,
    )


def main() -> None:
    OUTPUT_DIRECTORY.mkdir(parents=True, exist_ok=True)
    for pattern in ("straight", "sine", "zigzag"):
        generate(pattern)


if __name__ == "__main__":
    main()
