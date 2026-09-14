# SYNC DRIVE manual QA matrix

Record build ID, operator, date, hardware, result, defect ID, and Replay for each cell. Blank cells are not passes.

| Area | Matrix | Required evidence |
|---|---|---|
| Full route | Vector/Lancer/Halo × Novice/Arcade/Expert | Five-stage clear or intentional game-over Replay; no progression lock |
| Modes | SYNC DRIVE, Score Attack, Stage Training, all 15 Boss checkpoints | Start, result, retry, title return, category-correct leaderboard |
| Input | keyboard; Xbox; PlayStation via Steam Input | Controller-only boot/menu/run/pause/retry/quit; glyph switch |
| Device loss | disconnect/reconnect during play and menu | safe pause, keyboard fallback, reconnect recovery |
| Display | windowed/borderless; 1080p/1440p/4K; 100/150/200% DPI; single/multi-monitor | no lost input, clipped HUD, crash, or unrecoverable black screen |
| Focus | Alt+Tab during opening, boss, Replay, result | deterministic pause/resume and working audio |
| Audio | no device; mute; device switch; BGM/SE sliders | no progression block; silent fallback and restored output |
| Saves | fresh profile; v1/v2 migration; corrupted settings/profile/leaderboard/replay | backup/default recovery, actionable log, no unrelated data loss |
| Accessibility | preset; high contrast; deuteranopia; outline off/on; 75–150% HUD | bullets, items, hitbox and warnings distinguishable without color alone |
| Install | clean Windows VM without .NET; Steam offline; update/rollback; uninstall/reinstall | self-contained launch, save compatibility, correct notices |

Minimum-hardware performance pass: 60 simulation ticks/second without progressive slowdown during Expert boss and no unbounded projectile/effect growth. Capture actual frame-time and hardware; CI wall-clock values are informational only.
