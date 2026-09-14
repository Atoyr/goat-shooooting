[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'games/sync-drive'))
$expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'games')) + [System.IO.Path]::DirectorySeparatorChar
if (-not $packRoot.StartsWith($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to generate content outside games/: $packRoot"
}

function Write-Json([string]$RelativePath, [object]$Value) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $packRoot $RelativePath))
    $prefix = $packRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside the SYNC DRIVE pack: $path"
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    $Value | ConvertTo-Json -Depth 30 -Compress | Set-Content -LiteralPath $path -Encoding utf8
}

function New-Capability([string]$Type, [hashtable]$Parameters) {
    [ordered]@{ type = $Type; parameters = $Parameters }
}

function New-Emitter(
    [string]$Id,
    [string]$ProjectileId,
    [double]$Interval,
    [string]$AngleSource,
    [string]$Distribution,
    [int]$Count,
    [double]$Spread,
    [double]$Rotation = 0,
    [double[]]$Speed = @(1.0)) {
    [ordered]@{
        id = $Id; projectileId = $ProjectileId; fireInterval = $Interval
        angleSource = $AngleSource; distribution = $Distribution; projectileCount = $Count
        spreadDegrees = $Spread; rotationDegreesPerShot = $Rotation; speedMultipliers = $Speed
    }
}

Write-Json 'game.json' ([ordered]@{
    schemaVersion = 2; id = 'sync-drive'; defaultRuleSetId = 'sync-drive'
    ruleSetIds = @('sync-drive', 'score-attack'); difficultyIds = @('novice', 'arcade', 'expert')
    shipIds = @('vector', 'lancer', 'halo'); stageRouteId = 'sync-drive-route'
    width = 800; height = 720; screenLayout = 'touhou'; hudPanelWidth = 220; scorePosition = 'right-panel'
})

$difficulties = @(
    [ordered]@{ id = 'novice'; projectileSpeedMultiplier = 0.76; fireIntervalMultiplier = 1.25; enemyHpMultiplier = 0.82; additionalProjectileCount = 0; autoBomb = $true; patternTags = @('novice') },
    [ordered]@{ id = 'arcade'; projectileSpeedMultiplier = 1.0; fireIntervalMultiplier = 1.0; enemyHpMultiplier = 1.0; additionalProjectileCount = 0; autoBomb = $false; patternTags = @('arcade') },
    [ordered]@{ id = 'expert'; projectileSpeedMultiplier = 1.18; fireIntervalMultiplier = 0.84; enemyHpMultiplier = 1.18; additionalProjectileCount = 1; autoBomb = $false; patternTags = @('expert') }
)
foreach ($difficulty in $difficulties) {
    Write-Json "difficulties/$($difficulty.id).json" ([ordered]@{ schemaVersion = 2 } + $difficulty)
}

$visuals = [ordered]@{
    'vector-visual' = 'ship-vector'; 'lancer-visual' = 'ship-lancer'; 'halo-visual' = 'ship-halo'
    'player-shot-visual' = 'player-shot'; 'enemy-soft-visual' = 'enemy-soft-shot'
    'enemy-hard-visual' = 'enemy-hard-shot'; 'shard-visual' = 'sync-shard'
}
foreach ($entry in $visuals.GetEnumerator()) {
    Write-Json "visuals/$($entry.Key).json" ([ordered]@{ schemaVersion = 2; id = $entry.Key; assetId = $entry.Value })
}

