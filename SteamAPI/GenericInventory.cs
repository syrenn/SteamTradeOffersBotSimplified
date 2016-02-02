using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;
using System.Text.RegularExpressions;
using System.Net;
using System.Threading;

namespace SteamAPI
{
    /// <summary>
    /// Generic Steam Backpack Interface
    /// </summary>
    public class GenericInventory
    {
        private readonly BotInventories _inventories = new BotInventories();
        private readonly InventoryTasks _inventoryTasks = new InventoryTasks();        
        private readonly Task _constructTask;
        private const int WebRequestMaxRetries = 3;
        private const int WebRequestTimeBetweenRetriesMs = 1000;
        private SteamWeb _steamWeb;
        private bool _loaded = false;

        /// <summary>
        /// Gets the content of all inventories listed in http://steamcommunity.com/profiles/STEAM_ID/inventory/
        /// </summary>
        public BotInventories Inventories
        {
            get
            {
                WaitAllTasks();
                return _inventories;
            }
        }

        public GenericInventory(SteamID steamId, SteamWeb steamWeb, List<int> appIdsToFetch = null)
        {
            _constructTask = Task.Factory.StartNew(() =>
            {
                _steamWeb = steamWeb;
                var baseInventoryUrl = "http://steamcommunity.com/profiles/" + steamId.ConvertToUInt64() + "/inventory/";
                var response = RetryWebRequest(baseInventoryUrl);
                var reg = new Regex("var g_rgAppContextData = (.*?);");
                var m = reg.Match(response);
                if (m.Success)
                {
                    try
                    {
                        var json = m.Groups[1].Value;
                        var schemaResult = JsonConvert.DeserializeObject<Dictionary<int, InventoryApps>>(json);
                        foreach (var app in schemaResult)
                        {
                            var appId = app.Key;
                            if (appIdsToFetch != null && !appIdsToFetch.Contains(appId)) continue;
                            _inventoryTasks[appId] = new InventoryTasks.ContextTask();
                            foreach (var contextId in app.Value.RgContexts.Keys)
                            {
                                _inventoryTasks[appId][contextId] = Task.Factory.StartNew(() =>
                                {
                                    var inventoryUrl = string.Format("http://steamcommunity.com/profiles/{0}/inventory/json/{1}/{2}/", steamId.ConvertToUInt64(), appId, contextId);
                                    var inventory = FetchInventory(inventoryUrl, steamId, appId, contextId);
                                    if (!_inventories.HasAppId(appId))
                                        _inventories[appId] = new BotInventories.ContextInventory();
                                    if (inventory != null && !_inventories[appId].HasContextId(contextId))
                                        _inventories[appId].Add(contextId, inventory);
                                });
                            }
                        }
                        Success = true;
                    }
                    catch (Exception ex)
                    {
                        Success = false;
                        Console.WriteLine(ex);
                    }
                }
                else
                {
                    Success = false;
                    IsPrivate = true;
                }
            });
            new Thread(WaitAllTasks).Start();
        }

        public static GenericInventory FetchInventories(SteamID steamId, SteamWeb steamWeb, List<int> appIdsToFetch = null)
        {
            return new GenericInventory(steamId, steamWeb, appIdsToFetch);
        }  

        public enum AppId
        {
            TF2 = 440,
            Dota2 = 570,
            Portal2 = 620,
            CSGO = 730,
            SpiralKnights = 99900,
            H1Z1 = 295110,
            Steam = 753          
        }

        public enum ContextId
        {
            TF2 = 2,
            Dota2 = 2,
            Portal2 = 2,
            CSGO = 2,
            H1Z1 = 1,
            SteamGifts = 1,
            SteamCoupons = 3,
            SteamCommunity = 6,
            SteamItemRewards = 7           
        }

        /// <summary>
        /// Use this to iterate through items in the inventory.
        /// </summary>
        /// <param name="appId">App ID</param>
        /// <param name="contextId">Context ID</param>
        /// <exception cref="GenericInventoryException">Thrown when inventory does not exist</exception>
        /// <returns>An Inventory object</returns>
        public Inventory GetInventory(int appId, ulong contextId)
        {
            try
            {
                return Inventories[appId][contextId];
            }
            catch
            {
                throw new GenericInventoryException();
            }            
        }

        public void AddForeignInventory(SteamID steamId, int appId, ulong contextId)
        {
            var inventory = FetchForeignInventory(steamId, appId, contextId);
            if (!_inventories.HasAppId(appId))
                _inventories[appId] = new BotInventories.ContextInventory();
            if (inventory != null && !_inventories[appId].HasContextId(contextId))
                _inventories[appId].Add(contextId, inventory);
        }

