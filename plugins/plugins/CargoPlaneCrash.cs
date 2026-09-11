using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("CargoPlaneCrash", "noobless", "2.0.5")]
    [Description("Randomly triggers a cargo plane crash site with guarded loot crates.")]
    public class CargoPlaneCrash : RustPlugin
    {
        #region Fields

        private ConfigData config;

        private readonly List<BaseEntity> activeCrates = new List<BaseEntity>();
        private readonly List<ScientistNPC> activeGuards = new List<ScientistNPC>();
        private readonly Dictionary<ScientistNPC, Vector3> guardHomePositions = new Dictionary<ScientistNPC, Vector3>();
        private readonly List<Vector3> activeCratePositions = new List<Vector3>();
        private List<Vector3> cachedMonuments = new List<Vector3>();

        private Timer scheduleTimer;
        private Timer despawnTimer;
        private Timer fireLoopTimer;
        private Timer guardPatrolTimer;
        private Timer heliRetireTimer;
        private Timer heliArrivalTimer;
        private PatrolHelicopter activeHeli;
        private CargoPlane activePlane;
        private MapMarkerGenericRadius eventMarker;
        private bool eventActive;
        private bool guardNavMeshUnavailableForEvent;
        private int consecutiveLocationFailures;
        private Vector3 eventCenter;
        private float tickNetworkSyncTimer;

        // Plane flight state, driven from OnTick (see #region Flyover). Movement itself is
        // continuous LookAt-and-advance each tick (not a precomputed Lerp), matching a proven
        // reference implementation — planeFlightDuration/Elapsed are kept only as an ETA
        // estimate for the status command, not used to drive the actual motion.
        private Vector3 planeFlightEnd;
        private float planeFlightDuration;
        private float planeFlightElapsed;
        private BaseEntity planeFireChildEntity;
        // generator_smoke.prefab (and smoke_signal_full.prefab) have no server entity
        // component — CreateEntity returns null for them — so the smoke trail can't be
        // parented like the fire can. Instead it's re-triggered as a one-shot effect on this
        // interval at the plane's current position, same pattern as the crate fire loop below.
        private float planeSmokeTrailTimer;
        private int planeHitsTaken;
        private readonly List<BaseEntity> trackedRockets = new List<BaseEntity>();

        // Heli orbit state, also driven from OnTick.
        private Vector3 heliCircleCenter;
        private float heliOrbitHeight;
        private float heliAngleDegrees;
        private Vector3 heliPreviousPos;
        private bool heliApproaching;
        // True once PatrolHeliRetireAfterSeconds has elapsed and the heli is flying away to
        // leave, rather than just being deleted in place. heliRetreatDirection is fixed once
        // when retreat starts (see the retire timer in TickHeliApproach).
        private bool heliRetreating;
        private Vector3 heliRetreatDirection;
        // Diagnostic-only: accumulates dt so we can log the heli's phase/position/angle on a
        // human-readable cadence instead of spamming console every single tick. See
        // LogHeliDebugState. Safe to leave HeliDebugLogging on permanently — this is a single
        // Puts every couple of seconds, not per-frame.
        private float heliDebugLogTimer;

        // The main crash-site fire plus any smaller scattered "wreckage" fire entities —
        // tracked so CleanupEvent can kill all of them.
        private readonly List<BaseEntity> crashFireEntities = new List<BaseEntity>();
        // One position per crash fire (main + wreckage) — the lingering smoke at each is a
        // repeating one-shot effect (see StartCrashSmokeLoop), not an entity, since the
        // smoke prefabs available have no server entity component.
        private readonly List<Vector3> crashSmokePositions = new List<Vector3>();
        private Timer crashSmokeLoopTimer;

        // If a spawn ever fails, use the server "spawn" console command with a partial name
        // (e.g. "spawn heavyscientist") to find the current valid path.
        private const string MilitaryCratePrefab = "assets/bundled/prefabs/radtown/crate_normal.prefab";
        private const string AirdropCratePrefab = "assets/prefabs/misc/supply drop/supply_drop.prefab";
        private const string HeavyScientistPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
        private const string StandardScientistPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab";
        private const string PatrolHeliPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
        private const string ExplosionEffect = "assets/bundled/prefabs/fx/explosions/explosion_02.prefab";
        private const string SmokeEffect = "assets/bundled/prefabs/fx/smoke_signal.prefab";
        private const string FireEffect = "assets/bundled/prefabs/fx/fire/fire_v2.prefab";
        private const string MapMarkerPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        private const string CargoPlanePrefab = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
        private const string LockedCratePrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
        // Persistent engine fire, parented to the plane once trouble starts — moves with it
        // automatically, unlike a repeated one-shot burst effect. Path confirmed from a
        // separately working reference plugin that spawns/kills this same prefab.
        private const string EngineFireEffectPrefab = "assets/bundled/prefabs/oilfireballsmall.prefab";
        // Retired: the cargo-hold smoke used to be a second entity spawned from this prefab
        // (grenade.supplysignal.deployed.prefab). Its default color is pink/purple and can't
        // be changed server-side, and now that PlaneSmokeTrailPrefab below is real smoke,
        // having both was redundant and the purple one was the worse-looking of the two — so
        // it's been removed rather than kept alongside a working smoke trail.
        // The tail-mounted smoke trail entity below was previously being spawned from
        // EngineFireEffectPrefab (a second fireball, not smoke), and a later attempt to spawn
        // it as an entity failed outright — CreateEntity returns null for this prefab, since
        // it's a pure FX asset with no server entity component (confirmed by the server log:
        // "CreateEntity called on a prefab that isn't an entity!"). It's played as a
        // repeating one-shot effect instead (see planeSmokeTrailTimer / TickPlaneFlight). Two
        // options confirmed by the user: generator_smoke.prefab (default here) or
        // smoke_signal_full.prefab as an alternative — both are in the same non-entity FX
        // family, so both work fine with the one-shot approach. Swap the string below to try
        // the other one, no other code needs to change.
        private const string PlaneSmokeTrailPrefab = "assets/bundled/prefabs/fx/smoke/generator_smoke.prefab";
        // private const string PlaneSmokeTrailPrefab = "assets/bundled/prefabs/fx/smoke_signal_full.prefab";
        // One-shot burst played at the exact moment the plane hits the ground, before the
        // lingering crash fire (below) takes over.
        private const string PlaneImpactExplosionPrefab = "assets/bundled/prefabs/fx/survey_explosion.prefab";
        // Tried assets/prefabs/npc/ch47/effects/crashfire.prefab here for the lingering
        // wreckage fire, but it requires an asset scene ('AssetScene-props.other') that's only
        // loaded when a CH47 actually dies in the world — confirmed by the server log
        // ("requires asset scene ... to be loaded first" / "Couldn't find prefab"). No
        // reliable way to force that from a plugin, so SpawnCrashFire uses
        // EngineFireEffectPrefab instead (see below).


        #endregion

        #region Config

        private class ConfigData
        {
            public float MinIntervalMinutes = 150f; // 2h30
            public float MaxIntervalMinutes = 240f; // 4h
            public int CrateCount = 5; // first 4 follow CrateMode; any beyond that are forced Military
            public int TotalHeavyGuards = 5;
            public int TotalStandardGuards = 10;
            public float DespawnMinutes = 45f;
            public float MinDistanceFromMonument = 100f;
            public float MinDistanceFromPlayers = 200f;
            public float MinDistanceFromCoast = 150f;
            public float CrateSpreadRadius = 12f;
            public string CrateMode = "Mixed"; // "Military", "Airdrop", or "Mixed"
            public bool SpawnLockedCrate = true;
            public bool AnnounceEvent = true;
            public bool AnnounceApproxLocation = false;
            public bool AutoStart = true;
            public bool BurningCratesOnSpawn = true;
            public float FireDurationSeconds = 90f;
            public float FireRadius = 5f;
            public float FireDamagePerTick = 12f;
            public float FireTickInterval = 2f;
            public int MinPlayersOnline = 1;
            public bool PatrolHeliFlyover = true;
            public float PatrolHeliRetireAfterSeconds = 100f;
            public float PatrolHeliCircleRadius = 60f;
            public float PatrolHeliCircleAltitude = 40f;
            public float PatrolHeliCircleDegreesPerSecond = 25f;
            public float PatrolHeliDelaySeconds = 8f;
            public float PatrolHeliApproachDistance = 400f; // how far out the heli starts before flying in to the crash site
            public float PatrolHeliApproachSpeed = 30f; // world units per second during the flight-in, before it starts orbiting
            public float PatrolHeliRetreatSpeed = 40f; // world units per second while flying away after retiring, before it's deleted
            public float PatrolHeliRetreatDistance = 400f; // distance from the crash site the heli must reach before it's actually deleted
            // Minimum clearance kept between the heli and the terrain directly beneath it
            // during approach/orbit/retreat, on top of whatever heliOrbitHeight/altitude
            // config already implies. Added because nothing previously re-checked terrain
            // height along the flight path (unlike the plane's TickPlaneFlight, which ends
            // the flight the instant it would clip terrain) — a heli flying toward the crash
            // site over rising ground (a hill/ridge) could have its Rigidbody collide with
            // terrain and effectively get pinned in place while the script kept advancing its
            // target transform underneath it, reading as "flies fine, then freezes."
            public float PatrolHeliMinTerrainClearance = 15f;
            public bool AllowShootDown = true; // let players shoot the plane down mid-flight with rockets
            public int RocketHitsToDowned = 3; // rocket hits required to bring the plane down early
            public float RocketHitDetectionRadius = 15f; // how close a rocket needs to get to the plane to count as a hit
            public bool NoBuildZone = true;
            public float NoBuildRadius = 35f;
            public bool ShowMapMarker = true;
            public float MapMarkerRadius = 0.9f; // NOT meters — normalized map scale, eyeball and adjust in-game
            public float MapMarkerAlpha = 0.35f;
            public bool ShowPlaneFlyover = true;
            public float PlaneSpeed = 16f; // world units per second — this is a slow, dramatic flyover, not a race
            public float PlaneMinFlyoverSeconds = 8f;
            public float PlaneEdgeBuffer = 300f;
            public float PlaneFlightHeight = 150f;
            // Diagnostic toggle: logs the heli's phase (approach/circle/retreat), position,
            // distance-to-target, and orbit angle roughly every HeliDebugLogIntervalSeconds
            // while it's active. Turn this on when the heli appears to freeze in place — the
            // log makes it clear whether the script itself stalled (angle/position both stop
            // advancing) or something external is fighting it (script state keeps advancing,
            // e.g. heliAngleDegrees still increasing, while the actual transform position does
            // not move — that pattern points at the vanilla PatrolHelicopter AI or physics
            // overriding the transform rather than a bug in this plugin's own tick logic).
            public bool HeliDebugLogging = false;
            public float HeliDebugLogIntervalSeconds = 2f;

            // Tunable fire/smoke effect placement — edit these and run cargoplanecrash.force
            // to test without needing a recompile. Local offsets are relative to the plane's
            // own transform (X = right, Y = up, Z = forward, all in the plane's local space).
            // Defaults below are still estimates, not confirmed in-engine — start here and
            // adjust based on what you actually see.
            public Vector3 EngineFireLocalOffset = new Vector3(-12f, 7f, 9f);
            public float EngineFireScale = 4f; // uniform scale — non-uniform scaling didn't behave predictably on this prefab in testing
            public Vector3 RampFlameLocalOffset = new Vector3(0f, 3f, -6f); // raised and pulled toward center per feedback it sat too low and too far back
            public float RampFlameScale = 3f; // no longer used — Effect.server.Run has no scale parameter, kept for config-file compatibility only
            public float SmokeTrailInterval = 0.15f; // seconds between re-triggers of the one-shot smoke effect — lower = smoother trail, more effect spawns

            // Ground-zero wreckage: the main crash fire at the impact center, plus a handful
            // of smaller instances scattered nearby to read as a debris field, plus repeating
            // smoke at each fire's position. We tried a dedicated CH47 crash-fire prefab here
            // first, but it requires an asset scene that's only loaded when a CH47 actually
            // dies in the world (confirmed by the server log), so this reuses the plane's own
            // confirmed-working engine fire prefab instead of a model we can't spawn — swap in
            // real wreck props later if you find valid ones via the in-game `spawn` search.
            public float CrashFireScale = 1.5f; // scale of the main fire at the impact center
            public int WreckageFireCount = 3; // number of smaller scattered fire points around the crash
            public float WreckageScatterRadius = 12f; // max distance from center for scattered wreckage fires
            public float WreckageFireScale = 0.6f; // scale of each scattered wreckage fire (smaller than the main one)
            public float GroundSmokeInterval = 1f; // seconds between re-triggers of the lingering ground smoke at each fire

            public float LocationRetryBaseSeconds = 60f; // delay before retrying if no valid crash
                                                            // site is found; doubles on each
                                                            // consecutive failure up to the cap below
            public float LocationRetryMaxSeconds = 600f;
            public float LockedCrateHackSeconds = 900f; // 0 or less leaves the game's own default
            public bool CustomGuardLoadouts = true; // give heavy/standard guards distinct kits
                                                      // instead of relying on prefab defaults
            public bool GuardPatrolEnabled = true; // periodically move idle guards around near their spawn point instead of standing still
            public float GuardPatrolRadius = 15f; // how far from its spawn point a guard may wander
            public float GuardPatrolIntervalSeconds = 10f; // how often each guard gets a chance to start a new patrol move
            public int GuardPatrolChancePercent = 40; // chance per interval that an idle guard actually moves (0-100)
            public float GuardSenseRange = 40f; // how far a guard can detect/track a player for combat
            public float GuardLeashDistance = 50f; // hard cap: a guard beyond this distance from its spawn point is forcibly pulled back, regardless of what it's currently doing (chasing, fighting, etc.)
            public float CorpseCleanupSeconds = 120f; // how long a guard's corpse lingers after death
            public string AlarmEffectPrefab = ""; // optional looping/alarm sound effect path to
                                                    // play at the crash site; leave blank to disable
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) throw new Exception();
            }
            catch
            {
                PrintWarning("Config file is invalid or missing — creating a new one.");
                LoadDefaultConfig();
            }
            ValidateConfig();
            SaveConfig();
        }

        // Catches config mistakes at load time with a clear warning instead of letting them
        // cause confusing runtime behavior later.
        private void ValidateConfig()
        {
            if (config.MinIntervalMinutes > config.MaxIntervalMinutes)
            {
                PrintWarning($"CargoPlaneCrash: MinIntervalMinutes ({config.MinIntervalMinutes}) is greater than MaxIntervalMinutes ({config.MaxIntervalMinutes}) — swapping them.");
                float temp = config.MinIntervalMinutes;
                config.MinIntervalMinutes = config.MaxIntervalMinutes;
                config.MaxIntervalMinutes = temp;
            }

            if (config.CrateCount <= 0)
            {
                PrintWarning("CargoPlaneCrash: CrateCount is 0 or less — forcing it to 1.");
                config.CrateCount = 1;
            }

            if (config.TotalHeavyGuards + config.TotalStandardGuards <= 0)
            {
                PrintWarning("CargoPlaneCrash: both TotalHeavyGuards and TotalStandardGuards are 0 — the crash site will be unguarded.");
            }

            float heliApproachSeconds = config.PatrolHeliApproachDistance / Mathf.Max(1f, config.PatrolHeliApproachSpeed);
            float heliRetreatSeconds = config.PatrolHeliRetreatDistance / Mathf.Max(1f, config.PatrolHeliRetreatSpeed);
            float heliLifetimeMinutes = (config.PatrolHeliDelaySeconds + heliApproachSeconds + config.PatrolHeliRetireAfterSeconds + heliRetreatSeconds) / 60f;
            if (config.PatrolHeliFlyover && heliLifetimeMinutes > config.DespawnMinutes)
            {
                PrintWarning($"CargoPlaneCrash: the patrol heli's delay + approach flight + retire + retreat flight ({heliLifetimeMinutes:F1} min total) is longer than DespawnMinutes ({config.DespawnMinutes} min) — CleanupEvent will delete it mid-retreat instead of letting it fly all the way out.");
            }

            if (config.AllowShootDown && config.RocketHitsToDowned <= 0)
            {
                PrintWarning("CargoPlaneCrash: RocketHitsToDowned is 0 or less — forcing it to 1.");
                config.RocketHitsToDowned = 1;
            }

            if (config.GuardPatrolChancePercent < 0 || config.GuardPatrolChancePercent > 100)
            {
                PrintWarning($"CargoPlaneCrash: GuardPatrolChancePercent ({config.GuardPatrolChancePercent}) is outside 0-100 — clamping.");
                config.GuardPatrolChancePercent = Mathf.Clamp(config.GuardPatrolChancePercent, 0, 100);
            }

            if (config.FireDurationSeconds / 60f > config.DespawnMinutes)
            {
                PrintWarning($"CargoPlaneCrash: FireDurationSeconds ({config.FireDurationSeconds}s) outlasts DespawnMinutes ({config.DespawnMinutes} min) — the fire loop will get cut off early.");
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        #endregion

        #region Hooks

        [PluginReference] private Plugin Loottable;

        private void Init()
        {
            permission.RegisterPermission("cargoplanecrash.admin", this);
            Unsubscribe("OnTick"); // only active while a plane or heli is actually moving
        }

        // Drives both the plane's flight and the heli's orbit off the engine's own per-tick
        // hook, rather than a separate Timer — this is the same approach a proven, working
        // community plugin uses for moving a CargoPlane, and avoids the network-update spam
        // that made our old timer-based approach look laggy (see #region Flyover).
        private void OnTick()
        {
            bool planeActive = activePlane != null && !activePlane.IsDestroyed;
            bool heliActive = activeHeli != null && !activeHeli.IsDestroyed;

            if (!planeActive && !heliActive)
            {
                Unsubscribe("OnTick");
                return;
            }

            float dt = UnityEngine.Time.deltaTime;

            // An explicit network push isn't needed every tick — Rust's own entity networking
            // handles that — but a periodic nudge is cheap insurance for distant/late-joining
            // observers.
            tickNetworkSyncTimer += dt;
            bool pushNetwork = tickNetworkSyncTimer >= 2f;
            if (pushNetwork) tickNetworkSyncTimer = 0f;

            if (planeActive)
            {
                TickPlaneFlight(dt, pushNetwork);

                if (config.AllowShootDown)
                    CheckRocketHits();
            }
            if (heliActive)
            {
                if (heliApproaching)
                    TickHeliApproach(dt, pushNetwork);
                else if (heliRetreating)
                    TickHeliRetreat(dt, pushNetwork);
                else
                    TickHeliCircle(dt, pushNetwork);

                if (config.HeliDebugLogging)
                    LogHeliDebugState(dt);
            }
        }

        private void OnServerInitialized()
        {
            CacheMonuments();

            Puts(Loottable != null
                ? "Loottable detected — regular crates will use its loot tables."
                : "Loottable not detected — regular crates will use Rust's default loot tables.");

            if (config.AutoStart)
                ScheduleNextEvent();
        }

        private void CacheMonuments()
        {
            cachedMonuments.Clear();
            foreach (var m in TerrainMeta.Path.Monuments)
            {
                if (m != null)
                    cachedMonuments.Add(m.transform.position);
            }
        }

        private void Unload()
        {
            scheduleTimer?.Destroy();
            despawnTimer?.Destroy();
            fireLoopTimer?.Destroy();
            guardPatrolTimer?.Destroy();
            heliRetireTimer?.Destroy();
            heliArrivalTimer?.Destroy();
            Unsubscribe("OnTick");
            CleanupEvent();
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (!eventActive || entity == null) return;

            var scientist = entity as ScientistNPC;
            if (scientist == null || !activeGuards.Contains(scientist)) return;

            activeGuards.Remove(scientist);
            guardHomePositions.Remove(scientist);

            if (config.CorpseCleanupSeconds > 0f)
                ScheduleCorpseCleanup(entity.transform.position);

            if (activeGuards.Count == 0 && config.AnnounceEvent)
            {
                Server.Broadcast("The cargo plane crash site has been cleared of its guards.");
            }
        }

        // Standard Oxide/uMod hook fired when a player fires a rocket. Signature/behavior not
        // independently verified against every Oxide build — if it never seems to fire, check
        // the server console for hook-related warnings on load.
        private void OnRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            if (!config.AllowShootDown || entity == null) return;
            if (activePlane == null || activePlane.IsDestroyed) return;

            trackedRockets.Add(entity);
        }

        // The dying NPC itself typically gets replaced by a separate ragdoll/corpse entity
        // near its death position, which we don't have a direct reference to — so instead of
        // guessing at that entity's creation path, we just sweep the area shortly after death
        // and clean up whatever corpse-type entity landed there.
        private void ScheduleCorpseCleanup(Vector3 deathPos)
        {
            timer.Once(config.CorpseCleanupSeconds, () =>
            {
                var nearby = new List<BaseEntity>();
                Vis.Entities(deathPos, 3f, nearby);
                foreach (var ent in nearby)
                {
                    if (ent is NPCPlayerCorpse && !ent.IsDestroyed)
                        ent.Kill();
                }
            });
        }

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (!eventActive || !config.NoBuildZone) return null;

            if (Vector3.Distance(target.position, eventCenter) > config.NoBuildRadius) return null;

            var player = planner?.GetOwnerPlayer();
            if (player != null)
                player.ChatMessage("You can't build this close to the cargo plane crash site while it's active.");

            return false;
        }

        #endregion

        #region Commands

        [ConsoleCommand("cargoplanecrash.force")]
        private void CmdForceCrash(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !permission.UserHasPermission(arg.Player().UserIDString, "cargoplanecrash.admin"))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            TriggerEvent();
            arg.ReplyWith("Cargo plane crash event triggered.");
        }

        [ChatCommand("forcecrash")]
        private void CmdForceCrashChat(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "cargoplanecrash.admin"))
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }

            TriggerEvent();
            SendReply(player, "Cargo plane crash event triggered.");
        }

        [ConsoleCommand("cargoplanecrash.status")]
        private void CmdStatus(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !permission.UserHasPermission(arg.Player().UserIDString, "cargoplanecrash.admin"))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            bool planeInFlight = activePlane != null && !activePlane.IsDestroyed;

            if (planeInFlight)
            {
                float remaining = Mathf.Max(0f, planeFlightDuration - planeFlightElapsed);
                arg.ReplyWith($"Plane in flight toward {planeFlightEnd} (grid {PositionToGrid(planeFlightEnd)}) — ETA {remaining:F0}s ({remaining / 60f:F1} min).");
                return;
            }

            bool heliInFlight = activeHeli != null && !activeHeli.IsDestroyed;
            if (heliInFlight)
            {
                string phase = heliApproaching ? "approaching" : heliRetreating ? "retreating" : "circling";
                arg.ReplyWith($"Patrol heli {phase} at {activeHeli.transform.position} (grid {PositionToGrid(activeHeli.transform.position)}).");
            }

            if (!eventActive)
            {
                string nextInfo = scheduleTimer != null ? "a future event is scheduled" : "no event is currently scheduled";
                arg.ReplyWith($"No cargo plane crash event is active ({nextInfo}).");
                return;
            }

            arg.ReplyWith($"Event active at {eventCenter} (grid {PositionToGrid(eventCenter)}). Guards remaining: {activeGuards.Count}. Crates: {activeCrates.Count}. Heli active: {(activeHeli != null && !activeHeli.IsDestroyed)}.");
        }

        // Diagnostic only — fires the exact smoke trail effect at the caller's own feet, up
        // close and stationary, so it can be visually confirmed independently of whether the
        // plane/movement logic is working. Run as: cargoplanecrash.testsmoke
        [ConsoleCommand("cargoplanecrash.testsmoke")]
        private void CmdTestSmoke(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && !permission.UserHasPermission(player.UserIDString, "cargoplanecrash.admin"))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            Vector3 pos = player != null ? player.transform.position + player.transform.forward * 2f + Vector3.up : eventCenter;
            Effect.server.Run(PlaneSmokeTrailPrefab, pos);
            arg.ReplyWith($"Fired '{PlaneSmokeTrailPrefab}' at {pos} — look right in front of you. If nothing appears, the path is likely wrong or the asset requires something CreateEntity-style checks wouldn't have caught.");
        }

        // Diagnostic only — toggles the per-tick heli phase/position logging at runtime
        // without needing a config reload. Run as: cargoplanecrash.helidebug
        [ConsoleCommand("cargoplanecrash.helidebug")]
        private void CmdHeliDebug(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !permission.UserHasPermission(arg.Player().UserIDString, "cargoplanecrash.admin"))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            config.HeliDebugLogging = !config.HeliDebugLogging;
            heliDebugLogTimer = 0f;
            arg.ReplyWith($"Heli debug logging {(config.HeliDebugLogging ? "enabled" : "disabled")}.");
        }

        [ConsoleCommand("cargoplanecrash.stop")]
        private void CmdStop(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !permission.UserHasPermission(arg.Player().UserIDString, "cargoplanecrash.admin"))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (!eventActive && activePlane == null)
            {
                arg.ReplyWith("No cargo plane crash event is active.");
                return;
            }

            CleanupEvent();
            arg.ReplyWith("Cargo plane crash event stopped and cleaned up.");
        }

        #endregion

        #region Scheduling

        private void ScheduleNextEvent()
        {
            scheduleTimer?.Destroy();

            float minutes = UnityEngine.Random.Range(config.MinIntervalMinutes, config.MaxIntervalMinutes);
            scheduleTimer = timer.Once(minutes * 60f, TriggerEvent);
        }

        private void TriggerEvent()
        {
            if (eventActive || activePlane != null)
            {
                CleanupEvent();
            }

            if (BasePlayer.activePlayerList.Count < config.MinPlayersOnline)
            {
                ScheduleNextEvent();
                return;
            }

            Vector3 position;
            if (!TryFindCrashPosition(out position))
            {
                consecutiveLocationFailures++;
                float retrySeconds = Mathf.Min(config.LocationRetryBaseSeconds * Mathf.Pow(2, consecutiveLocationFailures - 1), config.LocationRetryMaxSeconds);
                PrintWarning($"CargoPlaneCrash: couldn't find a valid crash site (failure #{consecutiveLocationFailures}) — retrying in {retrySeconds:F0}s. If this keeps happening, check MinDistanceFromCoast/MinDistanceFromMonument against your map.");
                timer.Once(retrySeconds, TriggerEvent);
                return;
            }
            consecutiveLocationFailures = 0;

            if (config.ShowPlaneFlyover)
            {
                StartPlaneFlyover(position);
            }
            else
            {
                PrintWarning("CargoPlaneCrash: ShowPlaneFlyover is false in config — spawning the crash site directly with no flyover.");
                SpawnCrashSite(position);
            }

            ScheduleNextEvent();
        }

        #endregion

        #region Flyover

        private void StartPlaneFlyover(Vector3 crashCenter)
        {
            planeHitsTaken = 0;
            trackedRockets.Clear();

            Vector3 startPos = ComputeOppositeEdgeStart(crashCenter);
            // PlaneFlightHeight is meant as clearance above the ground at the start point, not
            // a flat world-space altitude. The start point sits beyond the edge of the
            // playable map, where terrain height can be unpredictable (often higher than
            // expected near a mountainous border) — a flat altitude there could already be
            // below the real ground, which would trip the terrain-impact check on literally the
            // very first tick and "crash" the plane right where it spawned, off past the map
            // edge, before it ever really flew anywhere.
            startPos.y = GetGroundHeight(startPos) + config.PlaneFlightHeight;

            // Ground-level target — the plane LookAt()s this every tick, so the dive angle
            // steepens naturally as horizontal distance closes, without separate descent-phase
            // math (matches the confirmed-working reference technique).
            Vector3 target = GroundPosition(crashCenter);

            // startActive:false defers the entity's own initialization instead of spawning it
            // fully active and then disabling the component afterward — the same pattern the
            // reference plugin uses, and safer than our earlier post-hoc enabled=false toggle.
            var planeObj = GameManager.server.CreateEntity(CargoPlanePrefab, startPos, Quaternion.identity, false);
            if (planeObj == null)
            {
                PrintWarning($"Failed to create cargo plane entity from prefab '{CargoPlanePrefab}' — path may be outdated. Skipping the flyover and crashing immediately.");
                SpawnCrashSite(crashCenter);
                return;
            }

            activePlane = planeObj as CargoPlane;
            if (activePlane == null)
            {
                PrintWarning($"CargoPlaneCrash: entity created from '{CargoPlanePrefab}' was not a CargoPlane. Skipping the flyover and crashing immediately.");
                planeObj.Kill();
                SpawnCrashSite(crashCenter);
                return;
            }

            activePlane.Spawn();

            // The vanilla CargoPlane entity has its own built-in autonomous flight controller
            // (it normally flies its own randomly-computed course for airdrops). Its Update()/
            // FixedUpdate() drive continued movement along that course, and disabling the
            // component (below) stops those from running. But the *initial* jump to its own
            // randomly-computed start point happens synchronously inside Spawn() itself —
            // Unity's `enabled` flag only gates Update()/FixedUpdate(), not code that runs
            // directly inside an overridden Spawn()/OnEntitySpawn(). So by the time we reach
            // this line the transform may already have been moved thousands of meters away
            // under its own logic (matching the "Invalid Position" jumps observed in testing).
            // Disabling the component first stops it from drifting any further, and then
            // forcibly resetting the transform back to our own startPos undoes that one-time
            // spawn-time jump, so everything downstream (the engine fire, TickPlaneFlight) is
            // working from the correct position.
            activePlane.enabled = false;
            activePlane.transform.position = startPos;
            activePlane.InvalidateNetworkCache();
            activePlane.SendNetworkUpdate();

            var rb = activePlane.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // All offsets/scales below are read from config so they can be tuned by editing
            // the config file and re-running cargoplanecrash.force — no recompile needed. See
            // the ConfigData comment near these fields for the axis convention.
            planeFireChildEntity = GameManager.server.CreateEntity(EngineFireEffectPrefab, activePlane.transform.position);
            if (planeFireChildEntity != null)
            {
                planeFireChildEntity.Spawn();
                planeFireChildEntity.SetParent(activePlane);
                planeFireChildEntity.transform.localPosition = config.EngineFireLocalOffset;
                planeFireChildEntity.transform.localScale = Vector3.one * config.EngineFireScale;
            }
            else
            {
                PrintWarning($"CargoPlaneCrash: failed to create engine fire entity from prefab '{EngineFireEffectPrefab}' — path may be outdated. No visible fire on this flight.");
            }

            // Smoke trail — this used to be spawned from EngineFireEffectPrefab (a second
            // fireball, not smoke), so the plane only ever had two fire effects and no real
            // smoke. A later attempt to CreateEntity a real smoke prefab failed outright (it
            // has no server entity component), so this is instead re-triggered as a one-shot
            // effect every frame the plane flies — see planeSmokeTrailTimer in
            // TickPlaneFlight. A separate purple cargo-hold smoke entity
            // (grenade.supplysignal.deployed.prefab) used to sit alongside this — removed,
            // since its color couldn't be fixed server-side and it was redundant with a
            // working smoke trail.
            planeSmokeTrailTimer = 0f;

            float distance = Vector3.Distance(startPos, target);
            float estimatedDuration = Mathf.Max(config.PlaneMinFlyoverSeconds, distance / Mathf.Max(1f, config.PlaneSpeed));

            Puts($"CargoPlaneCrash: plane flyover started at {startPos}, target {target} — {distance:F0}m at {config.PlaneSpeed}u/s, roughly {estimatedDuration:F0}s ({estimatedDuration / 60f:F1} min).");

            planeFlightEnd = target;
            planeFlightDuration = estimatedDuration; // ETA estimate only — see field comment
            planeFlightElapsed = 0f;

            Subscribe("OnTick");
        }

        // Starts the plane on the map edge opposite the crash site (relative to map center),
        // so it crosses most of the map to reach its destination.
        private Vector3 ComputeOppositeEdgeStart(Vector3 crashCenter)
        {
            float worldHalf = TerrainMeta.Size.x / 2f;
            Vector3 mapCenter = Vector3.zero;

            Vector3 towardCrash = crashCenter - mapCenter;
            towardCrash.y = 0f;

            Vector3 dir = towardCrash.sqrMagnitude > 4f
                ? towardCrash.normalized
                : Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;

            Vector3 start = mapCenter - dir * (worldHalf + config.PlaneEdgeBuffer);
            start.y = 0f;
            return start;
        }

        // Called from OnTick every frame the plane is in flight. Continuously looks at and
        // advances toward the target each tick (rather than interpolating along a precomputed
        // start/end path) — this self-corrects every frame and can't drift off course the way
        // a fixed-duration Lerp could if any of its inputs were subtly wrong.
        private void TickPlaneFlight(float dt, bool pushNetwork)
        {
            planeFlightElapsed += dt;

            activePlane.transform.LookAt(planeFlightEnd);
            Vector3 pos = activePlane.transform.position + activePlane.transform.forward * (config.PlaneSpeed * dt);

            // Terrain impact: the descent above is a straight line toward the pre-verified
            // target, with no raycast along the way (see the arrival comment below for why) —
            // so if that straight line runs into a hill before reaching the target, that's a
            // real crash. Rather than clamping height to keep the plane artificially aloft (the
            // previous behavior), end the flight right here — a plane that can't keep flying
            // should crash and explode where it actually hit, not float along at a corrected
            // height. This uses the same "crash away from the pre-verified target" path as a
            // shoot-down (see FinishPlaneFlight/CrashReason).
            float terrainHeightHere = GetGroundHeight(pos);
            if (pos.y <= terrainHeightHere)
            {
                Vector3 crashPos = pos;
                crashPos.y = terrainHeightHere;

                Puts($"CargoPlaneCrash: plane crashed into terrain at {crashPos} — ending flight early.");
                if (config.AnnounceEvent)
                    Server.Broadcast("The cargo plane has crashed into the terrain!");

                FinishPlaneFlight(crashPos, CrashReason.TerrainImpact);
                return;
            }

            activePlane.transform.position = pos;

            if (pushNetwork)
            {
                activePlane.InvalidateNetworkCache();
                activePlane.SendNetworkUpdate();
            }

            // Smoke trail: generator_smoke.prefab has no server entity component, so it can't
            // be parented like the fire can — this re-plays it as a one-shot burst at the
            // plane's current position on a short interval instead, which reads as a
            // continuous trail without needing an entity to follow the plane.
            planeSmokeTrailTimer += dt;
            if (planeSmokeTrailTimer >= config.SmokeTrailInterval)
            {
                planeSmokeTrailTimer = 0f;
                Effect.server.Run(PlaneSmokeTrailPrefab, activePlane.transform.TransformPoint(config.RampFlameLocalOffset));
            }

            // Arrival: close enough to the pre-verified, water-checked target. We deliberately
            // do NOT use a "raycast finds ground nearby" trigger here (unlike the reference
            // plugin) — that plugin captures its crash position AFTER an organic crash
            // wherever that happens, but ours is pre-selected and already verified dry, so an
            // early terrain-impact check above already handles the "hit something along the
            // way" case; this is purely "did we reach the intended destination."
            float arriveThreshold = Mathf.Max(5f, config.PlaneSpeed * dt * 2f);
            if (Vector3.Distance(pos, planeFlightEnd) < arriveThreshold)
            {
                FinishPlaneFlight(pos);
            }
        }

        // Called from OnTick each frame the plane is flying, only when AllowShootDown is on.
        // CargoPlane has no health pool in vanilla Rust (it's not a BaseCombatEntity), so
        // standard damage hooks won't fire on it — instead we track rockets fired while the
        // plane is active (via OnRocketLaunched) and check their proximity to the plane
        // ourselves each tick, since a moving rocket and a moving plane both need re-checking
        // every frame to catch a hit reliably.
        private void CheckRocketHits()
        {
            if (activePlane == null || activePlane.IsDestroyed)
            {
                trackedRockets.Clear();
                return;
            }

            for (int i = trackedRockets.Count - 1; i >= 0; i--)
            {
                var rocket = trackedRockets[i];
                if (rocket == null || rocket.IsDestroyed)
                {
                    trackedRockets.RemoveAt(i);
                    continue;
                }

                if (Vector3.Distance(rocket.transform.position, activePlane.transform.position) <= config.RocketHitDetectionRadius)
                {
                    Vector3 hitPos = rocket.transform.position;
                    trackedRockets.RemoveAt(i);

                    if (!rocket.IsDestroyed)
                        rocket.Kill(); // detonate/remove on contact rather than letting it fly through

                    RegisterPlaneHit(hitPos);

                    // A hit may have just brought the plane down (RegisterPlaneHit calls
                    // FinishPlaneFlight, which kills activePlane and sets it to null) — bail
                    // out rather than keep comparing remaining rockets against a dead plane.
                    if (activePlane == null || activePlane.IsDestroyed)
                        return;
                }
            }
        }

        private void RegisterPlaneHit(Vector3 hitPos)
        {
            planeHitsTaken++;
            Effect.server.Run(ExplosionEffect, hitPos);

            if (config.AnnounceEvent)
                Server.Broadcast($"The cargo plane took a hit! ({planeHitsTaken}/{config.RocketHitsToDowned})");

            if (planeHitsTaken < config.RocketHitsToDowned) return;

            Puts($"CargoPlaneCrash: plane shot down after {planeHitsTaken} rocket hit(s) at {hitPos}.");
            if (config.AnnounceEvent)
                Server.Broadcast("The cargo plane has been shot down!");

            FinishPlaneFlight(hitPos, CrashReason.ShotDown);
        }

        private enum CrashReason { Arrival, ShotDown, TerrainImpact }

        // A ShotDown or TerrainImpact crash happens at finalPos itself (wherever the plane
        // actually was) rather than the pre-verified planeFlightEnd target — that target's
        // water/monument/player-distance checks were only ever guaranteed for a natural
        // Arrival, not an arbitrary point along the flight path.
        private void FinishPlaneFlight(Vector3 finalPos, CrashReason reason = CrashReason.Arrival)
        {
            // The impact explosion is played by SpawnCrashSite below (using
            // PlaneImpactExplosionPrefab) rather than here, since crashPos and finalPos land
            // at essentially the same spot for every reason — playing it here too would just
            // double it up.

            if (planeFireChildEntity != null && !planeFireChildEntity.IsDestroyed)
                planeFireChildEntity.Kill();
            planeFireChildEntity = null;

            if (activePlane != null && !activePlane.IsDestroyed)
                activePlane.Kill();
            activePlane = null;

            trackedRockets.Clear();
            planeHitsTaken = 0;

            Vector3 crashPos = reason == CrashReason.Arrival ? planeFlightEnd : finalPos;

            // A ShotDown or TerrainImpact crash can land anywhere along the flight path — that
            // point was never run through TryFindCrashPosition's water/NavMesh/monument checks
            // the way the natural Arrival target was. Rather than spawning the whole site at an
            // unverified spot (which, over water, is exactly what previously caused every crate
            // to fail its water check and the event to silently abort with zero loot/guards),
            // fall back to the pre-verified center instead. The wreckage visuals still played at
            // the real impact point above (crash fire, explosion) — only the loot/guard spawn
            // location gets redirected.
            if (reason != CrashReason.Arrival && IsUnderwater(crashPos))
            {
                PrintWarning($"CargoPlaneCrash: plane crashed over water at {crashPos} ({reason}) — falling back to the pre-verified crash site at {planeFlightEnd} so loot and guards still spawn correctly.");
                crashPos = planeFlightEnd;
            }

            SpawnCrashSite(crashPos);
        }

        #endregion

        #region Location selection

        private bool TryFindCrashPosition(out Vector3 result)
        {
            result = Vector3.zero;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                float worldSize = TerrainMeta.Size.x;
                float x = UnityEngine.Random.Range(-worldSize / 2f + 200f, worldSize / 2f - 200f);
                float z = UnityEngine.Random.Range(-worldSize / 2f + 200f, worldSize / 2f - 200f);

                float y = TerrainMeta.HeightMap.GetHeight(new Vector3(x, 0f, z));
                var candidate = new Vector3(x, y, z);

                if (!AreaClearOfWater(candidate, Mathf.Max(config.MinDistanceFromCoast, config.CrateSpreadRadius)))
                    continue;

                // Guards get placed anywhere within CrateSpreadRadius of the center, so the
                // whole spread area needs baked NavMesh, not just the exact center point —
                // otherwise SpawnGuard/ConfigureGuardAI later reject guards one-by-one with
                // "no NavMesh area matching this guard's own agent type" and they never patrol.
                // Checking here, before a site is accepted, is cheap (it's inside the existing
                // 50-attempt retry loop) and stops the whole event from spawning on off-mesh
                // terrain in the first place.
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit siteNavHit, config.CrateSpreadRadius + 10f, NavMesh.AllAreas))
                {
                    PrintWarning($"CargoPlaneCrash: rejected candidate site at {candidate} — no NavMesh within {config.CrateSpreadRadius + 10f:F0}m (attempt {attempt + 1}/50).");
                    continue;
                }

                bool tooCloseToMonument = false;
                for (int m = 0; m < cachedMonuments.Count; m++)
                {
                    if (Vector3.Distance(cachedMonuments[m], candidate) < config.MinDistanceFromMonument)
                    {
                        tooCloseToMonument = true;
                        break;
                    }
                }
                if (tooCloseToMonument) continue;

                bool tooCloseToPlayer = false;
                var players = BasePlayer.activePlayerList;
                for (int p = 0; p < players.Count; p++)
                {
                    if (Vector3.Distance(players[p].transform.position, candidate) < config.MinDistanceFromPlayers)
                    {
                        tooCloseToPlayer = true;
                        break;
                    }
                }
                if (tooCloseToPlayer) continue;

                // Extension point for other plugins (e.g. a ZoneManager bridge) to veto a
                // candidate location — hook "CanCargoPlaneCrashHere" and return false to reject it.
                object hookResult = Interface.Oxide.CallHook("CanCargoPlaneCrashHere", candidate);
                if (hookResult is bool allowed && !allowed) continue;

                Puts($"CargoPlaneCrash: crash site selected at {candidate} (terrain height {TerrainMeta.HeightMap.GetHeight(candidate):F2}, water height {TerrainMeta.WaterMap.GetHeight(candidate):F2}, attempt {attempt + 1}/50).");

                result = candidate;
                return true;
            }

            return false;
        }

        // A point counts as water if it's at/below world sea level (open ocean — Rust doesn't
        // paint ocean into the WaterMap texture, it's just "height <= 0", which is what the
        // old check missed near coastlines) OR if the WaterMap texture reports an inland lake
        ///river sitting above the terrain here. No raycast: relying on a "Water" physics layer
        // resolving/existing correctly was the other source of coastline false negatives.
        private bool IsUnderwater(Vector3 pos)
        {
            float terrainHeight = TerrainMeta.HeightMap.GetHeight(pos);

            if (terrainHeight <= 0.5f)
                return true;

            if (TerrainMeta.WaterMap.GetHeight(pos) > terrainHeight + 0.5f)
                return true;

            return false;
        }

        // True only if the whole disk (not just its boundary) around 'center' is dry — checks
        // the center plus 5 staggered concentric rings of 16 points each.
        private bool AreaClearOfWater(Vector3 center, float radius)
        {
            if (IsUnderwater(center)) return false;

            const int rings = 5;
            const int samplesPerRing = 16;
            for (int r = 1; r <= rings; r++)
            {
                float ringRadius = radius * r / rings;
                for (int s = 0; s < samplesPerRing; s++)
                {
                    float angle = s * (360f / samplesPerRing) + r * 5f;
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * ringRadius);
                    Vector3 samplePoint = center + offset;
                    samplePoint.y = TerrainMeta.HeightMap.GetHeight(samplePoint);

                    if (IsUnderwater(samplePoint))
                        return false;
                }
            }
            return true;
        }

        #endregion

        #region Spawning

        private void SpawnCrashSite(Vector3 center)
        {
            eventActive = true;
            eventCenter = center;
            guardNavMeshUnavailableForEvent = false; // reset per event; set true if a guard exhausts every search radius

            // The exact moment of ground impact — one sharp burst before the lingering fire
            // below takes over.
            Effect.server.Run(PlaneImpactExplosionPrefab, center);
            Effect.server.Run(SmokeEffect, center + Vector3.up * 1f);

            SpawnCrashFire(center, config.CrashFireScale);

            // Scattered smaller fires around the impact point to read as a wreckage/debris
            // field. We couldn't confirm an actual static wreckage prop path, so this reuses
            // the same confirmed engine-fire prefab at a reduced scale instead of guessing at
            // an unverified model.
            for (int w = 0; w < config.WreckageFireCount; w++)
            {
                float angle = UnityEngine.Random.Range(0f, 360f);
                float distance = UnityEngine.Random.Range(config.WreckageScatterRadius * 0.3f, config.WreckageScatterRadius);
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * distance);
                Vector3 wreckPos = GroundPosition(center + offset);

                if (IsUnderwater(wreckPos))
                    continue;

                SpawnCrashFire(wreckPos, config.WreckageFireScale);
            }

            StartCrashSmokeLoop();

            if (!string.IsNullOrEmpty(config.AlarmEffectPrefab))
                Effect.server.Run(config.AlarmEffectPrefab, center);

            var cratePositions = new List<Vector3>();
            for (int i = 0; i < config.CrateCount; i++)
            {
                float angle = i * (360f / config.CrateCount) + UnityEngine.Random.Range(-15f, 15f);
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * config.CrateSpreadRadius);
                Vector3 cratePos = GroundPosition(center + offset);

                if (IsUnderwater(cratePos))
                {
                    PrintWarning($"CargoPlaneCrash: skipped a crate at {cratePos} (terrain height {TerrainMeta.HeightMap.GetHeight(cratePos):F2}, water height {TerrainMeta.WaterMap.GetHeight(cratePos):F2}) because it would have landed in water.");
                    continue;
                }

                string cratePrefab = i < 4 ? PickCratePrefab() : MilitaryCratePrefab;
                var crate = GameManager.server.CreateEntity(cratePrefab, cratePos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                if (crate == null)
                {
                    PrintWarning($"Failed to create crate entity from prefab '{cratePrefab}' — path may be outdated.");
                    continue;
                }

                crate.Spawn();
                activeCrates.Add(crate);
                cratePositions.Add(cratePos);
                activeCratePositions.Add(cratePos);

                if (config.BurningCratesOnSpawn)
                    Effect.server.Run(FireEffect, cratePos + Vector3.up * 0.5f);
            }

            if (cratePositions.Count == 0)
            {
                // Every spread-out candidate failed (usually all underwater, on ground that's
                // still bad even after the fixes above). Rather than aborting the whole event
                // outright, try once more directly at the impact point itself before giving up —
                // this is the one spot we already know the wreckage/fire effects landed cleanly.
                PrintWarning("CargoPlaneCrash: no crates spawned on the spread pattern — retrying once at the exact impact point before giving up.");

                Vector3 fallbackCratePos = GroundPosition(center);
                if (!IsUnderwater(fallbackCratePos))
                {
                    string fallbackPrefab = PickCratePrefab();
                    var fallbackCrate = GameManager.server.CreateEntity(fallbackPrefab, fallbackCratePos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                    if (fallbackCrate != null)
                    {
                        fallbackCrate.Spawn();
                        activeCrates.Add(fallbackCrate);
                        cratePositions.Add(fallbackCratePos);
                        activeCratePositions.Add(fallbackCratePos);

                        if (config.BurningCratesOnSpawn)
                            Effect.server.Run(FireEffect, fallbackCratePos + Vector3.up * 0.5f);
                    }
                }

                if (cratePositions.Count == 0)
                {
                    PrintWarning("CargoPlaneCrash: still no crates spawned — aborting this event (check prefab paths above).");
                    eventActive = false;
                    despawnTimer?.Destroy();

                    foreach (var fire in crashFireEntities)
                    {
                        if (fire != null && !fire.IsDestroyed)
                            fire.Kill();
                    }
                    crashFireEntities.Clear();

                    crashSmokeLoopTimer?.Destroy();
                    crashSmokeLoopTimer = null;
                    crashSmokePositions.Clear();

                    return;
                }
            }

            if (config.BurningCratesOnSpawn && activeCratePositions.Count > 0)
                StartFireLoop();

            if (config.SpawnLockedCrate)
                SpawnLockedCrateEntity(center);

            DistributeGuards(cratePositions, center, HeavyScientistPrefab, config.TotalHeavyGuards);
            DistributeGuards(cratePositions, center, StandardScientistPrefab, config.TotalStandardGuards);

            if (config.GuardPatrolEnabled)
            {
                guardPatrolTimer?.Destroy();
                guardPatrolTimer = timer.Every(config.GuardPatrolIntervalSeconds, TickGuardPatrol);
            }

            if (config.PatrolHeliFlyover)
            {
                heliArrivalTimer?.Destroy();
                heliArrivalTimer = timer.Once(config.PatrolHeliDelaySeconds, () =>
                {
                    heliArrivalTimer = null;
                    if (eventActive)
                        SpawnPatrolHeliFlyover(center);
                });
            }
            else
            {
                PrintWarning("CargoPlaneCrash: PatrolHeliFlyover is false in config — skipping the heli flyover.");
            }

            if (config.ShowMapMarker)
                SpawnMapMarker(center);

            if (config.AnnounceEvent)
            {
                string msg = config.AnnounceApproxLocation
                    ? $"A cargo plane has gone down near grid {PositionToGrid(center)}! Heavily armed scientists are guarding the wreckage."
                    : "A cargo plane has gone down somewhere on the map! Heavily armed scientists are guarding the wreckage.";
                Server.Broadcast(msg);
            }

            despawnTimer?.Destroy();
            despawnTimer = timer.Once(config.DespawnMinutes * 60f, CleanupEvent);
        }

        private void SpawnGuard(Vector3 cratePos, Vector3 center, string prefab)
        {
            Vector3 offset = UnityEngine.Random.insideUnitCircle.ToVector3Vertical() * 4f;
            Vector3 rawSpawnPos = GroundPosition(cratePos + offset);

            Vector3 spawnPos = rawSpawnPos;
            if (NavMesh.SamplePosition(rawSpawnPos, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
            {
                spawnPos = navHit.position;
            }
            else
            {
                PrintWarning($"CargoPlaneCrash: no NavMesh found within 10m of guard spawn point {rawSpawnPos} — this guard may not move/patrol properly.");
            }

            if (IsUnderwater(spawnPos))
            {
                Vector3 fallbackPos = GroundPosition(cratePos);
                if (NavMesh.SamplePosition(fallbackPos, out NavMeshHit fallbackHit, 10f, NavMesh.AllAreas))
                    fallbackPos = fallbackHit.position;

                if (IsUnderwater(fallbackPos))
                {
                    PrintWarning($"CargoPlaneCrash: skipped a guard — both its spawn point {spawnPos} and the crate fallback {fallbackPos} were in water.");
                    return;
                }

                spawnPos = fallbackPos;
            }

            var npcObj = GameManager.server.CreateEntity(prefab, spawnPos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            if (npcObj == null) return;

            var scientist = npcObj as ScientistNPC;
            npcObj.Spawn();

            if (scientist != null)
            {
                activeGuards.Add(scientist);
                guardHomePositions[scientist] = spawnPos;

                if (config.CustomGuardLoadouts)
                    ApplyGuardLoadout(scientist, isHeavy: prefab == HeavyScientistPrefab);

                // Deferred a tick: the brain/navigator components aren't fully initialized in
                // the same frame as Spawn() — configuring them immediately threw a
                // NullReferenceException on every single guard (confirmed in testing, one per
                // guard spawned). Waiting one tick lets the NPC's own Awake/Init logic finish
                // first.
                NextTick(() =>
                {
                    if (scientist != null && !scientist.IsDestroyed)
                        ConfigureGuardAI(scientist);
                });
            }
        }

        // Directly spawning a scientistnpc_ prefab via CreateEntity bypasses the game's normal
        // NPC-population spawn handler, which is apparently what would otherwise configure the
        // brain/navigator for actual combat and movement — left alone, these NPCs were
        // observed just standing completely still, never chasing or engaging players. This
        // mirrors the exact ScientistBrain/BaseNavigator setup Fruster's reference plugin uses
        // for the same prefab types (proven working there), rather than a blind guess — sense
        // range, vision, and target-tracking fields, plus the NavMesh agent setup and the
        // explicit navigator.Init() call that plain CreateEntity+Spawn never triggers.
        private void ConfigureGuardAI(ScientistNPC scientist)
        {
            try
            {
                var brain = scientist.GetComponent<ScientistBrain>();
                if (brain != null)
                {
                    // AttackRangeMultiplier deliberately left at its default here (previously
                    // set to 10f, copied directly from Fruster's reference plugin) — his script
                    // pairs that multiplier with a very different, much larger set of range
                    // values it was tuned against. Copied in isolation, that 10x multiplier is
                    // almost certainly why guards were observed chasing players 2-3 grids
                    // (300-450m) away. The explicit leash below (TickGuardPatrol) is the actual
                    // hard guarantee against runaway chase distance now, rather than depending
                    // on getting this multiplier's interaction with the rest of the brain right.
                    brain.VisionCone = -1;
                    brain.SenseRange = config.GuardSenseRange;
                    brain.ListenRange = 15f;
                    brain.SenseTypes = EntityType.Player;
                    brain.MemoryDuration = 5f;
                    brain.TargetLostRange = config.GuardSenseRange;
                    brain.CheckVisionCone = false;
                    brain.CheckLOS = true;
                    brain.IgnoreNonVisionSneakers = true;
                    brain.HostileTargetsOnly = false;
                    brain.IgnoreSafeZonePlayers = false;
                    brain.RefreshKnownLOS = true;

                    // Explicit reset to Idle here, not just during the leash pull-back — the
                    // vanilla prefab's own default initialization may otherwise leave it in
                    // some active/roam state before our config above even applies, which could
                    // explain guards moving off immediately at spawn with nothing around.
                    brain.SwitchToState(AIState.Idle, 0);
                }
                else
                {
                    PrintWarning("CargoPlaneCrash: guard has no ScientistBrain component — it may never engage players.");
                }

                var navigator = scientist.GetComponent<BaseNavigator>();
                if (navigator != null)
                {
                    navigator.MaxRoamDistanceFromHome = navigator.BestMovementPointMaxDistance = navigator.BestRoamPointMaxDistance = config.GuardPatrolRadius;
                    navigator.DefaultArea = "Walkable";
                    navigator.topologyPreference = (TerrainTopology.Enum)TerrainTopology.EVERYTHING;
                    // Deliberately NOT setting navigator.Agent.agentTypeID here (previously
                    // hardcoded to Fruster's -1372625422). That value is specific to the NPC
                    // prefab his script uses (a generic test/tunnel-dweller NPC), not the
                    // standard scientistnpc_ prefabs this plugin spawns. Applied to the wrong
                    // prefab, a mismatched agent type ID can make the navigator think its
                    // current spot isn't valid ground for that agent and immediately head off
                    // toward wherever it thinks is — which lines up with guards bolting the
                    // instant they land, before anything happens. Our prefabs are standard
                    // vanilla scientists that already roam correctly all over the map without
                    // this override, so leaving Agent.agentTypeID at its own default instead.

                    // SpawnGuard's initial position sample used NavMesh.SamplePosition with
                    // NavMesh.AllAreas but no agent-type filter — that can snap onto navmesh
                    // baked for a DIFFERENT agent type than this scientist's own, which the
                    // engine then rejects every time it tries to actually place this agent
                    // there ("Agent still not on navmesh after a warp... Agent type: 0" spam
                    // seen in testing). Re-sampling here with a filter locked to this
                    // navigator's own agentTypeID, and correcting the NPC's position before
                    // Init, fixes it at the source instead of leaving default behavior.
                    //
                    // A fixed 10m search was too tight for TerrainImpact/ShotDown crashes,
                    // which land wherever the plane actually was rather than a pre-verified
                    // site — a hillside impact can be well over 10m from the nearest walkable
                    // mesh, so every guard fell back to "skip AI init" and never moved. Trying
                    // progressively wider radii means a guard only ends up stationary if
                    // there's genuinely no mesh anywhere near the crash, not just none within
                    // the first 10m.
                    //
                    // Some impact points (deep in mountainous/cave terrain, or a genuine gap in
                    // the map's navmesh bake) have no mesh at all within even 100m — confirmed
                    // by every guard on the same crash failing identically up to that radius.
                    // Two changes for that case: extend the search out to 500m as a real last
                    // resort, and once ANY guard on this event has exhausted every radius,
                    // remember that (guardNavMeshUnavailableForEvent) so the rest of the guards
                    // on the same crash skip straight to "give up" instead of each repeating the
                    // same expensive, already-failing wide search.
                    if (guardNavMeshUnavailableForEvent)
                    {
                        navigator = null;
                    }
                    else if (navigator.Agent != null)
                    {
                        var filter = new NavMeshQueryFilter
                        {
                            agentTypeID = navigator.Agent.agentTypeID,
                            areaMask = NavMesh.AllAreas
                        };

                        float[] searchRadii = { 10f, 25f, 50f, 100f, 250f, 500f };
                        bool foundMesh = false;

                        for (int r = 0; r < searchRadii.Length; r++)
                        {
                            if (NavMesh.SamplePosition(scientist.transform.position, out NavMeshHit typedHit, searchRadii[r], filter))
                            {
                                scientist.transform.position = typedHit.position;
                                scientist.InvalidateNetworkCache();
                                scientist.SendNetworkUpdate();
                                foundMesh = true;
                                break;
                            }
                        }

                        if (!foundMesh)
                        {
                            PrintWarning($"CargoPlaneCrash: no NavMesh area matching this guard's own agent type (id {navigator.Agent.agentTypeID}) within {searchRadii[searchRadii.Length - 1]:F0}m of {scientist.transform.position} — this crash site appears to be in an area with no baked NavMesh nearby at all (map edge, cave, or a gap in the navmesh bake). Skipping AI init for this and the rest of this event's guards; consider rebaking NavMesh for this part of the map.");
                            navigator = null; // skip the Init below — nothing valid to init onto
                            guardNavMeshUnavailableForEvent = true;
                        }
                    }
                    else
                    {
                        // Agent itself is null — not "wrong agent type", there's no NavMeshAgent
                        // attached at all (seen on TerrainImpact/ShotDown crashes, which land
                        // wherever the plane actually was rather than a pre-verified site, so
                        // this spot may have no baked NavMesh anywhere nearby). Calling
                        // navigator.Init(scientist, null) below throws a NullReferenceException
                        // from inside Init itself — caught by the try/catch around this whole
                        // method, but only after every guard on the site silently fails to
                        // configure. Skip it the same way as the "wrong agent type" case instead.
                        PrintWarning($"CargoPlaneCrash: guard's BaseNavigator has no NavMeshAgent at {scientist.transform.position} (likely off any baked NavMesh) — skipping AI init, guard will not patrol.");
                        navigator = null;
                    }

                    if (navigator != null && navigator.CanUseNavMesh)
                        navigator.Init(scientist, navigator.Agent);
                }
                else
                {
                    PrintWarning("CargoPlaneCrash: guard has no BaseNavigator component — it may never move.");
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"CargoPlaneCrash: failed to configure guard AI, leaving default behavior ({ex.Message}).");
            }
        }

        // Gives guards a distinct, deliberately-chosen kit instead of relying entirely on the
        // prefab's own default/random loadout. Wrapped defensively — a failure here shouldn't
        // stop the guard from existing, just leave it with its default kit. Item shortnames
        // below weren't verified against your specific game version — if any fail to create,
        // a warning names exactly which one, so a wrong/outdated shortname is easy to spot and
        // swap out without breaking anything else in the kit.
        private void ApplyGuardLoadout(ScientistNPC scientist, bool isHeavy)
        {
            try
            {
                if (scientist.inventory == null) return;

                scientist.inventory.Strip();

                string weaponName = isHeavy ? "lmg.m249" : "rifle.ak";
                Item weapon = ItemManager.CreateByName(weaponName, 1);
                if (weapon != null)
                {
                    var ammoDef = weapon.GetHeldEntity() is BaseProjectile projectile ? projectile.primaryMagazine?.ammoType : null;
                    if (ammoDef != null)
                        weapon.contents?.AddItem(ammoDef, 128);
                    scientist.inventory.GiveItem(weapon, scientist.inventory.containerBelt);
                }
                else
                {
                    PrintWarning($"CargoPlaneCrash: failed to create guard weapon '{weaponName}' — shortname may be outdated for this game version.");
                }

                // Full clothing set instead of a single armor piece — heavies in full metal
                // armor, standards in a lighter civilian-ish outfit for visual variety between
                // the two guard types. metal.plate.torso/metal.facemask/hoodie/pants/shoes.boots
                // are all confirmed working from server logs; roadsign.kilt/roadsign.gloves
                // replace two shortnames ("metal.plate.pants", "leather.gloves") that don't
                // actually exist — Rust has no metal-tier leg armor, and gloves come from the
                // roadsign armor set rather than a standalone leather item.
                string[] wearItems = isHeavy
                    ? new[] { "metal.facemask", "metal.plate.torso", "roadsign.kilt", "shoes.boots" }
                    : new[] { "hoodie", "pants", "shoes.boots", "roadsign.gloves" };

                foreach (var itemName in wearItems)
                {
                    Item wearable = ItemManager.CreateByName(itemName, 1);
                    if (wearable != null)
                        scientist.inventory.GiveItem(wearable, scientist.inventory.containerWear);
                    else
                        PrintWarning($"CargoPlaneCrash: failed to create guard clothing item '{itemName}' — shortname may be outdated for this game version.");
                }

                if (isHeavy)
                {
                    Item medkit = ItemManager.CreateByName("largemedkit", 2);
                    if (medkit != null)
                        scientist.inventory.GiveItem(medkit, scientist.inventory.containerBelt);
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"CargoPlaneCrash: failed to apply a custom guard loadout, leaving default kit ({ex.Message}).");
            }
        }

        private void SpawnLockedCrateEntity(Vector3 center)
        {
            Vector3 pos = GroundPosition(center);
            var crateObj = GameManager.server.CreateEntity(LockedCratePrefab, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            if (crateObj == null)
            {
                PrintWarning($"CargoPlaneCrash: failed to create locked crate entity from prefab '{LockedCratePrefab}' — path may be outdated.");
                return;
            }

            crateObj.Spawn();
            activeCrates.Add(crateObj);

            if (config.LockedCrateHackSeconds > 0f)
            {
                var hackable = crateObj as HackableLockedCrate;
                if (hackable != null)
                    hackable.hackSeconds = config.LockedCrateHackSeconds;
            }
        }

        private void SpawnPatrolHeliFlyover(Vector3 center)
        {
            heliCircleCenter = center;
            heliOrbitHeight = GetGroundHeight(center) + config.PatrolHeliCircleAltitude;

            // Start the heli out at a distance and fly it in, rather than spawning it directly
            // in place already circling — mirrors the plane's flyover for a proper "arriving
            // at the scene" entrance instead of popping into existence mid-orbit.
            Vector3 approachDir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 spawnPos = center + approachDir * config.PatrolHeliApproachDistance;
            spawnPos.y = heliOrbitHeight;

            var heliObj = GameManager.server.CreateEntity(PatrolHeliPrefab, spawnPos, Quaternion.identity);
            if (heliObj == null)
            {
                PrintWarning($"CargoPlaneCrash: failed to create patrol helicopter entity from prefab '{PatrolHeliPrefab}' — path may be outdated. No heli flyover this event.");
                return;
            }

            heliObj.Spawn();
            activeHeli = heliObj as PatrolHelicopter;

            if (activeHeli == null)
            {
                PrintWarning($"CargoPlaneCrash: entity created from '{PatrolHeliPrefab}' was not a PatrolHelicopter. No heli flyover this event.");
                heliObj.Kill();
                return;
            }

            // Same fix already applied to the plane in StartPlaneFlyover, applied here too:
            // the vanilla PatrolHelicopter has its own autonomous behavior (it normally flies
            // its own patrol route and can reposition/reorient as part of spawning). Setting
            // `enabled = false` only stops its own Update()/FixedUpdate() from running from
            // this point on — it does nothing about anything that already happened
            // synchronously inside Spawn()/OnEntitySpawn(). Without this reset, heliPreviousPos
            // could get seeded from wherever vanilla logic left the transform rather than the
            // spawnPos we actually chose (a water-checked, distance-checked point), which can
            // manifest as the heli looking like it starts from the wrong place or immediately
            // behaves oddly.
            activeHeli.enabled = false;
            activeHeli.transform.position = spawnPos;
            activeHeli.transform.rotation = Quaternion.identity;
            activeHeli.InvalidateNetworkCache();
            activeHeli.SendNetworkUpdate();

            // Also previously missing versus the plane: without locking the Rigidbody to
            // kinematic and zeroing its velocities, gravity/collision response can keep acting
            // on the heli every physics step while this plugin is also hard-writing its
            // transform.position every tick in TickHeliApproach/TickHeliCircle/TickHeliRetreat.
            // A non-kinematic Rigidbody still participates in collision — if the flight path
            // clips terrain (a hill/ridge) or another collider, physics can effectively pin the
            // heli in place while our own state (heliAngleDegrees, etc.) keeps advancing
            // underneath it, which matches "flies fine, then gets stuck in a spot."
            var rb = activeHeli.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                PrintWarning("CargoPlaneCrash: patrol heli has no Rigidbody component — skipping the kinematic/velocity reset (this is informational, not necessarily a problem).");
            }

            heliApproaching = true;
            heliRetreating = false;
            heliPreviousPos = activeHeli.transform.position;
            heliDebugLogTimer = 0f;
            Subscribe("OnTick");

            if (config.HeliDebugLogging)
                Puts($"CargoPlaneCrash: [heli-debug] spawned at {spawnPos}, approaching target center {heliCircleCenter}, orbit height {heliOrbitHeight:F1}.");
        }

        // Enforces PatrolHeliMinTerrainClearance under the heli's current XZ position. Nothing
        // previously re-checked terrain height along the heli's flight path the way
        // TickPlaneFlight does for the plane — heliOrbitHeight is only ever computed once, from
        // the terrain height at the crash center, so a flight path that crosses higher ground
        // (a hill or ridge) on the way in/out was never accounted for. Returns the input
        // position with y raised just enough to clear the terrain beneath it, if needed.
        private Vector3 EnforceHeliTerrainClearance(Vector3 pos)
        {
            float terrainHeightHere = GetGroundHeight(pos);
            float minY = terrainHeightHere + config.PatrolHeliMinTerrainClearance;
            if (pos.y < minY)
                pos.y = minY;
            return pos;
        }

        // Diagnostic only, called once per OnTick while the heli is active and
        // config.HeliDebugLogging is on. Logs on a fixed real-world cadence rather than every
        // tick, and reports both the heli's actual transform position and the script's own
        // internal progress (heliAngleDegrees, distance to target) side by side — if the
        // position stops changing while heliAngleDegrees (during circling) keeps advancing,
        // that means something other than this plugin's own tick logic is holding the
        // transform in place (most likely the vanilla PatrolHelicopter AI or unresolved
        // physics/collision). If both stop together, the script's own loop has stalled instead
        // (check for an exception in the console around that time).
        private void LogHeliDebugState(float dt)
        {
            heliDebugLogTimer += dt;
            if (heliDebugLogTimer < config.HeliDebugLogIntervalSeconds) return;
            heliDebugLogTimer = 0f;

            if (activeHeli == null || activeHeli.IsDestroyed) return;

            Vector3 pos = activeHeli.transform.position;
            float terrainHeight = TerrainMeta.HeightMap.GetHeight(pos);
            string phase = heliApproaching ? "approach" : heliRetreating ? "retreat" : "circle";

            if (heliApproaching)
            {
                Vector3 approachTarget = new Vector3(heliCircleCenter.x, heliOrbitHeight, heliCircleCenter.z);
                float dist = Vector3.Distance(pos, approachTarget);
                Puts($"CargoPlaneCrash: [heli-debug] phase={phase} pos={pos} terrainY={terrainHeight:F1} clearance={(pos.y - terrainHeight):F1} distToTarget={dist:F1}");
            }
            else if (heliRetreating)
            {
                float distFromCenterXZ = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(heliCircleCenter.x, 0f, heliCircleCenter.z));
                Puts($"CargoPlaneCrash: [heli-debug] phase={phase} pos={pos} terrainY={terrainHeight:F1} clearance={(pos.y - terrainHeight):F1} distFromCenterXZ={distFromCenterXZ:F1} (retreat target {config.PatrolHeliRetreatDistance:F0})");
            }
            else
            {
                Puts($"CargoPlaneCrash: [heli-debug] phase={phase} pos={pos} terrainY={terrainHeight:F1} clearance={(pos.y - terrainHeight):F1} angle={heliAngleDegrees:F1}");
            }
        }

        // Called from OnTick while the heli is still flying in toward the crash site, before it
        // starts orbiting. Same continuous LookAt-and-advance approach as the plane's flight.
        private void TickHeliApproach(float dt, bool pushNetwork)
        {
            Vector3 approachTarget = new Vector3(heliCircleCenter.x, heliOrbitHeight, heliCircleCenter.z);

            activeHeli.transform.LookAt(approachTarget);
            Vector3 pos = activeHeli.transform.position + activeHeli.transform.forward * (config.PatrolHeliApproachSpeed * dt);

            // See EnforceHeliTerrainClearance — without this, a flight path over rising ground
            // could drive the heli's Rigidbody into terrain (it isn't kinematic-only immune to
            // collision just because we're writing its transform; collision response can still
            // resist/hold the actual physics body), which is a plausible cause of "flies fine,
            // then gets stuck."
            pos = EnforceHeliTerrainClearance(pos);

            activeHeli.transform.position = pos;
            heliPreviousPos = pos;

            if (pushNetwork)
            {
                activeHeli.InvalidateNetworkCache();
                activeHeli.SendNetworkUpdate();
            }

            // Transition to orbit once within the circle radius of the center, rather than
            // waiting to reach the exact center point, so it peels into its circling pattern
            // smoothly instead of flying straight through the middle first.
            float distanceToCenterXZ = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(heliCircleCenter.x, 0f, heliCircleCenter.z));
            if (distanceToCenterXZ <= config.PatrolHeliCircleRadius)
            {
                heliApproaching = false;

                // Initialize the orbit angle from the heli's current bearing off the center, so
                // it continues into the circle smoothly instead of snapping to a random start
                // angle. Matches the same angle convention TickHeliCircle uses to place points
                // (Quaternion.Euler(0, angle, 0) * Vector3.forward), just inverted.
                Vector3 fromCenter = pos - heliCircleCenter;
                fromCenter.y = 0f;
                heliAngleDegrees = Mathf.Atan2(fromCenter.x, fromCenter.z) * Mathf.Rad2Deg;
                if (heliAngleDegrees < 0f) heliAngleDegrees += 360f;

                if (config.HeliDebugLogging)
                    Puts($"CargoPlaneCrash: [heli-debug] approach complete at {pos}, entering circle at angle {heliAngleDegrees:F1}.");

                // Retire timer starts here rather than at spawn, so PatrolHeliRetireAfterSeconds
                // measures actual orbit time and isn't eaten into by however long the approach
                // flight took.
                heliRetireTimer?.Destroy();
                heliRetireTimer = timer.Once(config.PatrolHeliRetireAfterSeconds, () =>
                {
                    if (activeHeli == null || activeHeli.IsDestroyed)
                        return;

                    // Fly it away instead of deleting it in place. It keeps flying outward
                    // along whatever bearing it was already on around the circle, so it reads
                    // as "breaking off and leaving" rather than snapping onto a new heading —
                    // and if a team is currently engaged with it, they get to keep fighting it
                    // as it flies off instead of it vanishing mid-fight.
                    Vector3 fromCenter2 = activeHeli.transform.position - heliCircleCenter;
                    fromCenter2.y = 0f;
                    heliRetreatDirection = fromCenter2.sqrMagnitude > 0.01f
                        ? fromCenter2.normalized
                        : activeHeli.transform.forward;
                    heliRetreating = true;
                });
            }
        }

        // Called from OnTick every frame the heli is orbiting.
        private void TickHeliCircle(float dt, bool pushNetwork)
        {
            heliAngleDegrees += config.PatrolHeliCircleDegreesPerSecond * dt;
            if (heliAngleDegrees >= 360f) heliAngleDegrees -= 360f;

            Vector3 offset = Quaternion.Euler(0f, heliAngleDegrees, 0f) * (Vector3.forward * config.PatrolHeliCircleRadius);
            Vector3 pos = heliCircleCenter + offset;
            pos.y = heliOrbitHeight;

            // Same terrain-clearance guard as the approach phase — the orbit ring can cross
            // uneven ground even though heliOrbitHeight was computed from the center point
            // alone.
            pos = EnforceHeliTerrainClearance(pos);

            Vector3 moveDir = pos - heliPreviousPos;
            if (moveDir.sqrMagnitude > 0.0001f)
                activeHeli.transform.rotation = Quaternion.LookRotation(moveDir.normalized);
            activeHeli.transform.position = pos;
            heliPreviousPos = pos;

            if (pushNetwork)
            {
                activeHeli.InvalidateNetworkCache();
                activeHeli.SendNetworkUpdate();
            }
        }

        // Called from OnTick once the retire timer has fired — flies the heli straight out
        // along heliRetreatDirection (fixed when retreat started) until it's far enough from
        // the crash site to vanish believably, then actually deletes it. This is what replaces
        // the old "insta-kill on a timer" behavior.
        private void TickHeliRetreat(float dt, bool pushNetwork)
        {
            Vector3 pos = activeHeli.transform.position + heliRetreatDirection * (config.PatrolHeliRetreatSpeed * dt);

            pos = EnforceHeliTerrainClearance(pos);

            Vector3 moveDir = pos - heliPreviousPos;
            if (moveDir.sqrMagnitude > 0.0001f)
                activeHeli.transform.rotation = Quaternion.LookRotation(moveDir.normalized);
            activeHeli.transform.position = pos;
            heliPreviousPos = pos;

            if (pushNetwork)
            {
                activeHeli.InvalidateNetworkCache();
                activeHeli.SendNetworkUpdate();
            }

            float distanceFromCenterXZ = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(heliCircleCenter.x, 0f, heliCircleCenter.z));
            if (distanceFromCenterXZ >= config.PatrolHeliRetreatDistance)
            {
                if (!activeHeli.IsDestroyed)
                    activeHeli.Kill();
                activeHeli = null;
                heliRetreating = false;
            }
        }

        private void SpawnMapMarker(Vector3 center)
        {
            var markerObj = GameManager.server.CreateEntity(MapMarkerPrefab, center);
            if (markerObj == null)
            {
                PrintWarning($"Failed to create map marker from prefab '{MapMarkerPrefab}' — path may be outdated.");
                return;
            }

            eventMarker = markerObj as MapMarkerGenericRadius;
            if (eventMarker == null)
            {
                markerObj.Kill();
                return;
            }

            eventMarker.alpha = config.MapMarkerAlpha;
            eventMarker.color1 = new Color(0.8f, 0.1f, 0.1f);
            eventMarker.color2 = new Color(0.8f, 0.1f, 0.1f);
            eventMarker.radius = config.MapMarkerRadius;
            eventMarker.Spawn();
            eventMarker.SendUpdate();
        }

        // crashfire.prefab (the CH47 wreckage fire/smoke asset) can't actually be spawned this
        // way — the server log confirmed it requires an asset scene ('AssetScene-props.other')
        // that's only loaded as part of a CH47 actually dying in the world; there's no
        // reliable way to force-preload that from a plugin. Using EngineFireEffectPrefab
        // instead — already confirmed to work for the plane's own engine fire — for the
        // persistent flame, and adding lingering smoke via a repeating one-shot effect (see
        // StartCrashSmokeLoop) instead of a second entity.
        private void SpawnCrashFire(Vector3 pos, float scale)
        {
            var fireObj = GameManager.server.CreateEntity(EngineFireEffectPrefab, pos);
            if (fireObj == null)
            {
                PrintWarning($"CargoPlaneCrash: failed to create crash fire entity from prefab '{EngineFireEffectPrefab}' — path may be outdated. No lingering fire at {pos}.");
            }
            else
            {
                fireObj.Spawn();
                fireObj.transform.localScale = Vector3.one * scale;
                crashFireEntities.Add(fireObj);
            }

            crashSmokePositions.Add(pos);
        }

        // Keeps re-triggering the smoke effect at every position in crashSmokePositions
        // (the main impact point plus each scattered wreckage fire) for as long as the event
        // is alive, so the ground fires read as lingering, smoking wreckage rather than silent
        // flames. Stopped and cleared in CleanupEvent.
        private void StartCrashSmokeLoop()
        {
            crashSmokeLoopTimer?.Destroy();

            int totalTicks = Mathf.Max(1, Mathf.CeilToInt((config.DespawnMinutes * 60f) / config.GroundSmokeInterval));
            crashSmokeLoopTimer = timer.Repeat(config.GroundSmokeInterval, totalTicks, () =>
            {
                for (int i = 0; i < crashSmokePositions.Count; i++)
                    Effect.server.Run(PlaneSmokeTrailPrefab, crashSmokePositions[i]);
            });
        }

        private void DistributeGuards(List<Vector3> cratePositions, Vector3 center, string prefab, int totalCount)
        {
            if (cratePositions.Count == 0) return;

            for (int i = 0; i < totalCount; i++)
            {
                Vector3 cratePos = cratePositions[i % cratePositions.Count];
                SpawnGuard(cratePos, center, prefab);
            }
        }

        // Called on a repeating timer (started in SpawnCrashSite, stopped in CleanupEvent)
        // while guards are alive. Does two things each tick, in order:
        // 1. A hard leash check (runs regardless of what the guard is doing — including mid-
        //    chase/combat) that forcibly pulls any guard back toward its spawn point if it's
        //    wandered past GuardLeashDistance. This is the actual guarantee against runaway
        //    chase distance, independent of whatever the brain's own internal range math does.
        // 2. For guards currently idle (not already moving), a periodic chance to wander a
        //    short distance from their spawn point and back — using the game's own
        //    BaseNavigator/NavMesh movement, and only ever issued while idle so it shouldn't
        //    interrupt genuine combat responses.
        private void TickGuardPatrol()
        {
            for (int i = 0; i < activeGuards.Count; i++)
            {
                var guard = activeGuards[i];
                if (guard == null || guard.IsDestroyed) continue;
                if (!guardHomePositions.TryGetValue(guard, out Vector3 home)) continue;

                var navigator = guard.GetComponent<BaseNavigator>();
                if (navigator == null) continue;

                float distanceFromHome = Vector3.Distance(guard.transform.position, home);
                if (distanceFromHome > config.GuardLeashDistance)
                {
                    var brain = guard.GetComponent<ScientistBrain>();
                    if (brain != null)
                        brain.SwitchToState(AIState.Idle, 0);

                    navigator.SetDestination(home, BaseNavigator.NavigationSpeed.Fast, 0f, 0f);
                    continue; // don't also roll a patrol move this tick — it's already being pulled back
                }

                if (navigator.Moving) continue; // already doing something — don't interrupt

                if (UnityEngine.Random.Range(0, 100) >= config.GuardPatrolChancePercent) continue;

                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * config.GuardPatrolRadius;
                Vector3 candidate = GroundPosition(home + new Vector3(randomCircle.x, 0f, randomCircle.y));

                if (IsUnderwater(candidate)) continue;

                if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
                {
                    navigator.SetDestination(navHit.position, BaseNavigator.NavigationSpeed.Slow, 0f, 0f);
                }
            }
        }

        private void StartFireLoop()
        {
            fireLoopTimer?.Destroy();

            int totalTicks = Mathf.Max(1, Mathf.CeilToInt(config.FireDurationSeconds / config.FireTickInterval));
            int visualEveryNTicks = Mathf.Max(1, Mathf.RoundToInt(6f / config.FireTickInterval));
            int tickIndex = 0;

            fireLoopTimer = timer.Repeat(config.FireTickInterval, totalTicks, () =>
            {
                tickIndex++;
                bool showVisual = tickIndex % visualEveryNTicks == 0;

                for (int c = 0; c < activeCratePositions.Count; c++)
                {
                    if (showVisual)
                        Effect.server.Run(FireEffect, activeCratePositions[c] + Vector3.up * 0.5f);
                }

                var players = BasePlayer.activePlayerList;
                for (int i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player == null || !player.IsAlive() || player.IsSleeping()) continue;

                    for (int c = 0; c < activeCratePositions.Count; c++)
                    {
                        if (Vector3.Distance(player.transform.position, activeCratePositions[c]) <= config.FireRadius)
                        {
                            player.Hurt(config.FireDamagePerTick, Rust.DamageType.Heat, null, false);
                            break;
                        }
                    }
                }

                if (tickIndex >= totalTicks)
                    fireLoopTimer = null;
            });
        }

        // TerrainMeta.HeightMap only describes the smooth base terrain mesh — it has no idea
        // that a rock formation, cliff face, or other static prop is sitting on top of a given
        // point. Every "how high is the ground here" question in this plugin used to go
        // straight to the heightmap, which is exactly why a plane flying low over rocky ground
        // (or a crate/guard spawn point picked near one) could end up placed well below the
        // rock's actual visible surface — i.e. embedded inside solid rock, invisible and
        // unreachable, which reads as "it never spawned." Raycasting straight down from well
        // above the map catches whatever's really there — heightmap terrain OR a rock/cliff on
        // top of it — and falls back to the heightmap only if the raycast finds nothing at all
        // (e.g. off the edge of the playable map, where colliders may not exist).
        private static readonly int GroundRaycastMask = LayerMask.GetMask("Terrain", "World", "Default", "Construction");

        private float GetGroundHeight(Vector3 pos)
        {
            float rayStartHeight = TerrainMeta.HighestPoint.y + 250f;
            Vector3 rayStart = new Vector3(pos.x, rayStartHeight, pos.z);

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayStartHeight + 250f, GroundRaycastMask))
                return hit.point.y;

            return TerrainMeta.HeightMap.GetHeight(pos);
        }

        private Vector3 GroundPosition(Vector3 pos)
        {
            pos.y = GetGroundHeight(pos);
            return pos;
        }

        private string PickCratePrefab()
        {
            switch (config.CrateMode?.ToLower())
            {
                case "military":
                    return MilitaryCratePrefab;
                case "airdrop":
                    return AirdropCratePrefab;
                case "mixed":
                default:
                    return UnityEngine.Random.value < 0.5f ? MilitaryCratePrefab : AirdropCratePrefab;
            }
        }

        private string PositionToGrid(Vector3 pos)
        {
            float worldSize = TerrainMeta.Size.x;
            int gridSize = 150;
            int x = Mathf.FloorToInt((pos.x + worldSize / 2f) / gridSize);
            int z = Mathf.FloorToInt((worldSize / 2f - pos.z) / gridSize);
            char letter = (char)('A' + x % 26);
            return $"{letter}{z}";
        }

        #endregion

        #region Cleanup

        private void CleanupEvent()
        {
            if (planeFireChildEntity != null && !planeFireChildEntity.IsDestroyed)
                planeFireChildEntity.Kill();
            planeFireChildEntity = null;
            if (activePlane != null && !activePlane.IsDestroyed)
                activePlane.Kill();
            activePlane = null;
            trackedRockets.Clear();
            planeHitsTaken = 0;

            heliArrivalTimer?.Destroy();
            heliArrivalTimer = null;

            foreach (var crate in activeCrates)
            {
                if (crate != null && !crate.IsDestroyed)
                    crate.Kill();
            }
            activeCrates.Clear();

            foreach (var guard in activeGuards)
            {
                if (guard != null && !guard.IsDestroyed)
                    guard.Kill();
            }
            activeGuards.Clear();
            guardHomePositions.Clear();

            guardPatrolTimer?.Destroy();
            guardPatrolTimer = null;

            fireLoopTimer?.Destroy();
            fireLoopTimer = null;
            activeCratePositions.Clear();

            foreach (var fire in crashFireEntities)
            {
                if (fire != null && !fire.IsDestroyed)
                    fire.Kill();
            }
            crashFireEntities.Clear();

            crashSmokeLoopTimer?.Destroy();
            crashSmokeLoopTimer = null;
            crashSmokePositions.Clear();

            heliRetireTimer?.Destroy();
            heliRetireTimer = null;
            if (activeHeli != null && !activeHeli.IsDestroyed)
                activeHeli.Kill();
            activeHeli = null;
            heliApproaching = false;
            heliRetreating = false;

            if (eventMarker != null && !eventMarker.IsDestroyed)
                eventMarker.Kill();
            eventMarker = null;

            eventActive = false;
            despawnTimer?.Destroy();
        }

        #endregion
    }

    internal static class CargoPlaneCrashExtensions
    {
        public static Vector3 ToVector3Vertical(this Vector2 v)
        {
            return new Vector3(v.x, 0f, v.y);
        }
    }
}