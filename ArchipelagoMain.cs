namespace RW_Archipelago;


using BepInEx;
using BepInEx.Logging;
using RWCustom;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class ArchipelagoMain : BaseUnityPlugin
{
    public const string PLUGIN_GUID = "archipelago";
    public const string PLUGIN_NAME = "Archipelago";
    public const string PLUGIN_VERSION = "0.1";

    public static ManualLogSource ArchipeLogger { get; private set; }

    static int challengeCount = 20;

	private static bool IsInExpedition =>
		RWCustom.Custom.rainWorld.processManager.currentMainLoop is RainWorldGame game &&
		game.session is StoryGameSession storySession &&
		ModManager.Expedition &&
		game.rainWorld.ExpeditionMode;


    private void OnEnable()
    {
        try{
            ArchipeLogger = base.Logger;
            ArchipeLogger.LogMessage(PLUGIN_NAME+" mod enabled !!!");

            On.Menu.ChallengeSelectPage.ctor += ChallengeSelectPage_Ctor_Hook;
            On.Menu.ChallengeSelectPage.Update += ChallengeSelectPage_Update_Hook;
            On.Menu.ChallengeSelectPage.Singal += ChallengeSelectPage_Singal_Hook;
            On.Expedition.Challenge.CompleteChallenge += Challenge_CompleteChallenge_Hook;
            On.Expedition.ChallengeOrganizer.AssignChallenge += ChallengeOrganizer_AssignChallenge_Hook;
			On.Menu.ChallengeSelectPage.UpdateChallengeButtons += ChallengeSelectPage_UpdateChallengeButtons_Hook;
			On.Expedition.ExpeditionProgression.UnlockSprite += ExpeditionProgression_UnlockSprite;

        } catch (Exception e)
        {
            ArchipeLogger.LogError(e);
            return;
        }
    }

	private void Update() {
		if(UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Alpha1))
        {
            if(RWCustom.Custom.rainWorld.processManager.currentMainLoop is RainWorldGame game &&
               game.session is StoryGameSession storySession && ModManager.Expedition && game.rainWorld.ExpeditionMode)
            {
                AddRandomUnlock(game);

				//Add Max Karma
                // if(storySession.saveState.deathPersistentSaveData.karmaCap < 10)
                // 	storySession.saveState.deathPersistentSaveData.karmaCap++;
            }
        }
	}

    private static void AddRandomUnlock(RainWorldGame game)
    {
        List<string> candidates = new List<string>();
        foreach (List<string> perkgroup in Expedition.ExpeditionProgression.perkGroups.Values)
        {
            perkgroup.ForEach(unl =>
            {
                if (!Expedition.ExpeditionGame.activeUnlocks.Contains(unl))
                    candidates.Add(unl);
            });
        }
        foreach (List<string> burdenGroup in Expedition.ExpeditionProgression.burdenGroups.Values)
        {
            burdenGroup.ForEach(bur =>
            {
                if (!Expedition.ExpeditionGame.activeUnlocks.Contains(bur))
                    candidates.Add(bur);
            });
        }

        if (candidates.Count > 0)
        {
            int randomChoice = UnityEngine.Random.Range(0, candidates.Count);
            AddUnlock(candidates[randomChoice], game);
        }
        else
        {
            ArchipeLogger.LogMessage("No perks/burdens left");
        }
    }


    private static void AddUnlock(string unlock, RainWorldGame game)
	{
		Expedition.ExpeditionGame.activeUnlocks.Add(unlock);
		if(unlock.Contains("unl-slow"))
		{
			Expedition.ExpeditionGame.unlockTrackers.Add(new Expedition.ExpeditionGame.SlowTimeTracker(game));
		}
		if(unlock.Contains("bur-pursued"))
		{
			Expedition.ExpeditionGame.burdenTrackers.Add(new Expedition.ExpeditionGame.PursuedTracker(game));
		}
        if(unlock.Contains("unl-glow"))
        {
            game.Players.ForEach(item => ((Player)item.realizedCreature).glowing = true);
        }

		ArchipeLogger.LogMessage("Added "+ (unlock.StartsWith("bur-") ? "burden" : "perk") + ": "+unlock);
	}
    


	private static string ExpeditionProgression_UnlockSprite(On.Expedition.ExpeditionProgression.orig_UnlockSprite orig, string key, bool alwaysShow)
	{
		return orig(key, IsInExpedition ? true : alwaysShow);
	} 

	/// <summary>
	/// When starting an expedition, the challenges descriptions do not get updated outside of UpdateChallengeButtons, which only affects the 5 or less first challenges.
	/// We need to also update the remaining challenge descriptions, or they stay empty.
	/// </summary>
	private static void ChallengeSelectPage_UpdateChallengeButtons_Hook(On.Menu.ChallengeSelectPage.orig_UpdateChallengeButtons orig, Menu.ChallengeSelectPage self)
	{
		orig(self);
		for (int i = 0; i < Expedition.ExpeditionData.challengeList.Count; i++)
		{
			Expedition.ExpeditionData.challengeList[i].UpdateDescription();
		}
	}

    /// <summary>
    /// Used to notify AP that a challenge has been completed
    /// </summary>
    private static void Challenge_CompleteChallenge_Hook(On.Expedition.Challenge.orig_CompleteChallenge orig, Expedition.Challenge self)
    {
        if(!self.completed)
        {
            int index = Expedition.ExpeditionData.challengeList.FindIndex(item => item == self);
            ArchipeLogger.LogMessage("Challenge #"+index+" completed !");
        }
        orig(self);
    }

    /// <summary>
    /// Sets the amount of challenges to challengeCount instead of 3 when the "DESELECT" message is sent to the menu
    /// </summary>
    private static void ChallengeSelectPage_Singal_Hook(On.Menu.ChallengeSelectPage.orig_Singal orig, Menu.ChallengeSelectPage self, Menu.MenuObject sender, string message)
    {
        orig(self, sender, message);
        // ArchipeLogger.LogDebug("ChallengeSelectPage Singal message: "+message);
        if(message == "DESELECT")
        {
            ResetChallenges();
            self.UpdateChallengeButtons();
        }
    }
    
    /// <summary>
    /// Reset the challenges to ChallengeCount instead of 3
    /// </summary>
    private static void ChallengeSelectPage_Ctor_Hook(On.Menu.ChallengeSelectPage.orig_ctor orig, Menu.ChallengeSelectPage self, Menu.Menu menu, Menu.MenuObject owner, Vector2 pos)
    {
        orig(self, menu, owner, pos);
        ResetChallenges();
        self.UpdateChallengeButtons();
    }

    private static void ResetChallenges()
    {
        Expedition.ExpeditionData.challengeList.Clear();
        for (int i = 0; i < challengeCount; i++)
        {
            Expedition.ChallengeOrganizer.AssignChallenge(i, false);
        }
    }

    /// <summary>
    /// An almost exact copy of the original ChallengeOrganizer.AssignChallenge, that adds support for more than 5 challenges
    /// </summary>
    private static void ChallengeOrganizer_AssignChallenge_Hook(On.Expedition.ChallengeOrganizer.orig_AssignChallenge orig, int slot, bool hidden)
    {
        int i = 0;
			while (i < 15)
			{
				if (Expedition.ExpeditionData.challengeList != null)
				{
					Expedition.Challenge challenge = Expedition.ChallengeOrganizer.RandomChallenge(hidden);
					if (!challenge.ValidForThisSlugcat(Expedition.ExpeditionData.slugcatPlayer))
					{
						i++;
						continue;
					}
					bool flag = false;
					for (int j = 0; j < Expedition.ExpeditionData.challengeList.Count; j++)
					{
						if (!Expedition.ExpeditionData.challengeList[j].Duplicable(challenge))
						{
							flag = true;
						}
					}
					if (flag)
					{
						i++;
						continue;
					}
					if (hidden && !challenge.CanBeHidden())
					{
						i++;
						continue;
					}
					if (Expedition.ExpeditionData.challengeList.Count <= slot /*&& slot <= 4*/) //Changed line
					{
						Expedition.ExpeditionData.challengeList.Add(challenge);
					}
					else
					{
						challenge.hidden = hidden;
						Expedition.ExpeditionData.challengeList[slot] = challenge;
					}
                    Expedition.ExpLog.Log("Got new challenge: " + challenge.GetType().Name);
					// ArchipeLogger.LogDebug("Got new challenge: " + challenge.GetType().Name); // Added
				}
				return;
			}
            Expedition.ExpLog.Log("ChallengOrganiser gave up after 15 attempts!");
			ArchipeLogger.LogMessage("ChallengOrganiser gave up after 15 attempts!"); // Added
    }

    /// <summary>
    /// Makes sure the minus and plus button cannot be used. Challenge are locations checks, so their amount must be consistent for AP
    /// </summary>
    private static void ChallengeSelectPage_Update_Hook(On.Menu.ChallengeSelectPage.orig_Update orig, Menu.ChallengeSelectPage self)
    {
        orig(self);
        self.minusButton.GetButtonBehavior.greyedOut = true;
        self.plusButton.GetButtonBehavior.greyedOut = true;
    }
}