$projectiles = @(
    [ordered]@{ id = 'player-pulse'; speed = 760; damage = 52; hitRadius = 4; lifetime = 2; visualId = 'player-shot-visual'; cancelResistance = 'soft' },
    [ordered]@{ id = 'player-needle'; speed = 940; damage = 82; hitRadius = 3; lifetime = 2; visualId = 'player-shot-visual'; cancelResistance = 'soft'; pierceCount = 1 },
    [ordered]@{ id = 'enemy-soft'; speed = 125; damage = 1; hitRadius = 4; lifetime = 9; visualId = 'enemy-soft-visual'; cancelResistance = 'soft' },
    [ordered]@{ id = 'enemy-hard'; speed = 160; damage = 1; hitRadius = 5; lifetime = 8; visualId = 'enemy-hard-visual'; cancelResistance = 'hard' }
)
foreach ($projectile in $projectiles) {
    $value = [ordered]@{ schemaVersion = 2; canDamage = $true; canBeCancelled = $true; behavior = (New-Capability 'straight' @{}); damageType = 'normal'; clearBehavior = 'remove' }
    foreach ($property in $projectile.GetEnumerator()) { $value[$property.Key] = $property.Value }
    Write-Json "projectiles/$($projectile.id).json" $value
}

$playerWeapons = @(
    [ordered]@{ id = 'vector-wide'; projectileId = 'player-pulse'; cooldown = 0.07; emitters = @((New-Emitter 'wide' 'player-pulse' 0.07 'forward' 'fan' 5 32)) },
    [ordered]@{ id = 'vector-focus'; projectileId = 'player-needle'; cooldown = 0.055; emitters = @((New-Emitter 'focus' 'player-needle' 0.055 'forward' 'fan' 2 5)) },
    [ordered]@{ id = 'lancer-twin'; projectileId = 'player-needle'; cooldown = 0.06; emitters = @((New-Emitter 'left' 'player-needle' 0.06 'forward' 'single' 1 0), (New-Emitter 'right' 'player-needle' 0.06 'forward' 'single' 1 0)) },
    [ordered]@{ id = 'halo-wave'; projectileId = 'player-pulse'; cooldown = 0.08; emitters = @((New-Emitter 'wave' 'player-pulse' 0.08 'forward' 'fan' 7 54)) },
    [ordered]@{ id = 'halo-focus'; projectileId = 'player-needle'; cooldown = 0.065; emitters = @((New-Emitter 'pin' 'player-needle' 0.065 'forward' 'fan' 3 12)) }
)
foreach ($weapon in $playerWeapons) {
    Write-Json "weapons/$($weapon.id).json" ([ordered]@{ schemaVersion = 2; actionType = 'projectile' } + $weapon)
}

for ($index = 1; $index -le 12; $index++) {
    $hard = $index -in @(6, 10, 12)
    $count = 2 + (($index - 1) % 7)
    $distribution = if ($index % 4 -eq 0) { 'ring' } elseif ($index % 3 -eq 0) { 'random-arc' } else { 'fan' }
    $angle = if ($index % 2 -eq 0) { 'rotating' } else { 'aim-at-player' }
    $spread = if ($distribution -eq 'ring') { 360 } else { 32 + ($index * 5) }
    $emitter = New-Emitter "emitter-$index" $(if ($hard) { 'enemy-hard' } else { 'enemy-soft' }) (0.34 + (($index % 4) * 0.08)) $angle $distribution $count $spread (7 + $index) @(0.82, 1.0)
    Write-Json "weapons/enemy-pattern-$index.json" ([ordered]@{
        schemaVersion = 2; id = "enemy-pattern-$index"; projectileId = $(if ($hard) { 'enemy-hard' } else { 'enemy-soft' })
        cooldown = 0.2; actionType = 'projectile'; emitters = @($emitter)
    })
}

