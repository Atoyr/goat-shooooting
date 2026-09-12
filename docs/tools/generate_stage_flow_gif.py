"""Generate the stage opening/result/transition GIF used by the manual.

Requires Pillow. Run this file from the repository root.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


WIDTH = 624
HEIGHT = 440
PLAYFIELD_WIDTH = 489
FRAME_COUNT = 70
FRAME_DURATION_MS = 100
OUTPUT_PATH = Path("docs/assets/stage-flow.gif")


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    font_name = "consolab.ttf" if bold else "consola.ttf"
    try:
        return ImageFont.truetype(f"C:/Windows/Fonts/{font_name}", size)
    except OSError:
        return ImageFont.load_default()


def centered_text(
    draw: ImageDraw.ImageDraw,
    y: int,
    text: str,
    font: ImageFont.FreeTypeFont | ImageFont.ImageFont,
    fill: str,
) -> None:
    bounds = draw.textbbox((0, 0), text, font=font)
    width = bounds[2] - bounds[0]
    draw.text(((PLAYFIELD_WIDTH - width) / 2, y), text, font=font, fill=fill)


def draw_shell(draw: ImageDraw.ImageDraw, score: int) -> None:
    draw.rectangle((0, 0, PLAYFIELD_WIDTH - 1, HEIGHT), fill="#080d1a")
    draw.rectangle((PLAYFIELD_WIDTH, 0, WIDTH, HEIGHT), fill="#0c162a")
    draw.rectangle((PLAYFIELD_WIDTH, 0, PLAYFIELD_WIDTH + 1, HEIGHT), fill="#375073")
    draw.rectangle((WIDTH - 2, 0, WIDTH - 1, HEIGHT), fill="#375073")
    draw.text((PLAYFIELD_WIDTH + 12, 16), f"SCORE {score:08d}", font=load_font(12, True), fill="#ffeb54")
    draw.text((PLAYFIELD_WIDTH + 12, 56), "STAGE FLOW", font=load_font(11, True), fill="#44d2ff")
    draw.text((PLAYFIELD_WIDTH + 12, 78), "OPENING", font=load_font(10), fill="#a0b9d2")
    draw.text((PLAYFIELD_WIDTH + 12, 96), "PLAYING", font=load_font(10), fill="#a0b9d2")
    draw.text((PLAYFIELD_WIDTH + 12, 114), "RESULTS", font=load_font(10), fill="#a0b9d2")


def draw_presentation_lines(draw: ImageDraw.ImageDraw) -> None:
    center_x = PLAYFIELD_WIDTH // 2
    center_y = HEIGHT // 2
    draw.rectangle((28, center_y, PLAYFIELD_WIDTH - 29, center_y + 1), fill="#44d2ff")
    draw.rectangle((center_x, center_y - 94, center_x + 1, center_y + 94), fill="#244866")


def draw_opening(draw: ImageDraw.ImageDraw, stage: int, title: str, subtitle: str) -> None:
    draw.rectangle((0, 0, PLAYFIELD_WIDTH - 1, HEIGHT), fill="#030710")
    draw_presentation_lines(draw)
    centered_text(draw, 125, f"STAGE {stage:02d}", load_font(18, True), "#a0b9d2")
    centered_text(draw, 177, title, load_font(27, True), "#ffffff")
    centered_text(draw, 253, subtitle, load_font(13, True), "#ffeb54")


def draw_playing(draw: ImageDraw.ImageDraw, frame_index: int) -> int:
    local_frame = frame_index - 20
    score = 4200 if local_frame < 12 else 6200
    draw.text((12, 12), "LIVES 02", font=load_font(11, True), fill="#44d2ff")
    draw.text((12, 32), "BOMBS 01", font=load_font(11, True), fill="#ffb432")

    player_x = PLAYFIELD_WIDTH // 2
    player_y = HEIGHT - 42
    draw.polygon(
        ((player_x, player_y - 13), (player_x + 11, player_y + 11),
         (player_x, player_y + 5), (player_x - 11, player_y + 11)),
        fill="#44d2ff",
    )

    boss_x = PLAYFIELD_WIDTH // 2
    boss_y = 92
    health_width = max(0, 80 - (local_frame * 6))
    if local_frame < 13:
        draw.rectangle((boss_x - 22, boss_y - 22, boss_x + 22, boss_y + 22), fill="#ff5c5c")
        draw.rectangle((boss_x - 22, boss_y - 29, boss_x - 22 + health_width, boss_y - 26), fill="#62e66b")
        for bullet_index in range(16):
            side = -1 if bullet_index % 2 == 0 else 1
            ring = bullet_index // 2
            x = boss_x + side * (28 + ring * 18)
            y = boss_y + 38 + ring * 25 + (local_frame * 5)
            if 0 < x < PLAYFIELD_WIDTH and y < HEIGHT:
                draw.ellipse((x - 3, y - 3, x + 3, y + 3), fill="#ff8c3c")
    else:
        radius = 18 + ((local_frame - 13) * 10)
        draw.ellipse(
            (boss_x - radius, boss_y - radius, boss_x + radius, boss_y + radius),
            outline="#ffb432",
            width=5,
        )
    return score


def draw_results(draw: ImageDraw.ImageDraw) -> None:
    draw.rectangle((0, 0, PLAYFIELD_WIDTH - 1, HEIGHT), fill="#030710")
    draw_presentation_lines(draw)
    centered_text(draw, 125, "STAGE CLEAR", load_font(26, True), "#ffffff")
    centered_text(draw, 195, "STAGE SCORE 00002000", load_font(15, True), "#ffeb54")
    centered_text(draw, 235, "TOTAL SCORE 00006200", load_font(15, True), "#44d2ff")
    centered_text(draw, 302, "NEXT STAGE", load_font(12, True), "#a0b9d2")


def draw_frame(frame_index: int) -> Image.Image:
    score = 4200 if frame_index < 32 else 6200
    image = Image.new("RGB", (WIDTH, HEIGHT), "#080d1a")
    draw = ImageDraw.Draw(image)
    draw_shell(draw, score)
    if frame_index < 20:
        draw_opening(draw, 1, "THE SILENT HORIZON", "IDEAL RELEASE")
    elif frame_index < 35:
        score = draw_playing(draw, frame_index)
        draw.rectangle((PLAYFIELD_WIDTH + 12, 16, WIDTH - 5, 32), fill="#0c162a")
        draw.text((PLAYFIELD_WIDTH + 12, 16), f"SCORE {score:08d}", font=load_font(12, True), fill="#ffeb54")
    elif frame_index < 55:
        draw_results(draw)
    else:
        draw_opening(draw, 2, "REFLECTION", "TRIAL OF RESOLVE")
    return image


def main() -> None:
    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    frames = [draw_frame(frame_index) for frame_index in range(FRAME_COUNT)]
    frames[0].save(
        OUTPUT_PATH,
        save_all=True,
        append_images=frames[1:],
        duration=FRAME_DURATION_MS,
        loop=0,
        optimize=True,
    )
    print(f"wrote {OUTPUT_PATH} ({OUTPUT_PATH.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
