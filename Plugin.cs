#pragma warning disable CS0436 // Type conflicts with imported type
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarGameShared.Infra.Messages;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using TheBazaar;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Net.Http;
using System.Text;
using BepInEx.Configuration;
using System.IO;
using System.Threading;
using System.Reflection;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
namespace BazaarLogicMod;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
    private static DateTime _lastSentTime = DateTime.MinValue;
    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(2);
    private static string SupabaseUrl = "https://cyqfxfpdktioceyyacvv.supabase.co";
    private static string SupabaseServiceKey = "sb_secret_Z-r_uS5ySBtIbFsdqllJ5w_UiUaO_ts"; // Get from Supabase Dashboard → Settings → API
    private static string _runId;
    private static ConfigEntry<string> UidConfig;
    private static ConfigEntry<string> TokenConfig;
    private static ConfigEntry<string> RefreshTokenConfig;
    private static ConfigEntry<string> TokenExpiryConfig;
    private static ConfigFile BPConfig;
    private static string _lastBoardState = "";
    private static int _encounterId = 0;
    private static ConfigEntry<string> DisplayNameConfig;
    private static DateTime _lastUpdateTime = DateTime.MinValue;
    private static EVictoryCondition _lastVictoryCondition;
    private static Dictionary<string, List<string>> _baseItemTags;
    private const string GithubApiUrl = "https://api.github.com/repos/oceanseth/BazaarLogicMod/releases/latest";

    private static async Task SaveCombat()
    {        
        string uid = UidConfig.Value;
        RunInfo runInfo = getRunInfo();
        string json = CreateBazaarLogicJson(runInfo);
        string compressed = LZString.CompressToEncodedURIComponent(json);
        string runId = runInfo.RunId;
        string battleName = $"Day {Data.Run.Day} - {runInfo.OppName}";
        string timestamp = ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds().ToString();
        
        var newEncounter = new Dictionary<string, object>
        {
            { "name", battleName },
            { "d", compressed },
            { "t", timestamp },
            { "v", _lastVictoryCondition==EVictoryCondition.Win ? "1" : "0" }
        };

        await SaveToSupabase(uid, runId, runInfo, newEncounter, timestamp);
        _encounterId++;
    }
    
    private static async Task SaveCurrentState()
    {
        try
        {
            // Check if we should throttle (only update every 2 seconds)
            if ((DateTime.Now - _lastSentTime) < SendInterval)
            {
                return;
            }
            
            string uid = UidConfig.Value;
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(_runId))
            {
                return;
            }
            
            RunInfo runInfo = getRunInfo();
            string json = CreateBazaarLogicJson(runInfo);
            
            // Check if board state actually changed
            if (json == _lastBoardState)
            {
                return;
            }
            
            _lastBoardState = json;
            _lastSentTime = DateTime.Now;
            
            string compressed = LZString.CompressToEncodedURIComponent(json);
            string timestamp = ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds().ToString();
            
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("apikey", SupabaseServiceKey);
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {SupabaseServiceKey}");
                httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation");
                
                var updateData = new Dictionary<string, object>
                {
                    { "current_state", new Dictionary<string, object>
                        {
                            { "d", compressed },
                            { "t", timestamp }
                        }
                    },
                    { "wins", runInfo.Wins },
                    { "losses", runInfo.Losses },
                    { "day", runInfo.Day },
                    { "timestamp", timestamp }
                };
                
                var jsonData = JsonConvert.SerializeObject(updateData);
                
                var request = new HttpRequestMessage
                {
                    Method = new HttpMethod("PATCH"),
                    RequestUri = new Uri($"{SupabaseUrl}/rest/v1/runs?id=eq.{_runId}"),
                    Content = new StringContent(jsonData, Encoding.UTF8, "application/json")
                };
                
                var response = await httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Failed to save current state: {response.StatusCode} - {errorContent}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SaveCurrentState: {ex.Message}");
        }
    }

    private static async Task SaveToSupabase(string uid, string runId, RunInfo runInfo, Dictionary<string, object> newEncounter, string timestamp)
    {        
        try 
        {
            if (string.IsNullOrEmpty(uid))
            {
                Console.WriteLine("Cannot save to BazaarLogic: Missing UID");
                return;
            }
            
            using (var httpClient = new HttpClient())
            {
                // Set up Supabase headers with service role key (bypasses RLS)
                httpClient.DefaultRequestHeaders.Add("apikey", SupabaseServiceKey);
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {SupabaseServiceKey}");
                httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation,resolution=merge-duplicates");
                
                // Fetch existing run to get current encounters array
                var getResponse = await httpClient.GetAsync(
                    $"{SupabaseUrl}/rest/v1/runs?id=eq.{runId}&select=encounters"
                );
                
                List<object> encounters = new List<object>();
                if (getResponse.IsSuccessStatusCode)
                {
                    var existingData = await getResponse.Content.ReadAsStringAsync();
                    var existingRuns = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(existingData);
                    if (existingRuns != null && existingRuns.Count > 0 && existingRuns[0].ContainsKey("encounters"))
                    {
                        var encountersJson = JsonConvert.SerializeObject(existingRuns[0]["encounters"]);
                        encounters = JsonConvert.DeserializeObject<List<object>>(encountersJson);
                    }
                }
                
                // Add new encounter to the array
                encounters.Add(newEncounter);
                
                // Prepare run data for upsert
                var runData = new Dictionary<string, object>
                {
                    { "id", runId },
                    { "user_id", uid },
                    { "wins", runInfo.Wins },
                    { "losses", runInfo.Losses },
                    { "day", runInfo.Day },
                    { "timestamp", timestamp },
                    { "hero", runInfo.Hero },
                    { "encounters", encounters }
                };
                
                if (runInfo.PlayMode)
                {
                    runData["ranked"] = true;
                }
                
                var jsonData = JsonConvert.SerializeObject(runData);
                Console.WriteLine($"Attempting to upsert run {runId}");
                
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"{SupabaseUrl}/rest/v1/runs"),
                    Content = new StringContent(jsonData, Encoding.UTF8, "application/json")
                };
                
                var response = await httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Run {runId} saved successfully to Supabase");
                }
                else
                {
                    Console.WriteLine($"Failed to save run {runId}: {response.StatusCode} - {response.ReasonPhrase}");
                    Console.WriteLine($"Response content: {responseContent}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SaveToSupabase: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
     private static RunInfo getRunInfo() {
        return new RunInfo
        {
            Wins = Data.Run.Victories,
            Losses = Data.Run.Losses,
            Hero = Data.Run.Player.Hero.ToString(),
            Day = (int)Data.Run.Day,
            Gold = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Gold),
            Income = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Income),
            Cards = GetCardInfo(GetItemsAsCards(Data.Run.Player.Hand)),
            Stash = GetCardInfo(GetItemsAsCards(Data.Run.Player.Stash)),
            Skills = GetSkillInfo(Data.Run.Player.Skills),
            OppCards = GetCardInfo(GetItemsAsCards(Data.Run.Opponent?.Hand)),
            OppStash = GetCardInfo(GetItemsAsCards(Data.Run.Opponent?.Stash)),
            OppSkills = GetSkillInfo(Data.Run.Opponent?.Skills),
            Health = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.HealthMax),
            Shield = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Shield),
            Regen = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.HealthRegen),
            Level = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Level),
            Prestige = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Prestige),
            Name = Data.Profile?.Username,
            OppHealth = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.HealthMax),
            OppRegen = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.HealthRegen),
            OppName = Data.Run.Opponent?.Hero==EHero.Common ? "PvE":Data.SimPvpOpponent?.Name,
            OppHero = Data.Run.Opponent?.Hero.ToString(),
            OppShield = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Shield),
            OppGold = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Gold),
            OppIncome = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Income),
            OppLevel = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Level),
            OppPrestige = Data.Run.Opponent?.GetAttributeValue(EPlayerAttributeType.Prestige),
            RunId = _runId,
            PlayMode = Data.SelectedPlayMode==EPlayMode.Ranked
        };
    }
    private static string GetHashedRunId(string runId, string displayName)
{
    using (var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(displayName)))
    {
        byte[] hashBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(runId));
        // Convert to base64 and make URL-safe
        return Convert.ToBase64String(hashBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "")
            .Substring(0, 20); // Truncate to reasonable length
    }
}

    private static List<Card> GetItemsAsCards(IPlayerInventory container)
    {
        return container.Container.GetSocketables()
            .Cast<Card>() // Cast all items to Card (throws InvalidCastException if any object is incompatible)
            .ToList<Card>();
    }

    static List<RunInfo.SkillInfo> GetSkillInfo(IEnumerable<SkillCard> skills)
    {
        List<RunInfo.SkillInfo> skillInfos = new List<RunInfo.SkillInfo>();
        foreach (var skill in skills)
        {
            if (skill.Template != null)
                skillInfos.Add(new RunInfo.SkillInfo
                {
                    TemplateId = skill.TemplateId,
                    Tier = skill.Tier,
                    Name = skill.Template.Localization.Title.Text,
                    Attributes = skill.Attributes
                });
        }

        return skillInfos;
    }

    private static string CreateBazaarLogicJson(RunInfo runInfo)
    {
        var result = new List<object>();

        // Add player and opponent data objects (unchanged)
        result.Add(new
        {
            name = "_b_b",
            health = runInfo.Health,
            shield = runInfo.Shield,
            regen = runInfo.Regen,
            playerName = DisplayNameConfig.Value ?? runInfo.Name ?? "Unknown",
            hero = runInfo.Hero,
            level = runInfo.Level,
            prestige = runInfo.Prestige,
            income = runInfo.Income,
            gold = runInfo.Gold,
            skills = runInfo.Skills?.Select(s =>
            {
                var skillDict = new Dictionary<string, object>
                {
                    ["name"] = s.Name,
                    ["tier"] = s.Tier
                };
                
                if (s.Attributes.ContainsKey(ECardAttributeType.Custom_0))
                {
                    skillDict["Custom_0"] = s.Attributes[ECardAttributeType.Custom_0];
                }
                
                return skillDict;
            }).ToList(),
        });

        if (runInfo.OppCards != null && runInfo.OppCards.Count > 0) {
            result.Add(new
            {
                name = "_b_t",
                gold = runInfo.OppGold,
                health = runInfo.OppHealth,
                regen = runInfo.OppRegen,
                shield = runInfo.OppShield,
                playerName = runInfo.OppName ?? "Unknown",
                hero = runInfo.OppHero,
                level = runInfo.OppLevel,
                income = runInfo.OppIncome,
                prestige = runInfo.OppPrestige,
                day = runInfo.Day,
                skills = runInfo.OppSkills?.Select(s =>
                {
                    var skillDict = new Dictionary<string, object>
                    {
                        ["name"] = s.Name,
                        ["tier"] = s.Tier
                    };
                    
                    if (s.Attributes.ContainsKey(ECardAttributeType.Custom_0))
                    {
                        skillDict["Custom_0"] = s.Attributes[ECardAttributeType.Custom_0];
                    }
                    
                    return skillDict;
                }).ToList()
            });
        }
        result.Add(new 
        {
            name = "_b_backpack"
        });
        if(runInfo.OppStash != null && runInfo.OppStash.Count > 0)
        {
            result.Add(new 
            {
                name = "_b_tb"
            });
        }

        // Helper function to create card object with conditional attributes
        object CreateCardObject(RunInfo.CardInfo card, string board)
        {
            var cardDict = new Dictionary<string, object>
            {
                ["name"] = card.Enchant.Length > 0 ? card.Enchant + " " + card.Name : card.Name,
                ["startIndex"] = card.Left,
                ["board"] = board,
                ["tier"] = card.Tier
            };
           
            // Only include tags if they differ from base item
            if (card.Tags != null && card.Tags.Count > 0 && HasNewTags(card.Name, card.Tags.Select(t => t.ToString()).ToList()))
            {
                cardDict["tags"] = card.Tags.Select(t => t.ToString()).ToList();
            }
            
            if (card.Attributes?.ContainsKey(ECardAttributeType.SellPrice) == true)
                cardDict["valueFinal"] = card.Attributes[ECardAttributeType.SellPrice];
            
            if (card.Attributes?.ContainsKey(ECardAttributeType.HealAmount) == true)
                cardDict["healFinal"] = card.Attributes[ECardAttributeType.HealAmount];
            
            if (card.Attributes?.ContainsKey(ECardAttributeType.CooldownMax) == true)
                cardDict["cooldownFinal"] = card.Attributes[ECardAttributeType.CooldownMax];
            
            if (card.Attributes?.ContainsKey(ECardAttributeType.CritChance) == true && card.Attributes[ECardAttributeType.CritChance]>0)
                cardDict["critFinal"] = card.Attributes[ECardAttributeType.CritChance];
            
            if (card.Attributes?.ContainsKey(ECardAttributeType.BurnApplyAmount) == true)
                cardDict["burnFinal"] = card.Attributes[ECardAttributeType.BurnApplyAmount];
            if (card.Attributes?.ContainsKey(ECardAttributeType.ShieldApplyAmount) == true)
                cardDict["shieldFinal"] = card.Attributes[ECardAttributeType.ShieldApplyAmount];
            if (card.Attributes?.ContainsKey(ECardAttributeType.PoisonApplyAmount) == true)
                cardDict["poisonFinal"] = card.Attributes[ECardAttributeType.PoisonApplyAmount];
            if (card.Attributes?.ContainsKey(ECardAttributeType.DamageAmount) == true)
                cardDict["damageFinal"] = card.Attributes[ECardAttributeType.DamageAmount];
            if (card.Attributes?.ContainsKey(ECardAttributeType.Lifesteal) == true && card.Attributes[ECardAttributeType.Lifesteal]>0)
                cardDict["lifestealFinal"] = card.Attributes[ECardAttributeType.Lifesteal];
            if (card.Attributes?.ContainsKey(ECardAttributeType.RegenApplyAmount) == true)
                cardDict["regenFinal"] = card.Attributes[ECardAttributeType.RegenApplyAmount];
            if (card.Attributes?.ContainsKey(ECardAttributeType.AmmoMax) == true && card.Attributes[ECardAttributeType.AmmoMax]>0)
                cardDict["ammoFinal"] = card.Attributes[ECardAttributeType.AmmoMax];
            if(card.Attributes?.ContainsKey(ECardAttributeType.SlowAmount) == true)
                cardDict["slowFinal"] = card.Attributes[ECardAttributeType.SlowAmount];
            if(card.Attributes?.ContainsKey(ECardAttributeType.HasteAmount) == true)
                cardDict["hasteFinal"] = card.Attributes[ECardAttributeType.HasteAmount];
            if(card.Attributes?.ContainsKey(ECardAttributeType.FreezeAmount) == true)
                cardDict["freezeFinal"] = card.Attributes[ECardAttributeType.FreezeAmount];
            if(card.Attributes?.ContainsKey(ECardAttributeType.Custom_0) == true)
                cardDict["Custom_0"] = card.Attributes[ECardAttributeType.Custom_0];
            if(card.Attributes?.ContainsKey(ECardAttributeType.Custom_1) == true)
                cardDict["Custom_1"] = card.Attributes[ECardAttributeType.Custom_1];
            if(card.Attributes?.ContainsKey(ECardAttributeType.Custom_2) == true)
                cardDict["Custom_2"] = card.Attributes[ECardAttributeType.Custom_2];
            if(card.Attributes?.ContainsKey(ECardAttributeType.Custom_3) == true)
                cardDict["Custom_3"] = card.Attributes[ECardAttributeType.Custom_3];
            if(card.Attributes?.ContainsKey(ECardAttributeType.Quest_1) == true)
                cardDict["quest_1"] = card.Attributes[ECardAttributeType.Quest_1];
            if(card.Attributes?.ContainsKey(ECardAttributeType.Quest_2) == true)
                cardDict["quest_2"] = card.Attributes[ECardAttributeType.Quest_2];
            if(card.Attributes?.ContainsKey(ECardAttributeType.Quest_3) == true)
                cardDict["quest_3"] = card.Attributes[ECardAttributeType.Quest_3];
            if(card.Attributes?.ContainsKey(ECardAttributeType.Quest_4) == true)
                cardDict["quest_4"] = card.Attributes[ECardAttributeType.Quest_4];
            if(card.Attributes?.ContainsKey(ECardAttributeType.Quest_5) == true)
                cardDict["quest_5"] = card.Attributes[ECardAttributeType.Quest_5];


            return cardDict;
        }

        // Add player cards
        if (runInfo.Cards != null)
        {
            foreach (var card in runInfo.Cards)
            {
                result.Add(CreateCardObject(card, "b"));
            }
        }

        // Add opponent cards
        if (runInfo.OppCards != null)
        {
            foreach (var card in runInfo.OppCards)
            {
                result.Add(CreateCardObject(card, "t"));
            }
        }
        if(runInfo.OppStash != null)
        {
            foreach (var card in runInfo.OppStash)
            {
                result.Add(CreateCardObject(card, "tb"));
            }
        }
        if(runInfo.Stash != null)
        {
            foreach (var card in runInfo.Stash)
            {
                result.Add(CreateCardObject(card, "backpack"));
            }
        }

        return JsonConvert.SerializeObject(result);
    }

    static void OpenInBazaarLogic(string compressedData)
    {
        try
        {
    
            string url = $"https://bazaarlogic.quest/#{compressedData}";
            
            Application.OpenURL(url);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error opening BazaarLogic: {ex.Message}");
        }
    }
    
    protected virtual void Awake()
    {
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        _harmony.PatchAll();
        
        // Add version check on startup
        CheckForUpdates();
        
        // Load config
        BPConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarLogic.cfg"), true);
        try
        {
            Console.WriteLine("Initializing configurations...");
            UidConfig = BPConfig.Bind("Authentication", "Uid", "", "Supabase User ID");
            TokenExpiryConfig = BPConfig.Bind("Authentication", "TokenExpiry", DateTime.MinValue.ToString(), "(Obsolete) Token Expiration Time");
            TokenConfig = BPConfig.Bind("Authentication", "Token", "", "(Obsolete) Supabase Token");
            RefreshTokenConfig = BPConfig.Bind("Authentication", "RefreshToken", "", "(Obsolete) Refresh Token");
            DisplayNameConfig = BPConfig.Bind("Authentication", "DisplayName", "", "Display Name");
            
            Console.WriteLine("Configurations initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing configurations: {ex.Message}");
        }

        // Load base items data on startup
        LoadBaseItems();
        
        // Check for mid-run initialization after a delay
        Task.Run(async () =>
        {
            await Task.Delay(5000); // Wait for game to fully load
            
            try
            {
                // If we're in a run but don't have a runId set, initialize it
                if (Data.Run != null && !string.IsNullOrEmpty(UidConfig?.Value) && string.IsNullOrEmpty(_runId))
                {
                    Console.WriteLine("Detected mid-run scenario - generating run ID and initializing in Supabase");
                    
                    // Generate a stable run ID based on available data
                    // Since we don't have access to the game's internal run ID mid-run,
                    // we'll create one based on user ID + a stable run identifier
                    var runIdentifier = $"{UidConfig.Value}_{Data.Run.Day}_{Data.Run.Victories}_{Data.Run.Losses}";
                    _runId = GetHashedRunId(runIdentifier, DisplayNameConfig.Value);
                    
                    _encounterId = await GetEncounterCount(UidConfig.Value, _runId);
                    await InitializeRun();
                    
                    Console.WriteLine($"Mid-run initialization complete, runId: {_runId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in mid-run initialization: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        });
    }
    

    private void LoadBaseItems()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("BazaarLogicMod.items.js"))
            using (var reader = new StreamReader(stream))
            {
                string content = reader.ReadToEnd();
                
                // Remove the "export const items = " part and any trailing semicolon
                content = content.Replace("export const items =", "")
                                .Trim()
                                .TrimEnd(';');
                
                // Deserialize to dynamic to easily access the nested structure
                var items = JObject.Parse(content);
                
                // Create a simplified dictionary with just the tags
                _baseItemTags = items.Properties().ToDictionary(
                    prop => prop.Name,
                    prop => prop.Value["tags"].Select(t => t.ToString()).ToList()
                );
                
                Logger.LogInfo($"Loaded tags for {_baseItemTags.Count} base items");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load base items: {ex.Message}");
            _baseItemTags = new Dictionary<string, List<string>>();
        }
    }

    private static bool HasNewTags(string itemName, List<string> currentTags)
    {
        if (!_baseItemTags.TryGetValue(itemName, out var baseTags))
            return true; // If we don't have base data, include tags

        if (currentTags == null || currentTags.Count == 0)
            return false;

        // Check if any current tag is not in base tags
        return currentTags.Any(tag => !baseTags.Contains(tag));
    }

    static List<RunInfo.CardInfo> GetCardInfo(List<Card> cards)
    {
        List<RunInfo.CardInfo> cardInfos = new List<RunInfo.CardInfo>();
        foreach (var card in cards)
        {
                cardInfos.Add(new RunInfo.CardInfo
                {
                    TemplateId = card.TemplateId,
                    Tier = card.Tier,
                    Left = card.LeftSocketId,
                    Instance = card.GetInstanceId(),
                    Attributes = card.Attributes,
                    Tags = card.Tags,
                    Name = card.Template?.InternalName,
                    Enchant = card.GetEnchantment().ToString()                    
                });
        }
        return cardInfos;
    }

    [HarmonyPatch(typeof(BoardManager), "Update")]
    class Update
    {
        [HarmonyPrefix]
        static void Prefix()
        {
            // Periodically save current state while in a run
            if (Data.Run != null && !string.IsNullOrEmpty(_runId) && !string.IsNullOrEmpty(UidConfig?.Value))
            {
                Task.Run(() => SaveCurrentState());
            }
            
            if (Keyboard.current == null || !Keyboard.current.bKey.wasPressedThisFrame)
            {
                return;
            }

            if (DateTime.Now - _lastSentTime < SendInterval)
            {
                return;
            }

            _lastSentTime = DateTime.Now;

            RunInfo runInfo = getRunInfo();
            string json = CreateBazaarLogicJson(runInfo);
            string compressed = LZString.CompressToEncodedURIComponent(json);
            Task.Run(() => OpenInBazaarLogic(compressed));
        }
    }

    private static async Task<int> GetEncounterCount(string uid, string runId)
    {
        try
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("apikey", SupabaseServiceKey);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {SupabaseServiceKey}");
                
                var response = await client.GetAsync(
                    $"{SupabaseUrl}/rest/v1/runs?id=eq.{runId}&select=encounters"
                );
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    var runs = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(result);
                    
                    if (runs != null && runs.Count > 0 && runs[0].ContainsKey("encounters"))
                    {
                        var encountersJson = JsonConvert.SerializeObject(runs[0]["encounters"]);
                        var encounters = JsonConvert.DeserializeObject<List<object>>(encountersJson);
                        return encounters?.Count ?? 0;
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to fetch from Supabase: {response.ReasonPhrase}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading from Supabase: {ex.Message}");
        }
        return 0;
    }

    [HarmonyPatch(typeof(AppState), "OnRunInitializedMessageReceived")]
    public static class OnRunInitializedMessageReceived
    {
        [HarmonyPrefix]
        static async void Prefix(NetMessageRunInitialized obj)
        {
            _runId = GetHashedRunId(obj.RunId, DisplayNameConfig.Value);
            _encounterId = await GetEncounterCount(UidConfig.Value, _runId);
            
            // Initialize the run in Supabase when it starts
            await InitializeRun();
        }
    }
    
    private static async Task InitializeRun()
    {
        try
        {
            string uid = UidConfig.Value;
            if (string.IsNullOrEmpty(uid))
            {
                Console.WriteLine("Cannot initialize run: Missing UID");
                return;
            }
            
            RunInfo runInfo = getRunInfo();
            string timestamp = ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds().ToString();
            
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("apikey", SupabaseServiceKey);
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {SupabaseServiceKey}");
                httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation,resolution=merge-duplicates");
                
                var runData = new Dictionary<string, object>
                {
                    { "id", _runId },
                    { "user_id", uid },
                    { "wins", runInfo.Wins },
                    { "losses", runInfo.Losses },
                    { "day", runInfo.Day },
                    { "timestamp", timestamp },
                    { "hero", runInfo.Hero },
                    { "encounters", new List<object>() } // Start with empty encounters array
                };
                
                if (runInfo.PlayMode)
                {
                    runData["ranked"] = true;
                }
                
                var jsonData = JsonConvert.SerializeObject(runData);
                Console.WriteLine($"Initializing run {_runId} in Supabase");
                
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"{SupabaseUrl}/rest/v1/runs"),
                    Content = new StringContent(jsonData, Encoding.UTF8, "application/json")
                };
                
                var response = await httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Run {_runId} initialized successfully");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Failed to initialize run: {response.StatusCode}");
                    Console.WriteLine($"Error: {errorContent}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in InitializeRun: {ex.Message}");
        }
    }

    // Supabase authentication is handled via API key in headers, no token refresh needed

    private static string _lastMessageId = "";

    [HarmonyPatch(typeof(CombatSimHandler), "Simulate")]
    class CombatSimHandlerSimulate
    {
        [HarmonyPrefix]
        static void Prefix(NetMessageCombatSim message, CancellationTokenSource cancellationToken)
        {
            if(_lastMessageId == message.MessageId) return;
            _lastMessageId = message.MessageId;
            if(UidConfig.Value == null || UidConfig.Value == "") return;
            _lastVictoryCondition = message.Data.Winner == ECombatantId.Player ? EVictoryCondition.Win : EVictoryCondition.Lose;
            Task.Run(() => SaveCombat());
        }
    }

    /*
    
    [HarmonyPatch(typeof(HeroBannerController), "UpdatePlayer")]
    public static class UpdatePlayerPatch
    {
        [HarmonyPostfix]
        static void Postfix(HeroBannerController __instance, string userName, int nameId, string titlePrefix, string titleSuffix, TheBazaar.ProfileData.ISeasonRank currentSeasonRank, int? leaderboardPosition) {
            if(UidConfig.Value == null || UidConfig.Value == "") return;
            if(userName != Data.Profile?.Username) return;
            
            // Queue the UI update to happen on the next frame
            __instance.StartCoroutine(UpdateNameNextFrame(__instance, nameId));
            
        //    var getter = typeof(TheBazaar.ProfileData.ProfileContainer).GetProperty("Username");
        //    getter.SetValue(Data.Profile, DisplayNameConfig.Value);
        }
        
        private static IEnumerator UpdateNameNextFrame(HeroBannerController instance, int nameId)
        {
            yield return null; // Wait for next frame
            instance.SetHeroName(DisplayNameConfig.Value, nameId);
        }
    }
    */
    [HarmonyPatch(typeof(HeroBannerController), "UpdatePlayer")]
    public static class UpdatePlayerInterceptPatch
    {
        [HarmonyPrefix]
        static bool Prefix(HeroBannerController __instance, ref string userName, ref int nameId, 
            ref string titlePrefix, TheBazaar.ProfileData.ISeasonRank currentSeasonRank, int? leaderboardPosition)
        {
            // Check if this is for our player
            if(userName != Data.Profile?.Username) return true; // Let original method run unmodified
            if(UidConfig.Value == null || UidConfig.Value == "") return true;

            // Modify the parameters
            userName = DisplayNameConfig.Value;
            nameId = 0;
            // You can modify other parameters here as needed
            
            // Return true to let the original method run with our modified parameters
            // Return false if you want to skip the original method entirely
            return true;
        }
    }

    [HarmonyPatch(typeof(HeroBannerController), "SetHeroName")]
    public static class SetHeroNamePatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref string newName, ref int usernameId) {
            if(newName != Data.Profile?.Username) return true;
            if(UidConfig.Value == null || UidConfig.Value == "") return true;
            
            newName = DisplayNameConfig.Value;
            usernameId = 0;
            return true;
        }
    }
    

    [HarmonyPatch(typeof(BoardManager), "UpdateBoard")]
    public static class UpdateBoardPatch 
    {
        [HarmonyPostfix]
        static void Postfix()
        {            
            //Data.Profile.Username = DisplayNameConfig.Value;
            if(UidConfig.Value == null || UidConfig.Value == "") return;
            RunInfo runInfo = getRunInfo();
            string json = CreateBazaarLogicJson(runInfo);
            if(json == _lastBoardState) return;

            _lastBoardState = json;
            string compressed = LZString.CompressToEncodedURIComponent(json);
            var saveData = new {
                id = runInfo.RunId,
                d = compressed
            };

            // Note: Real-time current run syncing removed - Follow feature only syncs encounters after battles
            // If real-time board syncing is needed in the future, implement SaveCurrentRunToSupabase
        }
    }

    private async void CheckForUpdates()
    {
        Logger.LogInfo("Checking for updates...");
        try
        {
            using (var client = new HttpClient())
            {
                // Add required headers for GitHub API
                client.DefaultRequestHeaders.Add("User-Agent", "BazaarLogicMod");
                
                var response = await client.GetStringAsync(GithubApiUrl);
                Logger.LogInfo("Got reponse from github: " + response);
                var releaseInfo = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                
                string latestVersion = releaseInfo["tag_name"].ToString().TrimStart('v');
                string currentVersion = MyPluginInfo.PLUGIN_VERSION;
                
                if (IsNewerVersion(latestVersion, currentVersion))
                {                    
                    // Get the installer download URL
                    var assets = ((JArray)releaseInfo["assets"]);
                    var installerAsset = assets.FirstOrDefault(a => ((JObject)a)["name"].ToString().Contains("Installer"));
                    
                    if (installerAsset != null)
                    {
                        string downloadUrl = ((JObject)installerAsset)["browser_download_url"].ToString();
                        await DownloadAndStartInstaller(downloadUrl, latestVersion);
                    }
                } else {
                    Logger.LogInfo("No updates available, you are on version " + currentVersion + " and latest version is " + latestVersion);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogInfo($"Error checking for updates: {ex.Message}");
        }
    }

    private bool IsNewerVersion(string latest, string current)
    {
        Version latestVersion = Version.Parse(latest);
        Version currentVersion = Version.Parse(current);
        return latestVersion > currentVersion;
    }

    private async Task DownloadAndStartInstaller(string downloadUrl, string latestVersion)
    {
        try
        {
            // Create batch file first, which will handle download and installation if user agrees
            string batchPath = Path.Combine(Path.GetTempPath(), "UpdateBazaarLogic.bat");
            string currentDllPath = Assembly.GetExecutingAssembly().Location;
            string tempDir = Path.Combine(Path.GetTempPath(), "BazaarLogicUpdate");
            
            string batchContent = @$"
@echo off
echo Starting update process... >> %temp%\bp_update.log

echo Temp directory path: {tempDir} >> %temp%\bp_update.log
echo Download URL: {downloadUrl} >> %temp%\bp_update.log

set /p result=<nul
for /f %%i in ('powershell -command ""Add-Type -AssemblyName System.Windows.Forms; $result = [System.Windows.Forms.MessageBox]::Show('New version {latestVersion} of BazaarLogic available. You are on version {MyPluginInfo.PLUGIN_VERSION}. Update now?', 'BazaarLogic Update', 'YesNo', 'Question'); $result""') do set result=%%i

echo User clicked: %result% >> %temp%\bp_update.log

if ""%result%""==""No"" (
    echo Aborting update >> %temp%\bp_update.log
    exit /b 1
)

echo Creating temp directory... >> %temp%\bp_update.log
if exist ""{tempDir}"" (
    echo Temp directory already exists >> %temp%\bp_update.log
) else (
    mkdir ""{tempDir}"" 2>> %temp%\bp_update.log
    if errorlevel 1 (
        echo Failed to create temp directory >> %temp%\bp_update.log
        exit /b 1
    )
)

echo Starting download... >> %temp%\bp_update.log
powershell -command ""$ProgressPreference = 'SilentlyContinue'; (New-Object System.Net.WebClient).DownloadFile('{downloadUrl}', '{tempDir}\installer.zip')"" >> %temp%\bp_update.log 2>&1

if not exist ""{tempDir}\installer.zip"" (
    echo Download failed - installer.zip not found >> %temp%\bp_update.log
    exit /b 1
)

:wait
echo Waiting for TheBazaar to close... >> %temp%\bp_update.log
taskkill /F /IM TheBazaar.exe >nul 2>&1
if not ERRORLEVEL 1 (
    timeout /t 2 /nobreak
    goto wait
)

echo Starting extraction... >> %temp%\bp_update.log
powershell -command ""$ProgressPreference = 'SilentlyContinue'; Expand-Archive -Path '{tempDir}\installer.zip' -DestinationPath '{tempDir}' -Force"" >> %temp%\bp_update.log 2>&1

if not exist ""{tempDir}\BazaarLogicMod.dll"" (
    echo Extraction failed - BazaarLogicMod.dll not found >> %temp%\bp_update.log
    exit /b 1
)

echo Cleaning up old files... >> %temp%\bp_update.log
del /F ""{currentDllPath}"" 2>> %temp%\bp_update.log

echo Installing update... >> %temp%\bp_update.log
set ""random_name=%random%%random%.dll""
copy /Y ""{tempDir}\BazaarLogicMod.dll"" ""{Path.GetDirectoryName(currentDllPath)}\%random_name%"" 2>> %temp%\bp_update.log

echo Cleaning up... >> %temp%\bp_update.log
timeout /t 2 /nobreak
rmdir /S /Q ""{tempDir}"" 2>> %temp%\bp_update.log

powershell -command ""Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('BazaarLogic auto-update successful, you are now on version {latestVersion}, please relaunch game', 'BazaarLogic Update')""

del ""%~f0""
";
            File.WriteAllText(batchPath, batchContent);

            // Start the batch file and wait for it to complete
            var process = Process.Start(batchPath);
            await Task.Run(() => {
                process.WaitForExit();
                return process.ExitCode;
            });
          
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error installing update: {ex.Message}");
        }
    }
}