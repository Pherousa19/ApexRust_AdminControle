using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins;

[Info("ServerPanel Migrations", "Mevent", "2.0.25")]
public class ServerPanelMigrations : RustPlugin
{
    #region Fields

    [PluginReference] private Plugin ServerPanel = null;

    private Dictionary<string, IMigration> availableMigrations;

    private static ServerPanelMigrations Instance;

    #endregion

    #region Hooks

    private void Init()
    {
        Instance = this;

        InitializeMigrations();
    }

    private void Unload()
    {
        Instance = null;
    }

    #endregion

    #region Commands

    [ConsoleCommand("sp.migrations")]
    private void CmdConsoleMigrations(ConsoleSystem.Arg arg)
    {
        if (!arg.IsServerside) return;

        var command = arg.GetString(0);

        switch (command?.ToLower())
        {
            case "list":
                ListMigrations(arg);
                break;
            case "run":
                RunMigration(arg);
                break;
            default:
                ShowHelp(arg);
                break;
        }
    }

    private void ListMigrations(ConsoleSystem.Arg arg)
    {
        if (availableMigrations.Count == 0)
        {
            SendReply(arg, "No migrations available");
            return;
        }

        var sb = Pool.Get<StringBuilder>();
        try
        {
            sb.Append("Available migrations:");
            sb.AppendLine();

            foreach (var kvp in availableMigrations)
            {
                var migration = kvp.Value;

                sb.AppendLine($"{kvp.Key}: {migration.Name}");
                sb.AppendLine($"  Needed: {(migration.IsNeeded() ? "YES" : "NO")}");
                sb.AppendLine();
            }

            SendReply(arg, sb.ToString());
        }
        finally
        {
            Pool.FreeUnmanaged(ref sb);
        }
    }

    private void RunMigration(ConsoleSystem.Arg arg)
    {
        var migrationKey = arg.GetString(1);
        if (string.IsNullOrEmpty(migrationKey))
        {
            SendReply(arg, "Specify migration key. Use 'sp.migrations list' to see available migrations");
            return;
        }

        if (!availableMigrations.TryGetValue(migrationKey, out var migration))
        {
            SendReply(arg, $"Migration '{migrationKey}' not found");
            return;
        }

        var force = arg.GetString(2) == "force";

        SendReply(arg, $"Running migration: {migration.Name}");
        if (force)
            SendReply(arg, "WARNING: Forced execution!");

        timer.In(1f, () => ExecuteMigration(migration, force));
    }

    private void ShowHelp(ConsoleSystem.Arg arg)
    {
        var sb = Pool.Get<StringBuilder>();
        try
        {
            sb.Append("Usage: sp.migrations <command> [parameters]");
            sb.AppendLine();
            sb.AppendLine("Commands:");
            sb.AppendLine("  list - show all available migrations");
            sb.AppendLine("  run <key> [force] - execute migration");
            sb.AppendLine("  info - show system information");

            SendReply(arg, sb.ToString());
        }
        finally
        {
            Pool.FreeUnmanaged(ref sb);
        }
    }

    #region Migration Chain Execution

    private bool ExecuteMigrationChain(List<IMigration> migrations, int index)
    {
        if (index >= migrations.Count)
        {
            Puts("All migrations completed");
            ServerPanel?.Call("API_OnMigrationComplete");
            return true;
        }

        var migration = migrations[index];
        Puts($"Migration {index + 1}/{migrations.Count}: {migration.Name}");

        try
        {
            CreateBackup(migration.Name);
            if (!migration.Execute())
            {
                PrintError($"Migration {migration.Name} failed");
                return false;
            }
            Puts($"Migration {migration.Name} completed");

            UpdateConfigVersion(migration.ToVersion);
            timer.In(0.5f, () => ExecuteMigrationChain(migrations, index + 1));
            return true;
        }
        catch (Exception ex)
        {
            var errorMessage = $"Migration {migration.Name} failed: {ex.Message}";
            var errorDetails = $"Exception Type: {ex.GetType().Name}\nStack Trace: {ex.StackTrace}";
            
            if (ex.InnerException != null)
            {
                errorDetails += $"\nInner Exception: {ex.InnerException.Message}";
            }
            
            PrintError(errorMessage);
            PrintError($"Details: {errorDetails}");
            PrintError("Run: sp.migrations run all");
            
            ServerPanel?.Call("API_OnMigrationFailed", errorMessage, errorDetails);
            return false;
        }
    }

    #endregion Migration Chain Execution

    private void API_StartAutomaticMigration(string migrationName = "all", VersionNumber version = default)
    {
        if (string.IsNullOrEmpty(migrationName))
            migrationName = "all";
        
        var migrationNameLower = migrationName.ToLower();
        if (migrationNameLower == "all" || migrationNameLower == "run-all")
        {
            RunAllRequiredMigrationsAutomatic(version);
            return;
        }
        
        if (!availableMigrations.TryGetValue(migrationName, out var migration))
        {
            var errorMsg = $"Migration '{migrationName}' not found";
            PrintError($"{errorMsg} for automatic execution.");
            ServerPanel?.Call("API_OnMigrationFailed", errorMsg, 
                "The specified migration does not exist in the available migrations list.");
            return;
        }
        
        Puts($"Starting automatic migration: {migration.Name}");
        timer.In(0.5f, () => ExecuteMigration(migration, false));
    }

    private void RunAllRequiredMigrationsAutomatic(VersionNumber version = default)
    {
        var currentVersion = GetCurrentConfigVersion();
        var targetVersion = version == default ? GetTargetVersion() : version;
        
        if (targetVersion == default)
        {
            var errorMsg = "Cannot determine target version for automatic migration.";
            PrintError(errorMsg);
            ServerPanel?.Call("API_OnMigrationFailed", errorMsg, 
                "ServerPanel plugin is not loaded or version information is unavailable.");
            return;
        }
        
        if (currentVersion >= targetVersion)
        {
            Puts("No migrations required. Configuration is up to date.");
            ServerPanel?.Call("API_OnMigrationComplete");
            return;
        }
        
        #region Handle legacy versions (2.0.25 or missing version)
        List<IMigration> requiredMigrations;
        
        if (currentVersion == default)
        {
            var legacyMigrations = new List<IMigration>();
            foreach (var migration in availableMigrations.Values)
            {
                if (migration.IsNeeded())
                {
                    legacyMigrations.Add(migration);
                }
            }
            
            if (legacyMigrations.Count == 0)
            {
                var errorMsg = "Cannot determine starting migration for legacy version.";
                PrintError(errorMsg);
                ServerPanel?.Call("API_OnMigrationFailed", errorMsg, 
                    "No migration found that can handle the current configuration version.");
                return;
            }
            
            legacyMigrations.Sort((a, b) =>
            {
                if (a.FromVersion < b.FromVersion) return -1;
                if (a.FromVersion > b.FromVersion) return 1;
                if (a.ToVersion < b.ToVersion) return -1;
                if (a.ToVersion > b.ToVersion) return 1;
                return 0;
            });
            
            var firstMigration = legacyMigrations[0];
            requiredMigrations = new List<IMigration> { firstMigration };
            currentVersion = firstMigration.ToVersion;
            Puts($"Legacy version detected. Starting from migration: {firstMigration.Name}");
            
            var additionalMigrations = FindRequiredMigrations(currentVersion, targetVersion);
            if (additionalMigrations.Count > 0)
            {
                requiredMigrations.AddRange(additionalMigrations);
            }
        }
        else
        {
            requiredMigrations = FindRequiredMigrations(currentVersion, targetVersion);
        }
        
        if (requiredMigrations.Count == 0)
        {
            var errorMsg = $"No migrations found between version {currentVersion} and {targetVersion}.";
            PrintError(errorMsg);
            PrintError("Your configuration may need manual migration.");
            ServerPanel?.Call("API_OnMigrationFailed", errorMsg, 
                "No migration path exists between the current and target versions. Manual migration may be required.");
            return;
        }
        
        var migrationNames = new List<string>();
        for (var i = 0; i < requiredMigrations.Count; i++)
        {
            migrationNames.Add(requiredMigrations[i].Name);
        }
        Puts($"Found {requiredMigrations.Count} migration(s): {string.Join(", ", migrationNames)}");
        #endregion
        ExecuteMigrationChain(requiredMigrations, 0);
    }

