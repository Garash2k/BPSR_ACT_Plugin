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
    /// <summary>
    /// Provides functionality for handling and processing Blue Protocol packets.
    /// Receives them via PayloadReady, then exports events via OnLogMasterSwing
    /// Mostly ported from SRDC's packet.js
    /// </summary>
    internal static class BPSRPacketHandler
    {
        public static Action<string> OnLogStatus;
        public static Action<MasterSwing, bool> OnLogMasterSwing;

        public static void PayloadReady(uint methodId, ReadOnlyMemory<byte> payload)
        {
            var spanPayload = payload.Span;

            switch ((NotifyMethod)methodId)
            {
                case NotifyMethod.SyncNearEntities:
                    ProcessSyncNearEntities(spanPayload);
                    break;
                case NotifyMethod.SyncContainerData:
                    ProcessSyncContainerData(spanPayload);
                    break;
                case NotifyMethod.SyncContainerDirtyData:
                    //Removing _processSyncContainerDirtyData.
                    //  It's only use in SRDC was to retreive info about the current player such as their name, AS, job and hp levels.
                    //  We don't need most of those. For the name, we can just label ourselves as "YOU" FFXIV_ACT_Plugin style.
                    break;
                case NotifyMethod.SyncToMeDeltaInfo:
                    ProcessSyncToMeDeltaInfo(spanPayload);
                    break;
                case NotifyMethod.SyncNearDeltaInfo:
                    ProcessSyncNearDeltaInfo(spanPayload);
                    break;
                default:
                    //OnLogStatus?.Invoke($"Skipping NotifyMsg with methodId {methodId}");
                    break;
            }
        }

        private static void ProcessSyncNearEntities(ReadOnlySpan<byte> payloadBuffer)
        {
            var syncNearEntities = SyncNearEntities.Parser.ParseFrom(payloadBuffer);

            //TODO: Confirm that handling disappear is not needed.

            if (syncNearEntities?.Appear == null)
                return;

            foreach (var entity in syncNearEntities?.Appear)
            {
                switch ((EEntityType)entity.EntType)
                {
                    case EEntityType.EntMonster:
                        ProcessEnemyAttrs(entity.Uuid, entity.Attrs.Attrs);
                        break;
                    case EEntityType.EntChar:
                        ProcessPlayerAttrs(entity.Uuid >> 16, entity.Attrs.Attrs);
                        break;
                    default:
                        break;
                }
            }
        }

        private static void ProcessPlayerAttrs(long uid, RepeatedField<Attr> attrs)
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
                            UILabelHelper.AddUpdatePlayerName(uid, name);
                        }
                        break;
                    case AttrType.AttrProfessionId:
                        var classID = new CodedInputStream(attr.RawData?.ToByteArray()).ReadInt32();
                        string className = attr.RawData?.ToStringUtf8();
                        UILabelHelper.AddUpdatePlayerClass(uid, classID);
                        break;
                }
            }
        }

        private static void ProcessEnemyAttrs(long uuid, RepeatedField<Attr> attrs)
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
                            UILabelHelper.AddUpdateMonsterName(uuid, name);
                        }
                        break;
                    case AttrType.AttrId:
                        var monsterID = new CodedInputStream(attr.RawData?.ToByteArray()).ReadInt32();
                        UILabelHelper.AddUpdateMonsterName(uuid, UILabelHelper.GetMonsterName(monsterID));
                        break;
                }
            }
        }

        private static void ProcessSyncContainerData(ReadOnlySpan<byte> payloadBuffer)
        {
            var syncContainerData = SyncContainerData.Parser.ParseFrom(payloadBuffer);

            if (syncContainerData?.VData?.CharBase == null)
                return;

            UILabelHelper.AddUpdatePlayerName(syncContainerData.VData.CharId, syncContainerData.VData.CharBase.Name);
            UILabelHelper.AddUpdatePlayerClass(syncContainerData.VData.CharId, syncContainerData.VData.ProfessionList.CurProfessionId);

        }

        private static void ProcessSyncNearDeltaInfo(ReadOnlySpan<byte> payloadBuffer)
        {
            var syncNearDeltaInfo = SyncNearDeltaInfo.Parser.ParseFrom(payloadBuffer);

            foreach (var deltaInfo in syncNearDeltaInfo.DeltaInfos)
            {
                ProcessDeltaInfo(deltaInfo);
            }
        }

        private static void ProcessSyncToMeDeltaInfo(ReadOnlySpan<byte> payloadBuffer)
        {
            var syncToMeDeltaInfo = SyncToMeDeltaInfo.Parser.ParseFrom(payloadBuffer);

            var deltaInfo = syncToMeDeltaInfo.DeltaInfo;

            var uuid = deltaInfo.Uuid;
            if (uuid != 0 && UILabelHelper.CurrentUserUuid != uuid)
            {
                UILabelHelper.CurrentUserUuid = uuid;
                OnLogStatus?.Invoke("Got player UUID! UUID: " + UILabelHelper.CurrentUserUuid);
            }

            var aoiSyncDelta = deltaInfo.BaseDelta;
            if (aoiSyncDelta == null) return;

            ProcessDeltaInfo(aoiSyncDelta);
        }

        private static void ProcessDeltaInfo(AoiSyncDelta aoiSyncDelta)
        {
            if (aoiSyncDelta == null) return;

            var targetUuid = aoiSyncDelta.Uuid;
            if (targetUuid == 0) return;

            var isTargetPlayer = IsUuidPlayer(targetUuid);
            var isTargetMonster = IsUuidMonster(targetUuid);

            var attrCollection = aoiSyncDelta.Attrs;
            if (attrCollection != null)
            {
                if (isTargetPlayer)
                    ProcessPlayerAttrs(targetUuid >> 16, attrCollection?.Attrs);
                else if (isTargetMonster)
                    ProcessEnemyAttrs(targetUuid, attrCollection?.Attrs);
            }

            var damages = aoiSyncDelta.SkillEffects?.Damages;
            if (damages == null)
            {
                return;
            }

            foreach (var damage in damages)
            {
                ProcessSyncDamageInfo(damage, isTargetPlayer, isTargetMonster, targetUuid);
            }
        }

        public static bool IsUuidPlayer(long uuid)
        {
            return (uuid & 0xffffL) == 640L;
        }
        private static bool IsUuidMonster(long uuid)
        {
            var low = uuid & 0xffffL;
            return low == 64L || low == 32832L;
        }

        private static void ProcessSyncDamageInfo(SyncDamageInfo dmg, bool isTargetPlayer, bool isTargetMonster, long targetUuid)
        {
            if (dmg == null) return;

            try
            {
                long damage = 0;
                if (dmg.Value > 0)
                    damage = dmg.Value;
                else if (dmg.LuckyValue > 0)
                    damage = dmg.LuckyValue;

                bool isCrit = (dmg.TypeFlag & 1) == 1;
                bool isCauseLucky = (dmg.TypeFlag & 0b100) == 0b100;

                var extras = new List<string>();
                if (isCrit) extras.Add("Crit");
                if (isCauseLucky) extras.Add("CauseLucky");
                if (extras.Count == 0) extras.Add("Normal");

                bool isHeal = dmg.Type == (int)EDamageType.Heal;
                int swingType = isHeal ? ACTLogHandler.HealingSwingType : (int)SwingTypeEnum.NonMelee;

                var attacker = dmg.TopSummonerId != 0 ? dmg.TopSummonerId : dmg.AttackerUuid;

                string srcStr = $"Unknown Entity ({attacker})";
                if (attacker == UILabelHelper.CurrentUserUuid)
                    srcStr = "YOU";
                else if (IsUuidPlayer(attacker))
                    srcStr = UILabelHelper.GetPlayer(attacker >> 16)?.Name ?? $"Unknown Player ({attacker})";
                else if (IsUuidMonster(attacker))
                    srcStr = UILabelHelper.GetMonster(attacker)?.Name ?? $"Unknown Monster ({attacker})";

                string tgtStr = $"Unknown Entity ({targetUuid})";
                if (targetUuid == UILabelHelper.CurrentUserUuid)
                    tgtStr = "YOU";
                else if (isTargetPlayer)
                    tgtStr = UILabelHelper.GetPlayer(targetUuid >> 16)?.Name ?? $"Unknown Player ({targetUuid})";
                else if (isTargetMonster)
                    tgtStr = UILabelHelper.GetMonster(targetUuid)?.Name ?? $"Unknown Monster ({targetUuid})";

                OnLogMasterSwing?.Invoke(
                    new MasterSwing(
                        swingType,
                        isCrit,
                        string.Join(",", extras),
                        damage,
                        DateTime.Now,
                        0,
                        UILabelHelper.GetSkillName(dmg.OwnerId),
                        srcStr,
                        UILabelHelper.GetElementName(dmg.Property),
                        tgtStr),
                    dmg.IsDead);

                OnLogStatus?.Invoke($"[{(isHeal ? "HEAL" : "DMG")}] DS:{(EDamageSource)dmg.DamageSource} TGT: {tgtStr} ID:{dmg.OwnerId} VAL:{damage} HPLSN:{dmg.HpLessenValue} ELEM: {UILabelHelper.GetElementName(dmg.Property)} EXT:{string.Join(" | ", extras)}");
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
    }

    internal enum EEntityType
    {
        EntMonster = 1,
        EntChar = 10,
    }

    internal enum EDamageSource
    {
        Skill = 0,
        Bullet = 1,
        Buff = 2,
        Fall = 3,
        FakeBullet = 4,
        Other = 100,
    };
}