$ships = @(
    [ordered]@{ id = 'vector'; hitRadius = 3; grazeRadius = 22; normalSpeed = 285; focusSpeed = 135; normalWeaponIds = @('vector-wide'); focusWeaponIds = @('vector-focus'); visualId = 'vector-visual'; options = @() },
    [ordered]@{ id = 'lancer'; hitRadius = 2.8; grazeRadius = 20; normalSpeed = 320; focusSpeed = 120; normalWeaponIds = @('lancer-twin'); focusWeaponIds = @('lancer-twin'); visualId = 'lancer-visual'; options = @() },
    [ordered]@{ id = 'halo'; hitRadius = 3.2; grazeRadius = 25; normalSpeed = 250; focusSpeed = 150; normalWeaponIds = @('halo-wave'); focusWeaponIds = @('halo-focus'); visualId = 'halo-visual'; options = @(
        [ordered]@{ id = 'left-orbit'; offsetX = -26; offsetY = -10; followSpeed = 520; radius = 5; normalWeaponIds = @('vector-focus'); focusWeaponIds = @('vector-focus'); visualId = 'halo-visual' },
        [ordered]@{ id = 'right-orbit'; offsetX = 26; offsetY = -10; followSpeed = 520; radius = 5; normalWeaponIds = @('vector-focus'); focusWeaponIds = @('vector-focus'); visualId = 'halo-visual' }
    ) }
)
foreach ($ship in $ships) {
    Write-Json "ships/$($ship.id).json" ([ordered]@{
        schemaVersion = 2; initialLives = 9; initialBombs = 6; initialPower = 50; maximumPower = 100
        maximumLives = 9; maximumBombs = 9; deathAnimationSeconds = 0.2; respawnDelaySeconds = 0.25
        respawnInvincibilitySeconds = 2; powerLossOnDeath = 10; bombsAfterRespawn = 3
    } + $ship)
}

for ($index = 1; $index -le 10; $index++) {
    $lane = 100 + ((($index - 1) % 5) * 145)
    $mirror = 700 - $lane
    Write-Json "patterns/motion-$index.json" ([ordered]@{
        schemaVersion = 2; id = "motion-$index"; kind = 'motion'; commands = @(
            [ordered]@{ type = 'follow-path'; parameters = [ordered]@{ duration = 14 + $index; points = @([ordered]@{ x = $lane; y = 120 }, [ordered]@{ x = $mirror; y = 210 }, [ordered]@{ x = $lane; y = 310 }); easing = 'ease-in-out'; space = 'world'; reference = 'none' }; repeatCount = 1; maximumSpawnCount = 0; difficultyTags = @() }
        )
    })
}

for ($index = 1; $index -le 12; $index++) {
    $spawn = (2 + (($index - 1) % 7)) * 2
    Write-Json "patterns/attack-cycle-$index.json" ([ordered]@{
        schemaVersion = 2; id = "attack-cycle-$index"; kind = 'attack'; commands = @(
            [ordered]@{ type = 'fire'; parameters = [ordered]@{ weaponId = "enemy-pattern-$index" }; repeatCount = 1; maximumSpawnCount = $spawn; difficultyTags = @() },
            [ordered]@{ type = 'wait'; parameters = [ordered]@{ duration = 0.55 + (($index % 4) * 0.1) }; repeatCount = 1; maximumSpawnCount = 0; difficultyTags = @() }
        )
    })
}

for ($index = 1; $index -le 18; $index++) {
    $cycle = (($index - 1) % 12) + 1
    Write-Json "patterns/attack-$index.json" ([ordered]@{
        schemaVersion = 2; id = "attack-$index"; kind = 'attack'; commands = @(
            [ordered]@{ type = 'repeat'; parameters = @{}; patternId = "attack-cycle-$cycle"; repeatCount = 24 + ($index % 9); maximumSpawnCount = 0; difficultyTags = @() }
        )
    })
}

$drops = @(
    [ordered]@{ itemId = 'sync-shard'; count = 2; chance = 1.0; scatterSpeed = 70 },
    [ordered]@{ itemId = 'score-crystal'; count = 1; chance = 0.65; scatterSpeed = 55 }
)
for ($index = 1; $index -le 12; $index++) {
    Write-Json "enemies/drone-$index.json" ([ordered]@{
        schemaVersion = 2; id = "drone-$index"; hp = 32 + ($index * 5); speed = 0; radius = 13 + ($index % 3); score = 100 + ($index * 25)
        motion = (New-Capability 'straight' @{}); motionPatternId = "motion-$((($index - 1) % 10) + 1)"; attackPatternIds = @("attack-$index"); dropTable = $drops
    })
}
for ($index = 1; $index -le 5; $index++) {
    Write-Json "enemies/boss-body-$index.json" ([ordered]@{
        schemaVersion = 2; id = "boss-body-$index"; hp = 5000; speed = 0; radius = 38; score = 10000 + ($index * 2500)
        motion = (New-Capability 'straight' @{}); motionPatternId = "motion-$((($index + 3) % 10) + 1)"; attackPatternIds = @(); dropTable = @()
    })
}