        private Inventory FetchForeignInventory(SteamID steamId, int appId, ulong contextId)
        {
            var inventoryUrl = string.Format("http://steamcommunity.com/trade/{0}/foreigninventory/?sessionid={1}&steamid={2}&appid={3}&contextid={4}", steamId.ConvertToUInt64(), _steamWeb.SessionId, steamId.ConvertToUInt64(), appId, contextId);
            return FetchInventory(inventoryUrl, steamId, appId, contextId);
        }

        private Inventory FetchInventory(string inventoryUrl, SteamID steamId, int appId, ulong contextId, int start = 0)
        {
            inventoryUrl = inventoryUrl + "&start=" + start;
            var response = RetryWebRequest(inventoryUrl);
            try
            {
                var inventory = JsonConvert.DeserializeObject<Inventory>(response);
                if (inventory.More) {
                    var addInv = FetchInventory(inventoryUrl, steamId, appId, contextId, inventory.MoreStart);
                    inventory.Items = inventory.Items.Concat(addInv.Items).ToList();
                    inventory.Descriptions = inventory.Descriptions.Concat(addInv.Descriptions).ToList();
                }
                inventory.AppId = appId;
                inventory.ContextId = contextId;
                inventory.SteamId = steamId;
                return inventory;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to deserialize {0}.", inventoryUrl);
                Console.WriteLine(ex);
                return null;
            }
        }        

        private void WaitAllTasks()
        {
            _constructTask.Wait();
            foreach (var contextTask in _inventoryTasks.SelectMany(task => task.Value))
            {
                contextTask.Value.Wait();
            }
            OnInventoriesLoaded(EventArgs.Empty);
        }

        public delegate void InventoriesLoadedEventHandler(object sender, EventArgs e);

        public event InventoriesLoadedEventHandler InventoriesLoaded;

        protected virtual void OnInventoriesLoaded(EventArgs e)
        {
            if (_loaded) return;
            _loaded = true;
            if (InventoriesLoaded != null)
                InventoriesLoaded(this, e);            
        }

