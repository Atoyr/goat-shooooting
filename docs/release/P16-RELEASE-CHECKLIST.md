# P16 Release Candidate checklist

Build under review: `SYNC DRIVE 0.1.0 win-x64`, 2026-09-14. Package `goat-shooooting-0.1.0-win-x64.zip`, SHA-256 `503d55b7944ef775215747bc34c032f2ed6bf9b773a55f947cbef66778f1a04f`. This is an internal development candidate until the external gates below pass.

## Automated evidence

- [x] Content validation: 5 stages, 3 ships, Novice/Arcade/Expert, 12 regular enemies, 5 bosses, 15 phases, 40 reusable patterns, authored 22.67-minute route.
- [x] Full-route protected soak: every ship × difficulty reaches all-clear and defeats five bosses.
- [x] Boss Training Replay record/validate/playback ends with the same canonical hash.
- [x] 10,000 projectile × 600 tick functional stress workload completes.
- [x] Product WAV/PNG path, locale completeness, definition capability, renderer resolution, and package asset checks.
- [x] Existing save corruption recovery, controller disconnect, and display-mode transitions remain unit/integration tested.
- [x] Release workflow runs Release build/tests, sample/gauntlet/product validation and smoke, Replay regression, self-contained publish, published executable smoke, artifact inspection, ZIP and SHA-256.

## Human/external evidence

- [ ] 10+ external playtest reports; current count 0/10.
- [ ] No open progression-, input-, or visibility-blocking issue from external playtest.
- [ ] First-time players can explain score/BANK changes after reading the tutorial.
- [ ] Physical minimum and recommended hardware matrix completed.
- [ ] Controller-only boot-to-quit verified on Xbox and PlayStation/Steam Input hardware.
- [ ] No-audio-device, real device switch, Alt+Tab, multi-monitor/high-DPI, Steam offline/update/rollback verified.
- [ ] Real Steam private-beta upload and clean-client install completed with recorded AppID/DepotID/BuildID.
- [ ] Store-specific capsule/title renders, 60 fps gameplay trailer, screenshots, copy, privacy, support, and platform review approved.

Because human/external evidence is incomplete, this build is **not a Release Candidate and P16 remains unchecked**.
