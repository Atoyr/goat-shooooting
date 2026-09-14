# SYNC DRIVE asset ledger

The distributable ledger is [`ASSET-LICENSES.txt`](../../ASSET-LICENSES.txt). It records the generation date, exact normalized prompt for every image, synthesis source for every WAV, SHA-256, third-party-input status, and redistribution classification. Runtime dependencies remain in [`THIRD-PARTY-NOTICES.txt`](../../THIRD-PARTY-NOTICES.txt).

Product validation rejects `tone://` fallback cues in `games/sync-drive`; every product cue must resolve to a pack-relative WAV. Projectile geometry is rendered by the first-party code-native renderer rather than a bitmap placeholder. The product pack does not reference the sample pack's placeholder atlas or tone cues.

Before any asset replacement, add its author/source, acquisition date, commercial redistribution terms, modification terms, attribution text, and hash here and in the distributable notice. Unknown or personal-use-only licenses are release blockers.
