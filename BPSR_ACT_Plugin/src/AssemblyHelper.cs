using System;
using System.IO;
using System.Reflection;
using Advanced_Combat_Tracker;

namespace BPSR_ACT_Plugin.src
{
    /// <summary>
    /// Dlls in our BPSR_ACT_Plugin subfolder aren't automatically found by ACT, so we need to help it out.
    /// </summary>
    internal class AssemblyHelper
    {
        public static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {

                var name = new AssemblyName(args.Name).Name;
                var dllFilePath = Path.Combine(
                    ActGlobals.oFormActMain.AppDataFolder.FullName,
                    "Plugins",
                    "BPSR_ACT_Plugin",
                    $"{name}.dll");
                return Assembly.LoadFrom(dllFilePath);
            }
            catch (Exception)
            {
                //Happens too soon to log
                return null;
            }
        }
    }
}
