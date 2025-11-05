using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms.VisualStyles;
using Advanced_Combat_Tracker;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace BPSR_ACT_Plugin.src
{
    internal static class BPSRPacketHandler
    {
        public static Action<string> OnLogStatus;
        public static Action<MasterSwing, bool> OnLogMasterSwing;

        public static void OnPayloadReady(uint methodId, byte[] payload)
        {
            switch ((NotifyMethod)methodId)
            {
                case NotifyMethod.SyncNearEntities:
                    _processSyncNearEntities(payload);
                    break;
                case NotifyMethod.SyncContainerData:
                    _processSyncContainerData(payload);
                    break;
                case NotifyMethod.SyncContainerDirtyData:
                    //Removing _processSyncContainerDirtyData.
                    //  It's only use in SRDC was to retreive info about the current player such as their name, AS, job and hp levels.
                    //  We don't need most of those. For the name, we can just label ourselves as "YOU" FFXIV_ACT_Plugin style.
                    break;
                case NotifyMethod.SyncToMeDeltaInfo:
                    _processSyncToMeDeltaInfo(payload);
                    break;
                case NotifyMethod.SyncNearDeltaInfo:
                    _processSyncNearDeltaInfo(payload);
                    break;
                default:
                    //this.logger.debug(`Skipping NotifyMsg with methodId ${ methodId}`);
                    break;
            }
        }

        private static void _processSyncNearEntities(byte[] payloadBuffer)
        {
            var syncNearEntities = SyncNearEntities.Parser.ParseFrom(payloadBuffer);

            if (syncNearEntities?.Appear == null)
                return;

            foreach (var entity in syncNearEntities?.Appear)
            {
                AddNameFromAttr(entity.Uuid >> 16, entity.Attrs.Attrs);
            }
        }

        private static void AddNameFromAttr(long id, RepeatedField<Attr> attrs)
        {
            foreach (var attr in attrs)
            {
                switch ((AttrType)attr.Id)
                {
                    case AttrType.AttrName:
                        string name = attr.RawData?.ToStringUtf8();
                        if (!string.IsNullOrEmpty(name))
                        {
                            name = Regex.Replace(name, @"\p{Cc}+", string.Empty);
                            name = Regex.Replace(name, @"\s+", " ").Trim();
                            UILabelHelper.AddAssociation(id, name);
                        }
                        break;
                    case AttrType.AttrId:
                        int monsterID = 0;
                        var data = attr.RawData?.ToByteArray();
                        if (data != null && data.Length > 0)
                        {
                            monsterID = new CodedInputStream(data).ReadInt32();
                            UILabelHelper.AddAssociation(id, UILabelHelper.GetMonsterName(monsterID));
                        }
                        break;
                    case AttrType.AttrProfessionId:
                        break;
                    case AttrType.AttrFightPoint:
                        break;
                    case AttrType.AttrLevel:
                        break;
                    case AttrType.AttrRankLevel:
                        break;
                    case AttrType.AttrCri:
                        break;
                    case AttrType.AttrLucky:
                        break;
                    case AttrType.AttrHp:
                        break;
                    case AttrType.AttrMaxHp:
                        break;
                    case AttrType.AttrElementFlag:
                        break;
                    case AttrType.AttrReductionLevel:
                        break;
                    case AttrType.AttrReduntionId:
                        break;
                    case AttrType.AttrEnergyFlag:
                        break;
                    default:
                        break;
                }
                //TODO: Also associate the class&spec
            }
        }

        private static void _processSyncContainerData(byte[] payloadBuffer)
        {
            var syncContainerData = SyncContainerData.Parser.ParseFrom(payloadBuffer);

            if (syncContainerData?.VData?.CharBase == null)
                return;

            UILabelHelper.AddAssociation(syncContainerData.VData.CharId, syncContainerData.VData.CharBase.Name);
            //TODO: Also associate the class&spec
        }

        private static void _processSyncNearDeltaInfo(byte[] payloadBuffer)
        {
            var syncNearDeltaInfo = SyncNearDeltaInfo.Parser.ParseFrom(payloadBuffer);

            foreach (var deltaInfo in syncNearDeltaInfo.DeltaInfos)
            {
                _processAoiSyncDelta(deltaInfo);
            }
        }

        private static void _processSyncToMeDeltaInfo(byte[] payloadBuffer)
        {
            var syncToMeDeltaInfo = SyncToMeDeltaInfo.Parser.ParseFrom(payloadBuffer);

            var aoiSyncToMeDelta = syncToMeDeltaInfo.DeltaInfo;

            var uuid = aoiSyncToMeDelta.Uuid;
            if (uuid != 0 && UILabelHelper.CurrentUserUuid != uuid)
            {
                UILabelHelper.CurrentUserUuid = uuid;
                OnLogStatus?.Invoke("Got player UUID! UUID: " + UILabelHelper.CurrentUserUuid);
            }

            var aoiSyncDelta = aoiSyncToMeDelta.BaseDelta;
            if (aoiSyncDelta == null) return;

            _processAoiSyncDelta(aoiSyncDelta);
        }

        // Ported from packet.js PacketProcessor._processAoiSyncDelta
        private static void _processAoiSyncDelta(AoiSyncDelta aoiSyncDelta)
        {
            if (aoiSyncDelta == null) return;

            // Read raw UUID from proto (signed long -> interpret as unsigned for bit ops)
            ulong rawUuid = unchecked((ulong)aoiSyncDelta.Uuid);
            if (rawUuid == 0) return;

            // Some messages sometimes carry a value that is already shifted (JS used shiftRight(16) later).
            // Be tolerant: check both the raw low16 and the low16 of the value shifted right 16.
            bool isTargetPlayer = IsUuidPlayer(rawUuid) || IsUuidPlayer(rawUuid >> 16);
            bool isTargetMonster = IsUuidMonster(rawUuid) || IsUuidMonster(rawUuid >> 16);

            // The canonical target id for logging/lookup should be the entity id (high bits).
            // JS did: targetUuid = targetUuid.shiftRight(16)
            ulong targetUid = rawUuid >> 16;

            var attrCollection = aoiSyncDelta.Attrs;
            if (attrCollection != null)
            {
                AddNameFromAttr((long)targetUid, attrCollection?.Attrs);
                //TODO: Also associate the class
            }

            var damages = aoiSyncDelta.SkillEffects?.Damages;
            if (damages == null)
            {
                return;
            }

            foreach (var damage in damages)
            {
                ProcessSyncDamageInfo(damage, isTargetPlayer, isTargetMonster, targetUid);
            }
        }

        // Helper to determine whether UUID corresponds to player or monster (matches JS checks)
        public static bool IsUuidPlayer(ulong uuid) => (uuid & 0xffffUL) == 640UL;
        private static bool IsUuidMonster(ulong uuid) => (uuid & 0xffffUL) == 64UL;

        // Processes a single SyncDamageInfo
        private static void ProcessSyncDamageInfo(SyncDamageInfo dmg, bool isTargetPlayer, bool isTargetMonster, ulong targetUid)
        {
            if (dmg == null) return;

            try
            {
                int skillId = 0;
                try { skillId = dmg.OwnerId; } catch { /* ignore */ }

                long value = 0;
                long luckyValue = 0;
                long hpLessenValue = 0;
                try { value = dmg.Value; } catch { }
                try { luckyValue = dmg.LuckyValue; } catch { }
                try { hpLessenValue = dmg.HpLessenValue; } catch { }

                long damage = 0;
                if (value > 0)
                    damage = value;
                else if (luckyValue > 0)
                    damage = luckyValue;

                int typeFlag = 0;
                try { typeFlag = dmg.TypeFlag; } catch { }
                bool isDead = false;
                try { isDead = Convert.ToBoolean(dmg.IsDead); } catch { }
                int damageSource = 0;
                try { damageSource = dmg.DamageSource; } catch { }

                bool isCrit = (typeFlag & 1) == 1;
                bool isCauseLucky = (typeFlag & 0b100) == 0b100;
                //bool isLucky = luckyValue != 0;

                bool isHeal = false;
                try { isHeal = dmg.Type == (int)EDamageType.Heal; } catch { }

                var actionType = isHeal ? "HEAL" : "DMG";
                var extras = new List<string>();
                if (isCrit) extras.Add("Crit");
                //if (isLucky) extras.Add("Lucky");
                if (isCauseLucky) extras.Add("CauseLucky");
                if (extras.Count == 0) extras.Add("Normal");

                var attacker = dmg.TopSummonerId != 0 ? dmg.TopSummonerId : dmg.AttackerUuid;

                // Compose a human readable log similar to JS
                string srcStr = UILabelHelper.GetAssociation(uuid: attacker);
                string tgtStr = UILabelHelper.GetAssociation(uid: (long)targetUid, isTargetPlayer);

                int swingType = isHeal ? ACTLogHelper.HealingSwingType : (int)SwingTypeEnum.NonMelee;

                //TODO: Understand why true isCrits don't show up as crits
                OnLogMasterSwing?.Invoke(new MasterSwing(swingType, isCrit, string.Join(",", extras), damage, DateTime.Now, 0, UILabelHelper.GetSkillName(skillId), srcStr, UILabelHelper.GetElementName(dmg.Property), tgtStr), isDead);

                var log = $"[{actionType}] DS:{damageSource} {srcStr} {tgtStr} ID:{skillId} VAL:{damage} HPLSN:{hpLessenValue} EXT:{string.Join("|", extras)}";
                OnLogStatus?.Invoke(log);

            }
            catch (Exception ex)
            {
                OnLogStatus?.Invoke($"Error processing SyncDamageInfo: {ex.Message}");
            }
        }
    }

    internal enum NotifyMethod {
        SyncNearEntities = 0x00000006,
        SyncContainerData = 0x00000015,
        SyncContainerDirtyData = 0x00000016,
        SyncServerTime = 0x0000002b,
        SyncNearDeltaInfo = 0x0000002d,
        SyncToMeDeltaInfo = 0x0000002e,
    };

    internal enum AttrType {
        AttrName = 0x01,
        AttrId = 0x0a,
        AttrProfessionId = 0xdc,
        AttrFightPoint = 0x272e,
        AttrLevel = 0x2710,
        AttrRankLevel = 0x274c,
        AttrCri = 0x2b66,
        AttrLucky = 0x2b7a,
        AttrHp = 0x2c2e,
        AttrMaxHp = 0x2c38,
        AttrElementFlag = 0x646d6c,
        AttrReductionLevel = 0x64696d,
        AttrReduntionId = 0x6f6c65,
        AttrEnergyFlag = 0x543cd3c6,
    }
}
