﻿using FrooxEngine.UIX;
using FrooxEngine;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Elements.Core;
using SkyFrost.Base;

namespace SessionTabOverhaul
{
	[HarmonyPatch(typeof(SessionUserController))]
	internal static class SessionUserControllerPatches
	{
		private const string headless = "headless";
		private const string headlessSprite = $"<sprite name=\"{headless}\">";

		private const string screen = "screen";
		private const string screenSprite = $"<sprite name=\"{screen}\">";

		private const string vr = "vr";
		private const string vrSprite = $"<sprite name=\"{vr}\">";

		private const string muteSprite = $"<sprite name=\"{nameof(VoiceMode.Mute)}\">";
		private const string whisperSprite = $"<sprite name=\"{nameof(VoiceMode.Whisper)}\">";
		private const string normalSprite = $"<sprite name=\"{nameof(VoiceMode.Normal)}\">";
		private const string shoutSprite = $"<sprite name=\"{nameof(VoiceMode.Shout)}\">";
		private const string broadcastSprite = $"<sprite name=\"{nameof(VoiceMode.Broadcast)}\">";

		private static readonly colorX hostColor = new(1, .678f, .169f);
		private static readonly string queuedMessagesColor = colorX.Red.SetValue(.7f).ToHexString();

		private static readonly ConditionalWeakTable<SessionUserController, SessionUserControllerExtraData> controllerExtraData = new();

