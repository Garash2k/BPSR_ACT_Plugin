# Wot it is:

An ACT plugin for Blue Protocol: Star Resonance

![](https://files.catbox.moe/sx6atv.png)

# Installation

- Instal Npcap (https://nmap.org/npcap/) with "Install Npcap in WinPcap API-compatible Mode" checked.

- Extract BPSR_ACT_Plugin.zip to this folder `%appdata%\Advanced Combat Tracker\Plugins` (so you end up with something like `C:\Users\(USER)\AppData\Roaming\Advanced Combat Tracker\Plugins\BPSR_ACT_Plugin\BPSR_ACT_Plugin.dll`)

- Go to the Plugins tab in ACT, click "Browse" and select `BPSR_ACT_Plugin.dll`, then click Add/Enable Plugin.

# Advanced Combat Tracker?! Really?! You psychopath, why?!

Part stockholm syndrome. But also, if you dig into it, ACT is more than just a DPS meter. It has some pretty cool feature like death reports (how did I die?,) lookup damage by combatant (why did the add die too fast/not fast enough,) and individual hit information (that stack aoe did more damage that it was supposed to, who didn't get hit by it?)

# TODOs:

## Priority
- Fix some healing skills showing up as damage
- Show classes&specs in ACT
- Make sure character names work on par with SRDC's
	- Change Unknown -> Unknown Player
- Make Death Reports work, for both players and monsters

## Req before being happy with personal use
- Handle zones
- Find a way to name encounters instead of them ending up as "Encounter" (because no allies?)
- remove weird character before others' names
- Understand why true isCrits don't show up as crits
- Fix logs element emojis

## Req before ideally pubbing on github
- Review AssemblyHelper
- Review all of the vibe code, esp. packet nerdery
- Give PacketCaptureHelper it's offline timer back
- Double checks longs vs ulongs
- Wtf is FindEnumerableOfType
- Wtf is PacketBinaryReader
- Make sure unexpected catches are logged

## Pubbing
- Loicence
- readme.md
- contact ACT devs for ACT pub

## Bonus
- i18n?
- Make a user parameter for device selection
- proper translation for monster names and skill names

# Shoutouts
- [AdvancedCombatTracker](https://advancedcombattracker.com/) by EQAditu - (duh)
- [Star Resonance Damage Counter](https://github.com/dmlgzs/StarResonanceDamageCounter) by Dimole - I had zero network packet capture experience before starting this, most of the networking & packet handling code is ported from that project.
- [Blue Protocol Data Analysis](https://github.com/Zaarrg/BlueProtocolStarResonanceDataAnalysis) by Zaarrg's for some translation data
- Used Nuget libraries
	- [SharpPcap](https://www.nuget.org/packages/SharpPcap) by Tamir Gal, Chris Morgan and others
	- [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port) by Oleg Stepanischev
	- [AdvancedCombatTracker](https://www.nuget.org/packages/AdvancedCombatTracker) by bnVi
