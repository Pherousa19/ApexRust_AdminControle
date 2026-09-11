using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    // Replaces the standalone polling script apex-rust-store's DEPLOY.md describes
    // as the AGENT_SECRET fallback (used when a host won't expose RCON publicly).
    // Being a plugin rather than a separate process means it loads automatically
    // every time the server starts - nothing to notice has silently stopped
    // running, no separate deploy step, no surviving-a-reboot problem.
    //
    // Three jobs, each independent of the others:
    //   1. Poll the store's delivery queue and run whatever commands are pending
    //      (store purchases, and manual Player Actions pushes from the web panel).
    //   2. Report live player count / map on a timer, powering the storefront's
    //      status widget (see DEPLOY.md "Polling agent: reporting live status").
    //   3. Push the current case catalog + online player list so the web panel's
    //      Player Actions dropdowns have something to show, since it can't reach
    //      RCON directly in this mode either.
    [Info("Apex Agent", "Noobless Gaming", "1.0.0")]
    [Description("Polling-agent replacement for apex-rust-store's AGENT_SECRET delivery mode - runs delivery queue polling, status reporting, and dropdown data push as a plugin instead of a standalone script.")]
    public class ApexAgent : RustPlugin
    {
        [PluginReference] private Plugin Cases;
        [PluginReference] private Plugin PointShop;
        [PluginReference] private Plugin RustRankings;
        [PluginReference] private Plugin Kits;
        [PluginReference("ApexAdminAudit")] private Plugin ApexAudit;

        private Timer deliveryPollTimer;
        private Timer deliveryWatchdog;
        private bool deliveryPollInFlight;

        private Timer reportTimer;
        private Timer queryPollTimer;
        private bool queryPollInFlight;

        #region Oxide Hooks

        private void OnServerInitialized()
        {
            if (string.IsNullOrEmpty(Configuration.WorkerUrl) || string.IsNullOrEmpty(Configuration.AgentSecret))
            {
                PrintWarning("Worker URL / Agent Secret not configured - edit oxide/config/ApexAgent.json and reload. This plugin does nothing until then.");
                return;
            }

            deliveryPollTimer = timer.Every(Mathf.Max(5f, Configuration.DeliveryPollInterval), PollDeliveryQueue);
            reportTimer = timer.Every(Mathf.Max(15f, Configuration.ReportInterval), ReportTick);
            queryPollTimer = timer.Every(Mathf.Max(3f, Configuration.QueryPollInterval), PollAgentQueries);

            // Run once immediately on load rather than waiting for the first
            // interval to elapse, so a plugin reload doesn't leave the panel
            // showing stale data for up to a minute.
            timer.Once(5f, PollDeliveryQueue);
            timer.Once(5f, ReportTick);
            timer.Once(5f, PollAgentQueries);

            // The item catalog doesn't change at runtime, unlike cases/online
            // players - push it once now rather than on the recurring report
            // timer. A plugin reload re-pushing it is harmless (small, rare).
            timer.Once(6f, PushItemCatalog);
            timer.Once(7f, PushKitCatalog);
        }

        private void Unload()
        {
            deliveryPollTimer?.Destroy();
            deliveryWatchdog?.Destroy();
            reportTimer?.Destroy();
            queryPollTimer?.Destroy();
        }

        #endregion

        #region Delivery Queue Polling

        // Identical shape to Kits.cs's own PollDeliveryQueue/AcknowledgeDeliveryJobs -
        // kept consistent so anyone familiar with one recognises the other. The
        // difference is scope: this plugin polls once for ALL pending commands
        // across every plugin (Cases, PointShop, kits, subscriptions, manual
        // Player Actions pushes...), rather than each plugin polling separately.
        private void PollDeliveryQueue()
        {
            if (deliveryPollInFlight)
                return;

            deliveryPollInFlight = true;
            string url = Configuration.WorkerUrl.TrimEnd('/') + "/api/delivery/pending";
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + Configuration.AgentSecret,
                ["Accept"] = "application/json"
            };

            deliveryWatchdog?.Destroy();
            deliveryWatchdog = timer.Once(20f, () =>
            {
                if (!deliveryPollInFlight)
                    return;

                deliveryPollInFlight = false;
                PrintWarning("Delivery poll timed out locally after 20 seconds; polling has been reset.");
            });

            webrequest.Enqueue(url, null, (code, response) =>
            {
                try
                {
                    deliveryWatchdog?.Destroy();
                    deliveryWatchdog = null;

                    if (code < 200 || code >= 300)
                    {
                        PrintWarning($"Delivery poll failed: HTTP {code} {response}");
                        return;
                    }

                    var payload = JsonConvert.DeserializeObject<PendingResponse>(response ?? "{}");
                    if (payload?.jobs == null || payload.jobs.Count == 0)
                        return;

                    var acknowledged = new List<int>();

                    foreach (var job in payload.jobs)
                    {
                        if (job.id <= 0 || string.IsNullOrEmpty(job.command))
                            continue;

                        try
                        {
                            ConsoleSystem.Run(ConsoleSystem.Option.Server, job.command);
                            acknowledged.Add(job.id);
                            Puts($"Delivery command executed (job {job.id}): {job.command}");
                        }
                        catch (Exception ex)
                        {
                            PrintWarning($"Delivery command failed (job {job.id}): {ex.Message}");
                        }
                    }

                    if (acknowledged.Count > 0)
                        AcknowledgeDeliveryJobs(acknowledged);
                }
                catch (Exception ex)
                {
                    PrintWarning("Delivery poll response could not be processed: " + ex.Message);
                }
                finally
                {
                    deliveryPollInFlight = false;
                }
            }, this, RequestMethod.GET, headers, 15f);
        }

        private void AcknowledgeDeliveryJobs(List<int> ids)
        {
            string url = Configuration.WorkerUrl.TrimEnd('/') + "/api/delivery/ack";
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + Configuration.AgentSecret,
                ["Content-Type"] = "application/json"
            };
            string json = JsonConvert.SerializeObject(new { ids });

            webrequest.Enqueue(url, json, (code, response) =>
            {
                if (code < 200 || code >= 300)
                    PrintWarning($"Delivery acknowledgement failed: HTTP {code} {response}");
            }, this, RequestMethod.POST, headers, 15f);
        }

        private class PendingResponse
        {
            public List<PendingJob> jobs = new List<PendingJob>();
        }

        private class PendingJob
        {
            public int id;
            public string command;
        }

        #endregion

        #region Status + State Reporting

        private void ReportTick()
        {
            ReportServerStatus();
            PushAgentState();
            ReportTelemetry();
        }

        // Matches DEPLOY.md's "Polling agent: reporting live status" contract
        // exactly - see /api/agent/status in the Worker for the receiving end.
        private void ReportServerStatus()
        {
            var payload = new Dictionary<string, object>
            {
                ["players"] = BasePlayer.activePlayerList.Count,
                ["maxPlayers"] = ConVar.Server.maxplayers,
                ["queued"] = ServerMgr.Instance != null ? ServerMgr.Instance.connectionQueue.queue.Count : 0,
                ["hostname"] = ConVar.Server.hostname,
                ["map"] = ConVar.Server.level,
                ["seed"] = ConVar.Server.seed,
                ["size"] = ConVar.Server.worldsize,
            };

            string url = Configuration.WorkerUrl.TrimEnd('/') + "/api/agent/status";
            webrequest.Enqueue(
                url,
                JsonConvert.SerializeObject(payload),
                (code, response) =>
                {
                    if (code != 200)
                        PrintWarning($"Status report failed: {code} {response}");
                },
                this,
                RequestMethod.POST,
                new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {Configuration.AgentSecret}",
                    ["Content-Type"] = "application/json"
                },
                15f
            );
        }

        // Feeds /admin/actions' "Give a Case" and "one player" dropdowns on the
        // web panel, which otherwise have no way to see this data in AGENT_SECRET
        // mode (the Worker can't reach RCON, and there's no RCON on this end
        // either to bypass - so it has to be pushed, same as status above).
        private void PushAgentState()
        {
            var onlinePlayers = BasePlayer.activePlayerList
                .Where(p => p != null && !p.IsNpc)
                .Select(p => new Dictionary<string, object>
                {
                    ["steamid"] = p.UserIDString,
                    ["name"] = p.displayName
                })
                .ToList();

            // Cases is optional - if it's not loaded, just report an empty
            // catalog rather than failing the whole state push over it.
            List<Dictionary<string, object>> cases = null;
            try
            {
                cases = Cases?.Call<List<Dictionary<string, object>>>("Cases_GetCaseCatalog");
            }
            catch (Exception ex)
            {
                PrintWarning("Cases_GetCaseCatalog call failed: " + ex.Message);
            }

            var payload = new Dictionary<string, object>
            {
                ["onlinePlayers"] = onlinePlayers,
                ["cases"] = cases ?? new List<Dictionary<string, object>>()
            };

            string url = Configuration.WorkerUrl.TrimEnd('/') + "/api/agent/state";
            webrequest.Enqueue(
                url,
                JsonConvert.SerializeObject(payload),
                (code, response) =>
                {
                    if (code != 200)
                        PrintWarning($"State push failed: {code} {response}");
                },
                this,
                RequestMethod.POST,
                new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {Configuration.AgentSecret}",
                    ["Content-Type"] = "application/json"
                },
                15f
            );
        }

        // Feeds the web panel's Server Console telemetry cards (FPS, entity
        // count, memory, uptime) and the Plugins registry, since neither is
        // reachable over RCON in this mode. Posts to the shared Apex Control
        // ingestion endpoint (see APEX_CONTROL_V4.md) rather than a bespoke
        // one, so this and any future plugin telemetry share one contract.
        // Every individual reading is wrapped so one unavailable API (e.g. an
        // older Rust build without a field this uses) drops just that one
        // number instead of breaking the whole report.
        private void ReportTelemetry()
        {
            float framerate = 0f;
            try { framerate = Performance.current.frameRate; } catch { }

            int entityCount = 0;
            try { entityCount = BaseNetworkable.serverEntities.Count; } catch { }

            long uptimeSeconds = 0;
            try { uptimeSeconds = (long)UnityEngine.Time.realtimeSinceStartup; } catch { }

            double? memoryPercent = null;
            try
            {
                double usedMb = Performance.current.memoryUsageSystem / 1024.0 / 1024.0;
                double totalMb = UnityEngine.SystemInfo.systemMemorySize;
                if (totalMb > 0) memoryPercent = Math.Round(usedMb / totalMb * 100.0, 1);
            }
            catch { }

            var metrics = new Dictionary<string, object>
            {
                ["players"] = BasePlayer.activePlayerList.Count,
                ["maxPlayers"] = ConVar.Server.maxplayers,
                ["queued"] = ServerMgr.Instance != null ? ServerMgr.Instance.connectionQueue.queue.Count : 0,
                ["entityCount"] = entityCount,
                ["uptimeSeconds"] = uptimeSeconds,
            };
            if (framerate > 0) metrics["framerate"] = Math.Round(framerate, 1);
            if (memoryPercent.HasValue) metrics["memoryPercent"] = memoryPercent.Value;

            var plugins = new List<Dictionary<string, object>>();
            try
            {
                foreach (var p in Interface.Oxide.RootPluginManager.GetPlugins())
                {
                    if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                    plugins.Add(new Dictionary<string, object>
                    {
                        ["name"] = p.Name,
                        ["version"] = p.Version.ToString(),
                        ["status"] = "online",
                        ["enabled"] = true,
                    });
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Plugin enumeration failed: " + ex.Message);
            }

            var payload = new Dictionary<string, object>
            {
                ["serverKey"] = "primary",
                ["hostname"] = ConVar.Server.hostname,
                ["map"] = ConVar.Server.level,
                ["plugins"] = plugins,
                ["metrics"] = metrics,
            };

            string url = Configuration.WorkerUrl.TrimEnd('/') + "/api/agent/telemetry";
            webrequest.Enqueue(
                url,
                JsonConvert.SerializeObject(payload),
                (code, response) =>
                {
                    if (code != 200)
                        PrintWarning($"Telemetry report failed: {code} {response}");
                },
                this,
                RequestMethod.POST,
                new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {Configuration.AgentSecret}",
                    ["Content-Type"] = "application/json"
                },
                15f
            );
        }

        #endregion

        #region Player-Card Query Polling

        // Answers on-demand player-card lookups from /admin/players - the
        // Worker drops a row in agent_queries since it has no RCON connection
        // to ask any of these plugins directly; this picks it up, resolves the
        // player, calls each plugin's hook in-process (no console-command/RCON
        // round trip needed at all, since everything here runs in the same
        // Rust process), and posts the combined result back. Polled more
        // frequently than delivery/status since a human is actively waiting
        // on the other end of this one.
        private void PollAgentQueries()
        {
            if (queryPollInFlight)
                return;

            queryPollInFlight = true;
            string url = Configuration.WorkerUrl.TrimEnd('/') + "/api/agent/queries/pending";
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + Configuration.AgentSecret,
                ["Accept"] = "application/json"
            };

            webrequest.Enqueue(url, null, (code, response) =>
            {
                try
                {
                    if (code < 200 || code >= 300)
                    {
                        if (code != 0) PrintWarning($"Query poll failed: HTTP {code} {response}");
                        return;
                    }

                    var payload = JsonConvert.DeserializeObject<PendingQueriesResponse>(response ?? "{}");
                    if (payload?.queries == null)
                        return;

                    foreach (var q in payload.queries)
                    {
                        if (q.id <= 0)
                            continue;

                        try
                        {
                            Dictionary<string, object> result = q.query_type == "player_lookup"
                                ? BuildPlayerLookupResult(q.target)
                                : new Dictionary<string, object> { ["error"] = $"unknown query_type '{q.query_type}'" };

                            CompleteAgentQuery(q.id, result);
                        }
                        catch (Exception ex)
                        {
                            PrintWarning($"Query {q.id} failed: {ex.Message}");
                            CompleteAgentQuery(q.id, new Dictionary<string, object> { ["error"] = ex.Message });
                        }
                    }
                }
                catch (Exception ex)
                {
                    PrintWarning("Query poll response could not be processed: " + ex.Message);
                }
                finally
                {
                    queryPollInFlight = false;
                }
            }, this, RequestMethod.GET, headers, 15f);
        }

        // Resolves name-or-SteamID against who's currently online, same
        // fallback as every other admin lookup in this system: if nobody
        // online matches, fall back to treating the query as a literal
        // SteamID64 so offline players can still be looked up by ID.
        private string ResolveSteamId(string query)
        {
            var online = BasePlayer.activePlayerList.FirstOrDefault(p =>
                p != null && (
                    p.UserIDString.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                    p.displayName.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                    p.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                ));

            return online != null ? online.UserIDString : query;
        }

        private Dictionary<string, object> BuildPlayerLookupResult(string query)
        {
            string steamid = ResolveSteamId(query);

            Dictionary<string, object> audit = null;
            try
            {
                var auditJson = ApexAudit?.Call<string>("ApexAudit_GetPlayerProfileJson", query);
                if (!string.IsNullOrEmpty(auditJson))
                {
                    audit = JsonConvert.DeserializeObject<Dictionary<string, object>>(auditJson);
                    // ApexAdminAudit does its own name-or-id resolution too and may
                    // land on a different (correct) SteamID than our simple online-only
                    // lookup above - prefer its answer when it found a profile.
                    if (audit != null && audit.TryGetValue("found", out var foundObj) && foundObj is bool found && found
                        && audit.TryGetValue("steamid", out var idObj) && idObj is string resolvedId)
                    {
                        steamid = resolvedId;
                    }
                }
            }
            catch (Exception ex)
            {
                PrintWarning("ApexAudit_GetPlayerProfileJson call failed: " + ex.Message);
            }

            object points = null;
            try
            {
                if (PointShop != null)
                {
                    double balance = PointShop.Call<double>("PointShop_GetBalance", ulong.Parse(steamid));
                    string currencyName = PointShop.Call<string>("PointShop_GetCurrencyName");
                    points = new Dictionary<string, object> { ["found"] = true, ["steamid"] = steamid, ["balance"] = balance, ["currencyName"] = currencyName };
                }
            }
            catch (Exception ex)
            {
                PrintWarning("PointShop balance lookup failed: " + ex.Message);
            }

            object cases = null;
            try
            {
                if (Cases != null)
                {
                    var owned = Cases.Call<List<Dictionary<string, object>>>("Cases_GetOwnedCases", ulong.Parse(steamid));
                    cases = new Dictionary<string, object> { ["found"] = true, ["steamid"] = steamid, ["owned"] = owned ?? new List<Dictionary<string, object>>() };
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Cases owned lookup failed: " + ex.Message);
            }

            object rankings = null;
            try
            {
                var rankingsJson = RustRankings?.Call<string>("RustRankings_GetStatsJson", steamid, false);
                if (!string.IsNullOrEmpty(rankingsJson))
                    rankings = JsonConvert.DeserializeObject<Dictionary<string, object>>(rankingsJson);
            }
            catch (Exception ex)
            {
                PrintWarning("RustRankings stats lookup failed: " + ex.Message);
            }

            return new Dictionary<string, object>
            {
                ["steamid"] = steamid,
                ["audit"] = audit,
                ["points"] = points,
                ["cases"] = cases,
                ["rankings"] = rankings
            };
        }

        private void CompleteAgentQuery(int id, Dictionary<string, object> result)
        {
            string url = Configuration.WorkerUrl.TrimEnd('/') + $"/api/agent/queries/{id}/complete";
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + Configuration.AgentSecret,
                ["Content-Type"] = "application/json"
            };
            string json = JsonConvert.SerializeObject(new { result });

            webrequest.Enqueue(url, json, (code, response) =>
            {
                if (code < 200 || code >= 300)
                    PrintWarning($"Query {id} completion post failed: {code} {response}");
            }, this, RequestMethod.POST, headers, 15f);
        }

        private class PendingQueriesResponse
        {
            public List<PendingQuery> queries = new List<PendingQuery>();
        }

        private class PendingQuery
        {
            public int id;
            public string query_type;
            public string target;
        }

        #endregion

        #region Item Catalog

        // One-time push (see OnServerInitialized) of every current Rust item's
        // shortname + display name, for the "Give Rust Item" dropdown on the
        // web panel. Excludes blueprintbase same as Kits.cs's own item picker
        // does, for the same reason - it's an internal base item, not
        // something anyone should be handing out.
        private void PushItemCatalog()
        {
            var items = ItemManager.itemList
                .Where(d => d != null && !d.shortname.Equals("blueprintbase"))
                .OrderBy(d => d.displayName.english)
                .Select(d => new Dictionary<string, object>
                {
                    ["shortname"] = d.shortname,
                    ["displayName"] = d.displayName.english
                })
                .ToList();

            var payload = new Dictionary<string, object> { ["items"] = items };

            string url = Configuration.WorkerUrl.TrimEnd('/') + "/api/agent/state";
            webrequest.Enqueue(
                url,
                JsonConvert.SerializeObject(payload),
                (code, response) =>
                {
                    if (code != 200)
                        PrintWarning($"Item catalog push failed: {code} {response}");
                    else
                        Puts($"Pushed item catalog ({items.Count} items).");
                },
                this,
                RequestMethod.POST,
                new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {Configuration.AgentSecret}",
                    ["Content-Type"] = "application/json"
                },
                20f
            );
        }

        // One-time push of every configured Kits.cs kit name, for the "Give
        // Kit" dropdown - same reasoning as PushItemCatalog above. Kits.cs
        // exposes this as a void hook that fills a passed-in list rather than
        // returning one, so that's called exactly as Kits.cs's own callers do.
        private void PushKitCatalog()
        {
            if (Kits == null) return;

            var kitNames = new List<string>();
            try
            {
                Kits.Call("GetKitNames", kitNames);
            }
            catch (Exception ex)
            {
                PrintWarning("GetKitNames call failed: " + ex.Message);
                return;
            }

            var payload = new Dictionary<string, object> { ["kits"] = kitNames.OrderBy(k => k).ToList() };

            string url = Configuration.WorkerUrl.TrimEnd('/') + "/api/agent/state";
            webrequest.Enqueue(
                url,
                JsonConvert.SerializeObject(payload),
                (code, response) =>
                {
                    if (code != 200)
                        PrintWarning($"Kit catalog push failed: {code} {response}");
                    else
                        Puts($"Pushed kit catalog ({kitNames.Count} kits).");
                },
                this,
                RequestMethod.POST,
                new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {Configuration.AgentSecret}",
                    ["Content-Type"] = "application/json"
                },
                20f
            );
        }

        #endregion

        #region Config

        private static ConfigData Configuration;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Worker URL (e.g. https://apex-rust-store.weaky19.workers.dev)")]
            public string WorkerUrl { get; set; } = string.Empty;

            [JsonProperty(PropertyName = "Agent Secret (must match the AGENT_SECRET set on the Worker via wrangler secret put)")]
            public string AgentSecret { get; set; } = string.Empty;

            [JsonProperty(PropertyName = "Delivery poll interval (seconds)")]
            public float DeliveryPollInterval { get; set; } = 15f;

            [JsonProperty(PropertyName = "Status/state report interval (seconds)")]
            public float ReportInterval { get; set; } = 60f;

            [JsonProperty(PropertyName = "Player-card query poll interval (seconds)")]
            public float QueryPollInterval { get; set; } = 5f;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Configuration = Config.ReadObject<ConfigData>();
            Config.WriteObject(Configuration, true);
        }

        protected override void LoadDefaultConfig() => Configuration = new ConfigData();

        protected override void SaveConfig() => Config.WriteObject(Configuration, true);

        #endregion
    }
}