		[HarmonyPrefix]
		[HarmonyPatch(nameof(SessionUserController.Create))]
		private static bool CreatePrefix(out SessionUserController __result, FrooxEngine.User user, UIBuilder ui)
		{
			ui.Style.MinHeight = SessionUserController.HEIGHT + 8;
			var horizontal = ui.HorizontalLayout(4, 4, Alignment.MiddleCenter);
			horizontal.ForceExpandHeight.Value = false;
			horizontal.ForceExpandWidth.Value = false;

			var controller = horizontal.Slot.AttachComponent<SessionUserController>();
			controller._cachedUserName = user.UserName;
			controller.TargetUser = user;

			var extraData = controllerExtraData.GetOrCreateValue(controller);
			var badgeFont = controller.GetBadgeFontCollection();

			extraData.RowBackgroundImage = horizontal.Slot.AttachComponent<Image>();
			extraData.RowBackgroundImage.Tint.Value = (horizontal.Slot.ChildIndex & 1) == 0 ?
				SessionTabOverhaul.FirstRowColor : SessionTabOverhaul.SecondRowColor;

			if (SessionTabOverhaul.ShowFPSOrQueuedMessages)
			{
				ui.Style.MinHeight = SessionUserController.HEIGHT;
				ui.Style.MinWidth = 2.5f * SessionUserController.HEIGHT;

				ui.Panel();
				extraData.FPSOrQueuedMessagesLabel = ui.Text(GetUserFPSOrQueuedMessages(user), alignment: Alignment.MiddleCenter);
				extraData.FPSOrQueuedMessagesLabel.Font.Target = badgeFont;
				ui.NestOut();
			}

			if (SessionTabOverhaul.ShowDeviceLabel)
			{
				ui.Style.MinWidth = 1.5f * SessionUserController.HEIGHT;
				ui.Style.MinHeight = 0.8f * SessionUserController.HEIGHT;

				ui.Panel();
				extraData.DeviceLabel = ui.Text(GetUserDeviceLabel(user), alignment: Alignment.MiddleCenter);
				extraData.DeviceLabel.Font.Target = badgeFont;
				extraData.DeviceLabel.Color.Value = colorX.Red.SetValue(.7f);
				ui.NestOut();
			}

			ui.Style.MinWidth = -1;
			ui.Style.FlexibleWidth = 1;
			ui.Style.MinHeight = SessionUserController.HEIGHT;

			ui.Panel();
			controller._name.Target = ui.Text(controller._cachedUserName, alignment: Alignment.MiddleLeft);

			if (user.IsHost && SessionTabOverhaul.ColorHostName)
				controller._name.Target.Color.Value = hostColor;

			if (user.UserID != null)
			{
                // In LocalHome or for anonymous users, there is no id
                Button button = controller._name.Target.Slot.AttachComponent<Button>();
                button.SetupAction(SessionUserController.OpenUserProfile, user.UserID);
            }

			ui.NestOut();
			ui.Style.FlexibleWidth = -1;

			if (!SessionTabOverhaul.HideAllBadges)
			{
				ui.Style.MinWidth = 224;
				ui.Style.MinHeight = 0.8f * SessionUserController.HEIGHT;

				ui.Panel();
				extraData.BadgesLabel = ui.Text("", alignment: Alignment.MiddleLeft);
				extraData.BadgesLabel.Font.Target = badgeFont;
				ui.NestOut();
			}

			ui.Style.MinWidth = 192;
			ui.Style.MinHeight = SessionUserController.HEIGHT;
			controller._slider.Target = ui.Slider(SessionUserController.HEIGHT, 1f, 0f, 2f);
			controller._slider.Target.BaseColor.Value = GetUserVoiceModeColor(user);

			var colorXDrive = controller._slider.Target.ColorDrivers[0];
			colorXDrive.TintColorMode.Value = InteractionElement.ColorMode.Explicit;
			colorXDrive.NormalColor.Value = colorX.LightGray;
			colorXDrive.HighlightColor.Value = colorX.White;
			colorXDrive.PressColor.Value = colorX.Gray;
			colorXDrive.DisabledColor.Value = colorX.DarkGray;

			if (SessionTabOverhaul.ShowVoiceMode)
			{
				ui.Style.MinWidth = SessionUserController.HEIGHT;
				var voiceModeButton = ui.Button(GetUserVoiceModeLabel(user));
				voiceModeButton.BaseColor.Value = new colorX(1, 0);
				voiceModeButton.LocalPressed += (button, eventData) =>
				{
					if (!controller.IsDestroyed)
						controller._slider.Target.Value.Value = 1;
				};

				extraData.VoiceModeLabel = voiceModeButton.Slot.GetComponentInChildren<Text>();
				extraData.VoiceModeLabel.Font.Target = badgeFont;
			}

			ui.Style.MinWidth = 64;
			ui.Style.MinHeight = SessionUserController.HEIGHT;
			controller._mute.Target = ui.Button("User.Actions.Mute".AsLocaleKey(), controller.OnMute);
			controller._jump.Target = ui.Button("User.Actions.Jump".AsLocaleKey(), controller.OnJump);

			if (SessionTabOverhaul.ShowBringButton)
			{
				extraData.BringButton = ui.Button("Bring");
				extraData.BringButton.LocalPressed += (button, eventData) =>
				{
					if (controller.World != Userspace.UserspaceWorld)
						return;

					user.World.RunSynchronously(() =>
					{
						if (user.World.LocalUser.Root != null)
							user.Root?.JumpToPoint(user.World.LocalUser.Root.HeadPosition);
					});
				};
			}

            if (SessionTabOverhaul.ShowSteamButton)
            {
                ui.Style.MinWidth = 80;
                var steamButton = ui.Button("Steam");
                steamButton.Enabled = false;
                if (user.Metadata.TryGetElement("SteamID", out var value) && value.TryGetValue(out ulong steamID))
                {
                    steamButton.Enabled = true;
                    steamButton.LocalPressed += (button, eventData) => Process.Start($"https://steamcommunity.com/profiles/{steamID}");
                }
            }

			ui.Style.MinWidth = 108;
			controller._respawn.Target = ui.Button("User.Actions.Respawn".AsLocaleKey(), controller.OnRespawn);

			ui.Style.MinWidth = 80;
			controller._silence.Target = ui.Button("User.Actions.Silence".AsLocaleKey(), controller.OnSilence);

			ui.Style.MinWidth = 48;
			controller._kick.Target = ui.Button("User.Actions.Kick".AsLocaleKey(), controller.OnKick);
			controller._ban.Target = ui.Button("User.Actions.Ban".AsLocaleKey(), controller.OnBan);

			ui.NestOut();

			if (user.IsHost)
				controller.AddBadge("host");

			if (user.Platform.IsMobilePlatform())
				controller.AddBadge("mobile");

			if (user.Platform == Platform.Linux)
				controller.AddBadge("linux");

			if (user.HeadDevice == HeadOutputDevice.Headless)
				controller.AddBadge("headless");

			if (user.UserID != null)
			{
				controller.StartTask(async delegate
				{
					CloudResult<SkyFrost.Base.User> cloudResult = await controller.Cloud.Users.GetUserCached(user.UserID);
					if (cloudResult.IsOK)
					{
						controller.SetCloudData(cloudResult.Entity);
					}
				});
			}

			__result = controller;
			return false;
		}

		[HarmonyPrefix]
		[HarmonyPatch(nameof(SessionUserController.AddBadge), new[] { typeof(string) })]
		private static bool AddStandardBadgePrefix(SessionUserController __instance, string spriteName)
		{
			if (!controllerExtraData.TryGetValue(__instance, out var extraData) || extraData?.BadgesLabel == null)
				return false;

			if (SessionTabOverhaul.HidePatreonBadge && spriteName == "patreon")
				return false;

			var text = extraData.BadgesLabel.Content;

			if (text.Value.Length > 0)
				text.Value += " ";

			text.Value += $"<sprite name=\"{spriteName}\">";

			return false;
		}