    private static void UpdateConfigVersion(VersionNumber version)
    {
        try
        {
            var configPath = Path.Combine(Interface.Oxide.ConfigDirectory, "ServerPanel.json");
            if (!File.Exists(configPath)) return;

            var configJson = File.ReadAllText(configPath);
            var configData = JObject.Parse(configJson);
            configData["Version"] = JToken.FromObject(version);

            File.WriteAllText(configPath, JsonConvert.SerializeObject(configData, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Instance?.PrintError($"Failed to update config version: {ex.Message}");
        }
    }

    #endregion

    #region Migrations

    public interface IMigration
    {
        string Name { get; }

        VersionNumber FromVersion { get; }
        VersionNumber ToVersion { get; }

        bool IsNeeded();
        bool Execute();
    }

    public class CategoryTitleUiRefactoring : IMigration
    {
        public string Name => "1.3.0";

        public VersionNumber FromVersion => new(1, 2, 0);
        public VersionNumber ToVersion => new(1, 3, 0);

        public bool IsNeeded()
        {
            try
            {
                var templatePath = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Template.json");
                if (!File.Exists(templatePath))
                    return false;

                var templateJson = File.ReadAllText(templatePath);
                var templateData = JsonConvert.DeserializeObject<JObject>(templateJson);

                var categoryTitleToken = templateData?["UI Settings"]?["Categories"]?["Category Title"];
                if (categoryTitleToken is JObject categoryTitleObj)
                {
                    return categoryTitleObj.Property("Background Color") != null ||
                        categoryTitleObj.Property("Selected Background Color") != null ||
                        categoryTitleObj.Property("Background Sprite") != null ||
                        categoryTitleObj.Property("Background Material") != null;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool Execute()
        {
            var templatePath = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Template.json");

            var templateJson = File.ReadAllText(templatePath);
            var templateData = JsonConvert.DeserializeObject<JObject>(templateJson);

            MigrateCategoryTitleUi(templateData);

            var updatedJson = JsonConvert.SerializeObject(templateData, Formatting.Indented);
            File.WriteAllText(templatePath, updatedJson);

            return true;
        }

        private void MigrateCategoryTitleUi(JObject templateData)
        {
            var categoryTitleToken = templateData?["UI Settings"]?["Categories"]?["Category Title"];
            if (categoryTitleToken is not JObject categoryTitleObj)
                return;

            var backgroundObj = new JObject();
            var selectedBackgroundObj = new JObject();

            if (categoryTitleObj.TryGetValue("Background Color", out var backgroundColorToken))
            {
                backgroundObj["Color"] = backgroundColorToken;
                categoryTitleObj.Remove("Background Color");
            }

            if (categoryTitleObj.TryGetValue("Selected Background Color", out var selectedBackgroundColorToken))
            {
                selectedBackgroundObj["Color"] = selectedBackgroundColorToken;
                categoryTitleObj.Remove("Selected Background Color");
            }

            if (categoryTitleObj.TryGetValue("Background Sprite", out var backgroundSpriteToken))
            {
                var spriteValue = backgroundSpriteToken.Value<string>();
                if (!string.IsNullOrEmpty(spriteValue))
                {
                    backgroundObj["Sprite"] = spriteValue;
                    selectedBackgroundObj["Sprite"] = spriteValue;
                }

                categoryTitleObj.Remove("Background Sprite");
            }

            if (categoryTitleObj.TryGetValue("Background Material", out var backgroundMaterialToken))
            {
                var materialValue = backgroundMaterialToken.Value<string>();
                if (!string.IsNullOrEmpty(materialValue))
                {
                    backgroundObj["Material"] = materialValue;
                    selectedBackgroundObj["Material"] = materialValue;
                }

                categoryTitleObj.Remove("Background Material");
            }

            EnsureUiElementFields(backgroundObj);
            EnsureUiElementFields(selectedBackgroundObj);

            categoryTitleObj["Background"] = backgroundObj;
            categoryTitleObj["Selected Background"] = selectedBackgroundObj;
        }

        private void EnsureUiElementFields(JObject uiElementObj)
        {
            if (uiElementObj.Property("Enabled?") == null)
                uiElementObj["Enabled?"] = true;

            if (uiElementObj.Property("Visible") == null)
                uiElementObj["Visible"] = true;

            if (uiElementObj.Property("Name") == null)
                uiElementObj["Name"] = "";

            if (uiElementObj.Property("Type (Label/Panel/Button/Image)") == null)
                uiElementObj["Type (Label/Panel/Button/Image)"] = "Panel";

            if (uiElementObj.Property("Color") == null)
                uiElementObj["Color"] = new JObject
                {
                    ["HEX"] = "#FFFFFF",
                    ["Opacity (0 - 100)"] = 100
                };

            if (uiElementObj.Property("Text") == null)
                uiElementObj["Text"] = new JArray();

            if (uiElementObj.Property("Font Size") == null)
                uiElementObj["Font Size"] = 14;

            if (uiElementObj.Property("Font") == null)
                uiElementObj["Font"] = "RobotoCondensedBold";

            if (uiElementObj.Property("Align") == null)
                uiElementObj["Align"] = "UpperLeft";

            if (uiElementObj.Property("Text Color") == null)
                uiElementObj["Text Color"] = new JObject
                {
                    ["HEX"] = "#FFFFFF",
                    ["Opacity (0 - 100)"] = 100
                };

            if (uiElementObj.Property("Command ({user} - user steamid)") == null)
                uiElementObj["Command ({user} - user steamid)"] = "";

            if (uiElementObj.Property("Image") == null)
                uiElementObj["Image"] = "";

            if (uiElementObj.Property("Cursor Enabled") == null)
                uiElementObj["Cursor Enabled"] = false;

            if (uiElementObj.Property("Keyboard Enabled") == null)
                uiElementObj["Keyboard Enabled"] = false;

            if (uiElementObj.Property("Sprite") == null)
                uiElementObj["Sprite"] = "";

            if (uiElementObj.Property("Material") == null)
                uiElementObj["Material"] = "";

            if (uiElementObj.Property("AnchorMin (X)") == null)
                uiElementObj["AnchorMin (X)"] = 0f;

            if (uiElementObj.Property("AnchorMin (Y)") == null)
                uiElementObj["AnchorMin (Y)"] = 0f;

            if (uiElementObj.Property("AnchorMax (X)") == null)
                uiElementObj["AnchorMax (X)"] = 1f;

            if (uiElementObj.Property("AnchorMax (Y)") == null)
                uiElementObj["AnchorMax (Y)"] = 1f;

            if (uiElementObj.Property("OffsetMin (X)") == null)
                uiElementObj["OffsetMin (X)"] = 0f;

            if (uiElementObj.Property("OffsetMin (Y)") == null)
                uiElementObj["OffsetMin (Y)"] = 0f;

            if (uiElementObj.Property("OffsetMax (X)") == null)
                uiElementObj["OffsetMax (X)"] = 0f;

            if (uiElementObj.Property("OffsetMax (Y)") == null)
                uiElementObj["OffsetMax (Y)"] = 0f;
        }
    }

    public class LocalizationFormatMigration : IMigration
    {
        public string Name => "1.2.3";
        public VersionNumber FromVersion => new(1, 2, 0);
        public VersionNumber ToVersion => new(1, 2, 3);

        public bool IsNeeded()
        {
            try
            {
                var localizationPath = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Localization.json");
                if (!File.Exists(localizationPath))
                    return false;

                var localizationJson = File.ReadAllText(localizationPath);
                var localizationData = JObject.Parse(localizationJson);

                var localizationSettings = localizationData["Localization Settings"];
                if (localizationSettings == null)
                    return false;

                var elements = localizationSettings["UI Elements"] as JObject;
                if (elements == null)
                    return false;

                foreach (var kvp in elements)
                {
                    var key = kvp.Key;
                    if (!key.Contains('_') || key.Split('_').Length < 3) return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error checking localization migration need: {ex.Message}");
                return false;
            }
        }

        public bool Execute()
        {
            try
            {
                var localizationPath = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Localization.json");
                var categoriesPath = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Categories.json");

                if (!File.Exists(localizationPath) || !File.Exists(categoriesPath))
                {
                    Instance?.PrintWarning("Required files not found for localization migration");
                    return false;
                }

                var localizationJson = File.ReadAllText(localizationPath);
                var categoriesJson = File.ReadAllText(categoriesPath);

                var localizationData = JObject.Parse(localizationJson);
                var categoriesData = JObject.Parse(categoriesJson);

                MigrateLocalizationFormat(localizationData, categoriesData);

                File.WriteAllText(localizationPath, localizationData.ToString(Formatting.Indented));
                Instance?.Puts("Localization format migration completed");
                return true;
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Localization migration failed: {ex.Message}");
                return false;
            }
        }

        private void MigrateLocalizationFormat(JObject localizationData, JObject categoriesData)
        {
            var localizationSettings = localizationData["Localization Settings"];
            if (localizationSettings == null)
                return;

            var elements = localizationSettings["UI Elements"] as JObject;
            if (elements == null)
                return;

            var categories = categoriesData["Categories"] as JArray;
            if (categories == null)
                return;

            var elementsToMigrate = new List<(string oldKey, string newKey, JToken data)>();

            foreach (var kvp in elements)
            {
                var key = kvp.Key;
                var elementData = kvp.Value;

                if (!key.Contains('_') || key.Split('_').Length < 3)
                    foreach (var categoryToken in categories)
                    {
                        var category = categoryToken as JObject;
                        if (category == null) continue;

                        var categoryId = category["ID"]?.Value<int>() ?? 0;
                        var pages = category["Pages"] as JArray;
                        if (pages == null) continue;

                        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
                        {
                            var page = pages[pageIndex] as JObject;
                            if (page == null) continue;

                            var pageElements = page["UI Elements"] as JArray;
                            if (pageElements == null) continue;

                            foreach (var elementToken in pageElements)
                            {
                                var element = elementToken as JObject;
                                if (element == null) continue;

                                var elementName = element["Name"]?.Value<string>();
                                if (elementName == key)
                                {
                                    var newKey = $"{categoryId}_{pageIndex}_{key}";
                                    elementsToMigrate.Add((key, newKey, elementData));
                                }
                            }
                        }
                    }
            }

            foreach (var migration in elementsToMigrate)
                if (elements[migration.newKey] == null)
                    elements[migration.newKey] = migration.data;
        }
    }

    public class V1ToV2Migration : IMigration
    {
        public string Name => "2.0.0";
        public VersionNumber FromVersion => new(1, 3, 0);
        public VersionNumber ToVersion => new(2, 0, 0);

        public bool IsNeeded()
        {
            try
            {
                var configPath = Path.Combine(Interface.Oxide.ConfigDirectory, "ServerPanel.json");
                if (!File.Exists(configPath)) return false;

                var configJson = File.ReadAllText(configPath);
                var configData = JObject.Parse(configJson);

                var version = configData["Version"]?.ToObject<VersionNumber>();
                return version == default || version == null || version < new VersionNumber(2, 0, 0);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error checking V1 to V2 migration need: {ex.Message}");
                return false;
            }
        }

        public bool Execute()
        {
            try
            {
                MigrateTemplateData();

                MigrateCategoriesData();

                Instance?.Puts("=== Migration completed! ===");
                return true;
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Migration failed: {ex.Message}");
                throw;
            }
        }

        private bool MigrateCategoriesData()
        {
            var categoriesPath = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Categories.json");
            if (!File.Exists(categoriesPath)) return false;

            string categoriesJson;
            
            try
            {
                categoriesJson = File.ReadAllText(categoriesPath);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error reading Categories.json: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(categoriesJson))
            {
                Instance?.PrintError("Categories.json is empty");
                return false;
            }

            JObject categoriesData;
            try
            {
                categoriesData = JObject.Parse(categoriesJson);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error parsing Categories.json: {ex.Message}");   
                return false;
            }

            if (categoriesData["Categories"] is not JArray categories) return false;

            foreach (var category in categories)
                if (category["Pages"] is JArray pages)
                    foreach (var page in pages)
                    {
                        var pageType = page["Type (Plugin/UI)"]?.Value<string>();
                        if (pageType == "Plugin") category["Show Pages?"] = false;
                    }

            #region Save Categories.json

            string updatedJson;
            try
            {
                updatedJson = JsonConvert.SerializeObject(categoriesData, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Failed to serialize migrated template data: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(updatedJson))
            {
                Instance?.PrintError("Serialized template data is empty. Aborting write to prevent data loss.");
                return false;
            }

            try
            {
                File.WriteAllText(categoriesPath, updatedJson);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error writing Categories.json: {ex.Message}");
                return false;
            }

            #endregion Save Categories.json

            Instance?.Puts("  Categories.json migrated");
            return true;
        }
    
        private bool MigrateTemplateData()
        {
            #region Read Template.json

            var templatePath = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Template.json");
            if (!File.Exists(templatePath))
                return false;

            string json;
            try
            {
                json = File.ReadAllText(templatePath);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error reading Template.json: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(json))
            {
                Instance?.PrintError("Template.json is empty");
                return false;
            }

            JObject data;
            try
            {
                data = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error parsing Template.json: {ex.Message}");
                return false;
            }

            if (data == null)
            {
                Instance?.PrintError("Failed to parse Template.json: result is null.");
                return false;
            }

            var templateName = data?["UI Settings"]?["ID (DONT CHANGE)"]?.Value<string>();
            if (templateName == null)
            {
                Instance?.PrintError("Template name not found");
                return false;
            }

            #endregion Read Template.json

            #region Migrations

            #region Parent

            if (data?["UI Settings"]?["Background"]?["Parent (Overlay/Hud)"] is JObject parentLayer)
                parentLayer["Parent (Overlay/Hud)"] = "OverlayNonScaled";

            #endregion Parent

            #region Text Pagination Settings

            if (data?["UI Settings"]?["Content"]?["Pagination"]?["Text Pagination Settings"] is JObject
                textPaginationSettings)
            {
                if (textPaginationSettings["Button Back"] is JObject buttonBack)
                {
                    buttonBack["Use Icon"] = true;
                    buttonBack["Icon"] = new JObject
                    {
                        ["Enabled?"] = false,
                        ["Visible"] = true,
                        ["Name"] = "",
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                        ["Text"] = new JArray(),
                        ["Font Size"] = 0,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0f,
                        ["AnchorMin (Y)"] = 0f,
                        ["AnchorMax (X)"] = 1f,
                        ["AnchorMax (Y)"] = 1f,
                        ["OffsetMin (X)"] = 0f,
                        ["OffsetMin (Y)"] = 0f,
                        ["OffsetMax (X)"] = 0f,
                        ["OffsetMax (Y)"] = 0f
                    };
                }

                if (textPaginationSettings["Button Next"] is JObject buttonNext)
                {
                    buttonNext["Use Icon"] = true;
                    buttonNext["Icon"] = new JObject
                    {
                        ["Enabled?"] = false,
                        ["Visible"] = true,
                        ["Name"] = "",
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                        ["Text"] = new JArray(),
                        ["Font Size"] = 0,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0f,
                        ["AnchorMin (Y)"] = 0f,
                        ["AnchorMax (X)"] = 1f,
                        ["AnchorMax (Y)"] = 1f,
                        ["OffsetMin (X)"] = 0f,
                        ["OffsetMin (Y)"] = 0f,
                        ["OffsetMax (X)"] = 0f,
                        ["OffsetMax (Y)"] = 0f
                    };
                }
            }

            #endregion Text Pagination Settings

            #region Multiple Buttons Settings

            if (data?["UI Settings"]?["Content"]?["Pagination"]?["Multiple Buttons Settings"] is JObject
                multipleButtonsSettings)
            {
                if (multipleButtonsSettings["Button Back"] is JObject buttonBack)
                {
                    buttonBack["Use Icon"] = true;
                    buttonBack["Icon"] = new JObject
                    {
                        ["Enabled?"] = false,
                        ["Visible"] = true,
                        ["Name"] = "",
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                        ["Text"] = new JArray(),
                        ["Font Size"] = 0,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0f,
                        ["AnchorMin (Y)"] = 0f,
                        ["AnchorMax (X)"] = 1f,
                        ["AnchorMax (Y)"] = 1f,
                        ["OffsetMin (X)"] = 0f,
                        ["OffsetMin (Y)"] = 0f,
                        ["OffsetMax (X)"] = 0f,
                        ["OffsetMax (Y)"] = 0f
                    };
                }

                if (multipleButtonsSettings["Button Next"] is JObject buttonNext)
                {
                    buttonNext["Use Icon"] = true;
                    buttonNext["Icon"] = new JObject
                    {
                        ["Enabled?"] = false,
                        ["Visible"] = true,
                        ["Name"] = "",
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                        ["Text"] = new JArray(),
                        ["Font Size"] = 0,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0f,
                        ["AnchorMin (Y)"] = 0f,
                        ["AnchorMax (X)"] = 1f,
                        ["AnchorMax (Y)"] = 1f,
                        ["OffsetMin (X)"] = 0f,
                        ["OffsetMin (Y)"] = 0f,
                        ["OffsetMax (X)"] = 0f,
                        ["OffsetMax (Y)"] = 0f
                    };
                }
            }

            #endregion Multiple Buttons Settings

            #region Sub Categories Settings

            if (data?["UI Settings"]?["Content"]?["Pagination"] is JObject paginationSettings)
                paginationSettings["Sub Categories Settings"] = new JObject
                {
                    ["Background"] = new JObject
                    {
                        ["Enabled?"] = false,
                        ["Visible"] = true,
                        ["Name"] = "",
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 0,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.0,
                        ["AnchorMax (X)"] = 1.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 0.0,
                        ["OffsetMin (Y)"] = 0.0,
                        ["OffsetMax (X)"] = 0.0,
                        ["OffsetMax (Y)"] = 0.0
                    },
                    ["Scroll Settings"] = new JObject
                    {
                        ["Scroll Type"] = "Horizontal",
                        ["Movement Type"] = "Unrestricted",
                        ["Elasticity"] = 0.0,
                        ["Deceleration Rate"] = 0.0,
                        ["Scroll Sensitivity"] = 0.0,
                        ["Scrollbar Settings"] = new JObject
                        {
                            ["Invert"] = false,
                            ["Auto Hide"] = false,
                            ["Handle Sprite"] = null,
                            ["Size"] = 0.0,
                            ["Handle Color"] = null,
                            ["Highlight Color"] = null,
                            ["Pressed Color"] = null,
                            ["Track Sprite"] = null,
                            ["Track Color"] = null
                        },
                        ["Scroll Size"] = 0.0,
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.0,
                        ["AnchorMax (X)"] = 1.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 0.0,
                        ["OffsetMin (Y)"] = 0.0,
                        ["OffsetMax (X)"] = 0.0,
                        ["OffsetMax (Y)"] = 0.0
                    },
                    ["Sub Category Button"] = new JObject
                    {
                        ["Width"] = 0.0,
                        ["Margin"] = 0.0,
                        ["Background"] = new JObject
                        {
                            ["Enabled?"] = false,
                            ["Visible"] = true,
                            ["Name"] = "",
                            ["Type (Label/Panel/Button/Image)"] = "Label",
                            ["Color"] = new JObject
                            {
                                ["HEX"] = "#FFFFFF",
                                ["Opacity (0 - 100)"] = 100.0
                            },
                            ["Text"] = new JArray(),
                            ["Font Size"] = 0,
                            ["Font"] = 0,
                            ["Align"] = "UpperLeft",
                            ["Text Color"] = new JObject
                            {
                                ["HEX"] = "#FFFFFF",
                                ["Opacity (0 - 100)"] = 100.0
                            },
                            ["Command ({user} - user steamid)"] = "",
                            ["Image"] = "",
                            ["Cursor Enabled"] = false,
                            ["Keyboard Enabled"] = false,
                            ["Sprite"] = "",
                            ["Material"] = "",
                            ["AnchorMin (X)"] = 0.0,
                            ["AnchorMin (Y)"] = 0.0,
                            ["AnchorMax (X)"] = 1.0,
                            ["AnchorMax (Y)"] = 1.0,
                            ["OffsetMin (X)"] = 0.0,
                            ["OffsetMin (Y)"] = 0.0,
                            ["OffsetMax (X)"] = 0.0,
                            ["OffsetMax (Y)"] = 0.0
                        },
                        ["Title"] = new JObject
                        {
                            ["Enabled?"] = false,
                            ["Visible"] = true,
                            ["Name"] = "",
                            ["Type (Label/Panel/Button/Image)"] = "Label",
                            ["Color"] = new JObject
                            {
                                ["HEX"] = "#FFFFFF",
                                ["Opacity (0 - 100)"] = 100.0
                            },
                            ["Text"] = new JArray(),
                            ["Font Size"] = 0,
                            ["Font"] = 0,
                            ["Align"] = "UpperLeft",
                            ["Text Color"] = new JObject
                            {
                                ["HEX"] = "#FFFFFF",
                                ["Opacity (0 - 100)"] = 100.0
                            },
                            ["Command ({user} - user steamid)"] = "",
                            ["Image"] = "",
                            ["Cursor Enabled"] = false,
                            ["Keyboard Enabled"] = false,
                            ["Sprite"] = "",
                            ["Material"] = "",
                            ["AnchorMin (X)"] = 0.0,
                            ["AnchorMin (Y)"] = 0.0,
                            ["AnchorMax (X)"] = 1.0,
                            ["AnchorMax (Y)"] = 1.0,
                            ["OffsetMin (X)"] = 0.0,
                            ["OffsetMin (Y)"] = 0.0,
                            ["OffsetMax (X)"] = 0.0,
                            ["OffsetMax (Y)"] = 0.0
                        },
                        ["Use Icon"] = false,
                        ["Icon"] = new JObject
                        {
                            ["Enabled?"] = false,
                            ["Visible"] = true,
                            ["Name"] = "",
                            ["Type (Label/Panel/Button/Image)"] = "Label",
                            ["Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                            ["Text"] = new JArray(),
                            ["Font Size"] = 0,
                            ["Font"] = 0,
                            ["Align"] = "UpperLeft",
                            ["Text Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                            ["Command ({user} - user steamid)"] = "",
                            ["Image"] = "",
                            ["Cursor Enabled"] = false,
                            ["Keyboard Enabled"] = false,
                            ["Sprite"] = "",
                            ["Material"] = "",
                            ["AnchorMin (X)"] = 0f,
                            ["AnchorMin (Y)"] = 0f,
                            ["AnchorMax (X)"] = 1f,
                            ["AnchorMax (Y)"] = 1f,
                            ["OffsetMin (X)"] = 0f,
                            ["OffsetMin (Y)"] = 0f,
                            ["OffsetMax (X)"] = 0f,
                            ["OffsetMax (Y)"] = 0f
                        }
                    },
                    ["Sub Category Selected Button"] = new JObject
                    {
                        ["Width"] = 0.0,
                        ["Margin"] = 0.0,
                        ["Background"] = new JObject
                        {
                            ["Enabled?"] = false,
                            ["Visible"] = true,
                            ["Name"] = "",
                            ["Type (Label/Panel/Button/Image)"] = "Label",
                            ["Color"] = new JObject
                            {
                                ["HEX"] = "#FFFFFF",
                                ["Opacity (0 - 100)"] = 100.0
                            },
                            ["Text"] = new JArray(),
                            ["Font Size"] = 0,
                            ["Font"] = 0,
                            ["Align"] = "UpperLeft",
                            ["Text Color"] = new JObject
                            {
                                ["HEX"] = "#FFFFFF",
                                ["Opacity (0 - 100)"] = 100.0
                            },
                            ["Command ({user} - user steamid)"] = "",
                            ["Image"] = "",
                            ["Cursor Enabled"] = false,
                            ["Keyboard Enabled"] = false,
                            ["Sprite"] = "",
                            ["Material"] = "",
                            ["AnchorMin (X)"] = 0.0,
                            ["AnchorMin (Y)"] = 0.0,
                            ["AnchorMax (X)"] = 1.0,
                            ["AnchorMax (Y)"] = 1.0,
                            ["OffsetMin (X)"] = 0.0,
                            ["OffsetMin (Y)"] = 0.0,
                            ["OffsetMax (X)"] = 0.0,
                            ["OffsetMax (Y)"] = 0.0
                        },
                        ["Title"] = new JObject
                        {
                            ["Enabled?"] = false,
                            ["Visible"] = true,
                            ["Name"] = "",
                            ["Type (Label/Panel/Button/Image)"] = "Label",
                            ["Color"] = new JObject
                            {
                                ["HEX"] = "#FFFFFF",
                                ["Opacity (0 - 100)"] = 100.0
                            },
                            ["Text"] = new JArray(),
                            ["Font Size"] = 0,
                            ["Font"] = 0,
                            ["Align"] = "UpperLeft",
                            ["Text Color"] = new JObject
                            {
                                ["HEX"] = "#FFFFFF",
                                ["Opacity (0 - 100)"] = 100.0
                            },
                            ["Command ({user} - user steamid)"] = "",
                            ["Image"] = "",
                            ["Cursor Enabled"] = false,
                            ["Keyboard Enabled"] = false,
                            ["Sprite"] = "",
                            ["Material"] = "",
                            ["AnchorMin (X)"] = 0.0,
                            ["AnchorMin (Y)"] = 0.0,
                            ["AnchorMax (X)"] = 1.0,
                            ["AnchorMax (Y)"] = 1.0,
                            ["OffsetMin (X)"] = 0.0,
                            ["OffsetMin (Y)"] = 0.0,
                            ["OffsetMax (X)"] = 0.0,
                            ["OffsetMax (Y)"] = 0.0
                        },
                        ["Use Icon"] = false,
                        ["Icon"] = new JObject
                        {
                            ["Enabled?"] = false,
                            ["Visible"] = true,
                            ["Name"] = "",
                            ["Type (Label/Panel/Button/Image)"] = "Label",
                            ["Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                            ["Text"] = new JArray(),
                            ["Font Size"] = 0,
                            ["Font"] = 0,
                            ["Align"] = "UpperLeft",
                            ["Text Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                            ["Command ({user} - user steamid)"] = "",
                            ["Image"] = "",
                            ["Cursor Enabled"] = false,
                            ["Keyboard Enabled"] = false,
                            ["Sprite"] = "",
                            ["Material"] = "",
                            ["AnchorMin (X)"] = 0f,
                            ["AnchorMin (Y)"] = 0f,
                            ["AnchorMax (X)"] = 1f,
                            ["AnchorMax (Y)"] = 1f,
                            ["OffsetMin (X)"] = 0f,
                            ["OffsetMin (Y)"] = 0f,
                            ["OffsetMax (X)"] = 0f,
                            ["OffsetMax (Y)"] = 0f
                        }
                    },
                    ["Edit Page Button"] = null,
                    ["AnchorMin (X)"] = 0.0,
                    ["AnchorMin (Y)"] = 0.0,
                    ["AnchorMax (X)"] = 1.0,
                    ["AnchorMax (Y)"] = 1.0,
                    ["OffsetMin (X)"] = 0.0,
                    ["OffsetMin (Y)"] = 0.0,
                    ["OffsetMax (X)"] = 0.0,
                    ["OffsetMax (Y)"] = 0.0
                };

            #endregion Sub Categories Settings

            #region Edit Button

            if (data?["UI Settings"]?["Content"]?["Edit Button"] is JObject contentEditButton)
            {
                #region Background

                contentEditButton["Background"] = new JObject
                {
                    ["Enabled?"] = true,
                    ["Visible"] = true,
                    ["Name"] = GenerateElementGUID("Panel"),
                    ["Type (Label/Panel/Button/Image)"] = "Panel",
                    ["Color"] = new JObject
                    {
                        ["HEX"] = "#175782",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Text"] = new JArray(),
                    ["Font Size"] = 14,
                    ["Font"] = 0,
                    ["Align"] = "UpperLeft",
                    ["Text Color"] = new JObject
                    {
                        ["HEX"] = "#FFFFFF",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Command ({user} - user steamid)"] = "",
                    ["Image"] = "",
                    ["Cursor Enabled"] = false,
                    ["Keyboard Enabled"] = false,
                    ["Sprite"] = "",
                    ["Material"] = "",
                    ["AnchorMin (X)"] = 0.0,
                    ["AnchorMin (Y)"] = 0.0,
                    ["AnchorMax (X)"] = 0.0,
                    ["AnchorMax (Y)"] = 0.0,
                    ["OffsetMin (X)"] = 40.0,
                    ["OffsetMin (Y)"] = 10.0,
                    ["OffsetMax (X)"] = 140.0,
                    ["OffsetMax (Y)"] = 36.0
                };

                #endregion Background

                #region Title

                contentEditButton["Title"] = new JObject
                {
                    ["Enabled?"] = true,
                    ["Visible"] = true,
                    ["Name"] = GenerateElementGUID("Label"),
                    ["Type (Label/Panel/Button/Image)"] = "Label",
                    ["Color"] = new JObject
                    {
                        ["HEX"] = "#FFFFFF",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Text"] = new JArray("EDIT CONTENT"),
                    ["Font Size"] = 10,
                    ["Font"] = 0,
                    ["Align"] = "MiddleLeft",
                    ["Text Color"] = new JObject
                    {
                        ["HEX"] = "#68C2FF",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Command ({user} - user steamid)"] = "",
                    ["Image"] = "",
                    ["Cursor Enabled"] = false,
                    ["Keyboard Enabled"] = false,
                    ["Sprite"] = "",
                    ["Material"] = "",
                    ["AnchorMin (X)"] = 0.0,
                    ["AnchorMin (Y)"] = 0.0,
                    ["AnchorMax (X)"] = 1.0,
                    ["AnchorMax (Y)"] = 1.0,
                    ["OffsetMin (X)"] = 27.0,
                    ["OffsetMin (Y)"] = 0.0,
                    ["OffsetMax (X)"] = 0.0,
                    ["OffsetMax (Y)"] = 0.0
                };

                #endregion Title

                #region Icon

                contentEditButton["Icon"] = new JObject
                {
                    ["Enabled?"] = true,
                    ["Visible"] = true,
                    ["Name"] = GenerateElementGUID("Image"),
                    ["Type (Label/Panel/Button/Image)"] = "Image",
                    ["Color"] = new JObject
                    {
                        ["HEX"] = "#68C2FF",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Text"] = new JArray(),
                    ["Font Size"] = 14,
                    ["Font"] = 0,
                    ["Align"] = "UpperLeft",
                    ["Text Color"] = new JObject
                    {
                        ["HEX"] = "#FFFFFF",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Command ({user} - user steamid)"] = "",
                    ["Image"] = "assets/icons/level_metal.png",
                    ["Cursor Enabled"] = false,
                    ["Keyboard Enabled"] = false,
                    ["Sprite"] = "",
                    ["Material"] = "",
                    ["AnchorMin (X)"] = 0.0,
                    ["AnchorMin (Y)"] = 0.5,
                    ["AnchorMax (X)"] = 0.0,
                    ["AnchorMax (Y)"] = 0.5,
                    ["OffsetMin (X)"] = 10.0,
                    ["OffsetMin (Y)"] = -6.0,
                    ["OffsetMax (X)"] = 22.0,
                    ["OffsetMax (Y)"] = 6.0
                };

                #endregion Icon

                contentEditButton["Description Background"] = null;
                contentEditButton["Description Title"] = null;
            }

            #endregion Edit Button

            #region Header

            if (data?["UI Settings"]?["Header"] is JObject header)
            {
                header["Show line?"] = false;
                header["Line"] = new JObject
                {
                    ["Enabled?"] = false,
                    ["Visible"] = true,
                    ["Name"] = "",
                    ["Type (Label/Panel/Button/Image)"] = "Label",
                    ["Color"] = new JObject
                    {
                        ["HEX"] = "#FFFFFF",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Text"] = new JArray(),
                    ["Font Size"] = 0,
                    ["Font"] = 0,
                    ["Align"] = "UpperLeft",
                    ["Text Color"] = new JObject
                    {
                        ["HEX"] = "#FFFFFF",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Command ({user} - user steamid)"] = "",
                    ["Image"] = "",
                    ["Cursor Enabled"] = false,
                    ["Keyboard Enabled"] = false,
                    ["Sprite"] = "",
                    ["Material"] = "",
                    ["AnchorMin (X)"] = 0.0,
                    ["AnchorMin (Y)"] = 0.0,
                    ["AnchorMax (X)"] = 1.0,
                    ["AnchorMax (Y)"] = 1.0,
                    ["OffsetMin (X)"] = 0.0,
                    ["OffsetMin (Y)"] = 0.0,
                    ["OffsetMax (X)"] = 0.0,
                    ["OffsetMax (Y)"] = 0.0
                };
                header["Edit Button"] = new JObject
                {
                    ["Background"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Panel"),
                        ["Type (Label/Panel/Button/Image)"] = "Panel",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#175782",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 1.0,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 104.0,
                        ["OffsetMin (Y)"] = 4.0,
                        ["OffsetMax (X)"] = 204.0,
                        ["OffsetMax (Y)"] = 30.0
                    },
                    ["Title"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Label"),
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray("EDIT HEADER"),
                        ["Font Size"] = 10,
                        ["Font"] = 0,
                        ["Align"] = "MiddleLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.0,
                        ["AnchorMax (X)"] = 1.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 27.0,
                        ["OffsetMin (Y)"] = 0.0,
                        ["OffsetMax (X)"] = 0.0,
                        ["OffsetMax (Y)"] = 0.0
                    },
                    ["Icon"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Image"),
                        ["Type (Label/Panel/Button/Image)"] = "Image",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "assets/icons/level_metal.png",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.5,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 0.5,
                        ["OffsetMin (X)"] = 10.0,
                        ["OffsetMin (Y)"] = -6.0,
                        ["OffsetMax (X)"] = 22.0,
                        ["OffsetMax (Y)"] = 6.0
                    },
                    ["Description Background"] = null,
                    ["Description Title"] = null
                };
                header["Edit PopUps Button"] = new JObject
                {
                    ["Background"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Panel"),
                        ["Type (Label/Panel/Button/Image)"] = "Panel",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#175782",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 1.0,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 208.0,
                        ["OffsetMin (Y)"] = 4.0,
                        ["OffsetMax (X)"] = 308.0,
                        ["OffsetMax (Y)"] = 30.0
                    },
                    ["Title"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Label"),
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray("EDIT POPUPS"),
                        ["Font Size"] = 10,
                        ["Font"] = 0,
                        ["Align"] = "MiddleLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.0,
                        ["AnchorMax (X)"] = 1.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 27.0,
                        ["OffsetMin (Y)"] = 0.0,
                        ["OffsetMax (X)"] = 0.0,
                        ["OffsetMax (Y)"] = 0.0
                    },
                    ["Icon"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Image"),
                        ["Type (Label/Panel/Button/Image)"] = "Image",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "assets/icons/level_metal.png",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.5,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 0.5,
                        ["OffsetMin (X)"] = 10.0,
                        ["OffsetMin (Y)"] = -6.0,
                        ["OffsetMax (X)"] = 22.0,
                        ["OffsetMax (Y)"] = 6.0
                    },
                    ["Description Background"] = null,
                    ["Description Title"] = null
                };
                header["Edit Pages Button"] = new JObject
                {
                    ["Background"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Panel"),
                        ["Type (Label/Panel/Button/Image)"] = "Panel",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#175782",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 1.0,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 312.0,
                        ["OffsetMin (Y)"] = 4.0,
                        ["OffsetMax (X)"] = 412.0,
                        ["OffsetMax (Y)"] = 30.0
                    },
                    ["Title"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Label"),
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray("EDIT PAGES"),
                        ["Font Size"] = 10,
                        ["Font"] = 0,
                        ["Align"] = "MiddleLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.0,
                        ["AnchorMax (X)"] = 1.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 27.0,
                        ["OffsetMin (Y)"] = 0.0,
                        ["OffsetMax (X)"] = 0.0,
                        ["OffsetMax (Y)"] = 0.0
                    },
                    ["Icon"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Image"),
                        ["Type (Label/Panel/Button/Image)"] = "Image",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "assets/icons/level_metal.png",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.5,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 0.5,
                        ["OffsetMin (X)"] = 10.0,
                        ["OffsetMin (Y)"] = -6.0,
                        ["OffsetMax (X)"] = 22.0,
                        ["OffsetMax (Y)"] = 6.0
                    },
                    ["Description Background"] = null,
                    ["Description Title"] = null
                };
            }

            #endregion Header

            #region Categories

            if (data?["UI Settings"]?["Categories"] is JObject categories)
            {
                categories["Use scrolling?"] = true;

                if (categories["Categories Scroll"] is JObject categoriesScroll)
                {
                    categoriesScroll["Scroll Size"] = 1200f;
                    categoriesScroll["AnchorMin (X)"] = 0.0;
                    categoriesScroll["AnchorMin (Y)"] = 1.0;
                    categoriesScroll["AnchorMax (X)"] = 1.0;
                    categoriesScroll["AnchorMax (Y)"] = 1.0;
                    categoriesScroll["OffsetMin (X)"] = 0.0;
                    categoriesScroll["OffsetMin (Y)"] = -45.0;
                    categoriesScroll["OffsetMax (X)"] = 0.0;
                    categoriesScroll["OffsetMax (Y)"] = 0.0;
                    categoriesScroll["Movement Type"] = "Clamped";

                    if (categoriesScroll["Scrollbar Settings"] is JObject scrollbarSettings)
                    {
                        scrollbarSettings["Auto Hide"] = true;
                        scrollbarSettings["Invert"] = true;
                        scrollbarSettings["Size"] = 3f;
                        scrollbarSettings["Handle Sprite"] = "assets/content/ui/ui.background.tile.psd";
                        scrollbarSettings["Handle Color"] = new JObject
                        {
                            ["HEX"] = "#CE412B",
                            ["Opacity (0 - 100)"] = 100.0
                        };
                        scrollbarSettings["Highlight Color"] = new JObject
                        {
                            ["HEX"] = "#CE412B",
                            ["Opacity (0 - 100)"] = 100.0
                        };
                        scrollbarSettings["Pressed Color"] = new JObject
                        {
                            ["HEX"] = "#CE412B",
                            ["Opacity (0 - 100)"] = 100.0
                        };
                        scrollbarSettings["Track Color"] = new JObject
                        {
                            ["HEX"] = "#2C2F31",
                            ["Opacity (0 - 100)"] = 100.0
                        };
                        scrollbarSettings["Track Sprite"] = "assets/content/ui/ui.background.tile.psd";
                    }
                }

                categories.Remove("Category Edit Panel");
                categories.Remove("Admin Category");

                categories["Use adaptive width for localization?"] = false;
                categories["Show line?"] = false;
                categories["Line"] = new JObject
                {
                    ["Enabled?"] = false,
                    ["Visible"] = true,
                    ["Name"] = "",
                    ["Type (Label/Panel/Button/Image)"] = "Label",
                    ["Color"] = new JObject
                    {
                        ["HEX"] = "#FFFFFF",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Text"] = new JArray(),
                    ["Font Size"] = 0,
                    ["Font"] = 0,
                    ["Align"] = "UpperLeft",
                    ["Text Color"] = new JObject
                    {
                        ["HEX"] = "#FFFFFF",
                        ["Opacity (0 - 100)"] = 100.0
                    },
                    ["Command ({user} - user steamid)"] = "",
                    ["Image"] = "",
                    ["Cursor Enabled"] = false,
                    ["Keyboard Enabled"] = false,
                    ["Sprite"] = "",
                    ["Material"] = "",
                    ["AnchorMin (X)"] = 0.0,
                    ["AnchorMin (Y)"] = 0.0,
                    ["AnchorMax (X)"] = 1.0,
                    ["AnchorMax (Y)"] = 1.0,
                    ["OffsetMin (X)"] = 0.0,
                    ["OffsetMin (Y)"] = 0.0,
                    ["OffsetMax (X)"] = 0.0,
                    ["OffsetMax (Y)"] = 0.0
                };

                categories["Admin Mode Button"] = new JObject
                {
                    ["Background"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Panel"),
                        ["Type (Label/Panel/Button/Image)"] = "Panel",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#222222",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 1.0,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 0.0,
                        ["OffsetMin (Y)"] = 74.0,
                        ["OffsetMax (X)"] = 100.0,
                        ["OffsetMax (Y)"] = 100.0
                    },
                    ["Title"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Label"),
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray("ADMIN MODE"),
                        ["Font Size"] = 10,
                        ["Font"] = 0,
                        ["Align"] = "MiddleLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 60.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.0,
                        ["AnchorMax (X)"] = 1.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 27.0,
                        ["OffsetMin (Y)"] = 0.0,
                        ["OffsetMax (X)"] = 0.0,
                        ["OffsetMax (Y)"] = 0.0
                    },
                    ["Use Icon"] = true,
                    ["Icon"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Image"),
                        ["Type (Label/Panel/Button/Image)"] = "Image",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 60.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "assets/icons/gear.png",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.5,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 0.5,
                        ["OffsetMin (X)"] = 10.0,
                        ["OffsetMin (Y)"] = -6.0,
                        ["OffsetMax (X)"] = 22.0,
                        ["OffsetMax (Y)"] = 6.0
                    }
                };
                categories["Admin Mode Selected Button"] = new JObject
                {
                    ["Background"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Panel"),
                        ["Type (Label/Panel/Button/Image)"] = "Panel",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#175782",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 1.0,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 0.0,
                        ["OffsetMin (Y)"] = 74.0,
                        ["OffsetMax (X)"] = 100.0,
                        ["OffsetMax (Y)"] = 100.0
                    },
                    ["Title"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Label"),
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray("ADMIN MODE"),
                        ["Font Size"] = 10,
                        ["Font"] = 0,
                        ["Align"] = "MiddleLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.0,
                        ["AnchorMax (X)"] = 1.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 27.0,
                        ["OffsetMin (Y)"] = 0.0,
                        ["OffsetMax (X)"] = 0.0,
                        ["OffsetMax (Y)"] = 0.0
                    },
                    ["Use Icon"] = true,
                    ["Icon"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Image"),
                        ["Type (Label/Panel/Button/Image)"] = "Image",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "assets/icons/gear.png",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.5,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 0.5,
                        ["OffsetMin (X)"] = 10.0,
                        ["OffsetMin (Y)"] = -6.0,
                        ["OffsetMax (X)"] = 22.0,
                        ["OffsetMax (Y)"] = 6.0
                    }
                };
                categories["Edit Category Button"] = new JObject
                {
                    ["Width"] = 101.0,
                    ["Background"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Panel"),
                        ["Type (Label/Panel/Button/Image)"] = "Panel",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#175782",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.5,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 0.5,
                        ["OffsetMin (X)"] = 0.0,
                        ["OffsetMin (Y)"] = -14.0,
                        ["OffsetMax (X)"] = 101.0,
                        ["OffsetMax (Y)"] = 14.0
                    },
                    ["Title"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Label"),
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray("EDIT CATEGORY"),
                        ["Font Size"] = 10,
                        ["Font"] = 0,
                        ["Align"] = "MiddleLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.0,
                        ["AnchorMax (X)"] = 1.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 27.0,
                        ["OffsetMin (Y)"] = 0.0,
                        ["OffsetMax (X)"] = 0.0,
                        ["OffsetMax (Y)"] = 0.0
                    },
                    ["Use Icon"] = true,
                    ["Icon"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Image"),
                        ["Type (Label/Panel/Button/Image)"] = "Image",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "assets/icons/level_metal.png",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.5,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 0.5,
                        ["OffsetMin (X)"] = 10.0,
                        ["OffsetMin (Y)"] = -6.0,
                        ["OffsetMax (X)"] = 22.0,
                        ["OffsetMax (Y)"] = 6.0
                    }
                };
            }

            #endregion Categories

            #region Close Button

            if (data?["UI Settings"]?["Close Button"] is JObject closeButton)
            {
                closeButton["Use Icon"] = false;
                closeButton["Icon"] = new JObject
                {
                    ["Enabled?"] = false,
                    ["Visible"] = true,
                    ["Name"] = GenerateElementGUID("Label"),
                    ["Type (Label/Panel/Button/Image)"] = "Label",
                    ["Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                    ["Text"] = new JArray(),
                    ["Font Size"] = 0,
                    ["Font"] = 0,
                    ["Align"] = "UpperLeft",
                    ["Text Color"] = new JObject {["HEX"] = "#FFFFFF", ["Opacity (0 - 100)"] = 100f},
                    ["Command ({user} - user steamid)"] = "",
                    ["Image"] = "",
                    ["Cursor Enabled"] = false,
                    ["Keyboard Enabled"] = false,
                    ["Sprite"] = "",
                    ["Material"] = "",
                    ["AnchorMin (X)"] = 0f,
                    ["AnchorMin (Y)"] = 0f,
                    ["AnchorMax (X)"] = 1f,
                    ["AnchorMax (Y)"] = 1f,
                    ["OffsetMin (X)"] = 0f,
                    ["OffsetMin (Y)"] = 0f,
                    ["OffsetMax (X)"] = 0f,
                    ["OffsetMax (Y)"] = 0f
                };
            }

            #endregion Close Button

            MigrateTemplateDataByTemplateName(ref data, templateName);

            #endregion Migrations

            #region Save Template.json

            string updatedJson;
            try
            {
                updatedJson = JsonConvert.SerializeObject(data, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Failed to serialize migrated template data: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(updatedJson))
            {
                Instance?.PrintError("Serialized template data is empty. Aborting write to prevent data loss.");
                return false;
            }

            try
            {
                File.WriteAllText(templatePath, updatedJson);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error writing Template.json: {ex.Message}");
                return false;
            }

            #endregion Save Template.json

            Instance?.Puts("  Template.json migrated");
            return true;
        }

        private void MigrateTemplateDataByTemplateName(ref JObject data, string templateName)
        {
            switch (templateName)
            {
                case "t1":
                {
                    break;
                }
                case "t1_1":
                {
                    break;
                }
                case "t2":
                {
                    #region Header

                    if (data?["UI Settings"]?["Header"] is JObject header)
                    {
                        header["Edit Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = -178.0,
                                ["OffsetMin (Y)"] = -26.0,
                                ["OffsetMax (X)"] = -78.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT HEADER"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };
                        header["Edit PopUps Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 30.0,
                                ["OffsetMin (Y)"] = -26.0,
                                ["OffsetMax (X)"] = 130.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT POPUPS"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };
                        header["Edit Pages Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = -74.0,
                                ["OffsetMin (Y)"] = -26.0,
                                ["OffsetMax (X)"] = 26.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT PAGES"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };
                    }

                    #endregion Header

                    #region Content

                    #region Pagination

                    if (data?["UI Settings"]?["Content"]?["Pagination"]?["Text Pagination Settings"] is JObject
                        textPaginationSettings)
                    {
                        textPaginationSettings["Button Back"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_936e561e43",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#2C2F31",
                                    ["Opacity (0 - 100)"] = 70.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 0.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 38.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_9a6000a4c9",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("<"),
                                ["Font Size"] = 16,
                                ["Font"] = 1,
                                ["Align"] = "MiddleCenter",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 90.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 0.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            }
                        };

                        textPaginationSettings["Button Next"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_e560cc703e",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#CF432D",
                                    ["Opacity (0 - 100)"] = 90.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 1.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = -38.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_28d18931a1",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(">"),
                                ["Font Size"] = 16,
                                ["Font"] = 1,
                                ["Align"] = "MiddleCenter",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 90.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 0.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            }
                        };

                        textPaginationSettings["Label Settings"] = new JObject
                        {
                            ["Enabled?"] = true,
                            ["Visible"] = true,
                            ["Name"] = "Label_cff6e9891e",
                            ["Type (Label/Panel/Button/Image)"] = "Label",
                            ["Color"] = new JObject
                            {
                                ["HEX"] = "#FFFFFF",
                                ["Opacity (0 - 100)"] = 100.0
                            },
                            ["Text"] = new JArray("<color=#DCDCDC>{page}</color>/{maxPages}"),
                            ["Font Size"] = 16,
                            ["Font"] = 1,
                            ["Align"] = "MiddleCenter",
                            ["Text Color"] = new JObject
                            {
                                ["HEX"] = "#DCDCDC",
                                ["Opacity (0 - 100)"] = 50.0
                            },
                            ["Command ({user} - user steamid)"] = "",
                            ["Image"] = "",
                            ["Cursor Enabled"] = false,
                            ["Keyboard Enabled"] = false,
                            ["Sprite"] = "",
                            ["Material"] = "",
                            ["AnchorMin (X)"] = 1.0,
                            ["AnchorMin (Y)"] = 0.0,
                            ["AnchorMax (X)"] = 1.0,
                            ["AnchorMax (Y)"] = 0.0,
                            ["OffsetMin (X)"] = -169.0,
                            ["OffsetMin (Y)"] = 15.0,
                            ["OffsetMax (X)"] = -36.0,
                            ["OffsetMax (Y)"] = 37.0
                        };
                    }

                    #endregion Pagination

                    #region Edit Button

                    if (data?["UI Settings"]?["Content"] is JObject content)
                        content["Edit Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.0,
                                ["OffsetMin (X)"] = 90.0,
                                ["OffsetMin (Y)"] = 20.0,
                                ["OffsetMax (X)"] = 190.0,
                                ["OffsetMax (Y)"] = 46.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT CONTENT"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };

                    #endregion Edit Button

                    #endregion Content

                    #region Categories

                    if (data?["UI Settings"]?["Categories"] is JObject categories)
                    {
                        if (categories["Background"] is JObject categoriesBackground)
                        {
                            categoriesBackground["AnchorMin (X)"] = 0.0;
                            categoriesBackground["AnchorMin (Y)"] = 0.0;
                            categoriesBackground["AnchorMax (X)"] = 0.0;
                            categoriesBackground["AnchorMax (Y)"] = 1.0;
                            categoriesBackground["OffsetMin (X)"] = 0.0;
                            categoriesBackground["OffsetMin (Y)"] = 0.0;
                            categoriesBackground["OffsetMax (X)"] = 314.0;
                            categoriesBackground["OffsetMax (Y)"] = 0.0;
                        }

                        if (categories["Categories Scroll"] is JObject categoriesScroll)
                        {
                            categoriesScroll["Scroll Type"] = "Vertical";

                            if (categoriesScroll["Scrollbar Settings"] is JObject scrollbarSettings)
                            {
                                scrollbarSettings["Invert"] = false;
                                scrollbarSettings["Size"] = 5.0;
                            }

                            categoriesScroll["Scroll Size"] = 505.0;
                            categoriesScroll["AnchorMin (X)"] = 0.0;
                            categoriesScroll["AnchorMin (Y)"] = 0.0;
                            categoriesScroll["AnchorMax (X)"] = 1.0;
                            categoriesScroll["AnchorMax (Y)"] = 1.0;
                            categoriesScroll["OffsetMin (X)"] = 35.0;
                            categoriesScroll["OffsetMin (Y)"] = 20.0;
                            categoriesScroll["OffsetMax (X)"] = -15.0;
                            categoriesScroll["OffsetMax (Y)"] = -102.0;
                        }

                        if (categories["Admin Mode Button"] is JObject adminModeButton)
                            if (adminModeButton["Background"] is JObject background)
                            {
                                background["AnchorMin (X)"] = 0.0;
                                background["AnchorMin (Y)"] = 1.0;
                                background["AnchorMax (X)"] = 0.0;
                                background["AnchorMax (Y)"] = 1.0;
                                background["OffsetMin (X)"] = 32.0;
                                background["OffsetMin (Y)"] = -26.0;
                                background["OffsetMax (X)"] = 132.0;
                                background["OffsetMax (Y)"] = 0.0;
                            }

                        if (categories["Admin Mode Selected Button"] is JObject adminModeSelectedButton)
                            if (adminModeSelectedButton["Background"] is JObject background)
                            {
                                background["AnchorMin (X)"] = 0.0;
                                background["AnchorMin (Y)"] = 1.0;
                                background["AnchorMax (X)"] = 0.0;
                                background["AnchorMax (Y)"] = 1.0;
                                background["OffsetMin (X)"] = 32.0;
                                background["OffsetMin (Y)"] = -26.0;
                                background["OffsetMax (X)"] = 132.0;
                                background["OffsetMax (Y)"] = 0.0;
                            }

                        if (categories["Edit Category Button"] is JObject editCategoryButton)
                            if (editCategoryButton["Background"] is JObject background)
                            {
                                background["AnchorMin (X)"] = 0.0;
                                background["AnchorMin (Y)"] = 0.5;
                                background["AnchorMax (X)"] = 0.0;
                                background["AnchorMax (Y)"] = 0.5;
                                background["OffsetMin (X)"] = 0.0;
                                background["OffsetMin (Y)"] = -14.0;
                                background["OffsetMax (X)"] = 101.0;
                                background["OffsetMax (Y)"] = 14.0;
                            }
                    }

                    #endregion Categories

                    break;
                }
                case "t3":
                {
                    #region Header

                    if (data?["UI Settings"]?["Header"] is JObject header)
                    {
                        header["Edit Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_36835bdb77",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 104.0,
                                ["OffsetMin (Y)"] = 5.0,
                                ["OffsetMax (X)"] = 204.0,
                                ["OffsetMax (Y)"] = 31.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_4915bf8f11",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT HEADER"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Image_7c93d3f29a",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };
                        header["Edit PopUps Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_3c2b224258",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 208.0,
                                ["OffsetMin (Y)"] = 5.0,
                                ["OffsetMax (X)"] = 308.0,
                                ["OffsetMax (Y)"] = 31.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_ae91c41608",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT POPUPS"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Image_837095a371",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };
                        header["Edit Pages Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_3c2b224258",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 312.0,
                                ["OffsetMin (Y)"] = 5.0,
                                ["OffsetMax (X)"] = 412.0,
                                ["OffsetMax (Y)"] = 31.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_ae91c41608",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT PAGES"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Image_837095a371",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };
                    }

                    #endregion Header

                    #region Edit Button

                    if (data?["UI Settings"]?["Content"] is JObject content)
                        content["Edit Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.0,
                                ["OffsetMin (X)"] = 0.0,
                                ["OffsetMin (Y)"] = -30.0,
                                ["OffsetMax (X)"] = 100.0,
                                ["OffsetMax (Y)"] = -4.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT CONTENT"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };

                    #endregion Edit Button

                    #region Categories

                    if (data?["UI Settings"]?["Categories"] is JObject categories)
                    {
                        if (categories["Background"] is JObject categoriesBackground)
                        {
                            categoriesBackground["AnchorMin (X)"] = 0.5;
                            categoriesBackground["AnchorMin (Y)"] = 0.5;
                            categoriesBackground["AnchorMax (X)"] = 0.5;
                            categoriesBackground["AnchorMax (Y)"] = 0.5;
                            categoriesBackground["OffsetMin (X)"] = -512.0;
                            categoriesBackground["OffsetMin (Y)"] = 155.0;
                            categoriesBackground["OffsetMax (X)"] = 512.0;
                            categoriesBackground["OffsetMax (Y)"] = 205.0;
                        }

                        if (categories["Categories Scroll"] is JObject categoriesScroll)
                        {
                            categoriesScroll["Scroll Type"] = "Horizontal";

                            if (categoriesScroll["Scrollbar Settings"] is JObject scrollbarSettings)
                            {
                                scrollbarSettings["Auto Hide"] = true;
                                scrollbarSettings["Invert"] = true;
                                scrollbarSettings["Size"] = 2.0;
                                scrollbarSettings["Handle Color"] = new JObject
                                {
                                    ["HEX"] = "#4B68FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                };
                                scrollbarSettings["Highlight Color"] = new JObject
                                {
                                    ["HEX"] = "#4B68FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                };
                                scrollbarSettings["Pressed Color"] = new JObject
                                {
                                    ["HEX"] = "#4B68FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                };
                                scrollbarSettings["Track Color"] = new JObject
                                {
                                    ["HEX"] = "#2C2F31",
                                    ["Opacity (0 - 100)"] = 100.0
                                };
                            }

                            categoriesScroll["Scroll Size"] = 1000.0;
                            categoriesScroll["AnchorMin (X)"] = 0.0;
                            categoriesScroll["AnchorMin (Y)"] = 1.0;
                            categoriesScroll["AnchorMax (X)"] = 0.0;
                            categoriesScroll["AnchorMax (Y)"] = 1.0;
                            categoriesScroll["OffsetMin (X)"] = 12.0;
                            categoriesScroll["OffsetMin (Y)"] = -50.0;
                            categoriesScroll["OffsetMax (X)"] = 1012.0;
                            categoriesScroll["OffsetMax (Y)"] = 0.0;
                        }

                        if (categories["Admin Mode Button"] is JObject adminModeButton)
                            if (adminModeButton["Background"] is JObject background)
                            {
                                background["AnchorMin (X)"] = 0.0;
                                background["AnchorMin (Y)"] = 1.0;
                                background["AnchorMax (X)"] = 0.0;
                                background["AnchorMax (Y)"] = 1.0;
                                background["OffsetMin (X)"] = 0.0;
                                background["OffsetMin (Y)"] = 95.0;
                                background["OffsetMax (X)"] = 100.0;
                                background["OffsetMax (Y)"] = 121.0;
                            }

                        if (categories["Admin Mode Selected Button"] is JObject adminModeSelectedButton)
                            if (adminModeSelectedButton["Background"] is JObject background)
                            {
                                background["AnchorMin (X)"] = 0.0;
                                background["AnchorMin (Y)"] = 1.0;
                                background["AnchorMax (X)"] = 0.0;
                                background["AnchorMax (Y)"] = 1.0;
                                background["OffsetMin (X)"] = 0.0;
                                background["OffsetMin (Y)"] = 95.0;
                                background["OffsetMax (X)"] = 100.0;
                                background["OffsetMax (Y)"] = 121.0;
                            }

                        if (categories["Edit Category Button"] is JObject editCategoryButton)
                        {
                            editCategoryButton["Width"] = 101.0;
                            if (editCategoryButton["Background"] is JObject background)
                            {
                                background["AnchorMin (X)"] = 0.0;
                                background["AnchorMin (Y)"] = 0.5;
                                background["AnchorMax (X)"] = 0.0;
                                background["AnchorMax (Y)"] = 0.5;
                                background["OffsetMin (X)"] = 0.0;
                                background["OffsetMin (Y)"] = -14.0;
                                background["OffsetMax (X)"] = 101.0;
                                background["OffsetMax (Y)"] = 14.0;
                            }
                        }
                    }

                    #endregion Categories

                    break;
                }
                case "t5":
                {
                    #region Header

                    if (data?["UI Settings"]?["Header"] is JObject header)
                    {
                        header["Edit Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_3e87759150",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 0.0,
                                ["OffsetMin (Y)"] = 4.0,
                                ["OffsetMax (X)"] = 100.0,
                                ["OffsetMax (Y)"] = 30.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_3e673cf5cc",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT HEADER"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Image_3d1af320c2",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };
                        header["Edit PopUps Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_518b0e2e31",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 1.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 366.0,
                                ["OffsetMin (Y)"] = 4.0,
                                ["OffsetMax (X)"] = 466.0,
                                ["OffsetMax (Y)"] = 30.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_be18245bc2",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT POPUPS"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Image_51205b563a",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };
                        header["Edit Pages Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_518b0e2e31",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 1.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 262.0,
                                ["OffsetMin (Y)"] = 4.0,
                                ["OffsetMax (X)"] = 362.0,
                                ["OffsetMax (Y)"] = 30.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_be18245bc2",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT PAGES"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Image_51205b563a",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };
                    }

                    #endregion Header

                    #region Content

                    if (data?["UI Settings"]?["Content"] is JObject content)
                        content["Edit Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 0.0,
                                ["OffsetMin (Y)"] = 4.0,
                                ["OffsetMax (X)"] = 100.0,
                                ["OffsetMax (Y)"] = 30.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT CONTENT"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };

                    #endregion Content

                    #region Categories

                    if (data?["UI Settings"]?["Categories"] is JObject categories)
                    {
                        if (categories["Background"] is JObject categoriesBackground)
                        {
                            categoriesBackground["AnchorMin (X)"] = 0.5;
                            categoriesBackground["AnchorMin (Y)"] = 0.5;
                            categoriesBackground["AnchorMax (X)"] = 0.5;
                            categoriesBackground["AnchorMax (Y)"] = 0.5;
                            categoriesBackground["OffsetMin (X)"] = -452.0;
                            categoriesBackground["OffsetMin (Y)"] = -225.0;
                            categoriesBackground["OffsetMax (X)"] = -237.0;
                            categoriesBackground["OffsetMax (Y)"] = 225.0;
                        }

                        categories["Use scrolling?"] = false;

                        if (categories["Categories Scroll"] is JObject categoriesScroll)
                        {
                            categoriesScroll["Scroll Type"] = "Vertical";

                            if (categoriesScroll["Scrollbar Settings"] is JObject scrollbarSettings)
                            {
                                scrollbarSettings["Auto Hide"] = true;
                                scrollbarSettings["Invert"] = true;
                                scrollbarSettings["Size"] = 2.0;
                                scrollbarSettings["Handle Color"] = new JObject
                                {
                                    ["HEX"] = "#4B68FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                };
                                scrollbarSettings["Highlight Color"] = new JObject
                                {
                                    ["HEX"] = "#4B68FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                };
                                scrollbarSettings["Pressed Color"] = new JObject
                                {
                                    ["HEX"] = "#4B68FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                };
                                scrollbarSettings["Track Color"] = new JObject
                                {
                                    ["HEX"] = "#2C2F31",
                                    ["Opacity (0 - 100)"] = 100.0
                                };
                            }

                            categoriesScroll["Scroll Size"] = 0.0;
                            categoriesScroll["AnchorMin (X)"] = 0.0;
                            categoriesScroll["AnchorMin (Y)"] = 0.0;
                            categoriesScroll["AnchorMax (X)"] = 1.0;
                            categoriesScroll["AnchorMax (Y)"] = 1.0;
                            categoriesScroll["OffsetMin (X)"] = 0.0;
                            categoriesScroll["OffsetMin (Y)"] = 0.0;
                            categoriesScroll["OffsetMax (X)"] = 0.0;
                            categoriesScroll["OffsetMax (Y)"] = 0.0;
                        }

                        if (categories["Category Title"] is JObject categoryTitle)
                        {
                            categoryTitle["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_b212334f14",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#8B8B8B",
                                    ["Opacity (0 - 100)"] = 5.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "assets/content/ui/ui.background.tile.psd",
                                ["Material"] = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 0.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            };
                            categoryTitle["Selected Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_8ed1c5e211",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#8B8B8B",
                                    ["Opacity (0 - 100)"] = 15.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "assets/content/ui/ui.background.tile.psd",
                                ["Material"] = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 0.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            };
                        }

                        if (categories["Admin Mode Button"] is JObject adminModeButton)
                            if (adminModeButton["Background"] is JObject background)
                            {
                                background["AnchorMin (X)"] = 1.0;
                                background["AnchorMin (Y)"] = 1.0;
                                background["AnchorMax (X)"] = 1.0;
                                background["AnchorMax (Y)"] = 1.0;
                                background["OffsetMin (X)"] = 470.0;
                                background["OffsetMin (Y)"] = 4.0;
                                background["OffsetMax (X)"] = 570.0;
                                background["OffsetMax (Y)"] = 30.0;
                            }

                        if (categories["Admin Mode Selected Button"] is JObject adminModeSelectedButton)
                            if (adminModeSelectedButton["Background"] is JObject background)
                            {
                                background["AnchorMin (X)"] = 1.0;
                                background["AnchorMin (Y)"] = 1.0;
                                background["AnchorMax (X)"] = 1.0;
                                background["AnchorMax (Y)"] = 1.0;
                                background["OffsetMin (X)"] = 470.0;
                                background["OffsetMin (Y)"] = 4.0;
                                background["OffsetMax (X)"] = 570.0;
                                background["OffsetMax (Y)"] = 30.0;
                            }

                        if (categories["Edit Category Button"] is JObject editCategoryButton)
                        {
                            editCategoryButton["Width"] = 101.0;
                            if (editCategoryButton["Background"] is JObject background)
                            {
                                background["AnchorMin (X)"] = 0.0;
                                background["AnchorMin (Y)"] = 0.5;
                                background["AnchorMax (X)"] = 0.0;
                                background["AnchorMax (Y)"] = 0.5;
                                background["OffsetMin (X)"] = 0.0;
                                background["OffsetMin (Y)"] = -14.0;
                                background["OffsetMax (X)"] = 101.0;
                                background["OffsetMax (Y)"] = 14.0;
                            }
                        }
                    }

                    #endregion Categories

                    break;
                }
            }
        }
    }

    public class V2_0_to_01_Migration : IMigration
    {
        public string Name => "2.0.1";
        public VersionNumber FromVersion => new(2, 0, 0);
        public VersionNumber ToVersion => new(2, 0, 1);

        public bool IsNeeded()
        {
            try
            {
                var configPath = Path.Combine(Interface.Oxide.ConfigDirectory, "ServerPanel.json");
                if (!File.Exists(configPath)) return false;

                var configJson = File.ReadAllText(configPath);
                var configData = JObject.Parse(configJson);

                var version = configData["Version"]?.ToObject<VersionNumber>();
                return version == new VersionNumber(2, 0, 0);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error checking V1 to V2 migration need: {ex.Message}");
                return false;
            }
        }

        public bool Execute()
        {
            try
            {
                if (!MigrateTemplateData())
                {
                    return false;
                }

                Instance?.Puts("=== Migration completed! ===");
                return true;
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Migration failed: {ex.Message}");
                return false;
            }
        }

        private bool MigrateTemplateData()
        {
            #region Read Template.json

            var templatePath = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Template.json");
            if (!File.Exists(templatePath))
                return false;

            string json;
            try
            {
                json = File.ReadAllText(templatePath);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error reading Template.json: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(json))
            {
                Instance?.PrintError("Template.json is empty");
                return false;
            }

            JObject data;
            try
            {
                data = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error parsing Template.json: {ex.Message}");
                return false;
            }

            if (data == null)
            {
                Instance?.PrintError("Failed to parse Template.json: result is null.");
                return false;
            }

            var templateName = data?["UI Settings"]?["ID (DONT CHANGE)"]?.Value<string>();
            if (templateName == null)
            {
                Instance?.PrintError("Template name not found");
                return false;
            }

            #endregion Read Template.json

            #region Migrations

            #region Header

            if (data?["UI Settings"]?["Header"] is JObject header)
                header["Edit Pages Button"] = new JObject
                {
                    ["Background"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Panel"),
                        ["Type (Label/Panel/Button/Image)"] = "Panel",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#175782",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 1.0,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 312.0,
                        ["OffsetMin (Y)"] = 4.0,
                        ["OffsetMax (X)"] = 412.0,
                        ["OffsetMax (Y)"] = 30.0
                    },
                    ["Title"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Label"),
                        ["Type (Label/Panel/Button/Image)"] = "Label",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray("EDIT PAGES"),
                        ["Font Size"] = 10,
                        ["Font"] = 0,
                        ["Align"] = "MiddleLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.0,
                        ["AnchorMax (X)"] = 1.0,
                        ["AnchorMax (Y)"] = 1.0,
                        ["OffsetMin (X)"] = 27.0,
                        ["OffsetMin (Y)"] = 0.0,
                        ["OffsetMax (X)"] = 0.0,
                        ["OffsetMax (Y)"] = 0.0
                    },
                    ["Icon"] = new JObject
                    {
                        ["Enabled?"] = true,
                        ["Visible"] = true,
                        ["Name"] = GenerateElementGUID("Image"),
                        ["Type (Label/Panel/Button/Image)"] = "Image",
                        ["Color"] = new JObject
                        {
                            ["HEX"] = "#68C2FF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Text"] = new JArray(),
                        ["Font Size"] = 14,
                        ["Font"] = 0,
                        ["Align"] = "UpperLeft",
                        ["Text Color"] = new JObject
                        {
                            ["HEX"] = "#FFFFFF",
                            ["Opacity (0 - 100)"] = 100.0
                        },
                        ["Command ({user} - user steamid)"] = "",
                        ["Image"] = "assets/icons/level_metal.png",
                        ["Cursor Enabled"] = false,
                        ["Keyboard Enabled"] = false,
                        ["Sprite"] = "",
                        ["Material"] = "",
                        ["AnchorMin (X)"] = 0.0,
                        ["AnchorMin (Y)"] = 0.5,
                        ["AnchorMax (X)"] = 0.0,
                        ["AnchorMax (Y)"] = 0.5,
                        ["OffsetMin (X)"] = 10.0,
                        ["OffsetMin (Y)"] = -6.0,
                        ["OffsetMax (X)"] = 22.0,
                        ["OffsetMax (Y)"] = 6.0
                    },
                    ["Description Background"] = null,
                    ["Description Title"] = null
                };

            #endregion Header

            MigrateTemplateDataByTemplateName(ref data, templateName);

            #endregion Migrations

            #region Save Template.json

            string updatedJson;
            try
            {
                updatedJson = JsonConvert.SerializeObject(data, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Failed to serialize migrated template data: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(updatedJson))
            {
                Instance?.PrintError("Serialized template data is empty. Aborting write to prevent data loss.");
                return false;
            }

            try
            {
                File.WriteAllText(templatePath, updatedJson);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error writing Template.json: {ex.Message}");
                return false;
            }

            #endregion Save Template.json

            Instance?.Puts("  Template.json migrated");
            return true;
        }

        private void MigrateTemplateDataByTemplateName(ref JObject data, string templateName)
        {
            switch (templateName)
            {
                case "t1":
                {
                    break;
                }
                case "t1_1":
                {
                    break;
                }
                case "t2":
                {
                    #region Header

                    if (data?["UI Settings"]?["Header"] is JObject header)
                        header["Edit Pages Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = -74.0,
                                ["OffsetMin (Y)"] = -26.0,
                                ["OffsetMax (X)"] = 26.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT PAGES"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };

                    #endregion Header

                    break;
                }
                case "t3":
                {
                    #region Header

                    if (data?["UI Settings"]?["Header"] is JObject header)
                        header["Edit Pages Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_3c2b224258",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 312.0,
                                ["OffsetMin (Y)"] = 5.0,
                                ["OffsetMax (X)"] = 412.0,
                                ["OffsetMax (Y)"] = 31.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_ae91c41608",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT PAGES"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Image_837095a371",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };

                    #endregion Header

                    break;
                }
                case "t5":
                {
                    #region Header

                    if (data?["UI Settings"]?["Header"] is JObject header)
                        header["Edit Pages Button"] = new JObject
                        {
                            ["Background"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Panel_518b0e2e31",
                                ["Type (Label/Panel/Button/Image)"] = "Panel",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#175782",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 1.0,
                                ["AnchorMin (Y)"] = 1.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 262.0,
                                ["OffsetMin (Y)"] = 4.0,
                                ["OffsetMax (X)"] = 362.0,
                                ["OffsetMax (Y)"] = 30.0
                            },
                            ["Title"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Label_be18245bc2",
                                ["Type (Label/Panel/Button/Image)"] = "Label",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray("EDIT PAGES"),
                                ["Font Size"] = 10,
                                ["Font"] = 0,
                                ["Align"] = "MiddleLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.0,
                                ["AnchorMax (X)"] = 1.0,
                                ["AnchorMax (Y)"] = 1.0,
                                ["OffsetMin (X)"] = 27.0,
                                ["OffsetMin (Y)"] = 0.0,
                                ["OffsetMax (X)"] = 0.0,
                                ["OffsetMax (Y)"] = 0.0
                            },
                            ["Icon"] = new JObject
                            {
                                ["Enabled?"] = true,
                                ["Visible"] = true,
                                ["Name"] = "Image_51205b563a",
                                ["Type (Label/Panel/Button/Image)"] = "Image",
                                ["Color"] = new JObject
                                {
                                    ["HEX"] = "#68C2FF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Text"] = new JArray(),
                                ["Font Size"] = 14,
                                ["Font"] = 0,
                                ["Align"] = "UpperLeft",
                                ["Text Color"] = new JObject
                                {
                                    ["HEX"] = "#FFFFFF",
                                    ["Opacity (0 - 100)"] = 100.0
                                },
                                ["Command ({user} - user steamid)"] = "",
                                ["Image"] = "assets/icons/level_metal.png",
                                ["Cursor Enabled"] = false,
                                ["Keyboard Enabled"] = false,
                                ["Sprite"] = "",
                                ["Material"] = "",
                                ["AnchorMin (X)"] = 0.0,
                                ["AnchorMin (Y)"] = 0.5,
                                ["AnchorMax (X)"] = 0.0,
                                ["AnchorMax (Y)"] = 0.5,
                                ["OffsetMin (X)"] = 10.0,
                                ["OffsetMin (Y)"] = -6.0,
                                ["OffsetMax (X)"] = 22.0,
                                ["OffsetMax (Y)"] = 6.0
                            },
                            ["Description Background"] = null,
                            ["Description Title"] = null
                        };

                    #endregion Header

                    break;
                }
            }
        }
    }

    public class V2_01_to_02_Migration : IMigration
    {
        public string Name => "2.0.2";
        public VersionNumber FromVersion => new(2, 0, 1);
        public VersionNumber ToVersion => new(2, 0, 2);

        public bool IsNeeded()
        {
            try
            {
                var configPath = Path.Combine(Interface.Oxide.ConfigDirectory, "ServerPanel.json");
                if (!File.Exists(configPath)) return false;

                var configJson = File.ReadAllText(configPath);
                var configData = JObject.Parse(configJson);

                var version = configData["Version"]?.ToObject<VersionNumber>();
                return version == new VersionNumber(2, 0, 1);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error checking V1 to V2 migration need: {ex.Message}");
                return false;
            }
        }

        public bool Execute()
        {
            try
            {
                if (!MigrateTemplateData())
                {
                    Instance?.PrintError("Template.json migration failed");
                    return false;
                }

                Instance?.Puts("=== Migration completed! ===");
                return true;
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Migration failed: {ex.Message}");
                return false;
            }
        }

        private bool MigrateTemplateData()
        {
            #region Read Template.json

            var templatePath = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Template.json");
            if (!File.Exists(templatePath))
                return false;

            string json;
            try
            {
                json = File.ReadAllText(templatePath);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error reading Template.json: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(json))
            {
                Instance?.PrintError("Template.json is empty");
                return false;
            }

            #endregion Read Template.json

            #region Migrations

            json = json.Replace("https://gitlab.com/TheMevent/Images/",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/");

            #endregion Migrations

            #region Save Template.json

            try
            {
                File.WriteAllText(templatePath, json);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error writing Template.json: {ex.Message}");
                return false;
            }

            #endregion Save Template.json

            Instance?.Puts("  Images URLs migrated");
            return true;
        }
    }

    public class V2_014_to_015_Migration : IMigration
    {
        public string Name => "2.0.15";
        public VersionNumber FromVersion => new(2, 0, 14);
        public VersionNumber ToVersion => new(2, 0, 15);

        public bool IsNeeded()
        {
            try
            {
                var configPath = Path.Combine(Interface.Oxide.ConfigDirectory, "ServerPanel.json");
                if (!File.Exists(configPath)) return false;

                var configJson = File.ReadAllText(configPath);
                var configData = JObject.Parse(configJson);

                var version = configData["Version"]?.ToObject<VersionNumber>();
                return version != null && version < new VersionNumber(2, 0, 15);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error checking V2.0.14 to V2.0.15 migration need: {ex.Message}");
                return false;
            }
        }

        public bool Execute()
        {
            try
            {
                if (!MigrateConfigData())
                {
                    Instance?.PrintError("ServerPanel.json migration failed");
                    return false;
                }

                Instance?.Puts("=== Migration completed! ===");
                return true;
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Migration failed: {ex.Message}");
                return false;
            }
        }

        private bool MigrateConfigData()
        {
            var configPath = Path.Combine(Interface.Oxide.ConfigDirectory, "ServerPanel.json");
            if (!File.Exists(configPath)) return false;

            string json;
            try
            {
                json = File.ReadAllText(configPath);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error reading ServerPanel.json: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(json))
            {
                Instance?.PrintError("ServerPanel.json is empty");
                return false;
            }

            JObject configData;
            try
            {
                configData = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error parsing ServerPanel.json: {ex.Message}");
                return false;
            }

            if (configData.Property("Wipe Time Format") == null)
            {
                configData.AddFirst(new JProperty("Wipe Time Format", "yyyy-MM-dd HH:mm:ss"));
                Instance?.Puts("  Added 'Wipe Time Format' with default value");
            }

            try
            {
                File.WriteAllText(configPath, JsonConvert.SerializeObject(configData, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Instance?.PrintError($"Error writing ServerPanel.json: {ex.Message}");
                return false;
            }

            Instance?.Puts("  Config migrated with WipeTimeFormat");
            return true;
        }
    }

    #endregion

    #region Utils

    private static string GenerateElementGUID(string elementType)
    {
        return $"{elementType}_{CuiHelper.GetGuid().Substring(0, 10)}";
    }


    private void InitializeMigrations()
    {
        availableMigrations = new Dictionary<string, IMigration>();

        RegisterMigration(new CategoryTitleUiRefactoring());
        RegisterMigration(new LocalizationFormatMigration());
        RegisterMigration(new V1ToV2Migration());
        RegisterMigration(new V2_0_to_01_Migration());
        RegisterMigration(new V2_01_to_02_Migration());
        RegisterMigration(new V2_014_to_015_Migration());

        Puts($"Loaded {availableMigrations.Count} migrations");
    }

    private void RegisterMigration(IMigration migration)
    {
        availableMigrations[migration.Name] = migration;
    }

    private void CreateBackup(string migrationName)
    {
        try
        {
            var backupBaseDir = Path.Combine(Interface.Oxide.DataDirectory, "ServerPanelMigrations", "backup");
            
            // Создаем базовую директорию для бекапов, если её нет
            if (!Directory.Exists(backupBaseDir))
            {
                Directory.CreateDirectory(backupBaseDir);
            }
            
            var backupDir = Path.Combine(backupBaseDir,
                $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{migrationName.Replace(" ", "_").Replace(":", "-")}");
            
            // Создаем директорию для конкретного бекапа
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }
            
            var filesBackedUp = 0;
            var filesToBackup = new[]
            {
                ("Template.json", Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Template.json")),
                ("Categories.json", Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "Categories.json")),
                ("HeaderFields.json", Path.Combine(Interface.Oxide.DataDirectory, "ServerPanel", "HeaderFields.json")),
                ("ServerPanel.json", Path.Combine(Interface.Oxide.ConfigDirectory, "ServerPanel.json"))
            };
            
            foreach (var (fileName, filePath) in filesToBackup)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var backupPath = Path.Combine(backupDir, fileName);
                        File.Copy(filePath, backupPath, overwrite: true);
                        filesBackedUp++;
                    }
                }
                catch (IOException ex)
                {
                    PrintError($"IO error when backing up {fileName}: {ex.Message}");
                    throw new Exception($"Failed to create backup: IO error with {fileName}. File may be locked or disk may be full.", ex);
                }
            }
            
            if (filesBackedUp > 0)
            {
                Puts($"Backup created successfully: {backupDir} ({filesBackedUp} file(s))");
            }
            else
            {
                PrintError("Warning: No files were backed up. This may indicate a problem with file paths.");
            }
        }
        catch (Exception ex)
        {
            PrintError($"Backup failed: {ex.Message}");
            PrintError("Check disk space and file permissions");
            throw;
        }
    }

    private bool ExecuteMigration(IMigration migration, bool force = false)
    {
        try
        {
            if (!force && !migration.IsNeeded())
            {
                Puts($"Migration {migration.Name} not needed");
                return false;
            }

            Puts($"Starting migration: {migration.Name}");
            if (force)
                Puts("FORCED execution");

            CreateBackup(migration.Name);
            if (!migration.Execute())
            {
                PrintError($"Migration {migration.Name} failed");
                return false;
            }

            Puts($"Migration completed: {migration.Name}");

            ServerPanel?.Call("API_OnMigrationComplete");
            return true;
        }
        catch (Exception ex)
        {
            PrintError($"Migration failed {migration.Name}: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Utils

    private static VersionNumber GetCurrentConfigVersion()
    {
        try
        {
            var configPath = Path.Combine(Interface.Oxide.ConfigDirectory, "ServerPanel.json");
            if (!File.Exists(configPath)) return default;

            var configJson = File.ReadAllText(configPath);
            var configData = JObject.Parse(configJson);
            return configData["Version"]?.ToObject<VersionNumber>() ?? default;
        }
        catch
        {
            return default;
        }
    }

    private static VersionNumber GetTargetVersion()
    {
        if (Instance.ServerPanel == null)
        {
            return default;
        }

        return Instance.ServerPanel.Version;
    }

    private List<IMigration> FindRequiredMigrations(VersionNumber currentVersion, VersionNumber targetVersion)
    {
        var requiredMigrations = new List<IMigration>();
        var candidateMigrations = new List<IMigration>();

        foreach (var migration in availableMigrations.Values)
            if (migration.FromVersion >= currentVersion && migration.FromVersion < targetVersion)
                candidateMigrations.Add(migration);

        candidateMigrations.Sort((a, b) =>
        {
            if (a.FromVersion < b.FromVersion) return -1;
            if (a.FromVersion > b.FromVersion) return 1;
            return 0;
        });

        var lastVersion = currentVersion;
        for (var i = 0; i < candidateMigrations.Count; i++)
        {
            var migration = candidateMigrations[i];

            if (migration.FromVersion == lastVersion)
            {
                requiredMigrations.Add(migration);
                lastVersion = migration.ToVersion;

                if (lastVersion >= targetVersion)
                    break;
            }
            else if (migration.FromVersion > lastVersion)
            {
                break;
            }
        }

        return requiredMigrations;
    }

    #endregion Utils
}