using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Advanced_Combat_Tracker;

namespace BPSR_ACT_Plugin.src
{
    internal class AssemblyHelper
    {
        public static System.Reflection.Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                // Resolve by simple name
                var name = new System.Reflection.AssemblyName(args.Name).Name;
                var pluginFolder = $@"{ActGlobals.oFormActMain.AppDataFolder}\Plugins\BPSR_ACT_Plugin";
                var candidate = Path.Combine(pluginFolder, name + ".dll");
                if (File.Exists(candidate))
                {
                    return System.Reflection.Assembly.LoadFrom(candidate);
                }
            }
            catch { }
            return null;
        }

        public static void TryPreloadPluginAssemblies()
        {
            try
            {
                var pluginFolder = $@"{ActGlobals.oFormActMain.AppDataFolder}\Plugins\BPSR_ACT_Plugin";
                if (!Directory.Exists(pluginFolder)) return;
                foreach (var dll in Directory.GetFiles(pluginFolder, "*.dll"))
                {
                    try { System.Reflection.Assembly.LoadFrom(dll); } catch { }
                }
            }
            catch (Exception ex)
            {
            }
        }
    }
}
