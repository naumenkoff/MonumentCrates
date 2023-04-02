using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Monument Crates", "naumenkoff", "0.4.2")]
    internal class MonumentCrates : RustPlugin
    {
        private readonly List<string> _messages = new List<string>();
        private Configuration _configuration;
        private DynamicConfigFile _dynamicConfigFile;
        private bool _isMenuShown;
        private List<LootCrate> _lootCrates = new List<LootCrate>();
        private Timer _timer;

        #region Methods

        /// <summary>
        ///     This method is used to kill all the LootContainers that match the specified short prefab name.
        /// </summary>
        /// <returns>The number of killed LootContainers.</returns>
        private int KillLootContainers(string shortPrefabName)
        {
            var suitable = GetLootContainers().Where(x => x.ShortPrefabName == shortPrefabName).ToList();
            var killed = KillLootContainers(suitable);
            return killed;
        }

        /// <summary>
        ///     This method is used to kill all the LootContainers on the map.
        /// </summary>
        /// <returns>The number of killed LootContainers.</returns>
        private int KillLootContainers()
        {
            var killed = KillLootContainers(GetLootContainers());
            return killed;
        }

        /// <summary>
        ///     This method is used to kill a list of LootContainers.
        /// </summary>
        /// <param name="lootContainers">A list of LootContainer objects that will be killed.</param>
        /// <returns>The number of killed LootContainers.</returns>
        private int KillLootContainers(List<LootContainer> lootContainers)
        {
            var killed = 0;
            foreach (var lootContainer in lootContainers)
            {
                lootContainer.Kill();
                killed++;
            }

            NextTick(UpdateLootContainersButtons);
            return killed;
        }

        /// <summary>
        ///     This method creates a new BaseEntity of a given LootCrate prefab name, at the specified position and rotation.
        /// </summary>
        /// <param name="lootCrate">A LootCrate object that defines the prefab name, position and rotation of the LootContainer.</param>
        /// <returns>The created BaseEntity object.</returns>
        private static BaseEntity CreateLootContainerEntity(LootCrate lootCrate)
        {
            return GameManager.server.CreateEntity(lootCrate.PrefabName,
                lootCrate.ServerPosition, lootCrate.ServerRotation);
        }

        /// <summary>
        ///     This method spawns all the LootContainers specified in the _lootCrates collection.
        /// </summary>
        /// <returns>The number of spawned LootContainers.</returns>
        private int SpawnLootContainers()
        {
            var spawned = 0;
            foreach (var lootContainerEntity in _lootCrates.Select(CreateLootContainerEntity))
            {
                lootContainerEntity.Spawn();
                spawned++;
            }

            NextTick(UpdateLootContainersButtons);
            return spawned;
        }

        /// <summary>
        ///     This method checks whether to skip the specified LootContainer based on its short prefab name and a collection of
        ///     short names of existing loot containers.
        /// </summary>
        /// <param name="lootContainer">The LootContainer to check.</param>
        /// <param name="lootContainerNames">A collection of short names of existing loot containers.</param>
        /// <returns>true if the LootContainer should be skipped, false otherwise.</returns>
        private static bool ShouldSkipLootContainer(BaseNetworkable lootContainer,
            ICollection<string> lootContainerNames)
        {
            return lootContainerNames.Contains(lootContainer.ShortPrefabName) ||
                   lootContainer.ShortPrefabName.Contains("roadsign");
        }

        /// <summary>
        ///     This method updates the LootContainer buttons for all active admin players.
        /// </summary>
        private void UpdateLootContainersButtons()
        {
            var lootContainers = GetLootContainers();
            var lootContainersNames = new List<string>();
            foreach (var lootContainer in lootContainers.Where(lootContainer =>
                         ShouldSkipLootContainer(lootContainer, lootContainersNames) == false))
                lootContainersNames.Add(lootContainer.ShortPrefabName);

            var buttons = GetLootContainerButtons(lootContainersNames);
            foreach (var admin in BasePlayer.activePlayerList.Where(x => x.IsAdmin))
                DrawLootContainersTypes(admin, buttons);
        }

        /// <summary>
        ///     Returns a list of all networkable entities currently present on the server.
        /// </summary>
        /// <returns>A BufferList of BaseNetworkable objects.</returns>
        private static BufferList<BaseNetworkable> GetServerEntities()
        {
            return BaseNetworkable.serverEntities.entityList.Values;
        }

        /// <summary>
        ///     Returns a list of all LootContainer objects currently present on the server.
        /// </summary>
        /// <returns>A List of LootContainer objects.</returns>
        private static List<LootContainer> GetLootContainers()
        {
            return GetServerEntities().OfType<LootContainer>().ToList();
        }

        /// <summary>
        ///     Returns a list of all JunkPile objects currently present on the server.
        /// </summary>
        /// <returns>A List of JunkPile objects.</returns>
        private static List<JunkPile> GetJunkPiles()
        {
            return GetServerEntities().OfType<JunkPile>().ToList();
        }

        /// <summary>
        ///     Checks if a given LootContainer has already been added to the _lootCrates list.
        /// </summary>
        /// <param name="lootContainer">The LootContainer to check.</param>
        /// <returns>True if the LootContainer has already been added, false otherwise.</returns>
        private bool HasLootContainerAddedAlready(BaseEntity lootContainer)
        {
            return _lootCrates.Any(x =>
                x.ServerPosition == lootContainer.ServerPosition && x.PrefabName == lootContainer.PrefabName);
        }


        /// <summary>
        ///     Checks if there is a JunkPile object within a specified radius of a given loot container.
        /// </summary>
        /// <param name="junkPiles">The list of JunkPile objects to check against.</param>
        /// <param name="lootContainer">The LootContainer to check.</param>
        /// <param name="radius">The radius within which to check for JunkPile objects (default is 5).</param>
        /// <returns>True if there is a JunkPile object nearby, false otherwise.</returns>
        private static bool IsThereJunkPileNearby(List<JunkPile> junkPiles, BaseEntity lootContainer, int radius = 5)
        {
            return junkPiles.Any(junkPile => junkPile.Distance(lootContainer) <= radius);
        }

        /// <summary>
        ///     Updates the _lootCrates list by adding any new loot containers that meet the specified conditions.
        /// </summary>
        private void UpdateListOfLootContainers()
        {
            var lootContainers = GetLootContainers();
            var junkPiles = GetJunkPiles();
            var containerTypes = new List<string>();
            var newContainers = 0;
            foreach (var lootContainer in lootContainers)
            {
                if (_configuration.WhitelistedContainers.Contains(lootContainer.ShortPrefabName) == false) continue;
                if (HasLootContainerAddedAlready(lootContainer)) continue;
                if (IsThereJunkPileNearby(junkPiles, lootContainer)) continue;

                var lootCrate = new LootCrate(lootContainer);
                _lootCrates.Add(lootCrate);

                newContainers++;

                if (containerTypes.Contains(lootCrate.PrefabName)) continue;
                containerTypes.Add(lootCrate.PrefabName);
            }

            NextTick(UpdateLootContainersButtons);

            foreach (var admin in BasePlayer.activePlayerList.Where(x => x.IsAdmin))
                DrawMessage(admin, $"Added {newContainers} new LootContainers of {containerTypes.Count} types.");
        }

        #endregion

        #region User Interface

        private int GetNumberOfLootContainers(string prefabName)
        {
            return _lootCrates?.Count(x => x.PrefabName == prefabName) ?? 0;
        }

        private void DrawUIShowHideButton(BasePlayer player, bool isMenuActive)
        {
            DestroyUI(player);
            _isMenuShown = isMenuActive;
            var cuiElementContainer = new CuiElementContainer();
            cuiElementContainer.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "0.9 0.96", AnchorMax = "1 1"
                },
                Button =
                {
                    Sprite = "assets/content/textures/generic/fulltransparent.tga",
                    Color = "0 0 0 0",
                    Command = isMenuActive
                        ? _commandList[nameof(HideUserInterfaceCommand)]
                        : _commandList[nameof(ShowUserInterfaceCommand)]
                },
                Text =
                {
                    Text = isMenuActive ? "Hide menu" : "Show menu", Align = TextAnchor.MiddleCenter,
                    FontSize = 12, Color = GetColor("#edededff")
                }
            }, "Overlay", UILayers.OpenManagerButton);
            CuiHelper.AddUi(player, cuiElementContainer);
        }

        private void DrawUI(BasePlayer player, bool isActive)
        {
            DestroyUI(player);
            DrawUIShowHideButton(player, isActive);
            _isMenuShown = isActive;
            if (!isActive) return;
            DrawPluginSettings(player);
            DrawJsonManagement(player);
            DrawLootContainerManagement(player);
            NextTick(UpdateLootContainersButtons);
            DrawMessage(player, string.Empty);
            DrawCratesCount(player);
        }

        private static CuiPanel CreatePanel(string anchorMin, string anchorMax)
        {
            return new CuiPanel
            {
                Image =
                {
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                    Color = GetColor("00000075")
                },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                CursorEnabled = true
            };
        }

        private static CuiButton CreateButton(string command, string anchorMin, string anchorMax, string text)
        {
            return new CuiButton
            {
                Button =
                {
                    Color = "0.25 0.25 0.25 0.5",
                    Command = command
                },

                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Text =
                {
                    Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                    Text = text
                }
            };
        }

        private static CuiElement CreateElement(string parent, string text, string anchorMin, string anchorMax)
        {
            return new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiTextComponent
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                        Text = text
                    },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }
            };
        }

        private static CuiLabel CreateLabel(string text, string anchorMin, string anchorMax)
        {
            return new CuiLabel
            {
                Text =
                {
                    Text = text,
                    Align = TextAnchor.MiddleCenter, FontSize = 8,
                    Color = "1 1 1 1"
                },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            };
        }

        private void DrawPluginSettings(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UILayers.PluginSection);
            if (!_isMenuShown) return;
            var cuiElementContainer = new CuiElementContainer();
            cuiElementContainer.Add(CreatePanel("0.04 0.25", "0.24 0.75"), "Overlay", UILayers.PluginSection);
            cuiElementContainer.Add(CreateElement(UILayers.PluginSection, "Plugin Settings", "0 0.8", "1 1"));
            cuiElementContainer.Add(
                CreateButton(_commandList[nameof(ClearConsoleCommand)], "0.1 0.45", "0.9 0.6", "Clear the Console"),
                UILayers.PluginSection);
            var isTimerDestroyed = _timer == null || _timer.Destroyed;
            cuiElementContainer.Add(
                CreateButton(
                    isTimerDestroyed
                        ? _commandList[nameof(EnableTimerCommand)]
                        : _commandList[nameof(DisableTimerCommand)], "0.1 0.65", "0.9 0.8",
                    isTimerDestroyed ? "Enable Timer" : "Disable Timer"), UILayers.PluginSection);
            CuiHelper.AddUi(player, cuiElementContainer);
        }

        private void DrawJsonManagement(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UILayers.JsonSection);
            if (!_isMenuShown) return;
            var cuiElementContainer = new CuiElementContainer();
            cuiElementContainer.Add(CreatePanel("0.28 0.25", "0.48 0.75"), "Overlay", UILayers.JsonSection);
            cuiElementContainer.Add(CreateElement(UILayers.JsonSection, "Json Management", "0 0.8", "1 1"));
            cuiElementContainer.Add(
                CreateButton(
                    _configuration.Autosave
                        ? _commandList[nameof(DisableAutosaveCommand)]
                        : _commandList[nameof(EnableAutosaveCommand)], "0.1 0.68", "0.9 0.8",
                    _configuration.Autosave ? "Disable Autosave" : "Enable Autosave"), UILayers.JsonSection);
            cuiElementContainer.Add(
                CreateButton(_commandList[nameof(SaveLootContainersCommand)], "0.1 0.36", "0.9 0.48",
                    "Save LootContainers"), UILayers.JsonSection);
            cuiElementContainer.Add(
                CreateButton(_commandList[nameof(ClearLootContainersCommand)], "0.1 0.52", "0.9 0.64",
                    "Clear LootContainers"), UILayers.JsonSection);
            CuiHelper.AddUi(player, cuiElementContainer);
        }

        private void DrawLootContainerManagement(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UILayers.ManagerSection);
            if (!_isMenuShown) return;
            var cuiElementContainer = new CuiElementContainer();
            cuiElementContainer.Add(CreatePanel("0.76 0.25", "0.96 0.75"), "Overlay", UILayers.ManagerSection);
            cuiElementContainer.Add(CreateElement(UILayers.ManagerSection, "LootContainer Management", "0 0.8", "1 1"));
            cuiElementContainer.Add(
                CreateButton(_commandList[nameof(KillLootContainersCommand)], "0.1 0.52", "0.9 0.64",
                    "Kill LootContainers"), UILayers.ManagerSection);
            cuiElementContainer.Add(
                CreateButton(_commandList[nameof(SpawnLootContainersCommand)], "0.1 0.68", "0.9 0.8",
                    "Spawn LootContainers"), UILayers.ManagerSection);
            CuiHelper.AddUi(player, cuiElementContainer);
        }

        private void DrawLootContainersTypes(BasePlayer player, List<CuiButton> buttons)
        {
            CuiHelper.DestroyUi(player, UILayers.LootContainersInformation);
            if (!_isMenuShown) return;
            var cuiElementContainer = new CuiElementContainer();
            cuiElementContainer.Add(CreatePanel("0.52 0.25", "0.72 0.75"), "Overlay",
                UILayers.LootContainersInformation);
            foreach (var button in buttons) cuiElementContainer.Add(button, UILayers.LootContainersInformation);
            CuiHelper.AddUi(player, cuiElementContainer);
        }

        /// <summary>
        ///     Returns a list of CuiButton objects representing LootContainer buttons.
        /// </summary>
        /// <param name="names">A list of LootContainer names to be used for button text and command arguments.</param>
        /// <returns>A list of CuiButton objects representing LootContainer buttons.</returns>
        private List<CuiButton> GetLootContainerButtons(IReadOnlyList<string> names)
        {
            var buttons = new List<CuiButton>();
            if (names.Count == 0) return new List<CuiButton>();

            const int rows = 10;
            const int cols = 3;
            const double startX = 0.02f;
            const double startY = 0.02f;
            const double buttonWidth = (1.0 - (cols + 1.0) * startX) / cols;
            const double buttonHeight = (1.0 - (rows + 1.0) * startY) / rows;

            var currentElement = 0;
            for (var row = 0; row < rows; row++)
            for (var col = 0; col < cols; col++)
            {
                if (currentElement == names.Count) return buttons;
                var currentX = startX + col * (buttonWidth + startX);
                var currentY = 1.0 - (startY + row * (buttonHeight + startY) + buttonHeight);

                var button = new CuiButton
                {
                    Button =
                    {
                        Color = "0.25 0.25 0.25 0.75",
                        Command = _commandList[nameof(KillLootContainerCommand)] + ' ' +
                                  $"{(currentElement < names.Count ? names[currentElement] : string.Empty)}"
                    },
                    Text =
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 8,
                        Text = currentElement < names.Count ? names[currentElement].ToUpper() : "null!"
                    },
                    RectTransform =
                    {
                        AnchorMin = $"{currentX} {currentY}",
                        AnchorMax = $"{currentX + buttonWidth} {currentY + buttonHeight}"
                    }
                };
                buttons.Add(button);
                currentElement++;
            }

            return buttons;
        }

        private void DrawCratesCount(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UILayers.ContainersInformation);
            if (!_isMenuShown) return;
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(
                $"<color=lime>Military</color> {GetNumberOfLootContainers("assets/bundled/prefabs/radtown/crate_normal.prefab")} | ");
            stringBuilder.Append(
                $"Crate {GetNumberOfLootContainers("assets/bundled/prefabs/radtown/crate_normal_2.prefab")} | ");
            stringBuilder.Append(
                $"<color=yellow>Primitive</color> {GetNumberOfLootContainers("assets/bundled/prefabs/radtown/crate_basic.prefab")} | ");
            stringBuilder.Append(
                $"Ration {GetNumberOfLootContainers("assets/bundled/prefabs/radtown/foodbox.prefab")} | ");
            stringBuilder.Append(
                $"<color=cyan>Vehicle</color> {GetNumberOfLootContainers("assets/bundled/prefabs/radtown/vehicle_parts.prefab")} | ");
            stringBuilder.Append(
                $"Elite {GetNumberOfLootContainers("assets/bundled/prefabs/radtown/crate_elite.prefab")}");
            var cuiElementContainer = new CuiElementContainer();
            cuiElementContainer.Add(CreatePanel("0.52 0.16", "0.96 0.24"), "Overlay", UILayers.ContainersInformation);
            cuiElementContainer.Add(new CuiLabel
            {
                Text =
                {
                    Text = stringBuilder.ToString(),
                    Align = TextAnchor.MiddleCenter, FontSize = 12,
                    Color = "1 1 1 1"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UILayers.ContainersInformation);
            CuiHelper.AddUi(player, cuiElementContainer);
        }

        private string GetColumnMessages(int startIndex, int endIndex)
        {
            var message = string.Empty;
            for (var i = startIndex; i < endIndex; i++) message += GetMessageAt(_messages.Count - i) + "\n";
            return message;
        }

        private void DrawMessage(BasePlayer player, string text)
        {
            CuiHelper.DestroyUi(player, UILayers.PluginConsole);
            if (!string.IsNullOrEmpty(text)) _messages.Add(text);
            if (!_isMenuShown) return;
            if (_messages.Count > 18) _messages.RemoveRange(0, _messages.Count - 18);
            var cuiElementContainer = new CuiElementContainer();
            cuiElementContainer.Add(CreatePanel("0.04 0.16", "0.48 0.24"), "Overlay", UILayers.PluginConsole);
            cuiElementContainer.Add(CreateLabel(GetColumnMessages(0, 6), "0 0", "0.3 1"), UILayers.PluginConsole);
            cuiElementContainer.Add(CreateLabel(GetColumnMessages(6, 12), $"{1f / 3f} 0", "0.6 1"),
                UILayers.PluginConsole);
            cuiElementContainer.Add(CreateLabel(GetColumnMessages(12, 18), $"{2f / 3f} 0", "1 1"),
                UILayers.PluginConsole);
            CuiHelper.AddUi(player, cuiElementContainer);
        }

        private static void DestroyUI(BasePlayer player)
        {
            foreach (var layer in UILayers.Layers) CuiHelper.DestroyUi(player, layer);
        }

        private string GetMessageAt(int index)
        {
            return index > 0 && _messages.Count >= index ? _messages[index - 1] : string.Empty;
        }

        private static byte ParseString(string str, int startIndex, int length)
        {
            return byte.Parse(str.Substring(startIndex, length), NumberStyles.HexNumber);
        }

        private static string GetColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) hex = "#FFFFFFFF";
            var str = hex.Trim('#');
            if (str.Length == 6) str += "FF";
            Color color = new Color32(ParseString(str, 0, 2), ParseString(str, 2, 2), ParseString(str, 4, 2),
                ParseString(str, 6, 2));
            return $"{color.r:F2} {color.g:F2} {color.b:F2} {color.a:F2}";
        }

        private static class UILayers
        {
            public const string ContainersInformation = "ContainersInformation";
            public const string PluginConsole = "PluginConsole";
            public const string JsonSection = "JsonSection";
            public const string PluginSection = "PluginSection";
            public const string OpenManagerButton = "OpenContainersManager";
            public const string ManagerSection = "ManagerSection";
            public const string LootContainersInformation = "LootContainersInformation";

            public static readonly List<string> Layers = new List<string>
            {
                ContainersInformation,
                PluginConsole,
                JsonSection,
                PluginSection,
                OpenManagerButton,
                ManagerSection,
                LootContainersInformation
            };
        }

        #endregion

        #region Data

        private void SaveData()
        {
            _dynamicConfigFile.Settings = new JsonSerializerSettings
                { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
            _dynamicConfigFile.WriteObject(_lootCrates);
        }

        private void LoadData()
        {
            _dynamicConfigFile.Settings = new JsonSerializerSettings
                { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
            _lootCrates =
                Interface.Oxide.DataFileSystem.ReadObject<List<LootCrate>>(
                    $"MonumentCrates&{World.Seed}&{World.Size}") ?? new List<LootCrate>();
        }

        private class LootCrate
        {
            public LootCrate(BaseEntity lootContainer)
            {
                ServerPosition = lootContainer.ServerPosition;
                ServerRotation = lootContainer.ServerRotation;
                PrefabName = lootContainer.PrefabName;
            }

            [JsonConstructor]
            public LootCrate(string prefabName, Vector3 serverPosition, Quaternion serverRotation)
            {
                PrefabName = prefabName;
                ServerPosition = serverPosition;
                ServerRotation = serverRotation;
            }

            [JsonProperty("Prefab Name")] public string PrefabName { get; }
            [JsonProperty("Position")] public Vector3 ServerPosition { get; }
            [JsonProperty("Rotation")] public Quaternion ServerRotation { get; }
        }

        #endregion

        #region Config

        private class Configuration
        {
            public Configuration()
            {
                WhitelistedContainers = new List<string>
                {
                    "crate_normal", "crate_normal_2", "crate_basic", "foodbox", "vehicle_parts", "vehicle_parts",
                    "crate_elite"
                };
                Autosave = true;
                LootContainersKillingDelay = 30;
                LootContainersUIRefreshDelay = 2;
                LootContainersListUpdateDelay = 30;
            }

            [JsonProperty("Whitelisted LootContainers (Short Prefab Name)")]
            public List<string> WhitelistedContainers { get; }

            [JsonProperty("Automatic saving LootContainers to data file")]
            public bool Autosave { get; set; }

            [JsonProperty("Time between killing LootContainers when the timer is on")]
            public int LootContainersKillingDelay { get; }

            [JsonProperty("Time between redraws in the interface of the number of LootContainers by type")]
            public int LootContainersUIRefreshDelay { get; }

            [JsonProperty("Time between updates of the LootContainers list")]
            public int LootContainersListUpdateDelay { get; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _configuration = Config.ReadObject<Configuration>();
                if (_configuration == null) throw new JsonException();
            }
            catch
            {
                Debug.LogWarning("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_configuration);
            Debug.LogWarning("Saved changes to the configuration file");
        }

        protected override void LoadDefaultConfig()
        {
            _configuration = new Configuration();
            Debug.LogWarning("Set the default values in the configuration file");
        }

        #endregion

        #region Oxide Events

        private void OnPlayerConnected(BasePlayer player)
        {
            DrawUIShowHideButton(player, false);
            timer.Every(_configuration.LootContainersUIRefreshDelay, () => { DrawCratesCount(player); });
        }

        private void Unload()
        {
            if (_configuration.Autosave) SaveData();
            foreach (var player in BasePlayer.activePlayerList) DestroyUI(player);
        }

        private void OnServerInitialized()
        {
            _dynamicConfigFile = Interface.Oxide.DataFileSystem.GetFile($"MonumentCrates&{World.Seed}&{World.Size}");
            LoadData();
            foreach (var command in _commandList) AddCovalenceCommand(command.Value, command.Key);
            timer.Every(_configuration.LootContainersListUpdateDelay, UpdateListOfLootContainers);
            foreach (var player in BasePlayer.activePlayerList.Where(player => player.IsAdmin))
                OnPlayerConnected(player);
        }

        #endregion

        #region Commands

        private readonly Dictionary<string, string> _commandList = new Dictionary<string, string>
        {
            { nameof(ShowUserInterfaceCommand), "mc.ui.show" },
            { nameof(HideUserInterfaceCommand), "mc.ui.hide" },
            { nameof(EnableTimerCommand), "mc.timer.enable" },
            { nameof(DisableTimerCommand), "mc.timer.disable" },
            { nameof(ClearConsoleCommand), "mc.console.clear" },
            { nameof(EnableAutosaveCommand), "mc.autosave.enable" },
            { nameof(DisableAutosaveCommand), "mc.autosave.disable" },
            { nameof(ClearLootContainersCommand), "mc.storage.clear" },
            { nameof(SaveLootContainersCommand), "mc.storage.save" },
            { nameof(SpawnLootContainersCommand), "mc.crates.spawn" },
            { nameof(KillLootContainersCommand), "mc.crates.kill" },
            { nameof(KillLootContainerCommand), "mc.crates.kill.custom" }
        };

        private void ShowUserInterfaceCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            DrawUI(player, true);
        }

        private void HideUserInterfaceCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            DrawUI(player, false);
        }

        private void EnableTimerCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;

            _timer = timer.Every(_configuration.LootContainersKillingDelay, () =>
            {
                var destroyedContainers = KillLootContainers(GetLootContainers());
                DrawMessage(player, $"Killed {destroyedContainers} LootContainers");
            });

            DrawMessage(player, "Timer <color=lime>started</color>");
            DrawPluginSettings(player);
        }

        private void DisableTimerCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            timer.Destroy(ref _timer);
            DrawMessage(player, "Timer <color=red>stopped</color>");
            DrawPluginSettings(player);
        }

        private void ClearConsoleCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            _messages.Clear();
            DrawMessage(player, null);
        }

        private void EnableAutosaveCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            _configuration.Autosave = true;
            SaveConfig();
            DrawMessage(player, "Automatic LootContainer saving <color=lime>enabled</color>");
            DrawJsonManagement(player);
        }

        private void DisableAutosaveCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            _configuration.Autosave = false;
            SaveConfig();
            DrawMessage(player, "Automatic LootContainer saving <color=red>disabled</color>");
            DrawJsonManagement(player);
        }

        private void ClearLootContainersCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            _lootCrates.Clear();
            DrawMessage(player, "The LootContainers list has been cleared");
        }

        private void SaveLootContainersCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            SaveData();
            DrawMessage(player, "LootContainers were successfully saved to a json file");
        }

        private void KillLootContainerCommand(IPlayer caller, string command, string[] args)
        {
            var arg = args[0];
            if (string.IsNullOrEmpty(arg)) return;

            var player = (BasePlayer)caller.Object;
            var killed = KillLootContainers(arg);
            DrawLootContainerManagement(player);
            DrawMessage(player, $"Killed {killed} {arg} LootContainers");
        }

        private void SpawnLootContainersCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            var spawned = SpawnLootContainers();
            DrawMessage(player, $"Spawned {spawned} LootContainers");
        }

        private void KillLootContainersCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            var killed = KillLootContainers();
            DrawMessage(player, $"Killed {killed} LootContainers");
        }

        #endregion
    }
}