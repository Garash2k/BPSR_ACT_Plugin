using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Advanced_Combat_Tracker;

namespace BPSR_ACT_Plugin.src
{
    internal static class ACTLogHelper
    {
        public static void LogMasterSwing(MasterSwing masterSwing)
        {
            if (ActGlobals.oFormActMain.SetEncounter(DateTime.Now, masterSwing.Attacker, masterSwing.Victim))
            {
                ActGlobals.oFormActMain.AddCombatAction(masterSwing);
            }
        }
    }
}
