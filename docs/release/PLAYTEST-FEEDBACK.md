# External playtest protocol and feedback form

Status: **BLOCKED — 0 of the required 10 external participants have submitted a completed run as of 2026-09-14.** P16 and the Release Candidate gate remain incomplete until at least 10 independent participants have been tested and no unresolved progression-, input-, or visibility-blocking issue remains.

## Tester instructions

1. Record the ZIP filename, SHA-256, executable product version, Windows build, CPU, GPU, RAM, display resolution/scale, audio device, and keyboard/controller model.
2. Start from a clean extracted ZIP. Use only a controller from boot through quit once, then use the tester's preferred controls.
3. Complete or attempt Arcade with one ship, one Training checkpoint, Replay playback, and local leaderboard retrieval. Try Alt+Tab, display-mode switch, controller disconnect/reconnect, and mute/no-audio-device behavior.
4. For every defect, retain the Replay and record stage, frame/time, seed if shown, exact steps, expected result, actual result, frequency, and whether progress/input/visibility was blocked.

## Submission form

- Anonymous tester ID:
- Test date/time and time zone:
- Build ID / ZIP SHA-256:
- Hardware and Windows version:
- Display mode, resolution, DPI, refresh rate:
- Input device and connection:
- Audio device/status:
- Ship / difficulty / mode / stage reached:
- Could you explain why score and BANK increased or decreased after reading the tutorial? What is your explanation?
- Controller-only boot-to-quit result:
- Training, Replay, leaderboard result:
- Disconnect, Alt+Tab, display switch, mute/no-device result:
- Blocking issue? (progression / input / visibility / none):
- Steps, expected, actual, frequency:
- Replay filename and final state hash:
- Consent to retain this report for test evaluation: yes/no

Release owner copies one completed section per participant into a dated private test record, triages every blocking issue, links its fix and regression test, then updates the count and checklist. Personally identifying data is not required and must not be committed to this public repository.
