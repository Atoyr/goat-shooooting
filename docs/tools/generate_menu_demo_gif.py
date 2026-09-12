"""Generate the title/options/pause/result menu GIF used by the manual.

The layout, labels, colors, and 5x7 glyphs mirror the Framework menu renderer.
Requires Pillow. Run this file from the repository root.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


LOGICAL_WIDTH = 1020
LOGICAL_HEIGHT = 720
OUTPUT_WIDTH = 624
OUTPUT_HEIGHT = 440
FRAME_DURATION_MS = 100
OUTPUT_PATH = Path("docs/assets/menu-demo.gif")

COLORS = {
    "background": "#050914",
    "playfield": "#080d1a",
    "side_panel": "#0c162a",
    "panel": "#0a1428",
    "panel_overlay": (10, 20, 40, 242),
    "line": "#44d2ff",
    "selected": "#194464",
    "white": "#ffffff",
    "yellow": "#ffeb54",
    "muted": "#a0b9d2",
    "enemy": "#ff5c5c",
    "enemy_bullet": "#ff8c3c",
    "player": "#44d2ff",
    "player_bullet": "#ffeb54",
}

GLYPHS = {
    " ": "00000000000000000000000000000000000",
    "0": "01110100011001110101110011000101110",
    "1": "00100011000010000100001000010001110",
    "2": "01110100010000100010001000100011111",
    "3": "11110000010000101110000010000111110",
    "4": "00010001100101010010111110001000010",
    "5": "11111100001111000001000011000101110",
    "6": "00110010001000011110100011000101110",
    "7": "11111000010001000100010000100001000",
    "8": "01110100011000101110100011000101110",
    "9": "01110100011000101111000010001001100",
    "A": "01110100011000111111100011000110001",
    "B": "11110100011000111110100011000111110",
    "C": "01110100011000010000100001000101110",
    "D": "11110100011000110001100011000111110",
    "E": "11111100001000011110100001000011111",
    "F": "11111100001000011110100001000010000",
    "G": "01110100011000010111100011000101110",
    "H": "10001100011000111111100011000110001",
    "I": "11111001000010000100001000010011111",
    "J": "00111000100001000010100100110001100",
    "K": "10001100101010011000101001001010001",
    "L": "10000100001000010000100001000011111",
    "M": "10001110111010110001100011000110001",
    "N": "10001110011010110011100011000110001",
    "O": "01110100011000110001100011000101110",
    "P": "11110100011000111110100001000010000",
    "Q": "01110100011000110001101011001001101",
    "R": "11110100011000111110101001001010001",
    "S": "01111100001000001110000010000111110",
    "T": "11111001000010000100001000010000100",
    "U": "10001100011000110001100011000101110",
    "V": "10001100011000110001010100010000100",
    "W": "10001100011000110101101011101110001",
    "X": "10001100010101000100010101000110001",
    "Y": "10001100010101000100001000010000100",
    "Z": "11111000010001000100010001000011111",
    "-": "00000000000000011111000000000000000",
    ".": "00000000000000000000000000011000110",
    "?": "01110100010001000100001000000000100",
}

TITLE_ITEMS = ["START", "OPTIONS", "QUIT"]
PAUSE_ITEMS = ["RESUME", "OPTIONS", "RETRY", "TITLE"]
RESULT_ITEMS = ["RETRY", "TITLE"]
OPTION_ITEMS = [
    ("WINDOW MODE", "WINDOWED"),
    ("WINDOW SCALE", "1X"),
    ("VSYNC", "ON"),
    ("MASTER VOLUME", "100%"),
    ("EFFECTS VOLUME", "100%"),
    ("MUTED", "OFF"),
    ("SCREEN SHAKE", "100%"),
    ("VIBRATION", "ON"),
    ("MOVE UP", "W"),
    ("MOVE DOWN", "S"),
    ("MOVE LEFT", "A"),
    ("MOVE RIGHT", "D"),
    ("FIRE", "Z"),
    ("BOMB", "X"),
    ("PAUSE KEY", "P"),
    ("CONFIRM", "ENTER"),
    ("CANCEL", "ESCAPE"),
    ("RETRY", "R"),
    ("BACK", ""),
]


def text_width(text: str, scale: int) -> int:
    return 0 if not text else (len(text) * 6 * scale) - scale


def draw_pixel_text(
    draw: ImageDraw.ImageDraw,
    text: str,
    left: int,
    top: int,
    scale: int,
    fill: str,
) -> None:
    for character_index, character in enumerate(text.upper()):
        glyph = GLYPHS.get(character, GLYPHS["?"])
        for pixel_index, enabled in enumerate(glyph):
            if enabled == "0":
                continue
            x = left + (character_index * 6 * scale) + ((pixel_index % 5) * scale)
            y = top + ((pixel_index // 5) * scale)
            draw.rectangle((x, y, x + scale - 1, y + scale - 1), fill=fill)


def draw_centered_text(
    draw: ImageDraw.ImageDraw,
    text: str,
    center_x: int,
    top: int,
    scale: int,
    fill: str,
) -> None:
    draw_pixel_text(draw, text, center_x - (text_width(text, scale) // 2), top, scale, fill)


def draw_gameplay(image: Image.Image, frame_index: int) -> None:
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, 799, LOGICAL_HEIGHT), fill=COLORS["playfield"])
    draw.rectangle((800, 0, LOGICAL_WIDTH, LOGICAL_HEIGHT), fill=COLORS["side_panel"])
    draw.rectangle((800, 0, 801, LOGICAL_HEIGHT), fill="#375073")
    draw.rectangle((LOGICAL_WIDTH - 2, 0, LOGICAL_WIDTH - 1, LOGICAL_HEIGHT), fill="#375073")
    draw_pixel_text(draw, "LIVES 03", 16, 16, 2, COLORS["player"])
    draw_pixel_text(draw, "BOMBS 02", 16, 36, 2, "#ffb432")
    score = "SCORE 00018400"
    draw_pixel_text(draw, score, LOGICAL_WIDTH - 16 - text_width(score, 2), 24, 2, COLORS["yellow"])

    player_x = 400 + ((frame_index % 12) - 6) * 3
    player_y = 640
    draw.rectangle((player_x - 11, player_y - 11, player_x + 11, player_y + 11), fill=COLORS["player"])
    for offset in (0, 22, 44, 66):
        draw.rectangle((player_x - 3, player_y - 42 - offset, player_x + 3, player_y - 28 - offset), fill=COLORS["player_bullet"])

    for enemy_index, enemy_x in enumerate((220, 400, 580)):
        enemy_y = 120 + (enemy_index * 42)
        draw.rectangle((enemy_x - 17, enemy_y - 17, enemy_x + 17, enemy_y + 17), fill=COLORS["enemy"])
        for bullet_index in range(5):
            bullet_y = enemy_y + 38 + (bullet_index * 54)
            drift = (bullet_index + 1) * 17
            draw.rectangle((enemy_x - drift - 4, bullet_y - 4, enemy_x - drift + 4, bullet_y + 4), fill=COLORS["enemy_bullet"])
            draw.rectangle((enemy_x + drift - 4, bullet_y - 4, enemy_x + drift + 4, bullet_y + 4), fill=COLORS["enemy_bullet"])


def draw_menu_option(
    draw: ImageDraw.ImageDraw,
    text: str,
    center_x: int,
    top: int,
    selected: bool,
    scale: int,
    width: int,
) -> None:
    if selected:
        draw.rectangle(
            (center_x - (width // 2), top - 8, center_x + (width // 2), top + (7 * scale) + 6),
            fill=COLORS["selected"],
        )
    draw_centered_text(
        draw,
        text,
        center_x,
        top,
        scale,
        COLORS["yellow"] if selected else COLORS["muted"],
    )


def draw_shell_menu(
    image: Image.Image,
    state: str,
    selection: int,
    game_id: str = "sample",
    option_overrides: dict[int, str] | None = None,
) -> None:
    center_x = LOGICAL_WIDTH // 2
    center_y = LOGICAL_HEIGHT // 2
    is_options = state == "options"
    panel_width = 680 if is_options else 420
    panel_height = 560 if is_options else 300
    panel_top = center_y - (panel_height // 2)

    if state == "title":
        image.paste(COLORS["background"], (0, 0, LOGICAL_WIDTH, LOGICAL_HEIGHT))

    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    overlay_draw = ImageDraw.Draw(overlay)
    panel_color = COLORS["panel"] if state == "title" else COLORS["panel_overlay"]
    overlay_draw.rectangle(
        (center_x - (panel_width // 2), panel_top, center_x + (panel_width // 2), panel_top + panel_height),
        fill=panel_color,
    )
    overlay_draw.rectangle(
        (center_x - (panel_width // 2), panel_top, center_x + (panel_width // 2), panel_top + 2),
        fill=COLORS["line"],
    )
    image.paste(overlay, (0, 0), overlay)
    draw = ImageDraw.Draw(image)

    title = {
        "title": "GOAT-SHOOOOTING",
        "pause": "PAUSED",
        "result": "ALL STAGES CLEAR",
        "options": "OPTIONS",
    }[state]
    draw_centered_text(draw, title, center_x, panel_top + 28, 2 if is_options else 3, COLORS["white"])

    if state == "title":
        draw_centered_text(
            draw,
            f"GAME  {game_id.upper()}  LEFT RIGHT CHANGE",
            center_x,
            panel_top + 76,
            1,
            COLORS["line"],
        )
        draw_centered_text(draw, "HIGH SCORE 00018400", center_x, panel_top + 94, 1, COLORS["yellow"])

    if is_options:
        option_overrides = option_overrides or {}
        items = [(label, option_overrides.get(index, value)) for index, (label, value) in enumerate(OPTION_ITEMS)]
        for index, (label, value) in enumerate(items):
            text = label if not value else f"{label}  {value}"
            draw_menu_option(draw, text, center_x, panel_top + 78 + (index * 23), index == selection, 1, panel_width - 48)
    else:
        items = TITLE_ITEMS if state == "title" else PAUSE_ITEMS if state == "pause" else RESULT_ITEMS
        items_top = center_y - 24 if state == "title" else center_y - (((len(items) - 1) * 46) // 2)
        for index, item in enumerate(items):
            draw_menu_option(draw, item, center_x, items_top + (index * 46), index == selection, 2, panel_width - 48)

    draw_centered_text(
        draw,
        "ARROWS SELECT  ENTER CONFIRM  ESC BACK",
        center_x,
        panel_top + panel_height - 30,
        1,
        COLORS["muted"],
    )


def draw_frame(frame_index: int) -> Image.Image:
    image = Image.new("RGB", (LOGICAL_WIDTH, LOGICAL_HEIGHT), COLORS["background"])
    if frame_index < 12:
        draw_shell_menu(image, "title", 0, "sample")
    elif frame_index < 24:
        draw_shell_menu(image, "title", 0, "gauntlet")
    elif frame_index < 34:
        draw_shell_menu(image, "title", 1, "gauntlet")
    elif frame_index < 46:
        draw_shell_menu(image, "options", 0, "gauntlet")
    elif frame_index < 58:
        draw_shell_menu(image, "options", 3, "gauntlet", {0: "BORDERLESS", 3: "80%"})
    elif frame_index < 70:
        draw_shell_menu(image, "options", 12, "gauntlet", {0: "BORDERLESS", 3: "80%", 12: "SPACE"})
    elif frame_index < 80:
        draw_shell_menu(image, "options", 18, "gauntlet", {0: "BORDERLESS", 3: "80%", 12: "SPACE"})
    elif frame_index < 92:
        draw_shell_menu(image, "title", 0, "gauntlet")
    elif frame_index < 104:
        draw_gameplay(image, frame_index)
    elif frame_index < 116:
        draw_gameplay(image, frame_index)
        draw_shell_menu(image, "pause", 0, "gauntlet")
    elif frame_index < 128:
        draw_gameplay(image, frame_index)
        draw_shell_menu(image, "pause", 2, "gauntlet")
    else:
        draw_gameplay(image, frame_index)
        draw_shell_menu(image, "result", 0, "gauntlet")
    return image.resize((OUTPUT_WIDTH, OUTPUT_HEIGHT), Image.Resampling.NEAREST)


def main() -> None:
    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    frames = [draw_frame(frame_index) for frame_index in range(140)]
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
