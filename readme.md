# Wot it is:

An ACT plugin for Blue Protocol: Star Resonance

![](https://files.catbox.moe/sx6atv.png)

# Installation

- Install **Advanced Combat Tracker**: https://advancedcombattracker.com/download.php

- Install **Npcap**: https://npcap.com/#download
	- While installing, keep this option checked: *"Install Npcap in WinPcap API-compatible Mode"*

- Extract **BPSR_ACT_Plugin-vX.X.X.zip** to this folder `%appdata%\Advanced Combat Tracker\Plugins`
	- You should end up with something like `C:\Users\(USER)\AppData\Roaming\Advanced Combat Tracker\Plugins\BPSR_ACT_Plugin\BPSR_ACT_Plugin.dll`

- Go to the **Plugins** tab in ACT, click **"Browse"**, select `BPSR_ACT_Plugin.dll`, then click **Add/Enable Plugin**.

# Advanced Combat Tracker?! Really?! You psychopath, why?!

Part stockholm syndrome. But also, if you dig into it, ACT is more than just a DPS meter. It has some pretty cool feature like death reports (how did I die?,) lookup damage by combatant (why did the add die too fast/not fast enough,) and individual hit information (that stack aoe did more damage that it was supposed to, who didn't get hit by it?)

# TODOs:

## Req before being happy with personal use
- Handle zones
- Understand why true isCrits don't show up as crits
- Fix logs element emojis

## Req before ideally pubbing on github
- Code quality: PacketCaptureHandler
	- Consider giving it it's offline timer back
- Code quality: BPSRPacketHandler
- Code quality: ACTLogHandler / UILabelHelper
- Double checks longs vs ulongs
- Make sure all unexpected catches are logged
- Write a readme for BPSR_ProtocolBuffer / Test again that it can't be in the main project

## Pubbing
- Loicence
- readme.md
	- Update and add screenshots
- contact ACT devs for ACT pub

## Bonus / Continuous
- Show classes&specs in ACT (Turns out the Job column isn't vanilla, it gets added by FFXIV_ACT_Plugin)
- Improve translation for monster names and skill names
- i18n?
- Make a user parameter for device selection

# Shoutouts
- [AdvancedCombatTracker](https://advancedcombattracker.com/) by EQAditu - (duh)
- [Star Resonance Damage Counter](https://github.com/dmlgzs/StarResonanceDamageCounter) by Dimole - I had zero network packet capture experience before starting this, most of the networking & packet handling code is ported from that project.
- [Blue Protocol Data Analysis](https://github.com/Zaarrg/BlueProtocolStarResonanceDataAnalysis) by Zaarrg's for some translation data
- Used Nuget libraries
	- [SharpPcap](https://www.nuget.org/packages/SharpPcap) by Tamir Gal, Chris Morgan and others
	- [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port) by Oleg Stepanischev
	- [AdvancedCombatTracker](https://www.nuget.org/packages/AdvancedCombatTracker) by bnVi