$items = @(
    [ordered]@{ id = 'sync-shard'; kind = 'gauge'; value = 5 },
    [ordered]@{ id = 'score-crystal'; kind = 'score'; value = 500 },
    [ordered]@{ id = 'power-cell'; kind = 'power'; value = 5 },
    [ordered]@{ id = 'bomb-core'; kind = 'bomb'; value = 1 },
    [ordered]@{ id = 'life-core'; kind = 'life'; value = 1 }
)
foreach ($item in $items) {
    Write-Json "items/$($item.id).json" ([ordered]@{ schemaVersion = 2; visualId = 'shard-visual' } + $item)
}

for ($bossIndex = 1; $bossIndex -le 5; $bossIndex++) {
    $phases = @()
    for ($phaseIndex = 1; $phaseIndex -le 3; $phaseIndex++) {
        $attack = 12 + (($bossIndex + $phaseIndex - 2) % 6) + 1
        $phases += [ordered]@{
            id = "phase-$phaseIndex"; displayName = "SYNC SIGNATURE $bossIndex-$phaseIndex"
            hp = 1500 + ($bossIndex * 250) + ($phaseIndex * 300); timeLimit = 45
            motionPatternId = "motion-$((($bossIndex + $phaseIndex + 4) % 10) + 1)"; attackPatternIds = @("attack-$attack")
            invulnerabilitySeconds = 0.3; checkpointId = "boss-$bossIndex-phase-$phaseIndex"
            startProjectileCancel = $(if ($phaseIndex -eq 1) { 'none' } else { 'soft' }); endProjectileCancel = 'soft'
            baseBonus = 20000 * $bossIndex; timeBonusPerSecond = 500; noMissBonus = 15000; noBombBonus = 10000
            dropTable = @([ordered]@{ itemId = 'sync-shard'; count = 6; chance = 1.0; scatterSpeed = 110 })
        }
    }
    Write-Json "bosses/boss-$bossIndex.json" ([ordered]@{
        schemaVersion = 2; id = "boss-$bossIndex"; displayName = "RESONATOR $bossIndex"; enemyId = "boss-body-$bossIndex"
        warningSeconds = 2; bgmAudioId = 'bgm-boss'; phases = $phases
    })
}

for ($stageIndex = 1; $stageIndex -le 5; $stageIndex++) {
    $events = @()
    for ($wave = 0; $wave -lt 15; $wave++) {
        $enemy = (($wave + (($stageIndex - 1) * 3)) % 12) + 1
        $events += [ordered]@{
            schemaVersion = 2; time = 5 + ($wave * 18); type = 'spawn-enemy'; enemyId = "drone-$enemy"
            x = 110 + (($wave % 5) * 145); y = 60; count = 3; spawnInterval = 0.35; spacingX = 34; isBoss = $false
        }
    }
    $events += [ordered]@{
        schemaVersion = 2; time = 270; type = 'spawn-enemy'; enemyId = "boss-body-$stageIndex"
        x = 400; y = 100; count = 1; spawnInterval = 0; spacingX = 0; isBoss = $true; bossId = "boss-$stageIndex"
    }
    $stage = [ordered]@{
        schemaVersion = 2; id = "stage-$stageIndex"; title = "RESONANCE $stageIndex"; subtitle = "SYNC LESSON $stageIndex"
        openingDuration = 1; resultsDuration = 1; backgroundId = 'sync-space'; bgmAudioId = 'bgm-stage'
        events = $events; objectives = @([ordered]@{ type = 'complete-boss'; bossId = "boss-$stageIndex" })
    }
    if ($stageIndex -lt 5) { $stage.nextStageId = "stage-$($stageIndex + 1)" }
    Write-Json "stages/stage-$stageIndex.json" $stage
}

