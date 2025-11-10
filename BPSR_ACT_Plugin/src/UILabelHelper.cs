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
            if (_map != null && _map.TryGetValue($"{id}", out var skillName))
                return skillName;
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

                    string json = File.ReadAllText(chosen, Encoding.UTF8);
                    var jss = new JavaScriptSerializer();
                    var obj = jss.Deserialize<Dictionary<string, string>>(json);
                    foreach (var kv in obj)
                    {
                        if (!map.ContainsKey(kv.Key)) map[kv.Key] = kv.Value;
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
            if (_monsterMap != null && _monsterMap.TryGetValue(id.ToString(), out var monsterName))
                return monsterName;
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

                    string chosen = $@"{ActGlobals.oFormActMain.AppDataFolder}\Plugins\BPSR_ACT_Plugin\tables\monster_names_en.json";

                    string json = File.ReadAllText(chosen, Encoding.UTF8);
                    var jss = new JavaScriptSerializer();
                    var obj = jss.Deserialize<Dictionary<string, string>>(json);
                    foreach (var kv in obj)
                    {
                        if (!map.ContainsKey(kv.Key)) map[kv.Key] = kv.Value;
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
            Player player;
            if (_players.TryGetValue(uid, out player))
                return player;

            player = new Player()
            {
                Uid = uid
            };
            _players.Add(uid, player);
            return player;
        }
        internal static void AddUpdatePlayerName(long uid, string name)
        {
            var c = GetOrCreatePlayer(uid);
            c.Name = name;
            //TODO: Update names in act if they're found
        }
        internal static void AddUpdatePlayerClass(long uid, int classID)
        {
            var c = GetOrCreatePlayer(uid);
            c.Class = classID;
            //TODO: Update names in act if they're found
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
            Monster monster;
            if (_monsters.TryGetValue(uuid, out monster))
                return monster;

            monster = new Monster()
            {
                Uuid = uuid
            };
            _monsters.Add(uuid, monster);
            return monster;
        }
        internal static void AddUpdateMonsterName(long uuid, string name)
        {
            var c = GetOrCreatemonster(uuid);
            c.Name = name;
            //TODO: Update names in act if they're found
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
