# Wot it is:

An ACT plugin for Blue Protocol: Star Resonance

![](https://files.catbox.moe/sx6atv.png)

# Advanced Combat Tracker?! Really?! You psychopath, why?!

Part stockholm syndrome. But also, if you dig into it, ACT is more than just a DPS meter. It has some pretty cool feature like death reports (how did I die?,) lookup damage by combatant (why did the add die to fast/not fast enough,) and individual hit information (that stack aoe did more damage that it was supposed to, who didn't get hit by it?)

# TODOs:

## Req before being happy with personal use
- Find a way to name encounters instead of them ending up as "Encounter" (because no allies?)
- Make Death Reports work, for both players and monsters
- remove weird character before others' names
	- Make sure character names work on par with SRDC's
- Show classes&specs in ACT
- Understand why true isCrits don't show up as crits
- Handle zones
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
