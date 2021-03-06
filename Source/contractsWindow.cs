﻿#region license
/*The MIT License (MIT)
Contract Window - Addon to control window for contracts

Copyright (c) 2014 DMagic

KSP Plugin Framework by TriggerAu, 2014: http://forum.kerbalspaceprogram.com/threads/66503-KSP-Plugin-Framework

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Contracts;
using Contracts.Parameters;
using Contracts.Agents;
using ContractsWindow.Toolbar;
using UnityEngine;

namespace ContractsWindow
{

	class contractsWindow: DMC_MBW
	{

		#region Initialization

		private List<Guid> cList = new List<Guid>();
		private List<Guid> pinnedList = new List<Guid>();
		private List<contractMission> missionList = new List<contractMission>();
		private List<contractUIObject> nextRemoveList = new List<contractUIObject>();
		private List<contractUIObject> nextRemoveMissionList = new List<contractUIObject>();
		private List<contractUIObject> nextPinnedList = new List<contractUIObject>();
		private contractMission currentMission;
		private contractUIObject tempContainer;
		private Agent currentAgent;
		private string version, inputField, editField;
		private Vector2 scroll, missionScroll;
		private bool resizing, editorLocked, spacecenterLocked, trackingLocked, contractsLoading, loaded, stockToolbar, replaceStock;
		private bool popup, showSort, rebuild, agencyPopup, missionCreator, missionTextBox, missionSelector, toolbar, missionEdit, replaceStockPopup;
		private Vector2 dragStart;
		private float windowHeight, windowWidth;
		//private int timer;
		private Rect popupRect;
		private int sceneInt;
		private int timer;
		private const string lockID = "ContractsWindow_LockID";
		private const string centerLockID = "ContractsWindow_SC_LockID";
		private const string trackingLockID = "ContractsWindow_TS_LockID";

		private contractScenario contract = contractScenario.Instance;

		protected override void Awake()
		{
			Assembly assembly = AssemblyLoader.loadedAssemblies.GetByAssembly(Assembly.GetExecutingAssembly()).assembly;
			var ainfoV = Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute)) as AssemblyInformationalVersionAttribute;
			switch (ainfoV == null)
			{
				case true: version = ""; break;
				default: version = ainfoV.InformationalVersion; break;
			}

			sceneInt = contractScenario.currentScene(HighLogic.LoadedScene);
			timer = 0;

			//Set up the various GUI options to their default values here
			WindowCaption = "Contracts +";
			WindowRect = new Rect(40, 80, 250, 300);
			WindowOptions = new GUILayoutOption[1] { GUILayout.MaxHeight(Screen.height) };
			WindowStyle = contractSkins.newWindowStyle;
			Visible = false;
			DragEnabled = true;
			ClampToScreen = true;
			ClampToScreenOffset = new RectOffset(-200, -200, -200, -200);
			TooltipMouseOffset = new Vector2d(-10, -25);
			RepeatingWorkerInitialWait = 10;

			//Make sure our click-through control locks are disabled
			InputLockManager.RemoveControlLock(lockID);

			DMC_SkinsLibrary.SetCurrent("ContractUnitySkin");
		}

		protected override void Start()
		{
			GameEvents.Contract.onAccepted.Add(contractAccepted);
			GameEvents.Contract.onContractsLoaded.Add(contractLoaded);
			PersistenceLoad();
		}

		protected override void OnDestroy()
		{
			GameEvents.Contract.onAccepted.Remove(contractAccepted);
			GameEvents.Contract.onContractsLoaded.Remove(contractLoaded);
			if (InputLockManager.lockStack.ContainsKey(lockID))
				EditorLogic.fetch.Unlock(lockID);
			if (InputLockManager.lockStack.ContainsKey(centerLockID))
				InputLockManager.RemoveControlLock(centerLockID);
			if (InputLockManager.lockStack.ContainsKey(trackingLockID))
				InputLockManager.RemoveControlLock(trackingLockID);
		}

		protected override void Update()
		{
			//Loading process triggered by the ContractSystem GameEvent
			if (contractsLoading && !loaded)
				StartCoroutine(loadContracts());

			//This is a backup loading system in case something blows up while the ContractSystem is loading
			if (timer < 500 && !loaded)
				timer++;
			else if (!loaded)
			{
				loadLists();

				contractsLoading = false;
				loaded = true;
			}
		}

		private void loadLists()
		{
			generateList();

			//Load ordering lists and contract settings after primary contract dictionary has been loaded
			if (currentMission != null)
			{

				if (currentMission.ShowActiveMissions)
				{
					cList = currentMission.ActiveMissionList;
					pinnedList = currentMission.loadPinnedContracts(cList);
				}
				else
				{
					cList = currentMission.HiddenMissionList;
					pinnedList = currentMission.loadPinnedContracts(cList);
				}
			}

			if (cList.Count > 0)
				refreshContracts(cList);
			else
				rebuildList();
		}

		private IEnumerator loadContracts()
		{
			int activeC = ContractSystem.Instance.GetActiveContractCount();
			int i = 0;
			contractsLoading = false;
			loaded = true;

			while (activeC < contractScenario.Instance.ContractCount && i < 200)
			{
				activeC = ContractSystem.Instance.GetActiveContractCount();
				i++;
				yield return null;
			}

			loadLists();
		}

		#endregion

		#region GUI Draw

		protected override void DrawWindowPre(int id)
		{
			//Update the drag rectangle
			DragRect.height = WindowRect.height - 24 - contractScenario.Instance.windowSize * 8;
			DragRect.width = WindowRect.width - 19;

			//Prevent click through from activating part options
			if (HighLogic.LoadedSceneIsFlight)
			{
				Vector2 mousePos = Input.mousePosition;
				mousePos.y = Screen.height - mousePos.y;
				if (WindowRect.Contains(mousePos) && GUIUtility.hotControl == 0 && Input.GetMouseButton(0))
				{
					foreach (var window in GameObject.FindObjectsOfType(typeof(UIPartActionWindow)).OfType<UIPartActionWindow>().Where(p => p.Display == UIPartActionWindow.DisplayType.Selected))
					{
						window.enabled = false;
						window.displayDirty = true;
					}
				}
			}

			//Lock space center click through
			if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
			{
				Vector2 mousePos = Input.mousePosition;
				mousePos.y = Screen.height - mousePos.y;
				if (WindowRect.Contains(mousePos) && !spacecenterLocked)
				{
					InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS | ControlTypes.KSC_FACILITIES | ControlTypes.KSC_UI, centerLockID);
					spacecenterLocked = true;
				}
				else if (!WindowRect.Contains(mousePos) && spacecenterLocked)
				{
					InputLockManager.RemoveControlLock(centerLockID);
					spacecenterLocked = false;
				}
			}

			//Lock tracking scene click through
			if (HighLogic.LoadedScene == GameScenes.TRACKSTATION)
			{
				Vector2 mousePos = Input.mousePosition;
				mousePos.y = Screen.height - mousePos.y;
				if (WindowRect.Contains(mousePos) && !trackingLocked)
				{
					InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS | ControlTypes.TRACKINGSTATION_ALL, trackingLockID);
					trackingLocked = true;
				}
				else if (!WindowRect.Contains(mousePos) && trackingLocked)
				{
					InputLockManager.RemoveControlLock(trackingLockID);
					trackingLocked = false;
				}
			}

			//Lock editor click through
			if (HighLogic.LoadedSceneIsEditor)
			{
				Vector2 mousePos = Input.mousePosition;
				mousePos.y = Screen.height - mousePos.y;
				if (WindowRect.Contains(mousePos) && !editorLocked)
				{
					EditorLogic.fetch.Lock(true, true, true, lockID);
					editorLocked = true;
				}
				else if (!WindowRect.Contains(mousePos) && editorLocked)
				{
					EditorLogic.fetch.Unlock(lockID);
					editorLocked = false;
				}
			}

			if (!popup)
			{
				showSort = false;
				rebuild = false;
				agencyPopup = false;
				missionCreator = false;
				missionSelector = false;
				missionTextBox = false;
				missionEdit = false;
				replaceStockPopup = false;
				toolbar = false;
			}
		}

		protected override void DrawWindow(int id)
		{
			int windowSizeAdjust = contractScenario.Instance.windowSize;
			//Menu Bar
			buildMenuBar(id, windowSizeAdjust);

			GUILayout.BeginVertical();
			GUILayout.Space(20 + (windowSizeAdjust * 4));

			Rect lastRect = new Rect(10, 20 + (windowSizeAdjust * 4), 230 + (windowSizeAdjust * 30), 24 + (windowSizeAdjust * 4));

			GUI.Label(lastRect, currentMission.Name + ":", contractSkins.missionLabel);

			if (!currentMission.MasterMission)
			{
				lastRect.x += 210 + (windowSizeAdjust * 24);
				lastRect.y += 2 + (windowSizeAdjust * 2);
				lastRect.width = 20 + (windowSizeAdjust * 4);
				lastRect.height = 20 + (windowSizeAdjust * 4);

				if (GUI.Button(lastRect, new GUIContent(contractSkins.missionEditIcon, "Edit Mission")))
				{
					editField = currentMission.Name;
					popup = true;
					missionEdit = true;
				}
			}

			GUILayout.Space(4);

			scroll = GUILayout.BeginScrollView(scroll);

			lastRect = new Rect(0, -2, 10, 0);

			//Contract List Begins
			foreach (Guid gID in cList)
			{
				contractUIObject c = currentMission.getContract(gID);

				if (c == null)
					continue;

				if (c.Container == null)
					continue;

				GUILayout.Space(-1);

				buildContractTitleBar(c, id, windowSizeAdjust, ref lastRect);


				GUILayout.Space(-5);

				buildContractTitle(c, id, windowSizeAdjust, ref lastRect);

				//Parameters
				if (c.ShowParams)
				{
					foreach (parameterContainer cP in c.Container.ParamList)
					{
						if (cP.Level == 0 && !string.IsNullOrEmpty(cP.Title))
							buildParameterLabel(cP, c.Container, 0, id, windowSizeAdjust, ref lastRect);
					}
				}
			}

			GUILayout.EndScrollView();
			GUILayout.Space(18 + windowSizeAdjust * 4);
			GUILayout.EndVertical();

			//Bottom bar
			buildBottomBar(id, windowSizeAdjust);

			//Draw various popup and dropdown windows
			buildPopup(id, windowSizeAdjust);

			//Resize window when the resizer is grabbed by the mouse
			buildResizer(id, windowSizeAdjust);
		}

		#region Top Menu

		private void buildMenuBar(int ID, int sizeAdjust)
		{
			Rect r = new Rect(4, 2, 26 + (sizeAdjust * 6), 18 + (sizeAdjust * 6));

			//Sort icons
			if (GUI.Button(r, new GUIContent(contractSkins.sortIcon, "Sort Contracts")))
			{
				popup = !popup;
				showSort = !showSort;
			}

			r.x += 36 + (sizeAdjust * 9);

			if (currentMission.AscendingOrder)
			{
				if (GUI.Button(r, new GUIContent(contractSkins.orderAsc, "Ascending Order")))
				{
					currentMission.AscendingOrder = false;
					refreshContracts(cList);
				}
			}
			else
			{
				if (GUI.Button(r, new GUIContent(contractSkins.orderDesc, "Descending Order")))
				{
					currentMission.AscendingOrder = true;
					refreshContracts(cList);
				}
			}

			r.x = WindowRect.width - 68 - (sizeAdjust * 9);

			//Show and hide icons
			if (currentMission.ShowActiveMissions)
			{
				if (GUI.Button(r, new GUIContent(contractSkins.revealShowIcon, "Show Hidden Contracts")))
				{
					currentMission.ShowActiveMissions = false;
					cList = currentMission.HiddenMissionList;
					pinnedList = currentMission.loadPinnedContracts(cList);
					refreshContracts(cList);
				}
			}
			else
			{
				if (GUI.Button(r, new GUIContent(contractSkins.revealHideIcon, "Show Standard Contracts")))
				{
					currentMission.ShowActiveMissions = true;
					cList = currentMission.ActiveMissionList;
					pinnedList = currentMission.loadPinnedContracts(cList);
					refreshContracts(cList);
				}
			}

			r.x += 36 + (sizeAdjust * 6);

			//Mission selection icon
			if (GUI.Button(r, new GUIContent(contractSkins.missionSelectionIcon, "Select Mission")))
			{
				popup = true;
				missionSelector = true;
			}

			GUI.DrawTexture(new Rect(2, 17 + (sizeAdjust * 6), WindowRect.width - 4, 4), contractSkins.headerBar);
		}


		#endregion

		#region Contract Title Bar

		private void buildContractTitleBar(contractUIObject c, int id, int size, ref Rect r)
		{
			r.x = 6;
			r.y += (r.height + 2);
			r.width = 54 + (size * 12);
			r.height = 15 + (size * 4);

			if (r.yMin >= (scroll.y - 20) && r.yMax <= (scroll.y + WindowRect.height - (30 + size * 6)))
			{
				//Difficulty icons
				if (c.Container.Contract.Prestige == Contract.ContractPrestige.Trivial)
					GUI.DrawTexture(r, contractSkins.goldStar);
				else if (c.Container.Contract.Prestige == Contract.ContractPrestige.Significant)
					GUI.DrawTexture(r, contractSkins.goldStarTwo);
				else
					GUI.DrawTexture(r, contractSkins.goldStarThree);

				r.x += r.width;
				r.width = 58 + (size * 15);

				//Expiration date
				if (c.Container.Duration >= 2160000)
					GUI.Label(r, c.Container.DaysToExpire, contractSkins.timerGood);
				else if (c.Container.Duration > 0)
					GUI.Label(r, c.Container.DaysToExpire, contractSkins.timerBad);
				else if (c.Container.Contract.ContractState == Contract.State.Completed)
					GUI.Label(r, c.Container.DaysToExpire, contractSkins.timerGood);
				else
					GUI.Label(r, c.Container.DaysToExpire, contractSkins.timerFinished);

				r.x += 60 + (size * 10);
				r.width = 16 + (size * 4);
				r.height += 1;

				//Draw icon buttons when no popup menus are open
				if (!popup)
				{
					//Agency Icon
					if (GUI.Button(r, new GUIContent(contractSkins.agencyIcon, "Agency"), contractSkins.texButtonSmall))
					{
						currentAgent = c.Container.Contract.Agent;
						popup = !popup;
						agencyPopup = !agencyPopup;
					}

					r.x += 22 + (size * 4);

					//Show and hide icons
					if (c.Container.Contract.ContractState == Contract.State.Active)
					{
						if (currentMission.ShowActiveMissions)
						{
							if (GUI.Button(r, new GUIContent(contractSkins.hideIcon, "Hide Contract"), contractSkins.texButtonSmall))
								nextRemoveList.Add(c);
						}
						else
						{
							if (GUI.Button(r, new GUIContent(contractSkins.showIcon, "Un-Hide Contract"), contractSkins.texButtonSmall))
								nextRemoveList.Add(c);
						}
					}
					else
					{
						if (GUI.Button(r, new GUIContent(contractSkins.closeIcon, "Remove Contract"), contractSkins.texButtonSmall))
							nextRemoveMissionList.Add(c);
					}

					r.x += 22 + (size * 4);

					//Pin icon button
					if (c.Order == null)
					{
						if (GUI.Button(r, new GUIContent(contractSkins.pinIcon, "Pin Contract"), contractSkins.texButtonSmall))
							nextPinnedList.Add(c);
					}
					else
					{
						r.width -= 2;
						if (GUI.Button(r, new GUIContent(contractSkins.pinDownIcon, "Un-Pin Contract"), contractSkins.texButtonSmall))
							nextPinnedList.Add(c);
					}

					r.x += 22 + (size * 4);
					r.width = 16 + (size * 4);

					//Mission list button
					if (GUI.Button(r, new GUIContent(contractSkins.missionIcon, "Add To Mission List"),contractSkins.texButtonSmall))
					{
						tempContainer = c;
						popup = true;
						missionCreator = true;
					}

					r.width = 12 + (size * 4);
					r.x += 18 + (size * 4);

					//Note icon button
					if (c.Container.Contract.ContractState == Contract.State.Active && !string.IsNullOrEmpty(c.Container.Notes))
					{
						if (!c.Container.ShowNote)
						{
							if (GUI.Button(r, new GUIContent(contractSkins.noteIcon, "Show Note"), contractSkins.texButtonSmall))
								c.Container.ShowNote = !c.Container.ShowNote;
						}
						else
						{
							if (GUI.Button(r, new GUIContent(contractSkins.noteIconOff, "Hide Note"), contractSkins.texButtonSmall))
								c.Container.ShowNote = !c.Container.ShowNote;
						}
					}
				}
				//Draw inactive icons while any popup menu is active
				else
				{
					//Agency Icon
					GUI.Label(r, contractSkins.agencyIcon, contractSkins.texButtonSmall);

					r.x += 22 + (size * 4);

					//Show and hide icons
					if (c.Container.Contract.ContractState == Contract.State.Active)
					{
						if (currentMission.ShowActiveMissions)
							GUI.Label(r, contractSkins.hideIcon, contractSkins.texButtonSmall);
						else
							GUI.Label(r, contractSkins.showIcon, contractSkins.texButtonSmall);
					}
					else
						GUI.Label(r, contractSkins.closeIcon, contractSkins.texButtonSmall);

					r.x += 22 + (size * 4);

					//Pin icon button
					if (c.Order == null)
						GUI.Label(r, contractSkins.pinIcon, contractSkins.texButtonSmall);
					else
					{
						r.width -= 2;
						GUI.Label(r, contractSkins.pinDownIcon, contractSkins.texButtonSmall);
					}

					r.x += 22 + (size * 4);
					r.width = 16 + (size * 4);

					//Mission list button
					GUI.Label(r, contractSkins.missionIcon, contractSkins.texButtonSmall);

					r.x += 18 + (size * 4);
					r.width = 12 + (size * 4);

					//Note icon button
					if (c.Container.Contract.ContractState == Contract.State.Active && !string.IsNullOrEmpty(c.Container.Notes))
					{
						if (!c.Container.ShowNote)
							GUI.Label(r, contractSkins.noteIcon, contractSkins.texButtonSmall);
						else
							GUI.Label(r, contractSkins.noteIconOff, contractSkins.texButtonSmall);
					}
				}
			}
		}

		#endregion

		#region Contract Titles

		private void buildContractTitle(contractUIObject c, int id, int size, ref Rect r)
		{
			string contractTitle = c.Container.Title;
			GUIStyle cStyle = titleState(c.Container.Contract.ContractState);
			bool active = c.Container.Contract.ContractState == Contract.State.Active || c.Container.Contract.ContractState == Contract.State.Completed;
			bool failed = c.Container.Contract.ContractState == Contract.State.Active || c
				.Container.Contract.ContractState == Contract.State.Cancelled || c.Container.Contract.ContractState == Contract.State.DeadlineExpired || c.Container.Contract.ContractState == Contract.State.Failed;

			//Add in space for the contract title buttons
			GUILayout.Space(23 + size * 4);

			//Draw inactive buttons if any popup window is open
			if (!popup)
			{
				if (GUILayout.Button(contractTitle, cStyle, GUILayout.MaxWidth(225 + size * 30)))
					c.ShowParams = !c.ShowParams;
			}
			else
				GUILayout.Box(contractTitle, hoverTitleState(c.Container.Contract.ContractState), GUILayout.MaxWidth(225 + size * 30));

			r = GUILayoutUtility.GetLastRect();

			//Only draw the rewards if they are visible in the window
			if (WindowRect.width >= 270 + (size* 30))
			{
				if (r.yMin >= (scroll.y - 20) && r.yMax <= (scroll.y + WindowRect.height - (20 + size * 6)))
				{
					Rect rewardsRect = r;
					rewardsRect.x = 230 + (size * 30);
					rewardsRect.y -= (2 + (size *2));

					scaledContent(ref rewardsRect, c.Container.FundsRewString, c.Container.FundsPenString, Currency.Funds, size, active, failed);

					scaledContent(ref rewardsRect, c.Container.SciRewString, "", Currency.Science, size, active, failed);

					scaledContent(ref rewardsRect, c.Container.RepRewString, c.Container.RepPenString, Currency.Reputation, size, active, failed);
				}
			}

			//Display note
			if (!string.IsNullOrEmpty(c.Container.Notes) && c.Container.ShowNote && c.Container.Contract.ContractState == Contract.State.Active)
			{
				GUILayout.Space(-3);
				GUILayout.Box(c.Container.Notes, GUILayout.MaxWidth(300 + size * 60));

				r.height += GUILayoutUtility.GetLastRect().height;
			}
		}

		#endregion

		#region Parameters

		private void buildParameterLabel(parameterContainer cP, contractContainer c, int level, int id, int size, ref Rect r)
		{
			string paramTitle = cP.Title;
			bool active = cP.CParam.State == ParameterState.Incomplete;
			bool greenState = cP.CParam.State == ParameterState.Complete || cP.CParam.State == ParameterState.Incomplete;
			bool redState = cP.CParam.State == ParameterState.Incomplete || cP.CParam.State == ParameterState.Failed;
			GUIStyle pStyle = paramState(cP);

			GUILayout.BeginHorizontal();
			GUILayout.Space(5 + (level * 5));

			r.x = 5 + (level * 5);
			r.y += r.height;

			//Note icon button
			if (active && !string.IsNullOrEmpty(cP.Notes))
			{
				r.x -= 2;
				r.y += 4;
				r.width = 12 + (size * 2);
				r.height = 14 + (size * 4);

				if (!cP.ShowNote)
				{
					if (GUI.Button(r, new GUIContent(contractSkins.noteIcon, "Show Note"), contractSkins.texButtonSmall))
						cP.ShowNote = !cP.ShowNote;
				}
				else
				{
					if (GUI.Button(r, new GUIContent(contractSkins.noteIconOff, "Hide Note"), contractSkins.texButtonSmall))
						cP.ShowNote = !cP.ShowNote;
				}
				GUILayout.Space(12 + size * 2);
			}

			/* FIXME - Disabled For Now; Need to Figure Out Changes Made In 0.90 */
			//Editor part icon button
			//if (cP.part != null && HighLogic.LoadedSceneIsEditor)
			//{
			//	if (GUILayout.Button(new GUIContent(contractSkins.partIcon, "Preview Part"), contractSkins.texButtonSmall, GUILayout.MaxWidth(18 + contractScenario.Instance.windowSize * 4), GUILayout.MaxHeight(18 + contractScenario.Instance.windowSize * 4)))
			//	{
			//		EditorLogic.fetch.Unlock(lockID);
			//		editorLocked = false;
			//		EditorPartList.Instance.RevealPart(cP.part, true);
			//	}
			//	GUILayout.Space(-3);
			//}

			//Contract parameter title
			if (!string.IsNullOrEmpty(cP.Notes))
				GUILayout.Box(paramTitle, pStyle, GUILayout.MaxWidth(208 - (level * 5) + size * 28));
			else
				GUILayout.Box(paramTitle, pStyle, GUILayout.MaxWidth(220 - (level * 5) + size * 30));

			r = GUILayoutUtility.GetLastRect();

			GUILayout.EndHorizontal();

			//Parameter reward info
			if (WindowRect.width >= 270 + (size * 30))
			{
				if (r.yMin >= (scroll.y - 20) && r.yMax <= (scroll.y + WindowRect.height - (30 + size * 6)))
				{
					Rect rewardsRect = r;
					rewardsRect.x = 230 + (size * 30);
					rewardsRect.y += 4;

					scaledContent(ref rewardsRect, cP.FundsRewString, cP.FundsPenString, Currency.Funds, size, greenState, redState);

					scaledContent(ref rewardsRect, cP.SciRewString, "", Currency.Science, size, greenState, redState);

					scaledContent(ref rewardsRect, cP.RepRewString, cP.RepPenString, Currency.Reputation, size, greenState, redState);
				}
			}

			//Display note
			if (!string.IsNullOrEmpty(cP.Notes) && cP.ShowNote && active)
			{
				GUILayout.Space(-6);
				GUILayout.Box(cP.Notes, GUILayout.MaxWidth(320 + size * 60));

				r.height += GUILayoutUtility.GetLastRect().height;
			}

			if (level < 4)
			{
				foreach (parameterContainer sP in cP.ParamList)
				{
					if (sP.Level == level + 1 && !string.IsNullOrEmpty(sP.Title))
					{
						if (active)
							buildParameterLabel(sP, c, level + 1, id, size, ref r);
					}
				}
			}

		}

		#endregion

		#region Rewards

		private void scaledContent(ref Rect r, string top, string bottom, Currency type, int i, bool active, bool failed)
		{
			bool t = string.IsNullOrEmpty(top);
			bool b = string.IsNullOrEmpty(bottom);
			if (t && b)
				return;

			int width = 0;
			int height = 0;
			if (type == Currency.Funds)
			{
				width = 8 + (i * 2);
				height = 11 + (i * 5);
			}
			else
			{
				width = 12 + (i * 4);
				height = 12 + (i * 4);
			}

			r.width = width;
			r.height = height;

			GUIStyle sTop = currencyStyle(type, true);
			Vector2 szTop = sTop.CalcSize(new GUIContent(top));
			if (!t && active)
			{
				GUI.DrawTexture(r, currencyIcon(type, true));

				r.x += r.width + 2 + (i * 2);

				r.width = szTop.x;
				r.height = szTop.y;

				GUI.Label(r, top, sTop);
			}

			r.width = width;
			r.height = height;

			GUIStyle sBot = currencyStyle(type, false);
			Vector2 szBot = sBot.CalcSize(new GUIContent(bottom));
			if (!b && failed)
			{
				r.x -= (width + 2 + (i * 2));
				r.y += 14 + (i * 5);
				GUI.DrawTexture(r, currencyIcon(type, false));

				r.x += r.width + 2 + (i * 2);

				r.width = szBot.x;
				r.height = szBot.y;

				GUI.Label(r, bottom, sBot);
				r.y -= (14 + (i * 5));
			}

			r.x += Math.Max(szTop.x, szBot.x) + 4 + (i * 4);
		}

		private GUIStyle currencyStyle(Currency t, bool reward)
		{
			switch (t)
			{
				case Currency.Funds:
					return reward ? contractSkins.reward : contractSkins.penalty;
				case Currency.Reputation:
					return reward ? contractSkins.repReward : contractSkins.repPenalty;
				default:
					return contractSkins.scienceReward;
			}
		}

		private Texture2D currencyIcon(Currency t, bool reward)
		{
			switch (t)
			{
				case Currency.Funds:
					return reward ? contractSkins.fundsGreen : contractSkins.fundsRed;
				case Currency.Reputation:
					return reward ? contractSkins.repGreen : contractSkins.repRed;
				default:
					return contractSkins.science;
			}
		}

		#endregion

		#region Popups

		private void buildPopup(int id, int size)
		{
			if (popup)
			{
				if (showSort)
				{
					popupRect = new Rect(10, 20, 80 + size * 15, 132 + size * 23);
					GUI.Box(popupRect, "", contractSkins.dropDown);

					var sortTypes = Enum.GetNames(typeof(sortClass));

					for (int i = 0; i < sortTypes.Length; i++)
					{
						if (GUI.Button(new Rect(popupRect.x + 2, popupRect.y + 2 + ((21 + size * 4) * i), popupRect.width - 4, 20 + size * 5), sortTypes[i], contractSkins.sortMenu))
						{
							showSort = false;
							currentMission.OrderMode = (sortClass)Enum.Parse(typeof(sortClass), sortTypes[i]);
							refreshContracts(cList);
						}
					}
				}

				else if (rebuild)
				{
					popupRect = new Rect(10, WindowRect.height - 180, 230, 150);
					GUI.Box(popupRect, "", contractSkins.dropDown);
					GUI.Label(new Rect(popupRect.x + 7, popupRect.y + 10, popupRect.width - 14, 100), "Rebuild\nContracts Window + Display:\n\n<b>Will Not</b> Affect Contract Status", contractSkins.resetBox);
					if (GUI.Button(new Rect(popupRect.x + 20, popupRect.y + 110, popupRect.width - 40, 25), "Reset Display", contractSkins.resetButton))
					{
						LogFormatted("Rebuilding Contract Window List");
						rebuildList();
						resetWindow();
						popup = false;
						rebuild = false;
					}
				}

				else if (agencyPopup)
				{
					popupRect = new Rect(10, 40, 230 + size * 20, 80);
					GUI.Box(popupRect, "", contractSkins.dropDown);
					Rect r = new Rect(popupRect.x + 5, popupRect.y + 10, 84, 60);
					GUI.Box(r, "", contractSkins.agentBackground);
					r.x += 10;
					r.y += 10;
					r.width = 64;
					r.height = 40;
					GUI.Label(r, currentAgent.LogoScaled);
					r.x += 85;
					r.y -= 10;
					r.width = 120 + size * 20;
					r.height = 60;
					GUI.Label(r, currentAgent.Name, contractSkins.agentName);
				}

				else if (missionCreator)
				{
					popupRect = new Rect(20, 30, 210 + size * 20, 200);
					GUI.Box(popupRect, "", contractSkins.dropDown);
					if (!missionTextBox)
					{
						for (int i = 0; i < missionList.Count; i++)
						{
							missionScroll = GUI.BeginScrollView(popupRect, missionScroll, new Rect(0, 0, 190 + size * 20, 25 * missionList.Count));
							Rect r = new Rect(2, (25 * i) + 2, 140 + size * 20, 25);
							if (i == 0)
							{
								if (GUI.Button(r, "Create New Mission", contractSkins.missionMenuCenter))
								{
									inputField = "";
									missionTextBox = true;
									popup = true;
								}
							}
							else
							{
								contractMission m = missionList[i];
								bool containsContract = m.containsContract(tempContainer.Container.Contract.ContractGuid);

								r.x += 15;

								if (containsContract)
								{
									GUI.DrawTexture(new Rect(r.x - 15, r.y + 6, 12 + size * 2, 10 + size * 2), contractSkins.checkIcon);

									GUI.Label(r, m.Name, contractSkins.missionMenu);
								}
								else
								{
									if (GUI.Button(r, m.Name, contractSkins.missionMenu))
									{
										m.addContract(tempContainer.Container, true, true);
										popup = false;
										missionCreator = false;
									}
								}

								r.x += 145 + size * 18;
								r.y += 4;
								r.width = 15 + size * 5;

								GUI.Label(r, m.ActiveContracts.ToString(), contractSkins.timerGood);

								if (!m.MasterMission && containsContract)
								{
									r.x += 15 + size * 2;
									r.width = 14 + size * 4;
									r.height = 14 + size * 4;

									if (GUI.Button(r, new GUIContent(contractSkins.cancelMissionIcon, "Remove From Mission List"), contractSkins.texButtonSmall))
									{
										if (m == currentMission)
											nextRemoveMissionList.Add(tempContainer);
										else
											m.removeContract(tempContainer.Container);
									}
								}
							}
							GUI.EndScrollView();
						}
					}
					else
					{
						Rect r = new Rect(popupRect.x + 2, popupRect.y + 2, 150 + size * 20, 25);
						GUI.Label(r, "Create New Mission", contractSkins.missionMenuCenter);

						r.y += 30;

						inputField = GUI.TextField(r, inputField, 20);

						r.y += 30;

						if (GUI.Button(r, "Create Mission", contractSkins.resetButton))
						{
							if (!string.IsNullOrEmpty(inputField))
							{
								if (contractScenario.Instance.addMissionList(inputField))
								{
									contractMission cM = contractScenario.Instance.getMissionList(inputField);
									if (cM != null)
										cM.addContract(tempContainer.Container, true, true);
									missionList = contractScenario.Instance.getAllMissions();
									popup = false;
									missionTextBox = false;
									missionCreator = false;
								}
							}
							else
								ScreenMessages.PostScreenMessage("Contract mission list must have a name", 5f, ScreenMessageStyle.UPPER_CENTER);
						}
					}
				}

				else if (missionSelector)
				{
					popupRect = new Rect(20, 30, 180 + size * 20, 200);
					GUI.Box(popupRect, "", contractSkins.dropDown);
					for (int i = 0; i < missionList.Count; i++)
					{
						missionScroll = GUI.BeginScrollView(popupRect, missionScroll, new Rect(0, 0, 160 + size * 20, 25 * missionList.Count));
						Rect r = new Rect(2, (25 * i) + 2, 140 + size * 20, 25);
						contractMission m = missionList[i];
						if (GUI.Button(r, m.Name, contractSkins.missionMenu))
						{
							currentMission = contractScenario.Instance.setCurrentMission(m.Name);

							if (currentMission.ShowActiveMissions)
								cList = currentMission.ActiveMissionList;
							else
								cList = currentMission.HiddenMissionList;

							pinnedList = currentMission.loadPinnedContracts(cList);

							refreshContracts(cList);

							popup = false;
							missionSelector = false;
						}
						r.x += 145 + size * 18;
						r.y += 4;
						r.width = 15 + size * 5;
						GUI.Label(r, m.ActiveContracts.ToString(), contractSkins.timerGood);
						GUI.EndScrollView();
					}
				}

				else if (toolbar)
				{
					popupRect = new Rect(10, WindowRect.height - 170, 230 + (size * 20), 140);
					GUI.Box(popupRect, "", contractSkins.dropDown);
					Rect r = new Rect(popupRect.x + 10, popupRect.y + 10, popupRect.width - 20, 30);
					GUI.Label(r, "Toolbar Options:", contractSkins.resetBox);

					r.y += 30;

					if (ToolbarManager.ToolbarAvailable)
					{
						contractScenario.Instance.stockToolbar = GUI.Toggle(r, contractScenario.Instance.stockToolbar, " Use Stock Toolbar");
						r.y += 30;
					}

					if (stockToolbar || !ToolbarManager.ToolbarAvailable)
					{
						contractScenario.Instance.replaceStockToolbar = GUI.Toggle(r, contractScenario.Instance.replaceStockToolbar, " Replace Stock Toolbar");
						r.y += 30;
					}

					r.x += 70;
					r.width = 70;

					if (GUI.Button(r, "Close", contractSkins.resetButton))
					{
						toolbar = false;
						popup = false;
					}
				}

				else if (replaceStockPopup)
				{
					popupRect = new Rect(10, WindowRect.height - 195, 230 + (size * 20), 165);
					GUI.Box(popupRect, "", contractSkins.dropDown);
					Rect r = new Rect(popupRect.x + 5, popupRect.y + 5, popupRect.width - 10, 90);
					GUI.Label(r, "Warning:\nReplacing Stock Contracts App May Produce Errors\nUse This Option\nAt Your Own Risk", contractSkins.resetBox);

					r.y += 95;
					r.height = 30;

					contractScenario.Instance.replaceStockWarned = GUI.Toggle(r, contractScenario.Instance.replaceStockWarned, "Do Not Display This Warning");

					r.x += 70;
					r.y += 30;
					r.width = 75;

					if (GUI.Button(r, "Confirm", contractSkins.resetButton))
					{
						popup = false;
						replaceStockPopup = false;
						if (contractScenario.Instance.appLauncherButton != null)
						{
							contractScenario.Instance.appLauncherButton.replaceStockApp();
							replaceStock = true;
							contractScenario.Instance.replaceStockToolbar = true;
						}
						else
							LogFormatted("Error In Setting Stock App Launcher Button...");
					}
				}

				else if (missionEdit)
				{
					popupRect = new Rect(20, 40, 180 + size * 20, 200);
					GUI.Box(popupRect, "", contractSkins.dropDown);

					Rect r = new Rect(popupRect.x + 2, popupRect.y + 2, popupRect.width - 14, 30);
					GUI.Label(r, "Edit Mission Title", contractSkins.resetBox);

					r.x += 10;
					r.y += 30;
					r.width = 150 + size * 20;
					r.height = 25;

					editField = GUI.TextField(r, editField, 20);

					r.y += 30;

					if (GUI.Button(r, "Change Name", contractSkins.resetButton))
					{
						if (!string.IsNullOrEmpty(editField))
						{
							string oldName = currentMission.Name;
							contractScenario.Instance.removeMissionList(oldName, false);

							currentMission.Name = editField;

							if (contractScenario.Instance.addMissionList(currentMission))
							{
								missionList = contractScenario.Instance.getAllMissions();
								currentMission = contractScenario.Instance.setCurrentMission(editField);

								if (currentMission.ShowActiveMissions)
									cList = currentMission.ActiveMissionList;
								else
									cList = currentMission.HiddenMissionList;

								pinnedList = currentMission.loadPinnedContracts(cList);

								refreshContracts(cList);

								popup = false;
								missionEdit = false;
							}
							else
								currentMission.Name = oldName;
						}
						else
							ScreenMessages.PostScreenMessage("Contract mission list must have a name", 5f, ScreenMessageStyle.UPPER_CENTER);
					}

					r.x -= 10;
					r.y += 30;
					r.width = popupRect.width - 14;
					r.height = 60;

					GUI.Label(r, "Delete Current Mission:\n<b>Will Not</b> Affect Contract Status", contractSkins.resetBox);

					r.x += 10;
					r.y += 60;
					r.width -= 25;
					r.height = 25;

					if (GUI.Button(r, "Delete Mission", contractSkins.resetButton))
					{
						contractScenario.Instance.removeMissionList(currentMission.Name);
						missionList = contractScenario.Instance.getAllMissions();
						currentMission = contractScenario.Instance.MasterMission;

						if (currentMission.ShowActiveMissions)
							cList = currentMission.ActiveMissionList;
						else
							cList = currentMission.HiddenMissionList;

						pinnedList = currentMission.loadPinnedContracts(cList);

						refreshContracts(cList);

						popup = false;
						missionEdit = false;
					}
				}

				else
					popup = false;
			}
		}

		#endregion

		#region Bottom Bar

		private void buildBottomBar(int id, int size)
		{
			Rect r = new Rect(2, WindowRect.height - 30 - (size * 4), WindowRect.width - 4, 4);
			GUI.DrawTexture(r, contractSkins.footerBar);

			//Version label
			r.x = 8;
			r.y = WindowRect.height - 23 - (size * 4);
			r.width = 30 + size * 4;
			r.height = 20 + size * 4;
			GUI.Label(r, version, contractSkins.paramText);

			//Tooltip toggle icon
			r.x = 36 + size * 4;
			r.y -= 2;
			r.height += 2;
			if (GUI.Button(r, new GUIContent(contractSkins.tooltipIcon, "Toggle Tooltips")))
			{
				TooltipsEnabled = !TooltipsEnabled;
				contractScenario.Instance.toolTips = TooltipsEnabled;
			}

			//Clear list button
			r.x = 74 + size * 10;
			if (GUI.Button(r, new GUIContent(contractSkins.resetIcon, "Reset Contracts Window Display")))
			{
				popup = !popup;
				rebuild = !rebuild;
			}

			//Font size button
			r.x = 112 + size * 16;
			if (GUI.Button(r, new GUIContent(contractSkins.fontSize, "Toggle Font Size")))
			{
				if (contractSkins.normalFontSize == 0)
					contractSkins.normalFontSize = 1;
				else
					contractSkins.normalFontSize = 0;
				contractSkins.initializeSkins();
				WindowStyle = contractSkins.newWindowStyle;
				DMC_SkinsLibrary.SetCurrent("ContractUnitySkin");
				contractScenario.Instance.fontSmall = !contractScenario.Instance.fontSmall;
			}

			//Window size button
			r.x = 150 + size * 22;
			if (GUI.Button(r, new GUIContent(contractSkins.windowSize, "Change Window Size")))
			{
				if (contractScenario.Instance.windowSize == 0)
				{
					contractScenario.Instance.windowSize = 1;
					contractSkins.windowFontSize = 2;
					WindowRect.width += 30;
				}
				else
				{
					contractScenario.Instance.windowSize = 0;
					contractSkins.windowFontSize = 0;
					WindowRect.width -= 30;
				}
				contractSkins.initializeSkins();
				WindowStyle = contractSkins.newWindowStyle;
				DMC_SkinsLibrary.SetCurrent("ContractUnitySkin");
			}

			//Toolbar options button
			r.x = 188 + size * 28;
			if (GUI.Button(r, new GUIContent(contractSkins.settingsIcon, "Toolbar Options")))
			{
				popup = true;
				toolbar = true;
			}
		}

		#endregion

		#region Resizer

		private void buildResizer(int id, int size)
		{
			Rect resizer = new Rect(WindowRect.width - 25 - size * 3, WindowRect.height - 25 - size * 3, 22 + size * 4, 22 + size * 4);
			GUI.Label(resizer, contractSkins.expandIcon, contractSkins.dragButton);
			if (Event.current.type == EventType.mouseDown && Event.current.button == 0)
			{
				if (resizer.Contains(Event.current.mousePosition))
				{
					resizing = true;
					dragStart.x = Input.mousePosition.x;
					dragStart.y = Input.mousePosition.y;
					windowHeight = WindowRect.height;
					windowWidth = WindowRect.width;
					Event.current.Use();
				}
			}
			if (resizing)
			{
				if (Input.GetMouseButtonUp(0))
				{
					resizing = false;
					WindowRect.yMax = WindowRect.y + windowHeight;
					WindowRect.xMax = WindowRect.x + windowWidth;
				}
				else
				{
					float height = Input.mousePosition.y;
					float width = Input.mousePosition.x;
					if (Input.mousePosition.y < 0)
						height = 0;
					if (Input.mousePosition.x < 0)
						width = 0;
					windowHeight += dragStart.y - height;
					dragStart.y = height;
					windowWidth += width - dragStart.x;
					dragStart.x = width;
					WindowRect.yMax = WindowRect.y + windowHeight;
					WindowRect.xMax = WindowRect.x + windowWidth;
					if (WindowRect.yMax > Screen.height)
					{
						WindowRect.yMax = Screen.height;
						windowHeight = WindowRect.yMax - WindowRect.y;
					}
					if (WindowRect.xMax > Screen.width)
					{
						WindowRect.xMax = Screen.width;
						windowWidth = WindowRect.xMax - WindowRect.x;
					}
					if (windowHeight < 200)
						windowHeight = 200;
					if (windowWidth < 250 + (size * 30))
						windowWidth = 250 + (size * 30);
					if (windowWidth > 540 + (size * 100))
						windowWidth = 540 + (size * 100);
				}
			}
		}

		#endregion

		protected override void DrawWindowPost(int id)
		{
			//Pin contracts after the window is drawn
			if (nextPinnedList.Count > 0)
			{
				foreach(contractUIObject c in nextPinnedList)
				{
					if (contractScenario.ListRemove(pinnedList, c.Container.Contract.ContractGuid))
						c.Order = null;
					else
					{
						c.Order = pinnedList.Count;
						pinnedList.Add(c.Container.Contract.ContractGuid);
					}
				}

				nextPinnedList.Clear();
				refreshContracts(cList);
			}

			//Remove any hidden contracts after the window is drawn
			if (nextRemoveList.Count > 0)
			{
				foreach (contractUIObject c in nextRemoveList)
					showHideContract(c);

				nextRemoveList.Clear();
				refreshContracts(cList);
			}

			if (nextRemoveMissionList.Count > 0)
			{
				foreach (contractUIObject c in nextRemoveMissionList)
				{
					if (c.Container.Contract.ContractState != Contract.State.Active)
					{
						contractScenario.Instance.removeContract(c.Container.Contract.ContractGuid);
						foreach (contractMission m in missionList)
							m.removeContract(c.Container);
					}
					else
						currentMission.removeContract(c.Container);
				}

				nextRemoveMissionList.Clear();
				refreshContracts(cList);
			}

			if (stockToolbar != contractScenario.Instance.stockToolbar)
			{
				stockToolbar = contractScenario.Instance.stockToolbar;
				if (stockToolbar)
				{
					contractScenario.Instance.appLauncherButton = gameObject.AddComponent<contractStockToolbar>();
					if (contractScenario.Instance.blizzyToolbarButton != null)
					{
						Destroy(contractScenario.Instance.blizzyToolbarButton);
					}
				}
				else
				{
					contractScenario.Instance.blizzyToolbarButton = gameObject.AddComponent<contractToolbar>();
					if (contractScenario.Instance.appLauncherButton != null)
					{
						Destroy(contractScenario.Instance.appLauncherButton);
					}
				}
			}

			if (!contractScenario.Instance.replaceStockWarned)
			{
				if (replaceStock != contractScenario.Instance.replaceStockToolbar)
				{
					replaceStock = contractScenario.Instance.replaceStockToolbar;
					if (replaceStock)
					{
						contractScenario.Instance.replaceStockToolbar = false;
						replaceStock = false;
						popup = true;
						toolbar = false;
						replaceStockPopup = true;
					}
				}
			}

			//Close the sort menu if clicked outside of its rectangle
			if (popup && Event.current.type == EventType.mouseDown && !popupRect.Contains(Event.current.mousePosition))
			{
				popup = false;
			}

			//Set the persistent window location
			contractScenario.Instance.windowRects[sceneInt] = WindowRect;
		}

		#endregion

		#region Methods

		//Reset contract list from the "refresh" button
		private void rebuildList()
		{
			contractScenario.Instance.loadAllContracts();
			contractScenario.Instance.addFullMissionList();

			currentMission = contractScenario.Instance.MasterMission;

			cList.Clear();
			pinnedList.Clear();

			foreach (Contract c in ContractSystem.Instance.Contracts)
			{
				contractContainer cC = contractScenario.Instance.getContract(c.ContractGuid);
				if (cC != null)
					currentMission.addContract(cC, true, false);
			}

			cList = currentMission.ActiveMissionList;
			refreshContracts(cList);
		}

		//Reset all parameters
		private void resetWindow()
		{
			//Reset window settings
			WindowRect = new Rect(40, 80, 250, 300);
			TooltipsEnabled = true;
			Visible = true;
			contractScenario.Instance.windowRects[sceneInt] = WindowRect;
			contractScenario.Instance.fontSmall = true;
			contractScenario.Instance.windowSize = 0;
			contractScenario.Instance.windowVisible[sceneInt] = Visible;
			contractScenario.Instance.toolTips = TooltipsEnabled;

			//Reset GUI settings
			contractSkins.normalFontSize = 0;
			contractSkins.windowFontSize = 0;
			contractSkins.initializeSkins();
			WindowStyle = contractSkins.newWindowStyle;
			DMC_SkinsLibrary.SetCurrent("ContractUnitySkin");
		}

		//Initial contract load
		private void generateList()
		{
			contractScenario.Instance.loadAllContracts();
			contractScenario.Instance.loadAllMissionLists();
			if (HighLogic.LoadedSceneIsFlight)
				currentMission = contractScenario.Instance.setLoadedMission(FlightGlobals.ActiveVessel);
			else
				currentMission = contractScenario.Instance.MasterMission;
		}

		//Update contract values
		private void refreshContracts(List<Guid> gID, bool sort = true)
		{
			List<Guid> removeList = new List<Guid>();
			List<Guid> pinnedRemoveList = new List<Guid>();
			foreach (Guid id in gID)
			{
				contractContainer cC = contractScenario.Instance.getContract(id);
				if (cC == null)
				{
					removeList.Add(id);
					continue;
				}
				else
				{
					if (cC.Contract.ContractState != Contract.State.Active)
					{
						cC.Duration = 0;
						cC.DaysToExpire = "----";

						cC.Title = cC.Contract.Title;
						cC.Notes = cC.Contract.Notes;

						foreach (parameterContainer pC in cC.AllParamList)
						{
							pC.Title = pC.CParam.Title;
							pC.Notes = pC.CParam.Notes;
						}

						continue;
					}

					//Update contract timers
					if (cC.Contract.DateDeadline <= 0)
					{
						cC.Duration = double.MaxValue;
						cC.DaysToExpire = "----";
					}
					else
					{
						cC.Duration = cC.Contract.DateDeadline - Planetarium.GetUniversalTime();
						//Calculate time in day values using Kerbin or Earth days
						cC.DaysToExpire = contractScenario.timeInDays(cC.Duration);
					}

					cC.Title = cC.Contract.Title;
					cC.Notes = cC.Contract.Notes;

					foreach (parameterContainer pC in cC.AllParamList)
					{
						pC.Title = pC.CParam.Title;
						pC.Notes = pC.CParam.Notes;
					}
				}
			}

			foreach(Guid id in pinnedList)
			{
				contractContainer cC = contractScenario.Instance.getContract(id);
				if (cC == null)
					pinnedRemoveList.Add(id);
			}

			foreach (Guid id in removeList)
				contractScenario.ListRemove(gID, id);

			foreach (Guid id in pinnedRemoveList)
				contractScenario.ListRemove(pinnedList, id);

			if (sort)
				gID = sortList(gID, currentMission.OrderMode, currentMission.AscendingOrder);
		}

		//Remove contract from current list and update
		private void showHideContract(contractUIObject c)
		{
			if (currentMission.ShowActiveMissions)
			{
				if (!currentMission.HiddenMissionList.Contains(c.Container.Contract.ContractGuid) && c.Container.Contract.ContractState == Contract.State.Active)
				{
					currentMission.HiddenMissionList.Add(c.Container.Contract.ContractGuid);
					c.ShowParams = false;
				}

				contractScenario.ListRemove(currentMission.ActiveMissionList, c.Container.Contract.ContractGuid);

				if (contractScenario.ListRemove(pinnedList, c.Container.Contract.ContractGuid))
					c.Order = null;

				cList = currentMission.ActiveMissionList;
			}
			else
			{
				if (!currentMission.ActiveMissionList.Contains(c.Container.Contract.ContractGuid) && c.Container.Contract.ContractState == Contract.State.Active)
				{
					currentMission.ActiveMissionList.Add(c.Container.Contract.ContractGuid);
					c.ShowParams = true;
				}

				contractScenario.ListRemove(currentMission.HiddenMissionList, c.Container.Contract.ContractGuid);

				if (contractScenario.ListRemove(pinnedList, c.Container.Contract.ContractGuid))
					c.Order = null;

				cList = currentMission.HiddenMissionList;
			}

			if (c.Container.Contract.ContractState != Contract.State.Active)
			{
				contractScenario.Instance.removeContract(c.Container.Contract.ContractGuid);
				currentMission.removeContract(c.Container);
				foreach (contractMission m in missionList)
					m.removeContract(c.Container);
			}
		}

		//Function to sort the list based on several criteria
		private List<Guid> sortList(List<Guid> gID, sortClass s, bool Asc)
		{
			List<contractUIObject> cL = new List<contractUIObject>();
			//Only add non-pinned contracts to the sort list
			foreach (Guid id in gID)
			{
				contractUIObject cC = currentMission.getContract(id);
				if (cC != null)
				{
					if (cC.Order == null)
						cL.Add(cC);
				}
			}
			switch (s)
			{
				case sortClass.Expiration:
					cL.Sort((a, b) => RUIutils.SortAscDescPrimarySecondary(Asc, a.Container.Duration.CompareTo(b.Container.Duration), a.Container.Title.CompareTo(b.Container.Title)));
					break;
				case sortClass.Acceptance:
					cL.Sort((a, b) => RUIutils.SortAscDescPrimarySecondary(Asc, a.Container.Contract.DateAccepted.CompareTo(b.Container.Contract.DateAccepted), a.Container.Title.CompareTo(b.Container.Title)));
					break;
				case sortClass.Reward:
					cL.Sort((a, b) => RUIutils.SortAscDescPrimarySecondary(Asc, a.Container.TotalReward.CompareTo(b.Container.TotalReward), a.Container.Title.CompareTo(b.Container.Title)));
					break;
				case sortClass.Difficulty:
					cL.Sort((a, b) => RUIutils.SortAscDescPrimarySecondary(Asc, a.Container.Contract.Prestige.CompareTo(b.Container.Contract.Prestige), a.Container.Title.CompareTo(b.Container.Title)));
					break;
				case sortClass.Planet:
					cL.Sort((a, b) => RUIutils.SortAscDescPrimarySecondary(Asc, a.Container.TargetPlanet.CompareTo(b.Container.TargetPlanet), a.Container.Title.CompareTo(b.Container.Title)));
					break;
				case sortClass.Type:
					cL.Sort((a, b) => RUIutils.SortAscDescPrimarySecondary(Asc, a.Container.Contract.GetType().Name.CompareTo(b.Container.Contract.GetType().Name), a.Container.Title.CompareTo(b.Container.Title)));
					cL = typeSort(cL, Asc);
					break;
			}
			gID.Clear();
			
			//Add pinned contracts to the beginning of the list
			if (pinnedList.Count > 0)
				gID.AddRange(pinnedList);

			//Next add the sorted contracts
			foreach (contractUIObject cC in cL)
				gID.Add(cC.Container.Contract.ContractGuid);

			return gID;
		}

		//Special method for handling altitude based parameters; only ReachAltitudeEnvelope seems to be relevant
		private List<contractUIObject> typeSort(List<contractUIObject> cL, bool B)
		{
			List<int> position = new List<int>();
			List<contractUIObject> altList = new List<contractUIObject>();
			for (int i = 0; i < cL.Count; i++)
			{
				foreach (ContractParameter cP in cL[i].Container.Contract.AllParameters)
				{
					if (cP.GetType() == typeof(ReachAltitudeEnvelope))
					{
						altList.Add(cL[i]);
						position.Add(i);
						break;
					}
				}
			}
			if (altList.Count > 1)
			{
				altList.Sort((a, b) => RUIutils.SortAscDescPrimarySecondary(B, ((ReachAltitudeEnvelope)a.Container.Contract.AllParameters.First(s => s.GetType() == typeof(ReachAltitudeEnvelope))).minAltitude.CompareTo(((ReachAltitudeEnvelope)b.Container.Contract.AllParameters.First(s => s.GetType() == typeof(ReachAltitudeEnvelope))).minAltitude), a.Container.Title.CompareTo(b.Container.Title)));
				for (int j = 0; j < position.Count; j++)
				{
					cL[position[j]] = altList[j];
				}
			}

			//ReachFlightEnvelop doesn't actually seem to be used by anything

			//position.Clear();
			//List<contractContainer> flightList = new List<contractContainer>();
			//for (int i = 0; i < cL.Count; i++)
			//{
			//    foreach (parameterContainer cP in cL[i].paramList)
			//    {
			//        if (cP.cParam.ID == "testFlightEnvelope")
			//        {
			//            flightList.Add(cL[i]);
			//            position.Add(i);
			//        }
			//    }
			//}
			//if (flightList.Count > 1)
			//{
			//    flightList.Sort((a, b) => RUIutils.SortAscDescPrimarySecondary(B, ((ReachFlightEnvelope)a.contract.AllParameters.First(s => s.ID == "testFlightEnvelope")).minAltitude.CompareTo(((ReachFlightEnvelope)b.contract.AllParameters.First(s => s.ID == "testFlightEnvelope")).minAltitude), a.contract.Title.CompareTo(b.contract.Title)));
			//    for (int j = 0; j < position.Count; j++)
			//    {
			//        cL[position[j]] = flightList[j];
			//    }
			//}

			return cL;
		}

		//Change the contract title's GUIStyle based on its current state
		private GUIStyle titleState(Contract.State s)
		{
			switch (s)
			{
				case Contract.State.Completed:
					return contractSkins.contractCompleted;
				case Contract.State.Cancelled:
				case Contract.State.DeadlineExpired:
				case Contract.State.Failed:
				case Contract.State.Withdrawn:
					return contractSkins.contractFailed;
				default:
					return contractSkins.contractActive;
			}
		}

		//Label style for contract titles when the sort menu is open
		private GUIStyle hoverTitleState(Contract.State s)
		{
			switch (s)
			{
				case Contract.State.Completed:
					return contractSkins.contractCompletedBehind;
				case Contract.State.Cancelled:
				case Contract.State.DeadlineExpired:
				case Contract.State.Failed:
				case Contract.State.Withdrawn:
					return contractSkins.contractFailedBehind;
				default:
					return contractSkins.contractActiveBehind;
			}
		}

		//Change parameter title GUIStyle based on its current state
		private GUIStyle paramState(parameterContainer cP)
		{
			switch (cP.CParam.State)
			{
				case ParameterState.Complete:
					return contractSkins.paramCompleted;
				case ParameterState.Failed:
					return contractSkins.paramFailed;
				default:
					if (cP.Level == 0)
						return contractSkins.paramText;
					else
						return contractSkins.paramSub;
			}
		}

		//Adds new contracts when they are accepted in Mission Control
		private void contractAccepted(Contract c)
		{
			contractScenario.Instance.addContract(c.ContractGuid, new contractContainer(c));
			contractContainer cC = contractScenario.Instance.getContract(c.ContractGuid);
			if (cC != null)
			{
				currentMission.addContract(cC, true, true);
				if (currentMission.ShowActiveMissions)
					refreshContracts(cList);

				if (!currentMission.MasterMission)
					contractScenario.Instance.MasterMission.addContract(cC, true, true);
			}
		}

		//Starts the rebuild timer when the contract list is loaded
		private void contractLoaded()
		{
			if (!contractsLoading && !loaded)
			{
				contractsLoading = true;
			}
		}

		#endregion

		#region Repeating Worker

		protected override void RepeatingWorker()
		{
			if (cList.Count > 0)
				refreshContracts(cList, false);
		}

		#endregion

		#region Persistence

		//Load window position and size settings
		private void PersistenceLoad()
		{
			if (contractScenario.Instance != null)
			{
				stockToolbar = contractScenario.Instance.stockToolbar;
				replaceStock = contractScenario.Instance.replaceStockToolbar;
				cList.Clear();
				missionList = contractScenario.Instance.getAllMissions();
				currentMission = missionList[0];
				if (currentMission == null)
					currentMission = new contractMission("");
				WindowRect = contractScenario.Instance.windowRects[sceneInt];
				if (contractScenario.Instance.fontSmall)
					contractSkins.normalFontSize = 0;
				else
					contractSkins.normalFontSize = 1;
				if (contractScenario.Instance.windowSize == 0)
					contractSkins.windowFontSize = 0;
				else
					contractSkins.windowFontSize = 2;
				contractSkins.initializeSkins();
				WindowStyle = contractSkins.newWindowStyle;
				DragRect = new Rect(0, 0, WindowRect.width - 19, WindowRect.height - 24 - contractScenario.Instance.windowSize * 8);
				Visible = contractScenario.Instance.windowVisible[sceneInt];
				TooltipsEnabled = contractScenario.Instance.toolTips;
				if (Visible)
					StartRepeatingWorker(5);
				if (WindowRect.width < 100)
					resetWindow();
			}
		}

		#endregion

	}

	#region SortClass

	public enum sortClass
	{
		Difficulty = 1,
		Expiration = 2,
		Acceptance = 3,
		Reward = 4,
		Type = 5,
		Planet = 6,
	}

	#endregion
}
