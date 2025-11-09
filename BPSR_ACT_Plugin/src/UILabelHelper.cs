using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Advanced_Combat_Tracker;

namespace BPSR_ACT_Plugin.src
{
    internal class UILabelHelper
    {
        private static Dictionary<string, string> _map;
        private static object _lock = new object();

        private static Dictionary<string, string> _monsterMap;
        private static object _monsterLock = new object();

        public static string GetSkillName(int id)
        {
            EnsureSkillNameLoaded();
            if (_map != null && _map.TryGetValue($"{id}", out var v)) return v;
            return $"Missing Skill ({id})";
        }

        private static void EnsureSkillNameLoaded()
        {
            if (_map != null) return;
            lock (_lock)
            {
                if (_map != null) return;
                try
                {
                    var map = new Dictionary<string, string>();

                    string chosen = $@"{ActGlobals.oFormActMain.AppDataFolder}\Plugins\BPSR_ACT_Plugin\tables\skill_names_en.json";

                    if (!string.IsNullOrEmpty(chosen) && File.Exists(chosen))
                    {
                        string json = File.ReadAllText(chosen, Encoding.UTF8);
                        // Use JavaScriptSerializer from System.Web.Extensions (available in .NET 4.8)
                        try
                        {
                            var jss = new JavaScriptSerializer();
                            var obj = jss.Deserialize<Dictionary<string, string>>(json);
                            if (obj != null)
                            {
                                foreach (var kv in obj)
                                {
                                    if (!map.ContainsKey(kv.Key)) map[kv.Key] = kv.Value;
                                }
                            }
                        }
                        catch
                        {
                            // If deserialization fails, leave map empty
                        }
                    }

                    _map = map;
                }
                catch
                {
                    _map = new Dictionary<string, string>();
                }
            }
        }

        public static string GetMonsterName(int id)
        {
            EnsureMonsterNameLoaded();
            if (_monsterMap != null && _monsterMap.TryGetValue(id.ToString(), out var name))
            {
                return name;
            }

            return $"Unknown Monster Name ({id})";
        }

        private static void EnsureMonsterNameLoaded()
        {
            if (_monsterMap != null) return;
            lock (_monsterLock)
            {
                if (_monsterMap != null) return;
                try
                {
                    var map = new Dictionary<string, string>();

                    // Look for the table in the application folder under "tables"
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;

                    string chosen = $@"{ActGlobals.oFormActMain.AppDataFolder}\Plugins\BPSR_ACT_Plugin\tables\monster_names_en.json";

                    if (File.Exists(chosen))
                    {
                        string json = File.ReadAllText(chosen, Encoding.UTF8);
                        try
                        {
                            var jss = new JavaScriptSerializer();
                            var obj = jss.Deserialize<Dictionary<string, string>>(json);
                            if (obj != null)
                            {
                                foreach (var kv in obj)
                                {
                                    if (!map.ContainsKey(kv.Key)) map[kv.Key] = kv.Value;
                                }
                            }
                        }
                        catch
                        {
                            // ignore deserialization errors
                        }
                    }

                    _monsterMap = map;
                }
                catch
                {
                    _monsterMap = new Dictionary<string, string>();
                }
            }
        }

        public static long CurrentUserUuid { get; internal set; } = 0;

        private static Dictionary<long, Player> _players = new Dictionary<long, Player>();
        private static Player GetOrCreatePlayer(long uid)
        {
            Player c;
            if (_players.TryGetValue(uid, out c))
            {
                return c;
            }

            c = new Player()
            {
                Uid = uid
            };
            _players.Add(uid, c);
            return c;
        }
        internal static void AddUpdatePlayerName(long uid, string name)
        {
            var c = GetOrCreatePlayer(uid);
            c.Name = name;
        }
        internal static void AddUpdatePlayerClass(long uid, int classID)
        {
            var c = GetOrCreatePlayer(uid);
            c.Class = classID;
        }
        internal static Player GetPlayer(long uid)
        {
            if (!_players.ContainsKey(uid))
                return null;
            return _players[uid];
        }

        private static Dictionary<long, Monster> _monsters = new Dictionary<long, Monster>();
        private static Monster GetOrCreatemonster(long uuid)
        {
            Monster c;
            if (_monsters.TryGetValue(uuid, out c))
            {
                return c;
            }

            c = new Monster()
            {
                Uuid = uuid
            };
            _monsters.Add(uuid, c);
            return c;
        }
        internal static void AddUpdateMonsterName(long uuid, string name)
        {
            var c = GetOrCreatemonster(uuid);
            c.Name = name;
        }
        internal static Monster GetMonster(long uuid)
        {
            if (!_monsters.ContainsKey(uuid))
                return null;
           return _monsters[uuid];
        }
    }

    internal class Player
    {
        public long Uid;
        public string Name;
        public int Class;
    }

    internal class Monster
    {
        public long Uuid;
        public string Name;
    }
}