		[HarmonyPrefix]
		[HarmonyPatch(nameof(SessionUserController.AddBadge), new[] { typeof(Uri), typeof(string), typeof(bool) })]
		private static bool AddCustomBadgePrefix(SessionUserController __instance, Uri badge, string key)
		{
			var spriteFont = (DynamicSpriteFont)__instance.GetBadgeFontCollection().FontSets[1];

			if (!spriteFont.HasSprite(key))
				spriteFont.AddSprite(key, badge, 1.25f);

			if (SessionTabOverhaul.HideCustomBadges)
				return false;

			AddStandardBadgePrefix(__instance, key);

			return false;
		}

		[HarmonyPostfix]
		[HarmonyPatch(nameof(SessionUserController.GetBadgeFontCollection))]
		private static void GetBadgeFontCollectionPostfix(FontCollection __result)
		{
			if (SessionTabOverhaul.SpritesInjected)
				return;

			var spriteFont = (DynamicSpriteFont)__result.FontSets[1];

			if (!spriteFont.HasSprite(screen))
				spriteFont.AddSprite(screen, new Uri("resdb:///1c88a45653f60a9b29eefc5e3adc4659f3021a85debd5b4f3425ff29d4564794"));

			if (!spriteFont.HasSprite(vr))
				spriteFont.AddSprite(vr, new Uri("resdb:///1d2dc53aa1b44d8a21aaaa3ce41b695ae724eb0553e7ee08d50fc0c7922ae149"));

			foreach (VoiceMode voiceMode in Enum.GetValues(typeof(VoiceMode)))
			{
				var name = voiceMode.ToString();

				if (!spriteFont.HasSprite(name))
					spriteFont.AddSprite(name, VoiceHelper.GetIcon(voiceMode));
			}

			SessionTabOverhaul.SpritesInjected = true;
		}

		private static string GetUserDeviceLabel(FrooxEngine.User user)
			=> (user.VR_Active && user.HeadDevice != HeadOutputDevice.Headless && !user.IsPresentInHeadset) ? $"<s>{GetUserDevice(user)}" : GetUserDevice(user);

		private static string GetUserDevice(FrooxEngine.User user)
		{
			if (user.HeadDevice == HeadOutputDevice.Headless)
				return headlessSprite;

			if (user.VR_Active)
				return vrSprite;

			return screenSprite;
		}

		private static VoiceMode GetUserVoiceMode(FrooxEngine.User user)
			=> user.isMuted ? VoiceMode.Mute : user.VoiceMode;

		private static string GetUserVoiceModeLabel(FrooxEngine.User user)
			=> GetUserVoiceMode(user) switch
			{
				VoiceMode.Mute => muteSprite,
				VoiceMode.Whisper => whisperSprite,
				VoiceMode.Normal => normalSprite,
				VoiceMode.Shout => shoutSprite,
				VoiceMode.Broadcast => broadcastSprite,
				_ => ""
			};

		private static colorX GetUserVoiceModeColor(FrooxEngine.User user)
			=> VoiceHelper.GetColor(GetUserVoiceMode(user)).SetSaturation(.5f);

		private static string GetUserFPSOrQueuedMessages(FrooxEngine.User user)
			=> user.QueuedMessages > 10 ? $"<color={queuedMessagesColor}>{user.QueuedMessages} <size=60%>Q'd" : $"<color=#F0F0F0>{MathX.RoundToInt(user.FPS)} <size=60%>FPS";

		[HarmonyPostfix]
		[HarmonyPatch("OnCommonUpdate")]
		private static void OnCommonUpdatePostfix(SessionUserController __instance)
		{
			if (!controllerExtraData.TryGetValue(__instance, out var extraData) || extraData == null)
				return;

			if (extraData.RowBackgroundImage != null)
				extraData.RowBackgroundImage.Tint.Value = (__instance.Slot.ChildIndex & 1) == 0 ?
					SessionTabOverhaul.FirstRowColor : SessionTabOverhaul.SecondRowColor;

			var user = __instance.TargetUser;

			if (extraData.FPSOrQueuedMessagesLabel != null)
				extraData.FPSOrQueuedMessagesLabel.Content.Value = GetUserFPSOrQueuedMessages(user);

			if (extraData.DeviceLabel != null)
				extraData.DeviceLabel.Content.Value = GetUserDeviceLabel(user);

			__instance._slider.Target.BaseColor.Value = GetUserVoiceModeColor(user);

			if (extraData.VoiceModeLabel != null)
				extraData.VoiceModeLabel.Content.Value = GetUserVoiceModeLabel(user);

			if (extraData.BringButton != null)
				extraData.BringButton.Enabled = !(user.World.LocalUser.Root?.GetRegisteredComponent<LocomotionController>()?.IsSupressed).GetValueOrDefault()
												&& !user.IsLocalUser && user.CanRespawn();
		}
	}
}