        /// <summary>
        /// Calls the given function multiple times, until we get a non-null/non-false/non-zero result, or we've made at least
        /// WEB_REQUEST_MAX_RETRIES attempts (with WEB_REQUEST_TIME_BETWEEN_RETRIES_MS between attempts)
        /// </summary>
        /// <returns>The result of the function if it succeeded, or an empty string otherwise</returns>
        private string RetryWebRequest(string url)
        {
            for (var i = 0; i < WebRequestMaxRetries; i++)
            {
                try
                {
                    return _steamWeb.Fetch(url, "GET");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                if (i != WebRequestMaxRetries)
                {
                    System.Threading.Thread.Sleep(WebRequestTimeBetweenRetriesMs);
                }
            }
            return string.Empty;
        }

        public bool Success = true;
        public bool IsPrivate;        

        public class InventoryApps
        {
            [JsonProperty("appid")]
            public int AppId { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("icon")]
            public string Icon { get; set; }

            [JsonProperty("link")]
            public string Link { get; set; }

            [JsonProperty("asset_count")]
            public int AssetCount { get; set; }

            [JsonProperty("inventory_logo")]
            public string InventoryLogo { get; set; }

            [JsonProperty("trade_permissions")]
            public string TradePermissions { get; set; }

            [JsonProperty("rgContexts")]
            public Dictionary<ulong, RgContext> RgContexts { get; set; }

            public class RgContext
            {
                [JsonProperty("asset_count")]
                public int AssetCount { get; set; }

                [JsonProperty("id")]
                public string Id { get; set; }

                [JsonProperty("name")]
                public string Name { get; set; }
            }
        }

        public class Inventory
        {
            public Inventory(dynamic moreStart)
            {
                this.moreStart = moreStart;
            }

            public Item GetItem(int appId, ulong contextId, ulong id)
            {
                var itemId = id.ToString();
                var item = RgCurrencies.ContainsKey(itemId) ? RgCurrencies[itemId] : null;
                if (item == null) return RgInventory.ContainsKey(itemId) ? RgInventory[itemId] : null;
                item.IsCurrency = true;
                return item;
            }

            public ItemDescription GetItemDescription(int appId, ulong contextId, ulong id, bool isCurrency)
            {
                var itemId = id.ToString();
                if (isCurrency)
                {
                    if (!RgCurrencies.ContainsKey(itemId)) return null;
                    var item = RgCurrencies[itemId];
                    var key = string.Format("{0}_{1}", item.ClassId, 0);
                    return RgDescriptions.ContainsKey(key) ? RgDescriptions[key] : null;
                }
                if (RgInventory.ContainsKey(itemId))
                {
                    var item = RgInventory[itemId];
                    var key = string.Format("{0}_{1}", item.ClassId, item.InstanceId);
                    return RgDescriptions.ContainsKey(key) ? RgDescriptions[key] : null;
                }
                return null;
            }

            public ItemDescription GetItemDescriptionByClassId(int appId, ulong contextId, ulong classId, bool isCurrency)
            {
                if (isCurrency)
                {
                    var key = string.Format("{0}_{1}", classId, 0);
                    return RgDescriptions.ContainsKey(key) ? RgDescriptions[key] : null;
                }
                foreach (var rgItem in RgInventory.Where(rgItem => rgItem.Value.ClassId == classId))
                {
                    var item = RgInventory[rgItem.Key];
                    var key = string.Format("{0}_{1}", classId, item.InstanceId);
                    return RgDescriptions.ContainsKey(key) ? RgDescriptions[key] : null;
                }
                return null;
            }

            public int AppId { get; set; }
            public ulong ContextId { get; set; }
            public SteamID SteamId { get; set; }

            private List<Item> _items { get; set; }
            public List<Item> Items
            {
                get
                {
                    if (_items == null)
                    {
                        _items = new List<Item>();
                        _items.AddRange(RgInventory.Values);
                        _items.AddRange(RgCurrencies.Values);
                    }                    
                    return _items;
                }
                set { _items = value; }
            }

            private List<ItemDescription> _descriptions { get; set; } 
            public List<ItemDescription> Descriptions
            {
                get
                {
                    if (_descriptions == null)
                    {
                        _descriptions = RgDescriptions.Values.ToList();
                    }
                    return _descriptions;
                }
                set { _descriptions = value;  }
            }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("rgInventory")]
            private dynamic _rgInventory { get; set; }
            private Dictionary<string, Item> rgInventory { get; set; }
            private Dictionary<string, Item> RgInventory
            {
                // for some games rgInventory will be an empty array instead of a dictionary (e.g. [])
                // this try-catch handles that
                get
                {
                    try
                    {
                        if (rgInventory == null)
                            rgInventory = JsonConvert.DeserializeObject<Dictionary<string, Item>>(Convert.ToString(_rgInventory));
                        return rgInventory;
                    }
                    catch
                    {
                        return new Dictionary<string, Item>();
                    }
                }
            }

            [JsonProperty("rgCurrency")]
            private dynamic _rgCurrency { get; set; }
            private Dictionary<string, Item> rgCurrencies { get; set; }
            private Dictionary<string, Item> RgCurrencies
            {
                // for some games rgCurrency will be an empty array instead of a dictionary (e.g. [])
                // this try-catch handles that
                get
                {
                    try
                    {
                        if (rgCurrencies == null)
                            rgCurrencies = JsonConvert.DeserializeObject<Dictionary<string, Item>>(Convert.ToString(_rgCurrency));
                        return rgCurrencies;
                    }
                    catch
                    {
                        return new Dictionary<string, Item>();
                    }
                }
            }

            [JsonProperty("rgDescriptions")]
            private dynamic _rgDescriptions { get; set; }
            private Dictionary<string, ItemDescription> rgDescriptions { get; set; }
            private Dictionary<string, ItemDescription> RgDescriptions
            {
                get
                {
                    try
                    {
                        if (rgDescriptions == null)
                            rgDescriptions = JsonConvert.DeserializeObject<Dictionary<string, ItemDescription>>(Convert.ToString(_rgDescriptions));
                        return rgDescriptions;
                    }
                    catch
                    {
                        return new Dictionary<string, ItemDescription>();
                    }
                }
            }

            [JsonProperty("more")]
            public bool More { get; set; }

            //If the JSON returns false it will be 0 (as it should be)
            [JsonProperty("more_start")]
            private dynamic moreStart { get; }
            public int MoreStart
            {
                get
                {
                    return More ? (int) moreStart : 0;                    
                }
            }

            public class Item : IEquatable<Item>
            {
                [JsonProperty("id")]
                public ulong Id { get; set; }

                [JsonProperty("classid")]
                public ulong ClassId { get; set; }

                [JsonProperty("instanceid")]
                public ulong InstanceId { get; set; }

                [JsonProperty("amount")]
                public int Amount { get; set; }

                [JsonProperty("is_currency")]
                public bool IsCurrency { get; set; }

                [JsonProperty("pos")]
                public int Position { get; set; }

                /// <summary>
                /// Only available in Inventory History
                /// </summary>
                [JsonProperty("owner")]
                public ulong OwnerId { get; set; }

                public override bool Equals(object obj)
                {
                    return Equals(obj as Item);
                }

                public override int GetHashCode()
                {
                    return base.GetHashCode();
                }

                public bool Equals(Item other)
                {
                    if (other == null)
                        return false;

                    return Id == other.Id &&
                           ClassId == other.ClassId &&
                           InstanceId == other.InstanceId &&
                           Amount == other.Amount &&
                           IsCurrency == other.IsCurrency &&
                           Position == other.Position &&
                           OwnerId == other.OwnerId;
                }
            }

            public class ItemDescription
            {
                /// <summary>
                /// Only available in Inventory History
                /// </summary>
                [JsonProperty("owner")]
                public ulong OwnerId { get; set; }

                [JsonProperty("appid")]
                public int AppId { get; set; }

                [JsonProperty("classid")]
                public ulong ClassId { get; set; }

                [JsonProperty("instanceid")]
                public ulong InstanceId { get; set; }

                [JsonProperty("icon_url")]
                public string IconUrl { get; set; }

                [JsonProperty("icon_url_large")]
                public string IconUrlLarge { get; set; }

                [JsonProperty("icon_drag_url")]
                public string IconDragUrl { get; set; }

                [JsonProperty("name")]
                public string DisplayName { get; set; }

                [JsonProperty("market_hash_name")]
                public string MarketHashName { get; set; }

                [JsonProperty("market_name")]
                private string name { get; set; }
                public string Name
                {
                    get
                    {
                        return string.IsNullOrEmpty(name) ? DisplayName : name;
                    }
                }

                [JsonProperty("name_color")]
                public string NameColor { get; set; }

                [JsonProperty("background_color")]
                public string BackgroundColor { get; set; }

                [JsonProperty("type")]
                public string Type { get; set; }

                [JsonProperty("tradable")]
                private short isTradable { get; set; }
                public bool IsTradable { get { return isTradable == 1; } set { isTradable = Convert.ToInt16(value); } }

                [JsonProperty("marketable")]
                private short isMarketable { get; set; }
                public bool IsMarketable { get { return isMarketable == 1; } set { isMarketable = Convert.ToInt16(value); } }

                [JsonProperty("commodity")]
                private short isCommodity { get; set; }
                public bool IsCommodity { get { return isCommodity == 1; } set { isCommodity = Convert.ToInt16(value); } }

                [JsonProperty("market_fee_app")]
                public int MarketFeeApp { get; set; }

                [JsonProperty("descriptions")]
                public Description[] Descriptions { get; set; }

                [JsonProperty("actions")]
                public Action[] Actions { get; set; }

                [JsonProperty("owner_actions")]
                public Action[] OwnerActions { get; set; }

                [JsonProperty("tags")]
                public Tag[] Tags { get; set; }

                [JsonProperty("app_data")]
                public App_Data AppData { get; set; }

                public class Description
                {
                    [JsonProperty("type")]
                    public string Type { get; set; }

                    [JsonProperty("value")]
                    public string Value { get; set; }
                }

                public class Action
                {
                    [JsonProperty("name")]
                    public string Name { get; set; }

                    [JsonProperty("link")]
                    public string Link { get; set; }
                }

                public class Tag
                {
                    [JsonProperty("internal_name")]
                    public string InternalName { get; set; }

                    [JsonProperty("name")]
                    public string Name { get; set; }

                    [JsonProperty("category")]
                    public string Category { get; set; }

                    [JsonProperty("color")]
                    public string Color { get; set; }

                    [JsonProperty("category_name")]
                    public string CategoryName { get; set; }
                }

                public class App_Data
                {
                    [JsonProperty("def_index")]
                    public ushort Defindex { get; set; }

                    [JsonProperty("quality")]
                    public int Quality { get; set; }
                }
            }
        }
    }

    public class GenericInventoryException : Exception
    {
        
    }

    public class InventoriesToFetch : Dictionary<SteamID, List<InventoriesToFetch.InventoryInfo>>
    {
        public class InventoryInfo
        {
            public int AppId { get; set; }
            public ulong ContextId { get; set; }

            public InventoryInfo(int appId, ulong contextId)
            {
                AppId = appId;
                ContextId = contextId;
            }
        }
    }

    public class BotInventories : Dictionary<int, BotInventories.ContextInventory>
    {
        public bool HasAppId(int appId)
        {
            return ContainsKey(appId);
        }

        public class ContextInventory : Dictionary<ulong, GenericInventory.Inventory>
        {
            public ulong ContextId { get; set; }
            public GenericInventory.Inventory Inventory { get; set; }

            public bool HasContextId(ulong contextId)
            {
                return ContainsKey(contextId);
            }
        }
    }

    public class InventoryTasks : Dictionary<int, InventoryTasks.ContextTask>
    {
        public bool HasAppId(int appId)
        {
            return ContainsKey(appId);
        }

        public class ContextTask : Dictionary<ulong, Task>
        {
            public ulong ContextId { get; set; }
            public Task InventoryTask { get; set; }
        }
    }
}