$scoreRules = @(
    (New-Capability 'chain' ([ordered]@{ timeoutFrames = 150; bonusPerChain = 40 })),
    (New-Capability 'hit-combo' ([ordered]@{ timeoutFrames = 90; bonusPerHit = 2 })),
    (New-Capability 'sync-bank' ([ordered]@{ base = 1.0; perShard = 0.025; maximum = 8.0; decayPerSecond = 0.04; hitLoss = 0.75 })),
    (New-Capability 'base-kill' @{}),
    (New-Capability 'graze' ([ordered]@{ points = 30 })),
    (New-Capability 'projectile-cancel' ([ordered]@{ points = 20 })),
    (New-Capability 'item-growth' ([ordered]@{ growthPerItem = 0.08; maximumMultiplier = 4.0; timeoutFrames = 180 })),
    (New-Capability 'boss-bonus' @{}),
    (New-Capability 'stage-clear' ([ordered]@{ stagePoints = 25000; allClearPoints = 250000 })),
    (New-Capability 'resource-conversion' ([ordered]@{ lifePoints = 20000; bombPoints = 5000 })),
    (New-Capability 'extend-threshold' @{})
)
$special = New-Capability 'radiant-drive' ([ordered]@{
    activation = 'staged'; stageCost = 100; maximumLevel = 3; damageCharge = 0.02; killCharge = 5
    grazeCharge = 2; cancelCharge = 0; itemCharge = 4; lockCharge = 1; drainPerSecond = 24
    killExtensionSeconds = 0.18; cooldownSeconds = 0.5; endOnBomb = $true; endOnDeath = $true
    damageMultiplier = 1.55; fireIntervalMultiplier = 0.76; scoreMultiplier = 2.25
    cancelProjectiles = $true; cancelItemId = 'sync-shard'; invincible = $false; visualCue = 'sync-drive'; audioCue = 'se-special'
})
$rank = New-Capability 'dynamic-rank' ([ordered]@{
    initial = 0.1; minimum = 0.0; maximum = 1.0; damageGain = 0.0004; killGain = 0.012
    grazeGain = 0.003; cancelGain = 0.001; itemGain = 0.002; bombLoss = 0.12; deathLoss = 0.25
    decayPerSecond = 0.0004; bulletSpeedPerRank = 0.22; fireRatePerRank = 0.18
    additionalProjectileEvery = 0.5; revengeEvery = 0.75; revengeCount = 1; revengeProjectileId = 'enemy-soft'
})
$stageIds = 1..5 | ForEach-Object { "stage-$_" }
Write-Json 'rulesets/sync-drive.json' ([ordered]@{
    schemaVersion = 2; id = 'sync-drive'; stageRouteId = 'sync-drive-route'; stageIds = $stageIds
    allowContinue = $false; initialCredits = 0; manualBombCost = 1; autoBombCost = 1; bombInvincibilitySeconds = 1.2
    deathClearsProjectiles = $true; extendScoreThresholds = @(400000, 1200000, 3000000); collectionLineY = 150
    itemFallSpeed = 80; itemMagnetSpeed = 560; focusMagnetRadius = 145; itemCollectionRadius = 20
    maximumGauge = 300; initialGauge = 0; clearCondition = 'route-complete'; maximumPowerItemScoreValue = 1000
    scoreRules = $scoreRules; specialGaugeRule = $special; rankRule = $rank
})
Write-Json 'rulesets/score-attack.json' ([ordered]@{
    schemaVersion = 2; id = 'score-attack'; stageRouteId = 'sync-drive-route'; stageIds = $stageIds
    allowContinue = $false; initialCredits = 0; manualBombCost = 1; autoBombCost = 1; bombInvincibilitySeconds = 1.2
    deathClearsProjectiles = $true; extendScoreThresholds = @(); collectionLineY = 150; itemFallSpeed = 80
    itemMagnetSpeed = 560; focusMagnetRadius = 145; itemCollectionRadius = 20; maximumGauge = 300; initialGauge = 100
    timeLimitSeconds = 1500; clearCondition = 'time-attack'; maximumPowerItemScoreValue = 1000
    scoreRules = $scoreRules; specialGaugeRule = $special; rankRule = $rank
})

