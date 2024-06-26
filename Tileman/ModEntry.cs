﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;
using Tileman;
using xTile.Dimensions;
using xTile.Tiles;
using static StardewValley.Minigames.TargetGame;

namespace Tileman
{

    public class PurchaseColumn
    {
        public HashSet<int> purchasedCells = new();
    }
    public class PurchaseRows
    {
        public Dictionary<int, PurchaseColumn> columns = new();
    }
    public class PurchaseData
    {
        public Dictionary<string, PurchaseRows> data = new();
    }

    public static class DictioanryExtensions
    {
        public static TValue GetOrCreate<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key)
            where TValue : new()
        {
            if (!dict.TryGetValue(key, out TValue val))
            {
                val = new TValue();
                dict.Add(key, val);
            }

            return val;
        }
    }

    public class PurchaseManifestFile
    {
        public PurchaseData purchaseData = new();
        public int version = 1; // future proofing for save versioning
    }

    public class ModEntry : Mod
    {
        public bool do_loop = true;
        public bool do_collision = true;
        public bool allow_player_placement = false;
        public bool toggle_overlay = true;
        private bool tool_button_pushed = false;
        private bool location_changed = false;

        public double tile_price = 1.0;
        public double tile_price_raise = 0.0008;
        public double dynamic_tile_price = 1.0;

        public int caverns_extra = 0;
        public int difficulty_mode = 0;
        public int purchase_count=0;
        public int overlay_mode = 2;
        private int NUM_OVERLAY_MODES = 4;
        public int location_transition_time = 10;

        private bool seen_emmalution_easter_egg = false;
        private bool should_show_emmalution_easter_egg = false;
        private bool shown_perfection = false;
        private bool show_perfection_on_change = false;

        private int days_started = 0;

        public int amountLocations = 200;
        private int locationDelay = 0;
        
        private int collisionTick = 0;
        private KaiTile lastCollidedTile = null;

        private Location targetTile = new();

        private GameLocation lastLocation = Game1.currentLocation;
        private GameLocation targetLocation;
        private GameLocation currentLocation;

        private Vector2 startLocationInNewLocation = default;

        List<KaiTile> tileList = new();
        List<KaiTile> ThisLocationTiles = new();
        Dictionary<string, List<KaiTile>> tileDict = new();
        PurchaseData purchased_tiles_by_location = new();


        Texture2D tex_baseTile  = new(Game1.game1.GraphicsDevice, Game1.tileSize, Game1.tileSize);
        Texture2D tex_purchaseTile = new(Game1.game1.GraphicsDevice, Game1.tileSize, Game1.tileSize);
        Texture2D tex_insufficientFundsTile = new(Game1.game1.GraphicsDevice, Game1.tileSize, Game1.tileSize);
        Texture2D tex_distantTile = new(Game1.game1.GraphicsDevice, Game1.tileSize, Game1.tileSize);

        private bool legacy = false; // True for Pre-spicykai fork


        HashSet<Vector2> specificInclusionsInTempLocation = new()
                {
                    new(21, 42),
                    new(21, 43),
                    new(21, 44),
                    new(21, 45),
                    new(21, 46),

                    new(49, 42),
                    new(49, 43),
                    new(49, 44),
                    new(49, 45),
                    new(49, 46),
                    new(49, 47),
                    new(49, 48),
                    new(49, 49),
                    new(49, 50),
                    new(49, 51),

                    new(55, 102),
                    new(55, 103),
                    new(55, 104),
                    new(55, 105),
                    new(55, 106),
                    new(55, 107)
                };

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonReleased += this.OnButtonReleased;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Display.RenderedWorld += this.DrawUpdate;

            helper.Events.GameLoop.Saved += this.SaveModData;
            helper.Events.GameLoop.SaveLoaded += this.LoadModData;
            helper.Events.GameLoop.DayStarted += this.DayStartedUpdate;
            helper.Events.GameLoop.ReturnedToTitle += this.TitleReturnUpdate;

            helper.Events.Player.InventoryChanged += this.InventoryChanged;
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            tex_baseTile = helper.ModContent.Load<Texture2D>("assets/tile.png");
            tex_purchaseTile = helper.ModContent.Load<Texture2D>("assets/tile_2.png");
            tex_insufficientFundsTile = helper.ModContent.Load<Texture2D>("assets/tile_3.png");
            tex_distantTile = helper.ModContent.Load<Texture2D>("assets/tile_4.png");
        }

        private void InventoryChanged(object sender, InventoryChangedEventArgs e)
        {
            if (IsEmmaPlaying() && !show_perfection_on_change && !shown_perfection && Game1.player.hasItemInInventoryNamed("Statue Of True Perfection"))
            {
                show_perfection_on_change = true;
            }
        }

        private void removeSpecificTile(int xTile, int yTile, string gameLocation)
        {
            var tileData = this.Helper.Data.ReadJsonFile<MapData>($"jsons/{Constants.SaveFolderName}/{gameLocation}.json") ?? new MapData();
            var tempList = tileData.AllKaiTilesList;

            for (int i = 0; i < tileData.AllKaiTilesList.Count; i++)
            {
                KaiTile t = tileData.AllKaiTilesList[i];

                if (t.IsSpecifiedTile(xTile, yTile, gameLocation)) {
                    
                    tempList.Remove(t);
                    RemoveProperties(t, Game1.getLocationFromName(gameLocation));

                }

            }
            var mapData = new MapData
            {
                AllKaiTilesList = tempList,
            };

            Helper.Data.WriteJsonFile<MapData>($"jsons/{Constants.SaveFolderName}/{gameLocation}.json", mapData);
            tileList = new();
        }

        private void RemoveProperties(KaiTile tile, GameLocation gameLocation)
        {
            gameLocation.removeTileProperty(tile.tileX, tile.tileY, "Back", "Buildable");
            if (gameLocation.doesTileHavePropertyNoNull(tile.tileX, tile.tileY, "Type", "Back") == "Dirt"
                || gameLocation.doesTileHavePropertyNoNull(tile.tileX, tile.tileY, "Type", "Back") == "Grass") gameLocation.setTileProperty(tile.tileX, tile.tileY, "Back", "Diggable", "true");

            gameLocation.removeTileProperty(tile.tileX, tile.tileY, "Back", "NoFurniture");
            gameLocation.removeTileProperty(tile.tileX, tile.tileY, "Back", "NoSprinklers");

            gameLocation.removeTileProperty(tile.tileX, tile.tileY, "Back", "Passable");
            gameLocation.removeTileProperty(tile.tileX, tile.tileY, "Back", "Placeable");

            var i = tile.tileX;
            var j = tile.tileY;
            var backLayer = gameLocation.map.GetLayer("Back");
            var isValidLayerLoc = backLayer.IsValidTileLocation(i, j);
            if (isValidLayerLoc && backLayer.Tiles[i,j] != null)
            {
                backLayer.Tiles[i, j].Properties["TilemanTile"] = "Purchased";
            }
            ThisLocationTiles.Remove(tile);
            tileList.Remove(tile);
            var locationName = GetTileKey(gameLocation);
            PurchaseRows rows = purchased_tiles_by_location.data.GetOrCreate(locationName);
            PurchaseColumn col = rows.columns.GetOrCreate(tile.tileX);
            col.purchasedCells.Add(tile.tileY);

        }
        public void DEPRECATED_RemoveTileExceptions()
        {
            this.Monitor.Log("Removing Unusual Tiles", LogLevel.Debug);

            removeSpecificTile(18,27,"Desert");

            removeSpecificTile(12, 9, "BusStop");
        }

        public void DEPRECATED_AddTileExceptions()
        {
            this.Monitor.Log("Placing Unusual Tiles", LogLevel.Debug);

            var tempName = "Town";

            //ADD UNUSAL TILES HERE
            tileDict[tempName].Add(new KaiTile(21, 42, tempName));
            tileDict[tempName].Add(new KaiTile(21, 43, tempName));
            tileDict[tempName].Add(new KaiTile(21, 44, tempName));
            tileDict[tempName].Add(new KaiTile(21, 45, tempName));
            tileDict[tempName].Add(new KaiTile(21, 46, tempName));

            tileDict[tempName].Add(new KaiTile(49, 42, tempName));
            tileDict[tempName].Add(new KaiTile(49, 43, tempName));
            tileDict[tempName].Add(new KaiTile(49, 44, tempName));
            tileDict[tempName].Add(new KaiTile(49, 45, tempName));
            tileDict[tempName].Add(new KaiTile(49, 46, tempName));
            tileDict[tempName].Add(new KaiTile(49, 47, tempName));
            tileDict[tempName].Add(new KaiTile(49, 48, tempName));
            tileDict[tempName].Add(new KaiTile(49, 49, tempName));
            tileDict[tempName].Add(new KaiTile(49, 50, tempName));
            tileDict[tempName].Add(new KaiTile(49, 51, tempName));

            tileDict[tempName].Add(new KaiTile(55, 102, tempName));
            tileDict[tempName].Add(new KaiTile(55, 103, tempName));
            tileDict[tempName].Add(new KaiTile(55, 104, tempName));
            tileDict[tempName].Add(new KaiTile(55, 105, tempName));
            tileDict[tempName].Add(new KaiTile(55, 106, tempName));
            tileDict[tempName].Add(new KaiTile(55, 107, tempName));

            //Helper.Data.WriteJsonFile<MapData>($"jsons/{Constants.SaveFolderName}/{tempName}.json", mapData);

            //
            //specific tiles to add in /// COPY ABOVE
            //Mountain 3X3 50,6 -> 52,8
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // ignore if player hasn't loaded a save yet
            if (!Context.IsWorldReady) return;
            if (!Context.IsPlayerFree) return;
            if (Game1.player.isFakeEventActor) return;

            if (e.Button == SButton.G)
            {
                toggle_overlay = !toggle_overlay;
                this.Monitor.Log($"Tileman Overlay set to:{toggle_overlay}", LogLevel.Debug);
                if(toggle_overlay) Game1.playSoundPitched("coin", 1000 );
                if(!toggle_overlay) Game1.playSoundPitched("coin", 600 );

            }
            if (e.Button == SButton.H)
            {
                overlay_mode = (overlay_mode + 1) % NUM_OVERLAY_MODES;
                var mode = "Mouse";
                switch (overlay_mode)
                {
                    case 0:
                        mode = "Controller (Legacy)";
                        break;
                    case 1:
                        mode = "Mouse";
                        break;
                    case 2:
                        mode = "Controller";
                        break;
                    case 3:
                        mode = "Mouse (nearby only)";
                        break;
                }

                Monitor.Log($"Tileman Overlay Mode set to: {mode}", LogLevel.Debug);
                Game1.playSoundPitched("coin", 1200);
            }

            if (!toggle_overlay) return;
            if (e.Button.IsUseToolButton()) tool_button_pushed = true;
        }

        private void OnButtonReleased(object sender, ButtonReleasedEventArgs e)
        {
            if (e.Button.IsUseToolButton()) tool_button_pushed = false;
        }

        private void DayStartedUpdate(object sender, DayStartedEventArgs e)
        {
            days_started += 1;
            if (legacy)
            {
                DEPRECATED_PlaceInMaps();
                DEPRECATED_GetLocationTiles(Game1.currentLocation);
            }
            else
            {
                FillLocationAndRemovePurchasedTiles(Game1.currentLocation);
            }
            if (should_show_emmalution_easter_egg && days_started > 7)
            {
                if (Game1.player.mailReceived.Contains(MyModMail.EasterEggName))
                {
                    should_show_emmalution_easter_egg = false;
                    seen_emmalution_easter_egg = true;
                }
                else
                {
                    Game1.player.mailbox.Add(MyModMail.EasterEggName);
                }
            }
        }

        private void TitleReturnUpdate(object sender, ReturnedToTitleEventArgs e)
        {
            ResetValues();
        }

        private void DrawUpdate(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            //Makes sure to not draw while a cutscene is happening
            if (Game1.CurrentEvent != null) {
                if (!Game1.CurrentEvent.playerControlSequence)
                {
                    return;
                }
            }

            UpdateTargetTile();
            GroupIfLocationChange();

            var anyCollision = false;

            bool hasFoundCollidingTile = false;
            float currentClosestTileDistance = float.MaxValue;
            Vector2 playerDelta = new();
            if (toggle_overlay || do_collision)
            {
                for (int i = 0; i < ThisLocationTiles.Count; i++)
                {
                    KaiTile t = ThisLocationTiles[i];
                    if (!(t.tileIsWhere == Game1.currentLocation.Name || Game1.currentLocation.Name == "Temp"))
                    {
                        continue;
                    }
                    if (toggle_overlay)
                    {
                        Texture2D texture = tex_baseTile;
                        DrawPrice(t, e, ref texture);
                        t.DrawTile(texture, e.SpriteBatch);
                    }

                    //Prevent player from being pushed out of bounds
                    if (do_collision)
                    {
                        anyCollision |= PlayerCollisionCheck(t, ref playerDelta, ref currentClosestTileDistance, ref hasFoundCollidingTile);
                    }
                }
            }
            if (anyCollision)
            {
                collisionTick++;
                Game1.player.Position += playerDelta;
                bool secondCollision = false;
                for (int i = ThisLocationTiles.Count - 1; i >= 0; i--)
                {
                    secondCollision |= PlayerCollisionCheck(ThisLocationTiles[i], ref playerDelta, ref currentClosestTileDistance, ref hasFoundCollidingTile);
                }
                if (secondCollision)
                {
                    Game1.player.Position += playerDelta;
                }
            }
            if (collisionTick > 120 && lastCollidedTile != null)
            {
                collisionTick = 0;
                PurchaseTileCheck(lastCollidedTile, true);
            }
            if (do_collision && !anyCollision)
            {
                collisionTick = 0;
            }

            if (tool_button_pushed) PurchaseTilePreCheck();
        }

        public void UpdateTargetTile()
        {
            Location result = default;

            Point loc = Game1.player.getStandingXY();
            switch (Game1.player.FacingDirection)
            {
                case 0:
                    result = new Location(loc.X, loc.Y - 64);
                    break;
                case 1:
                    result = new Location(loc.X + 64, loc.Y);
                    break;
                case 2:
                    result = new Location(loc.X, loc.Y + 64);
                    break;
                case 3:
                    result = new Location(loc.X - 64, loc.Y);
                    break;
            }

            targetTile = result / 64;
        }

        private void DrawPrice(KaiTile t, RenderedWorldEventArgs e, ref Texture2D texture)
        {
            var stringColor = Color.Gold;
            bool useCursor = overlay_mode == 1 || overlay_mode == 3;
            Vector2 target = GetCurrentTarget();
            int targetX = (int)Math.Floor(target.X);
            int targetY = (int)Math.Floor(target.Y);
            Vector2 textPosition = useCursor ? new Vector2(Game1.getMousePosition().X, Game1.getMousePosition().Y - Game1.tileSize)
                                            : new Vector2((t.tileX) * 64 - Game1.viewport.X, (t.tileY) * 64 - 64 - Game1.viewport.Y);

            Vector2 playerTile = Game1.player.getTileLocation();

            if (targetX == t.tileX && targetY == t.tileY)
            {
                texture = tex_purchaseTile;

                if (Game1.player.Money < (int)Math.Floor(dynamic_tile_price))
                {
                    stringColor = Color.Red;
                    texture = tex_insufficientFundsTile;
                }
                else if (overlay_mode == 3 && Math.Max(Math.Abs(playerTile.X - t.tileX), Math.Abs(playerTile.Y - t.tileY)) > 1)
                {
                    stringColor = Color.Yellow;
                    texture = tex_distantTile;
                }

                e.SpriteBatch.DrawString(Game1.dialogueFont, $"${(int)Math.Floor(GetTilePrice())}", textPosition, stringColor);
            }
        }

        private static IEnumerable<GameLocation> GetLocations()
        {
            var locations = Game1.locations
                .Concat(
                    from location in Game1.locations.OfType<BuildableGameLocation>()
                    from building in location.buildings
                    where building.indoors.Value != null
                    select building.indoors.Value
                );

            return locations;
        }

        private double GetTilePrice()
        {
            dynamic_tile_price = tile_price;
            switch (difficulty_mode)
            {
                case 0:
                    dynamic_tile_price = tile_price + (tile_price_raise * purchase_count);
                    break;

                case 1:
                    // Double the price for each digit in the purchase count
                    dynamic_tile_price = tile_price * Math.Pow(2.0, purchase_count <= 1 ? 0 : ("" + (purchase_count - 1)).Length);
                    break;

                case 2:
                    //Increment tile price with each one purchased
                    dynamic_tile_price = purchase_count;
                    break;
            }
            return dynamic_tile_price;
        }

        private Vector2 GetCurrentTarget()
        {
            Vector2 currentTarget;
            switch (overlay_mode)
            {
                case 0:
                    // Legacy "facing" system
                    currentTarget = new Vector2(Game1.player.nextPositionTile().X, Game1.player.nextPositionTile().Y);
                    break;
                case 1:
                    // Mouse
                    currentTarget = new Vector2(Game1.currentCursorTile.X, Game1.currentCursorTile.Y);
                    break;
                case 2:
                    // Newer facing system
                    currentTarget = new Vector2(targetTile.X, targetTile.Y);
                    break;
                case 3:
                    // Mouse, but only nearby
                    currentTarget = new Vector2(Game1.currentCursorTile.X, Game1.currentCursorTile.Y);
                    break;
                default:
                    return new();
            }
            return currentTarget;
        }

        private void PurchaseTilePreCheck()
        {
            Vector2 currentTarget = GetCurrentTarget();
            if (overlay_mode == 3)
            {
                Vector2 playerTile = Game1.player.getTileLocation();
                if (Math.Max(Math.Abs(playerTile.X - currentTarget.X), Math.Abs(playerTile.Y - currentTarget.Y)) > 1)
                {
                    // Tile was out of range
                    return;
                }
            }
            for (int i = 0; i < ThisLocationTiles.Count; i++)
            {
                KaiTile t = ThisLocationTiles[i];
                if (currentTarget == new Vector2(t.tileX, t.tileY))
                {
                    PurchaseTileCheck(t);
                }
            }
        }

        private void PurchaseTileCheck(KaiTile thisTile, bool free = false)
        {
            GetTilePrice();
            int floor_price = (int)Math.Floor(dynamic_tile_price);


            if (!free & Game1.player.Money < floor_price)
            {
                Game1.playSoundPitched("grunt", 700 + (100 * new Random().Next(0, 7)));
                return;
            }
            if (!free)
            {
                Game1.player.Money -= floor_price;
            }

            purchase_count++;

            Game1.playSoundPitched("purchase", 700 + (100* new Random().Next(0, 7)) );
            
            RemoveProperties(thisTile, Game1.currentLocation);
        }

        private void GroupIfLocationChange()

        {
            if (legacy)
            {
                DEPRECATED_GroupIfLocationChange();
                return;
            }

            lastLocation = currentLocation;
            currentLocation = Game1.currentLocation;
            location_changed = currentLocation != lastLocation && locationDelay <= 0;
            if (!location_changed)
            {
                return;
            }
            PurchaseTileCheck(new KaiTile(Game1.player.getTileX(), Game1.player.getTileY(), currentLocation.Name), true);
            if (show_perfection_on_change)
            {
                shown_perfection = true;
                show_perfection_on_change = false;

                string message = "A breeze passes through the valley, and you hear the whispers of the Berries congratulating you on your accomplishments";
                Game1.activeClickableMenu = new DialogueBox(message);
            }
            FillLocationAndRemovePurchasedTiles(Game1.currentLocation);
            this.Monitor.Log("Entered " + GetTileKey(Game1.currentLocation), LogLevel.Debug);
        }


        private bool IsTilePurchased(GameLocation location, int tileX, int tileY)
        {
            var locationName = GetTileKey(location);
            if (!purchased_tiles_by_location.data.ContainsKey(locationName))
            {
                return false;
            }
            if (!purchased_tiles_by_location.data[locationName].columns.ContainsKey(tileX))
            {
                return false;
            }

            PurchaseColumn column = purchased_tiles_by_location.data[locationName].columns[tileX];
            return column.purchasedCells.Contains(tileY);
        }

        private bool IsSpecificallyIncluded(GameLocation gameLocation, int tileX, int tileY)
        {
            if (IsTempLocation(gameLocation))
            {
                return specificInclusionsInTempLocation.Contains(new Vector2(tileX, tileY));
            }
            return false;
        }

        private bool IsSpecificallyExcluded(GameLocation gameLocation, int tileX, int tileY)
        {
            var locationName = GetTileKey(gameLocation);

            if (locationName == "Desert")
            {
                return tileX == 18 && tileY == 27;
            }

            if (locationName == "BusStop")
            {
                return tileX == 12 && tileY == 9;
            }
            return false;
        }

        private void FillLocationAndRemovePurchasedTiles(GameLocation gameLocation)
        {
            this.Monitor.Log("Filling location: " + GetTileKey(gameLocation), LogLevel.Debug);
            int mapWidth = gameLocation.map.Layers[0].LayerWidth;
            int mapHeight = gameLocation.map.Layers[0].LayerHeight;

            ThisLocationTiles = new();

            var playerPos = new Vector2(Game1.player.position.X, Game1.player.position.Y);
            for (int i = 1; i < mapWidth - 1; i++)
            {
                for (int j = 1; j < mapHeight - 1; j++)
                {
                    bool isSpecificallyIncluded = IsSpecificallyIncluded(gameLocation, i, j);
                    bool isSpecificallyExcluded = IsSpecificallyExcluded(gameLocation, i, j);
                    if (isSpecificallyExcluded)
                    {
                        continue;
                    }
                    if (IsTilePurchased(gameLocation, i, j))
                    {
                        continue;
                    }
                    var backLayer = gameLocation.map.GetLayer("Back");
                    var isValidLayerLoc = backLayer.IsValidTileLocation(i, j);
                    var hasTilemanProperty = isValidLayerLoc && backLayer.Tiles[i,j] != null && backLayer.Tiles[i, j].Properties.ContainsKey("TilemanTile") && backLayer.Tiles[i, j].Properties["TilemanTile"] == "Unpurchased";
                    if (isSpecificallyIncluded 
                        || hasTilemanProperty
                        || (
                            !gameLocation.isObjectAtTile(i, j)
                            && !gameLocation.isOpenWater(i, j)
                            && !gameLocation.isTerrainFeatureAt(i, j)
                            && gameLocation.isTilePlaceable(new Vector2(i, j))
                            && gameLocation.isTileLocationTotallyClearAndPlaceable(new Vector2(i, j))
                            && gameLocation.Map.Layers[0].IsValidTileLocation(i, j)
                            && gameLocation.isCharacterAtTile(new Vector2(i, j)) == null
                            && playerPos != new Vector2(i, j)))
                    {
                        var t = new KaiTile(i, j, gameLocation.Name);
                        ThisLocationTiles.Add(t);
                        if (isValidLayerLoc && backLayer.Tiles[i, j] != null)
                        {
                            backLayer.Tiles[i, j].Properties["TilemanTile"] = "Unpurchased";
                        }
                        if (!allow_player_placement)
                        {
                            gameLocation.removeTileProperty(t.tileX, t.tileY, "Back", "Diggable");

                            gameLocation.setTileProperty(t.tileX, t.tileY, "Back", "Buildable", "false");
                            gameLocation.setTileProperty(t.tileX, t.tileY, "Back", "NoFurniture", "true");
                            gameLocation.setTileProperty(t.tileX, t.tileY, "Back", "NoSprinklers", "");
                            gameLocation.setTileProperty(t.tileX, t.tileY, "Back", "Placeable", "");


                        }
                    }
                }
            }
        }

        private bool IsTempLocation(GameLocation gameLocation)
        {
            return gameLocation.Name == "Temp";
        }

        private string GetTileKey(GameLocation gameLocation)
        {
            var locationName = gameLocation.Name;
            if (locationName == "Temp")
            {
                locationName += Game1.currentSeason + Game1.dayOfMonth;
            }
            return locationName;
        }

        private string DEPRECATED_GetTileKey(GameLocation gameLocation)
        {
            var locationName = gameLocation.Name;
            if (locationName == "Temp") locationName += Game1.whereIsTodaysFest;
            return locationName;
        }

        private void ResetValues()
        {
            this.Monitor.Log("Resetting values", LogLevel.Debug);
            do_loop = true;
            toggle_overlay = true;
            do_collision = true;

            tile_price = 1.0;
            tile_price_raise = 0.20;
            purchase_count = 0;

            tileList.Clear();
            ThisLocationTiles.Clear();

            tileDict.Clear();
        }

        private bool PlayerCollisionCheck(KaiTile tile, ref Vector2 playerDelta, ref float currentClosestTileDistance, ref bool hasFoundCollidingTile)
        {

            if (!(Game1.getLocationFromName(tile.tileIsWhere) == Game1.currentLocation || Game1.currentLocation.Name == "Temp"))
            {
                return false;
            }
            Microsoft.Xna.Framework.Rectangle tileBox = new(tile.tileX * 64, tile.tileY * 64, tile.tileW, tile.tileH);
            Microsoft.Xna.Framework.Rectangle playerBox = Game1.player.GetBoundingBox();

            var collided = false;
            if (playerBox.Intersects(tileBox))
            {
                collided = true;
                Microsoft.Xna.Framework.Rectangle.Intersect(ref playerBox, ref tileBox, out Microsoft.Xna.Framework.Rectangle intersection);
                Point directionToMove = playerBox.Center - tileBox.Center;
                bool moveX = intersection.Width <= intersection.Height;
                bool moveY = intersection.Width >= intersection.Height;
                bool moveLeft = Math.Sign(directionToMove.X) <= 0;
                int deltaX = !moveX ? 0 : intersection.Width * (moveLeft ? -1 : 1);
                bool moveUp = Math.Sign(directionToMove.Y) <= 0;
                int deltaY = !moveY ? 0 : intersection.Height * (moveUp ? -1 : 1);

                float thisTileDistance = directionToMove.ToVector2().LengthSquared();

                if (!hasFoundCollidingTile || (thisTileDistance < currentClosestTileDistance))
                {
                    playerDelta = new Vector2(deltaX, deltaY);
                    hasFoundCollidingTile = true;
                    currentClosestTileDistance = thisTileDistance;

                }
            }
            if (playerBox.Center == tileBox.Center || playerBox.Intersects(tileBox) && locationDelay > 0)
            {
                collided = true;
                Game1.player.Position = Game1.player.lastPosition;
            }
            if (collided)
            {
                if (lastCollidedTile != tile)
                {
                    collisionTick = 0;
                }
                lastCollidedTile = tile;
            }
            return collided;
        }

        private void SaveModData(object sender, SavedEventArgs e)
        {
            if (legacy)
            {
                foreach (KeyValuePair<string, List<KaiTile>> entry in tileDict)
                {
                    DEPRECATED_SaveLocationTiles(Game1.getLocationFromName(entry.Key));
                }
                tileDict.Clear();
            }
            else
            {
                SavePurchaseManifest();
            }

            var tileData = new ModData
            {
                ToPlaceTiles   = do_loop,
                DoCollision    = do_collision,
                ToggleOverlay  = toggle_overlay,
                TilePrice      = tile_price,
                TilePriceRaise = tile_price_raise,
                CavernsExtra   = caverns_extra,
                DifficultyMode = difficulty_mode,
                PurchaseCount  = purchase_count,
                Legacy         = legacy,
                LocationTransitionTime = location_transition_time,
                OverlayMode    = overlay_mode
            };

            Helper.Data.WriteJsonFile<ModData>($"jsons/{Constants.SaveFolderName}/config.json", tileData);
        }

        private bool IsEmmaPlaying()
        {
            return Constants.SaveFolderName == "Terracotta_368105912" && Game1.player.Name == "Emma";
        }

        private void LoadModData(object sender, SaveLoadedEventArgs e)
        {
            var d = new ModData(); // defaultData
            d.ToPlaceTiles = do_loop;
            d.ToggleOverlay = toggle_overlay;
            d.DoCollision = do_collision;
            d.TilePrice = tile_price;
            d.TilePriceRaise = tile_price_raise;
            d.CavernsExtra = caverns_extra;
            d.DifficultyMode = difficulty_mode;
            d.PurchaseCount = purchase_count;
            d.Legacy = legacy;
            d.LocationTransitionTime = location_transition_time;
            d.OverlayMode = overlay_mode;
            d.RandomDebugBoolean = seen_emmalution_easter_egg;

            var tileData = Helper.Data.ReadJsonFile<ModData>("config.json") ?? d;

            //Load config Information
            if (Helper.Data.ReadJsonFile<ModData>($"jsons/{Constants.SaveFolderName}/config.json") != null)
            {
                tileData = Helper.Data.ReadJsonFile<ModData>($"jsons/{Constants.SaveFolderName}/config.json") ?? d;
            }
            else
            {
                Helper.Data.WriteJsonFile<ModData>($"jsons/{Constants.SaveFolderName}/config.json", tileData);
            }

            var manifestFile = $"jsons/{Constants.SaveFolderName}/purchaseManifest.json";
            var purchaseFile = Helper.Data.ReadJsonFile<PurchaseManifestFile>(manifestFile) ?? new();
            Helper.Data.WriteJsonFile(manifestFile, purchaseFile);
            purchased_tiles_by_location = purchaseFile.purchaseData;

            do_loop = tileData.ToPlaceTiles;
            toggle_overlay = tileData.ToggleOverlay;
            do_collision = tileData.DoCollision;
            tile_price = tileData.TilePrice;
            tile_price_raise = tileData.TilePriceRaise;
            caverns_extra = tileData.CavernsExtra;
            difficulty_mode = tileData.DifficultyMode;
            purchase_count = tileData.PurchaseCount;
            legacy = tileData.Legacy;
            location_transition_time = tileData.LocationTransitionTime;
            overlay_mode = tileData.OverlayMode;
            seen_emmalution_easter_egg = tileData.RandomDebugBoolean;
            should_show_emmalution_easter_egg = !seen_emmalution_easter_egg && IsEmmaPlaying();


            var hasOldLocationFiles = Helper.Data.ReadJsonFile<ModData>($"jsons/{Constants.SaveFolderName}/Farm.json") != null;
            if (!legacy && hasOldLocationFiles)
            {
                MigrateModDataFromLegacySystem();
            }

        }

        private bool MigrateRegion(string locationName, GameLocation gameLocation)
        {
            this.Monitor.Log("Migrating file " + locationName, LogLevel.Debug);
            var tileData = Helper.Data.ReadJsonFile<MapData>($"jsons/{Constants.SaveFolderName}/{locationName}.json");
            if (tileData == null)
            {
                return false;
            }

            var purchaseDataForRegion = purchased_tiles_by_location.data.GetOrCreate(locationName);

            // Used to use gameLocation.map.Layers[0].LayerWidth, but this covers the case where a legacy map was smaller than the new version. MAx width/height observed was 135x120

            int lookupWidth = 200;
            int lookupHeight = 200;

            bool[,] tilesInFile = new bool[lookupWidth + 1, lookupHeight + 1];
            

            for (int i = 0; i <= lookupWidth; i++)
            {
                for (int j = 0; j <= lookupHeight; j++)
                {
                    tilesInFile[i, j] = false;
                }
            }

            int mapWidth = gameLocation.map.Layers[0].LayerWidth;
            int mapHeight = gameLocation.map.Layers[0].LayerHeight;

            foreach (KaiTile tile in tileData.AllKaiTilesList)
            {
                if (tile.tileX < 0 || tile.tileX >= lookupWidth || tile.tileY < 0 || tile.tileY >= lookupHeight)
                {
                    var error = "Tile at " + tile.tileX + ", " + tile.tileY + " was out of bounds for map of size [" + lookupWidth + ", " + lookupHeight + "]";
                    this.Monitor.Log(error, LogLevel.Error);
                }
                tilesInFile[tile.tileX, tile.tileY] = true;
            }

            for (int i = 1; i < mapWidth - 1; i++)
            {
                for (int j = 1; j < mapHeight - 1; j++)
                {
                    var shouldHaveBeenATile = !gameLocation.isObjectAtTile(i, j)
                        && !gameLocation.isOpenWater(i, j)
                        && !gameLocation.isTerrainFeatureAt(i, j)
                        && gameLocation.isTilePlaceable(new Vector2(i, j))
                        && gameLocation.isTileLocationTotallyClearAndPlaceable(new Vector2(i, j))
                        && gameLocation.Map.Layers[0].IsValidTileLocation(i, j)
                        && gameLocation.isCharacterAtTile(new Vector2(i, j)) == null;
                    if (shouldHaveBeenATile && !tilesInFile[i,j])
                    {
                        var column = purchaseDataForRegion.columns.GetOrCreate(i);
                        column.purchasedCells.Add(j);
                    }
                }
            }
            SavePurchaseManifest();
            return true;
        }

        private void MigrateModDataFromLegacySystem()
        {
            this.Monitor.Log("HELLO! Welcome to the Automated Tileman Legacy Automated Switcheroo system. This ATLAS system will take your old mod files, migrate them to the new system, and store them away in case it went wrong.", LogLevel.Warn);
            this.Monitor.Log("This may take some time, sorry. When its done, the old files will now be in a folder in your mod save called LegacyBackup. Just... in case", LogLevel.Warn);

            var allFileNames = new List<string>();
            
            foreach (GameLocation location in GetLocations())
            {
                var filename = GetTileKey(location);
                if (MigrateRegion(filename, location))
                { 
                    allFileNames.Add(filename);
                }
            }

            for (int i = 1; i <= 220 + caverns_extra; i++)
            {
                var gameLocation = Game1.getLocationFromName("UndergroundMine" + i);
                var locationName = gameLocation.Name;
                if (MigrateRegion(locationName, gameLocation))
                {
                    allFileNames.Add(locationName);
                }
            }

            //VolcanoDungeon0 - 9
            for (int i = 0; i <= 9; i++)
            {
                var gameLocation = Game1.getLocationFromName("VolcanoDungeon" + i);
                var locationName = gameLocation.Name;
                if (MigrateRegion(locationName, gameLocation))
                {
                    allFileNames.Add(locationName);
                }
            }

            System.IO.DirectoryInfo root = new($"{Constants.GamePath}/Mods/Tileman/jsons/{Constants.SaveFolderName}");

            System.IO.FileInfo[] files = root.GetFiles();
            foreach (System.IO.FileInfo file in files)
            {
                if (file.Name.StartsWith("Temp") && file.Name.EndsWith(".json"))
                {
                    var filename = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                    var regionName = filename.Substring(4);
                    var gameLocation = Game1.getLocationFromName(regionName);
                    MigrateRegion(filename, gameLocation);
                    allFileNames.Add(filename);
                }
            }

            System.IO.Directory.CreateDirectory($"{root}/LegacyBackup");
            foreach (var file in allFileNames)
            {
                System.IO.File.Move($"{root}/{file}.json", $"{root}/LegacyBackup/{file}.json");
            }
        }

        public void SavePurchaseManifest()
        {
            var manifestFilePath = $"jsons/{Constants.SaveFolderName}/purchaseManifest.json";
            PurchaseManifestFile purchaseFile = new();
            purchaseFile.purchaseData = purchased_tiles_by_location;
            Helper.Data.WriteJsonFile(manifestFilePath, purchaseFile);
        }

        public void createJson(string fileName)
        {
            Monitor.Log($"Creating {fileName}.json", LogLevel.Debug);
            System.IO.File.Create($"jsons/{fileName}.json");
        }
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            Helper.Content.AssetEditors.Add(new MyModMail());
        }

        // --------------------------------------- DEPRECATED FUNCTIONS -------------------------------- //

        private void DEPRECATED_SaveLocationTiles(GameLocation gameLocation)
        {
            var locationName = gameLocation.Name;

            if (locationName == "Temp") locationName += Game1.whereIsTodaysFest;
            Monitor.Log($"Saving in {locationName}", LogLevel.Debug);

            var tileData = Helper.Data.ReadJsonFile<MapData>($"jsons/{Constants.SaveFolderName}/{locationName}.json") ?? new MapData();

            if (gameLocation.Name == "Temp")
            {
                tileData.AllKaiTilesList = ThisLocationTiles;
            }
            else
            {
                tileData.AllKaiTilesList = tileDict[locationName];
            }
            Helper.Data.WriteJsonFile<MapData>($"jsons/{Constants.SaveFolderName}/{locationName}.json", tileData);
        }

        private void DEPRECATED_PlaceInTempArea(GameLocation gameLocation)
        {
            Monitor.Log($"Placing Tiles in Temporary Area: {Game1.whereIsTodaysFest}", LogLevel.Debug);

            DEPRECATED_PlaceTiles(gameLocation);
            ThisLocationTiles = tileList;
            tileList = new();
        }

        private void DEPRECATED_PlaceTiles(GameLocation mapLocation)
        {
            int mapWidth = mapLocation.map.Layers[0].LayerWidth;
            int mapHeight = mapLocation.map.Layers[0].LayerHeight;

            for (int i = 1; i < mapWidth - 1; i++)
            {
                for (int j = 1; j < mapHeight - 1; j++)
                {
                    if (/*!IsTileAt(i, j, mapLocation)
                        &&*/
                        !mapLocation.isObjectAtTile(i, j)
                        && !mapLocation.isOpenWater(i, j)
                        && !mapLocation.isTerrainFeatureAt(i, j)
                        && mapLocation.isTilePlaceable(new Vector2(i, j))
                        && mapLocation.isTileLocationTotallyClearAndPlaceable(new Vector2(i, j))
                        && mapLocation.Map.Layers[0].IsValidTileLocation(i, j)
                        && mapLocation.isCharacterAtTile(new Vector2(i, j)) == null

                        )
                    {
                        if (new Vector2(Game1.player.position.X, Game1.player.position.Y) != new Vector2(i, j))
                        {
                            var t = new KaiTile(i, j, mapLocation.Name);
                            tileList.Add(t);
                        }
                    }
                }
            }
        }

        private void DEPRECATED_GroupIfLocationChange()
        {

            if (!location_changed && Game1.locationRequest != null && Game1.locationRequest.Location != Game1.currentLocation)
            {
                locationDelay = location_transition_time;
                lastLocation = Game1.currentLocation;
                targetLocation = Game1.locationRequest.Location;
                location_changed = true;

                if (legacy && Game1.currentLocation.Name == "Temp")
                {
                    DEPRECATED_SaveLocationTiles(Game1.currentLocation);
                }
            }
            if (!location_changed)
            {
                return;
            }

            if (Game1.currentLocation != lastLocation)
            {
                startLocationInNewLocation = Game1.player.Position;
                lastLocation = Game1.currentLocation;
            }

            locationDelay = Math.Max(locationDelay - 1, 0);

            if (!IsTempLocation(Game1.currentLocation) && Game1.currentLocation == targetLocation)
            {
                Game1.player.Position = startLocationInNewLocation;
            }

            if (locationDelay > 0)
            {
                return;
            }
            location_changed = false;
            this.Monitor.Log("Entered " + Game1.currentLocation.Name, LogLevel.Debug);

            //First encounter with specific Temp area
            if (Game1.currentLocation.Name == "Temp" && Helper.Data.ReadJsonFile<MapData>($"jsons/" +
                    $"{Constants.SaveFolderName}/" +
                    $"{Game1.currentLocation.Name + Game1.whereIsTodaysFest}.json") == null)
            {
                DEPRECATED_PlaceInTempArea(Game1.currentLocation);
            }
            else
            {
                Monitor.Log($"Grouping Tiles At: {Game1.currentLocation.NameOrUniqueName}", LogLevel.Debug);
                DEPRECATED_GetLocationTiles(Game1.currentLocation);
            }
        }

        private void DEPRECATED_PlaceInMaps()
        {
            if (!Context.IsWorldReady)
            {
                this.Monitor.Log("Tried to place tiles, but world wasn't ready", LogLevel.Warn);
                return;
            }
            if (!do_loop)
            {
                this.Monitor.Log("Tried to place tiles, but do_loop wasn't set", LogLevel.Debug);
                return;
            }
            if (!legacy)
            {
                this.Monitor.Log("Tried to place tiles, but that's the Old Way", LogLevel.Debug);
                return;
            }
            var locationCount = 0;
            foreach (GameLocation location in GetLocations())
            {
                if (!tileDict.ContainsKey(location.Name))
                {
                    Monitor.Log($"Placing Tiles in: {location.Name}", LogLevel.Debug);

                    locationCount++;

                    if (locationCount < amountLocations)
                    {
                        DEPRECATED_PlaceTiles(Game1.getLocationFromName(location.NameOrUniqueName));

                    }
                    else
                    {
                        break;
                    }

                    tileDict.Add(location.Name, tileList);
                    tileList = new();
                }
            }


            //Place Tiles in the Mine // Mine 1-120 // Skull Caverns 121-???
            for (int i = 1; i <= 220 + caverns_extra; i++)

            {
                var mineString = Game1.getLocationFromName("UndergroundMine" + i).Name;

                if (!tileDict.ContainsKey(mineString))
                {

                    if (Game1.getLocationFromName(mineString) != null)
                    {
                        DEPRECATED_PlaceTiles(Game1.getLocationFromName(mineString));
                        Monitor.Log($"Placing Tiles in: {mineString}", LogLevel.Debug);

                        tileDict.Add(mineString, tileList);
                        tileList = new();
                    }

                }
            }

            //VolcanoDungeon0 - 9
            for (int i = 0; i <= 9; i++)

            {
                var mineString = Game1.getLocationFromName("VolcanoDungeon" + i).Name;

                if (!tileDict.ContainsKey(mineString))
                {

                    if (Game1.getLocationFromName(mineString) != null)
                    {
                        DEPRECATED_PlaceTiles(Game1.getLocationFromName(mineString));
                        Monitor.Log($"Placing Tiles in: {mineString}", LogLevel.Debug);

                        tileDict.Add(mineString, tileList);
                        tileList = new();
                    }
                }
            }

            DEPRECATED_AddTileExceptions();
            DEPRECATED_RemoveTileExceptions();


            do_loop = false;

            //Save all the created files
            foreach (KeyValuePair<string, List<KaiTile>> entry in tileDict)
            {
                DEPRECATED_SaveLocationTiles(Game1.getLocationFromName(entry.Key));
            }
            tileDict.Clear();

            Monitor.Log("Press 'G' to toggle Tileman Overlay", LogLevel.Debug);
            Monitor.Log("Press 'H' to switch between Overlay Modes", LogLevel.Debug);
        }

        private void DEPRECATED_GetLocationTiles(GameLocation gameLocation)
        {
            this.Monitor.Log("Getting tiles for " + DEPRECATED_GetTileKey(gameLocation), LogLevel.Debug);
            var locationName = DEPRECATED_GetTileKey(gameLocation);

            if (tileDict.ContainsKey(locationName))
            {
                ThisLocationTiles = tileDict[locationName];
            }
            else
            {
                var tileData = Helper.Data.ReadJsonFile<MapData>($"jsons/{Constants.SaveFolderName}/{locationName}.json") ?? new MapData();
                if (tileData.AllKaiTilesList.Count > 0) ThisLocationTiles = tileData.AllKaiTilesList;
                if (!IsTempLocation(gameLocation)) tileDict.Add(locationName, ThisLocationTiles);
            }

            if (!IsTempLocation(gameLocation))
            {
                for (int i = 0; i < ThisLocationTiles.Count; i++)
                {
                    var t = ThisLocationTiles[i];

                    if (!allow_player_placement)
                    {
                        gameLocation.removeTileProperty(t.tileX, t.tileY, "Back", "Diggable");

                        gameLocation.setTileProperty(t.tileX, t.tileY, "Back", "Buildable", "false");
                        gameLocation.setTileProperty(t.tileX, t.tileY, "Back", "NoFurniture", "true");
                        gameLocation.setTileProperty(t.tileX, t.tileY, "Back", "NoSprinklers", "");
                        gameLocation.setTileProperty(t.tileX, t.tileY, "Back", "Placeable", "");
                    }
                }
            }
        }
    }



    public class MyModMail : IAssetEditor
    {
        public static string EasterEggName = "EmmaEasterEgg";
        public MyModMail()
        {
        }

        public bool CanEdit<T>(IAssetInfo asset)
        {
            return asset.AssetNameEquals("Data\\mail");
        }

        public void Edit<T>(IAssetData asset)
        {
            var data = asset.AsDictionary<string, string>().Data;

            data[EasterEggName] = "Hello @ and all berries out there! ^If you're seeing this, you've been playing the upgraded version " +
                "of this mod for a whole week straight. Hopefully that means nothing has broken and it's working well for you! " +
                "^Thank you for making so much wonderful content. Please give Chewie a cuddle for us and don't forget to like and subscribe! Take care!" +
                "  ^   - The Berries of Pickle Farm";
        }
    }
}