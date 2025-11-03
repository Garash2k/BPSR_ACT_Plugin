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

        // Monster name map + lock
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

        /// <summary>
        /// Return monster name for given id.
        /// Looks up runtime associations first, then loads `tables/monster_names_en.json`.
        /// </summary>
        public static string GetMonsterName(int id)
        {
            // Check runtime associations first (overrides)
            if (_ID_Name_Association.TryGetValue(id, out var assoc))
            {
                return assoc;
            }

            EnsureMonsterNameLoaded();
            if (_monsterMap != null && _monsterMap.TryGetValue(id.ToString(), out var name))
            {
                return name;
            }

            return $"Unknown Monster ({id})";
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

        public static string GetElementName(int property)
        {
            switch ((EDamageProperty)property)
            {
                case EDamageProperty.General:
                    return "⚔️General";
                case EDamageProperty.Fire:
                    return "🔥Fire";
                case EDamageProperty.Water:
                    return "❄️Water";
                case EDamageProperty.Electricity:
                    return "⚡Electricity";
                case EDamageProperty.Wood:
                    return "🍀Wood";
                case EDamageProperty.Wind:
                    return "💨Wind";
                case EDamageProperty.Rock:
                    return "⛰️Rock";
                case EDamageProperty.Light:
                    return "🌟Light";
                case EDamageProperty.Dark:
                    return "🌑Dark";
                case EDamageProperty.Count:
                    return "❓Count";
                default:
                    return "⚔️General";
            }
        }

        private enum EDamageProperty
        {
            General = 0,
            Fire = 1,
            Water = 2,
            Electricity = 3,
            Wood = 4,
            Wind = 5,
            Rock = 6,
            Light = 7,
            Dark = 8,
            Count = 9,
        }

        private static Dictionary<long, string> _ID_Name_Association = new Dictionary<long, string>();

        public static void AddAssociation(long id, string name)
        {
            if (!_ID_Name_Association.ContainsKey(id))
            {
                _ID_Name_Association.Add(id, name);
            }
        }

        public static string GetAssociation(long uuid)
        {
            return GetAssociation(uuid >> 16, BPSRPacketHandler.IsUuidPlayer((ulong)uuid));
        }

        public static string GetAssociation(long uid, bool isPlayer)
        {
            if (_ID_Name_Association.TryGetValue(uid, out var name))
            {
                return name;
            }
            return $"Unknown {(isPlayer ? "Player" : "Monster")} ({uid})";
        }
    }
}
