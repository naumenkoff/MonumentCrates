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
    [Info("Monument Crates", "naumenkoff", "0.3.9")]
    internal class MonumentCrates : RustPlugin
    {
        private const string BlurInGameMenu = "assets/content/ui/uibackgroundblur-ingamemenu.mat";
        private readonly List<string> _messages = new List<string>();
        private Configuration _configuration;
        private bool _isMenuShown;
        private bool _isTimerEnabled;
        private DynamicConfigFile _physicalStorage;
        private Timer _timer;
        private List<LootCrates> _virtualStorage = new List<LootCrates>();

        #region Methods

        private int KillLootContainers(string arg)
        {
            var containers = GetLootContainers().Where(x => x.ShortPrefabName == arg).ToList();
            var killedCrates = KillLootContainers(containers);
            UpdateLootContainersButtons();
            return killedCrates;
        }

        private int KillLootContainers()
        {
            var destroyedContainers = KillLootContainers(GetLootContainers());
            NextTick(UpdateLootContainersButtons);
            return destroyedContainers;
        }

        private static int KillLootContainers(List<LootContainer> cratesCollection)
        {
            var destroyed = 0;
            foreach (var lootContainer in cratesCollection)
            {
                lootContainer.Kill();
                destroyed++;
            }

            return destroyed;
        }

        private int SpawnLootContainers()
        {
            var counter = 0;
            foreach (var lootCrate in _virtualStorage)
            {
                var entity = GameManager.server.CreateEntity(lootCrate.PrefabName, lootCrate.ServerPosition,
                    lootCrate.ServerRotation);
                entity.Spawn();
                counter++;
            }

            NextTick(UpdateLootContainersButtons);
            return counter;
        }

        private void UpdateLootContainersButtons()
        {
            var lootContainers = GetLootContainers();

            var names = new List<string>();
            foreach (var container in lootContainers)
            {
                if (names.Contains(container.ShortPrefabName) ||
                    container.ShortPrefabName.Contains("roadsign"))
                    continue;

                names.Add(container.ShortPrefabName);
            }

            var buttons = GetLootContainerButtons(names);
            foreach (var player in BasePlayer.activePlayerList.Where(x => x.IsAdmin))
                DrawLootContainersTypes(player, buttons);
        }

        private static List<LootContainer> GetLootContainers()
        {
            var serverEntities = BaseNetworkable.serverEntities.entityList.Values;
            return serverEntities.OfType<LootContainer>().ToList();
        }

        private void UpdateListOfLootContainers()
        {
            var containerTypes = new List<string>();
            foreach (var entity in GetLootContainers())
            {
                if (_configuration.WhitelistedContainers.Contains(entity.ShortPrefabName) == false) continue;

                var lootCrate = new LootCrates(entity);
                if (_virtualStorage.Any(x =>
                        x.ServerPosition == lootCrate.ServerPosition && x.PrefabName == lootCrate.PrefabName))
                    continue;

                _virtualStorage.Add(lootCrate);

                if (containerTypes.Contains(lootCrate.PrefabName)) continue;
                containerTypes.Add(lootCrate.PrefabName);
            }

            UpdateLootContainersButtons();

            foreach (var admin in BasePlayer.activePlayerList.Where(x => x.IsAdmin))
                ConsoleOutput(admin, $"Added {containerTypes.Count} kinds of LootContainers.");
        }

        #endregion

        #region User Interface

        private int GetCrateCount(string name)
        {
            return _virtualStorage?.Count(x => x.PrefabName == name) ?? 0;
        }

        private void DrawUIButton(BasePlayer player, bool isMenuActive)
        {
            DestroyUI(player);
            _isMenuShown = isMenuActive;
            var menu = new CuiElementContainer
            {
                {
                    new CuiButton
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
                    },
                    "Overlay", UILayers.OpenManagerButton
                }
            };
            CuiHelper.AddUi(player, menu);
        }

        private void DrawUI(BasePlayer player, bool isActive)
        {
            DestroyUI(player);
            DrawUIButton(player, isActive);
            _isMenuShown = isActive;
            if (!isActive) return;
            DrawPluginSettings(player);
            DrawJsonManagement(player);
            DrawLootContainerManagement(player);
            UpdateLootContainersButtons();
            ConsoleOutput(player, string.Empty);
            DrawCratesCount(player);
        }

        private CuiPanel CreatePanel(string anchorMin, string anchorMax)
        {
            return new CuiPanel
            {
                Image =
                {
                    Material = BlurInGameMenu,
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

        private void DrawPluginSettings(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UILayers.PluginSection);
            if (!_isMenuShown) return;
            var cui = new CuiElementContainer();
            cui.Add(CreatePanel("0.04 0.25", "0.24 0.75"), "Overlay", UILayers.PluginSection);
            cui.Add(new CuiElement
            {
                Parent = UILayers.PluginSection,
                Components =
                {
                    new CuiTextComponent
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                        Text = "Plugin Settings"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0.8", AnchorMax = "1 1" }
                }
            });
            cui.Add(
                CreateButton(_commandList[nameof(ClearConsoleCommand)], "0.1 0.45", "0.9 0.6",
                    "Clear the Console"), UILayers.PluginSection);
            cui.Add(
                CreateButton(
                    _isTimerEnabled
                        ? _commandList[nameof(DisableTimerCommand)]
                        : _commandList[nameof(EnableTimerCommand)], "0.1 0.65", "0.9 0.8",
                    _isTimerEnabled ? "Disable Timer" : "Enable Timer"), UILayers.PluginSection);
            CuiHelper.AddUi(player, cui);
        }

        private void DrawJsonManagement(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UILayers.JsonSection);
            if (!_isMenuShown) return;
            var cui = new CuiElementContainer();
            cui.Add(CreatePanel("0.28 0.25", "0.48 0.75"), "Overlay", UILayers.JsonSection);
            cui.Add(new CuiElement
            {
                Parent = UILayers.JsonSection,
                Components =
                {
                    new CuiTextComponent
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                        Text = "Json Management"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0.8", AnchorMax = "1 1" }
                }
            });
            cui.Add(
                CreateButton(
                    _configuration.Autosave
                        ? _commandList[nameof(DisableAutosaveCommand)]
                        : _commandList[nameof(EnableAutosaveCommand)], "0.1 0.68", "0.9 0.8",
                    _configuration.Autosave ? "Disable Autosave" : "Enable Autosave"), UILayers.JsonSection);
            cui.Add(
                CreateButton(_commandList[nameof(SaveLootContainersCommand)], "0.1 0.36", "0.9 0.48",
                    "Save LootContainers"), UILayers.JsonSection);

            cui.Add(
                CreateButton(_commandList[nameof(ClearLootContainersCommand)], "0.1 0.52", "0.9 0.64",
                    "Clear LootContainers"), UILayers.JsonSection);
            CuiHelper.AddUi(player, cui);
        }

        private void DrawLootContainerManagement(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UILayers.ManagerSection);
            if (!_isMenuShown) return;
            var cui = new CuiElementContainer();
            cui.Add(CreatePanel("0.76 0.25", "0.96 0.75"), "Overlay", UILayers.ManagerSection);
            cui.Add(new CuiElement
            {
                Parent = UILayers.ManagerSection,
                Components =
                {
                    new CuiTextComponent
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                        Text = "LootContainer Management"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0.8", AnchorMax = "1 1" }
                }
            });
            cui.Add(
                CreateButton(_commandList[nameof(KillLootContainersCommand)], "0.1 0.52", "0.9 0.64",
                    "Kill LootContainers"), UILayers.ManagerSection);
            cui.Add(
                CreateButton(_commandList[nameof(SpawnLootContainersCommand)], "0.1 0.68", "0.9 0.8",
                    "Spawn LootContainers"), UILayers.ManagerSection);
            CuiHelper.AddUi(player, cui);
        }

        private void DrawLootContainersTypes(BasePlayer player, List<CuiButton> buttons)
        {
            CuiHelper.DestroyUi(player, UILayers.LootContainersInformation);
            if (!_isMenuShown) return;
            var hud = new CuiElementContainer
            {
                {
                    CreatePanel("0.52 0.25", "0.72 0.75"),
                    "Overlay", UILayers.LootContainersInformation
                }
            };

            foreach (var button in buttons) hud.Add(button, UILayers.LootContainersInformation);
            CuiHelper.AddUi(player, hud);
        }

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

            var sb = new StringBuilder();
            sb.Append(
                $"<color=lime>Military</color> {GetCrateCount("assets/bundled/prefabs/radtown/crate_normal.prefab")} | ");
            sb.Append($"Crate {GetCrateCount("assets/bundled/prefabs/radtown/crate_normal_2.prefab")} | ");
            sb.Append(
                $"<color=yellow>Primitive</color> {GetCrateCount("assets/bundled/prefabs/radtown/crate_basic.prefab")} | ");
            sb.Append($"Ration {GetCrateCount("assets/bundled/prefabs/radtown/foodbox.prefab")} | ");
            sb.Append(
                $"<color=cyan>Vehicle</color> {GetCrateCount("assets/bundled/prefabs/radtown/vehicle_parts.prefab")} | ");
            sb.Append($"Elite {GetCrateCount("assets/bundled/prefabs/radtown/crate_elite.prefab")}");

            var hud = new CuiElementContainer
            {
                {
                    CreatePanel("0.52 0.16", "0.96 0.24"),
                    "Overlay", UILayers.ContainersInformation
                },
                {
                    new CuiLabel
                    {
                        Text =
                        {
                            Text = sb.ToString(),
                            Align = TextAnchor.MiddleCenter, FontSize = 12,
                            Color = "1 1 1 1"
                        },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                    },
                    UILayers.ContainersInformation
                }
            };
            CuiHelper.AddUi(player, hud);
        }

        private string GetColumnMessages(int start, int end)
        {
            var message = string.Empty;
            for (var i = start; i < end; i++) message += GetMessage(_messages.Count - i) + "\n";

            return message;
        }

        private void ConsoleOutput(BasePlayer player, string text)
        {
            CuiHelper.DestroyUi(player, UILayers.PluginConsole);
            if (!string.IsNullOrEmpty(text)) _messages.Add(text);
            if (!_isMenuShown) return;
            if (_messages.Count > 18) _messages.RemoveRange(0, _messages.Count - 18);

            var hud = new CuiElementContainer
            {
                {
                    CreatePanel("0.04 0.16", "0.48 0.24"),
                    "Overlay", UILayers.PluginConsole
                },
                {
                    new CuiLabel
                    {
                        Text =
                        {
                            Text = GetColumnMessages(0, 6),
                            Align = TextAnchor.MiddleCenter, FontSize = 8,
                            Color = "1 1 1 1"
                        },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "0.3 1" }
                    },
                    UILayers.PluginConsole
                },
                // 0.307
                {
                    new CuiLabel
                    {
                        Text =
                        {
                            Text = GetColumnMessages(6, 12),
                            Align = TextAnchor.MiddleCenter, FontSize = 8,
                            Color = "1 1 1 1"
                        },
                        RectTransform = { AnchorMin = $"{1f / 3f} 0", AnchorMax = "0.6 1" }
                    },
                    UILayers.PluginConsole
                },
                {
                    new CuiLabel
                    {
                        Text =
                        {
                            Text = GetColumnMessages(12, 18),
                            Align = TextAnchor.MiddleCenter, FontSize = 8,
                            Color = "1 1 1 1"
                        },
                        RectTransform = { AnchorMin = $"{2f / 3f} 0", AnchorMax = "1 1" }
                    },
                    UILayers.PluginConsole
                }
            };
            CuiHelper.AddUi(player, hud);
        }

        private static void DestroyUI(BasePlayer player)
        {
            foreach (var layer in UILayers.Layers) CuiHelper.DestroyUi(player, layer);
        }

        private string GetMessage(int index)
        {
            return index > 0 && _messages.Count >= index ? _messages[index - 1] : string.Empty;
        }

        private static byte ParseString(string str, int startIndex, int length)
        {
            return byte.Parse(str.Substring(startIndex, length), NumberStyles.HexNumber);
        }

        private string GetColor(string hex)
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
            _physicalStorage.Settings = new JsonSerializerSettings
                { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
            _physicalStorage.WriteObject(_virtualStorage);
        }

        private void LoadData()
        {
            _physicalStorage.Settings = new JsonSerializerSettings
                { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
            _virtualStorage =
                Interface.Oxide.DataFileSystem.ReadObject<List<LootCrates>>(
                    $"MonumentCrates&{World.Seed}&{World.Size}") ?? new List<LootCrates>();
        }

        private class LootCrates
        {
            public LootCrates(BaseEntity lootContainer)
            {
                ServerPosition = lootContainer.ServerPosition;
                ServerRotation = lootContainer.ServerRotation;
                PrefabName = lootContainer.PrefabName;
            }

            [JsonConstructor]
            public LootCrates(string prefabName, Vector3 serverPosition, Quaternion serverRotation)
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

        private void Loaded()
        {
            _physicalStorage = Interface.Oxide.DataFileSystem.GetFile($"MonumentCrates&{World.Seed}&{World.Size}");
            LoadData();
            timer.Every(_configuration.LootContainersListUpdateDelay, UpdateListOfLootContainers);
            foreach (var player in BasePlayer.activePlayerList.Where(player => player.IsAdmin))
                OnPlayerConnected(player);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            DrawUIButton(player, false);
            timer.Every(_configuration.LootContainersUIRefreshDelay, () => { DrawCratesCount(player); });
        }

        private void Unload()
        {
            if (_configuration.Autosave) SaveData();
            foreach (var player in BasePlayer.activePlayerList) DestroyUI(player);
        }

        private void Init()
        {
            foreach (var command in _commandList) AddCovalenceCommand(command.Value, command.Key);
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
            _isTimerEnabled = true;

            _timer = timer.Every(_configuration.LootContainersKillingDelay, () =>
            {
                var destroyedContainers = KillLootContainers(GetLootContainers());
                ConsoleOutput(player, $"Killed {destroyedContainers} LootContainers");
            });

            ConsoleOutput(player, "Timer <color=lime>started</color>");
            DrawPluginSettings(player);
        }

        private void DisableTimerCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            _isTimerEnabled = false;

            timer.Destroy(ref _timer);

            ConsoleOutput(player, "Timer <color=red>stopped</color>");
            DrawPluginSettings(player);
        }

        private void ClearConsoleCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            _messages.Clear();
            ConsoleOutput(player, null);
        }

        private void EnableAutosaveCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            _configuration.Autosave = true;
            SaveConfig();
            ConsoleOutput(player, "Automatic LootContainer saving <color=lime>enabled</color>");
            DrawJsonManagement(player);
        }

        private void DisableAutosaveCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            _configuration.Autosave = false;
            SaveConfig();
            ConsoleOutput(player, "Automatic LootContainer saving <color=red>disabled</color>");
            DrawJsonManagement(player);
        }

        private void ClearLootContainersCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            _virtualStorage.Clear();
            ConsoleOutput(player, "The LootContainers list has been cleared");
        }

        private void SaveLootContainersCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            SaveData();
            ConsoleOutput(player, "LootContainers were successfully saved to a json file");
        }

        private void KillLootContainerCommand(IPlayer caller, string command, string[] args)
        {
            var player = (BasePlayer)caller.Object;
            var arg = args[0];
            if (string.IsNullOrEmpty(arg)) return;
            var killed = KillLootContainers(arg);
            DrawLootContainerManagement(player);
            ConsoleOutput(player, $"Killed {killed} {arg} LootContainers");
        }

        private void SpawnLootContainersCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            var spawned = SpawnLootContainers();
            ConsoleOutput(player, $"Spawned {spawned} LootContainers");
        }

        private void KillLootContainersCommand(IPlayer caller)
        {
            var player = (BasePlayer)caller.Object;
            var number = KillLootContainers();
            ConsoleOutput(player, $"Killed {number} LootContainers");
        }

        #endregion
    }
}