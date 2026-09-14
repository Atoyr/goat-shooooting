# Known issues and release blockers

## Release blockers

- External playtest is 0/10. Follow [`PLAYTEST-FEEDBACK.md`](PLAYTEST-FEEDBACK.md); P16 cannot be checked until 10 completed independent reports and blocker triage exist.
- Steamworks AppID/DepotID and credentials are not available in this repository, so a real private-beta upload, clean Steam install, rollback, and Default-branch promotion have not occurred. The dry-run procedure is validated by CI.
- Minimum/recommended hardware runs and the full manual matrix have not been executed on physical target machines.
- Store page submission and platform review remain external owner actions. Capsule master, gameplay screenshot, copy, requirements, support contact, and trailer shot list are prepared.

## Non-blocking limitations

- Replay compatibility is build/content-hash scoped; edited content intentionally rejects older Replays.
- Leaderboards are local-only; no online ranking or cloud sync is in the first Release Candidate scope.
- Generated key art is a text-free master. Store-specific localized title typography must be applied and reviewed in the store authoring system without altering this provenance record.