Write-Json 'audio/bgm-stage.json' ([ordered]@{ schemaVersion = 2; id = 'bgm-stage'; assetId = 'assets/audio/sync-stage.wav'; category = 'music'; baseVolume = 0.48; loop = $true; loopStartSeconds = 0; loopEndSeconds = 8; crossfadeSeconds = 1.2; ducking = 0; maximumInstances = 1; priority = 80 })
Write-Json 'audio/bgm-boss.json' ([ordered]@{ schemaVersion = 2; id = 'bgm-boss'; assetId = 'assets/audio/sync-boss.wav'; category = 'music'; baseVolume = 0.52; loop = $true; loopStartSeconds = 0; loopEndSeconds = 8; crossfadeSeconds = 0.8; ducking = 0; maximumInstances = 1; priority = 90 })
Write-Json 'audio/se-special.json' ([ordered]@{ schemaVersion = 2; id = 'se-special'; assetId = 'assets/audio/sync-special.wav'; category = 'effect'; baseVolume = 0.75; loop = $false; crossfadeSeconds = 0; ducking = 0.2; maximumInstances = 2; priority = 95; cooldownSeconds = 0.2; pitchVariation = 0.02 })

function Write-Wave([string]$RelativePath, [double[]]$Notes, [double]$Seconds, [double]$Amplitude) {
    $path = Join-Path $packRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    $sampleRate = 22050
    $sampleCount = [int]($sampleRate * $Seconds)
    $stream = [System.IO.File]::Create($path)
    try {
        $writer = [System.IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
            $writer.Write(36 + ($sampleCount * 2))
            $writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
            $writer.Write(16); $writer.Write([int16]1); $writer.Write([int16]1); $writer.Write($sampleRate)
            $writer.Write($sampleRate * 2); $writer.Write([int16]2); $writer.Write([int16]16)
            $writer.Write([System.Text.Encoding]::ASCII.GetBytes('data')); $writer.Write($sampleCount * 2)
            for ($sample = 0; $sample -lt $sampleCount; $sample++) {
                $time = $sample / $sampleRate
                $noteIndex = [Math]::Min($Notes.Count - 1, [int](($time / $Seconds) * $Notes.Count))
                $frequency = $Notes[$noteIndex]
                $envelope = [Math]::Min(1.0, [Math]::Min(($time % 0.5) * 20, (0.5 - ($time % 0.5)) * 10 + 0.15))
                $value = ([Math]::Sin(2 * [Math]::PI * $frequency * $time) * 0.62) +
                    ([Math]::Sin(2 * [Math]::PI * $frequency * 1.5 * $time) * 0.23) +
                    ([Math]::Sin(2 * [Math]::PI * ($frequency / 2) * $time) * 0.15)
                $writer.Write([int16]([Math]::Clamp($value * $Amplitude * $envelope * 32767, -32767, 32767)))
            }
        }
        finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }
}

Write-Wave 'assets/audio/sync-stage.wav' @(220, 277.18, 329.63, 440, 369.99, 329.63, 277.18, 246.94, 220, 329.63, 440, 554.37, 493.88, 440, 329.63, 277.18) 8 0.32
Write-Wave 'assets/audio/sync-boss.wav' @(110, 146.83, 164.81, 220, 123.47, 164.81, 246.94, 293.66, 110, 220, 329.63, 440, 146.83, 293.66, 369.99, 493.88) 8 0.35
Write-Wave 'assets/audio/sync-special.wav' @(440, 554.37, 659.25, 880) 1 0.42

Write-Host "SYNC DRIVE content generated at $packRoot"
