using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Advanced_Combat_Tracker;

namespace BPSR_ACT_Plugin.src
{
    /// <summary>
    /// Receives combat actions via LogMasterSwing and logs them into ACT.
    /// </summary>
    internal static class ACTLogHandler
    {
        private static int? _healingSwingType;
        /// <summary>
        /// Normaly it'd simply be (int)SwingTypeEnum.Healing, but FFXIV_ACT_Plugin breaks ACT's default mappings.
        /// This checks the current mappings to find the correct SwingType for healing.
        /// </summary>
        public static int HealingSwingType
        {
            get
            {
                if (!_healingSwingType.HasValue)
                {
                    _healingSwingType = CombatantData.SwingTypeToDamageTypeDataLinksOutgoing.First(s => s.Value.Contains(CombatantData.DamageTypeDataOutgoingHealing)).Key;
                }
                return _healingSwingType.Value;
            }
        }

        public static void LogMasterSwing(MasterSwing masterSwing, bool isDead)
        {
            if (!ActGlobals.oFormActMain.InCombat && masterSwing.SwingType == HealingSwingType)
            {
                //Do not start combat if it's a healing swing outside of combat
                return;
            }

            if (ActGlobals.oFormActMain.SetEncounter(DateTime.Now, masterSwing.Attacker, masterSwing.Victim))
            {
                if (!string.IsNullOrEmpty(ActGlobals.oFormActMain.ActiveZone.ActiveEncounter.Title))
                    ActGlobals.oFormActMain.ActiveZone.ActiveEncounter.Title = masterSwing.Victim;

                ActGlobals.oFormActMain.AddCombatAction(masterSwing);
                if (isDead)
                {
                    MasterSwing deathSwing = new MasterSwing(
                        masterSwing.SwingType,
                        masterSwing.Critical,
                        "Death",
                        Dnum.Death,
                        masterSwing.Time,
                        masterSwing.TimeSorter,
                        masterSwing.AttackType,
                        masterSwing.Attacker,
                        masterSwing.DamageType,
                        masterSwing.Victim
                    );
                    ActGlobals.oFormActMain.AddCombatAction(deathSwing);
                }
            }
        }
    }
}
