using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries.Covalence;

using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Oxide.Plugins
{
    [Info("PlayBonus", "stevemqeen", "1.0.1")]
    class PlayBonus : RustPlugin
    {
        class PlayersJson
        {
            public PlayersJson()
            {
                players = new Dictionary<string, PlayerObject>();
            }

            public Dictionary<string, PlayerObject> players;
        };

        struct PlayerObject
        {
            ulong userID;
            ulong lastTimestamp;
            public int distanceTraveled;
            public int timePlayed;
            Vector3 lastPosition;

            public PlayerObject(ulong id, ulong timestamp, Vector3 position)
            {
                userID = id - steamMagicNumber;
                lastTimestamp = timestamp;
                lastPosition = position;
                distanceTraveled = 0;
                timePlayed = 60;
            }

            public PlayerObject(string id, ulong timestamp, Vector3 position)
            {
                userID = Convert.ToUInt64(id) - steamMagicNumber;
                lastTimestamp = timestamp;
                lastPosition = position;
                distanceTraveled = 0;
                timePlayed = 60;
            }

            public ulong GetUserId()
            {
                return userID;
            }

            public void SetLastTimestamp(ulong timestamp)
            {
                lastTimestamp = timestamp;
            }

            public ulong GetLastTimestamp()
            {
                return lastTimestamp;
            }

            public void SetLastPosition(Vector3 position)
            {
                lastPosition = position;
            }

            public Vector3 GetLastPosition()
            {
                return lastPosition;
            }

            public void SetDistanceTraveled(int distance)
            {
                distanceTraveled = distance;
            }

            public void IncreaseDistanceTraveled(int distance)
            {
                distanceTraveled += distance;
            }

            public int GetDistanceTraveled()
            {
                return distanceTraveled;
            }
        };

        Dictionary<ulong, PlayerObject> usersSpawned = new Dictionary<ulong, PlayerObject>();

        // Request headers
        Dictionary<string, string> requestHeader = new Dictionary<string, string> {
            { "Authorization", "Bearer <auth_token>" },
            { "Content-Type", "application/json" }
        };

        static ulong steamMagicNumber = 76561197960265728;
        private const string Layer = "UI_OurServersLayer";

        ulong GetCurrentTimestamp()
        {
            TimeSpan timeSpan = DateTime.UtcNow - new DateTime(1970, 1, 1);
            return Convert.ToUInt64(timeSpan.TotalSeconds);
        }

        ulong GetCurrentTimestampMilliseconds()
        {
            TimeSpan timeSpan = DateTime.UtcNow - new DateTime(1970, 1, 1);
            return Convert.ToUInt64(timeSpan.TotalMilliseconds);
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (usersSpawned.ContainsKey(player.userID))
            {
                usersSpawned.Remove(player.userID);
            }
        }

        void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (usersSpawned.ContainsKey(player.userID))
            {
                usersSpawned.Remove(player.userID);
            }

            CuiHelper.DestroyUi(player, Layer);
        }

        void playerDataCallback(int code, string response, ulong userID)
        {
            if (code == 200)
            {
                JObject responseJson = JObject.Parse(response);

                if (responseJson["points"] != null)
                {
                    Dictionary<ulong, BasePlayer> activeDict = BasePlayer.activePlayerList.ToDictionary(key => key.userID);

                    if (activeDict.ContainsKey(userID))
                    {
                        string pointsString = responseJson["points"].ToString();

                        Puts("playerDataCallback() for user [" + userID + "] got response [" + response + "] points [" + pointsString + "]");
                        RefreshUI(activeDict[userID], Convert.ToInt32(pointsString));
                    }
                }
            }
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            if (!usersSpawned.ContainsKey(player.userID))
            {
                usersSpawned.Add(player.userID, new PlayerObject(player.userID, GetCurrentTimestamp(), player.ServerPosition));
            }

            // Send web request
            if (usersSpawned.ContainsKey(player.userID))
            {
                webrequest.Enqueue("https://www.rustnation.eu/api/players/" + usersSpawned[player.userID].GetUserId(), null, (code, response) => playerDataCallback(code, response, player.userID), this, Core.Libraries.RequestMethod.GET, requestHeader, 5000);
            }
        }

        void playBonusPostCallback(int code, string response, Dictionary<ulong, PlayerObject> updatedPlayers)
        {
            if (code == 200)
            {
                JObject responseJson = JObject.Parse(response);

                foreach (KeyValuePair<ulong, PlayerObject> player in updatedPlayers)
                {
                    Puts("playBonusPostCallback(): [" + code + "] got response [" + response + "] for user [" + player.Key + "]");

                    Dictionary<ulong, BasePlayer> activePlayers = BasePlayer.activePlayerList.ToDictionary(key => key.userID);

                    if (responseJson["players"][player.Value.GetUserId().ToString()]["points"] != null)
                    {
                        string pointsString = responseJson["players"][player.Value.GetUserId().ToString()]["points"].ToString();

                        if (activePlayers.ContainsKey(player.Key))
                        {
                            Puts("playBonusPostCallback(): points [" + pointsString + "] for user [" + player.Key + "]");
                            RefreshUI(activePlayers[player.Key], Convert.ToInt32(pointsString));
                        }
                    }
                }
            }
        }

        void playBonusTimerCallback()
        {
            ulong beginTime = GetCurrentTimestampMilliseconds();

            // Updated players list
            Dictionary<ulong, PlayerObject> updatedPlayers = new Dictionary<ulong, PlayerObject>();

            // Iterate over active players list
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (usersSpawned.ContainsKey(player.userID))
                {
                    // Previous location
                    int distanceTraveled = Convert.ToInt32(Math.Round((usersSpawned[player.userID].GetLastPosition() - player.ServerPosition).magnitude, 0));

                    if (distanceTraveled > 0)
                    {
                        var tempUserObj = usersSpawned[player.userID];
                        tempUserObj.IncreaseDistanceTraveled(distanceTraveled);
                        tempUserObj.SetLastPosition(player.ServerPosition);

                        usersSpawned[player.userID] = tempUserObj;
                    }

                    ulong timeDiff = GetCurrentTimestamp() - usersSpawned[player.userID].GetLastTimestamp();

                    if (timeDiff > 60)
                    {
                        // Get current user data
                        var tempUserObj = usersSpawned[player.userID];
                        tempUserObj.SetLastTimestamp(GetCurrentTimestamp());
                        tempUserObj.SetLastPosition(player.ServerPosition);

                        // Insert to updated user data (with actual traveled distance)
                        updatedPlayers.Add(player.userID, tempUserObj);

                        // Reset traveled distance
                        tempUserObj.SetDistanceTraveled(0);

                        // Update user data
                        usersSpawned[player.userID] = tempUserObj;
                    }
                }
				else
				{
					usersSpawned.Add(player.userID, new PlayerObject(player.userID, GetCurrentTimestamp(), player.ServerPosition));
				}
            }

            // Send updated players list to WEB
            if (updatedPlayers.Count > 0)
            {
                // Generate json
                PlayersJson sendJson = new PlayersJson();
                string jsonData = "";

                // Fill json object with player data
                foreach (KeyValuePair<ulong, PlayerObject> playerObj in updatedPlayers)
                {
                    sendJson.players.Add(playerObj.Value.GetUserId().ToString(), playerObj.Value);
                }

                // Generate json string
                jsonData = JsonConvert.SerializeObject(sendJson);
                Puts(jsonData);

                // Send web request
                webrequest.Enqueue("https://www.rustnation.eu/api/servers/report-statistics", JsonConvert.SerializeObject(sendJson), (code, response) => playBonusPostCallback(code, response, updatedPlayers), this, Core.Libraries.RequestMethod.POST, requestHeader, 5000);
            }
        }

        void Loaded()
        {
            AddCovalenceCommand("bonus", "bonusCheck", "");
            AddCovalenceCommand("hidebonus", "bonusHide", "");
            Puts("Loaded...");

            // Start timer for bonus checks
            timer.Every(5f, () => playBonusTimerCallback());
        }

        void bonusHide(IPlayer player, string command, string[] args)
        {
            if (player.Object != null)
            {
                BasePlayer new_player = player.Object as BasePlayer;
                CuiHelper.DestroyUi(new_player, Layer);
            }
        }

        void RefreshUI(BasePlayer player, int points)
        {
            CuiHelper.DestroyUi(player, Layer);
            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = false,
                RectTransform = { AnchorMin = "0.87 0.92", AnchorMax = "0.98 0.96", OffsetMax = "0 0" },
                Image = { FadeIn = 0f, Color = "0 0 0 0.8" }
            }, "Overlay", Layer);

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
                Button = { Color = "0 0 0 0", Close = Layer },
                Text = { Text = "" }
            }, Layer);

            string output = "Balance: " + points + " NP";

            container.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "17 -24", OffsetMax = "-10 -5" },
                Text = { FadeIn = 0f, Text = $"{output}", Font = "robotocondensed-bold.ttf", FontSize = 16, Align = TextAnchor.LowerLeft }
            }, Layer, "TEXT");

            CuiHelper.AddUi(player, container);
        }

        void bonusCheck(IPlayer player, string command, string[] args)
        {
            if (!usersSpawned.ContainsKey(Convert.ToUInt64(player.Id)))
            {
                GenericPosition currentPosition = player.Position();
                usersSpawned.Add(Convert.ToUInt64(player.Id), new PlayerObject(player.Id, GetCurrentTimestamp(), new Vector3(currentPosition.X, currentPosition.Y, currentPosition.Z)));
            }

            Puts("OnlineCommand[" + command + "]");
        }
    }
}
