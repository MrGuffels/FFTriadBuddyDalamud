﻿using Dalamud.Game.Gui;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace TriadBuddyPlugin
{
    public class UIReaderTriadPrep
    {
        public UIStateTriadPrep cachedState = new();
        public bool shouldScanDeckData = false;

        public bool HasMatchRequestUI => hasRequestUI;
        public bool HasDeckSelectionUI => hasDeckSelectionUI;

        public Action<UIStateTriadPrep> OnUIStateChanged;
        public Action<bool> OnMatchRequestChanged;
        public Action<bool> OnDeckSelectionChanged;

        private GameGui gameGui;
        private bool hasRequestUI;
        private bool hasDeckSelectionUI;
        private IntPtr cachedDeckSelAddon;

        public UIReaderTriadPrep(GameGui gameGui)
        {
            this.gameGui = gameGui;
        }

        public unsafe void Update()
        {
            bool foundActiveUI = false;
            bool newHasRequestUI = false;
            bool newHasDeckSelectUI = false;

            //////////////////////////////////////////////////////////////////
            // match request

            IntPtr addonReqPtr = gameGui.GetAddonByName("TripleTriadRequest", 1);
            if (addonReqPtr != IntPtr.Zero)
            {
                var baseNode = (AtkUnitBase*)addonReqPtr;
                if (baseNode != null && baseNode->RootNode != null && baseNode->RootNode->IsVisible)
                {
                    if (!hasRequestUI)
                    {
                        UpdateRequest(baseNode);

                        // notify always, if deck data depends on UI, it will be ignored by solver
                        OnUIStateChanged?.Invoke(cachedState);
                    }

                    (cachedState.screenPos, cachedState.screenSize) = GUINodeUtils.GetNodePosAndSize(baseNode->RootNode);
                    newHasRequestUI = true;
                    foundActiveUI = true;
                }
            }

            if (hasRequestUI != newHasRequestUI)
            {
                hasRequestUI = newHasRequestUI;
                OnMatchRequestChanged?.Invoke(hasRequestUI);
            }


            //////////////////////////////////////////////////////////////////
            // deck selection

            IntPtr addonDeckPtr = foundActiveUI ? IntPtr.Zero : gameGui.GetAddonByName("TripleTriadSelDeck", 1);
            if (cachedDeckSelAddon != addonDeckPtr)
            {
                // addon ptr changed? reset cached node ptrs
                cachedDeckSelAddon = addonDeckPtr;
                foreach (var deckOb in cachedState.decks)
                {
                    deckOb.rootNodeAddr = 0;
                }
            }

            if (addonDeckPtr != IntPtr.Zero)
            {
                var baseNode = (AtkUnitBase*)addonDeckPtr;
                if (baseNode != null && baseNode->RootNode != null && baseNode->RootNode->IsVisible)
                {
                    if (!hasDeckSelectionUI)
                    {
                        UpdateDeckSelect(baseNode);
                    }

                    newHasDeckSelectUI = cachedState.decks.Count > 0;

                    // notify only when deck data is coming from UI
                    if (!hasDeckSelectionUI && newHasDeckSelectUI && shouldScanDeckData)
                    {
                        OnUIStateChanged?.Invoke(cachedState);
                    }

                    if (newHasDeckSelectUI)
                    {
                        foundActiveUI = true;

                        foreach (var deckOb in cachedState.decks)
                        {
                            var updateNode = (AtkResNode*)deckOb.rootNodeAddr;
                            if (updateNode != null)
                            {
                                (deckOb.screenPos, deckOb.screenSize) = GUINodeUtils.GetNodePosAndSize(updateNode);
                            }
                        }
                    }
                }
            }

            if (hasDeckSelectionUI != newHasDeckSelectUI)
            {
                hasDeckSelectionUI = newHasDeckSelectUI;
                OnDeckSelectionChanged?.Invoke(hasDeckSelectionUI);
            }
        }

        private unsafe void UpdateRequest(AtkUnitBase* baseNode)
        {
            // 13 child nodes (sibling scan, root node list huge)
            //     [6] match/tournament rules, simple node
            //         [0] comp node with 3 children: [2] = text
            //         [1] comp node with 3 children: [2] = text
            //     [7] region rules, simple node
            //         [0] comp node with 3 children: [2] = text
            //         [1] comp node with 3 children: [2] = text
            //     [8] npc, simple node
            //         [0] text

            var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);
            var nodeRulesA = GUINodeUtils.PickNode(nodeArrL0, 6, 13);
            var nodeArrL1A = GUINodeUtils.GetImmediateChildNodes(nodeRulesA);
            var nodeL2A1 = GUINodeUtils.PickNode(nodeArrL1A, 0, 6);
            cachedState.rules[3] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2A1, 2, 3));
            var nodeL2A2 = GUINodeUtils.PickNode(nodeArrL1A, 1, 6);
            cachedState.rules[2] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2A2, 2, 3));

            var nodeRulesB = GUINodeUtils.PickNode(nodeArrL0, 7, 13);
            var nodeArrL1B = GUINodeUtils.GetImmediateChildNodes(nodeRulesB);
            var nodeL2B1 = GUINodeUtils.PickNode(nodeArrL1B, 0, 3);
            cachedState.rules[1] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2B1, 2, 3));
            var nodeL2B2 = GUINodeUtils.PickNode(nodeArrL1B, 1, 3);
            cachedState.rules[0] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2B2, 2, 3));

            var nodeNpc = GUINodeUtils.PickNode(nodeArrL0, 8, 13);
            cachedState.npc = GUINodeUtils.GetNodeText(GUINodeUtils.GetChildNode(nodeNpc));

            cachedState.decks.Clear();
        }

        private unsafe void UpdateDeckSelect(AtkUnitBase* baseNode)
        {
            // 5 child nodes (node list)
            //    [4] list 
            //        [x] comp nodes, each has 12 child nodes
            //            [3] simple node with 5 children, each is a card
            //                [x] comp node with 2 children
            //                    [1] comp node with 4 children
            //                        [0] card image
            //            [11] text, deck name

            cachedState.decks.Clear();

            var nodeA = (baseNode->UldManager.NodeListCount == 5) ? baseNode->UldManager.NodeList[4] : null;
            if (nodeA != null && (int)nodeA->Type > 1000)
            {
                var compNodeA = (AtkComponentNode*)nodeA;
                for (int idxA = 0; idxA < compNodeA->Component->UldManager.NodeListCount; idxA++)
                {
                    var nodeB = compNodeA->Component->UldManager.NodeList[idxA];
                    var nodeC1 = GUINodeUtils.PickChildNode(nodeB, 3, 12);

                    if (nodeC1 != null)
                    {
                        var deckOb = new UIStateTriadPrepDeck();
                        deckOb.id = cachedState.decks.Count;
                        deckOb.rootNodeAddr = (ulong)nodeB;

                        if (shouldScanDeckData)
                        {
                            var nodeArrC1 = GUINodeUtils.GetImmediateChildNodes(nodeC1);
                            if (nodeArrC1 != null && nodeArrC1.Length == 5)
                            {
                                for (int idxC = 0; idxC < nodeArrC1.Length; idxC++)
                                {
                                    var nodeD = GUINodeUtils.PickChildNode(nodeArrC1[idxC], 1, 2);
                                    var nodeE = GUINodeUtils.PickChildNode(nodeD, 0, 4);
                                    var texPath = GUINodeUtils.GetNodeTexturePath(nodeE);
                                    if (string.IsNullOrEmpty(texPath))
                                    {
                                        break;
                                    }

                                    deckOb.cardTexPaths[idxC] = texPath;
                                }
                            }

                            var nodeC2 = GUINodeUtils.PickChildNode(nodeB, 11, 12);
                            deckOb.name = GUINodeUtils.GetNodeText(nodeC2);
                        }

                        cachedState.decks.Add(deckOb);
                    }
                }
            }
        }
    }

    public class UIStateTriadPrepDeck
    {
        public string[] cardTexPaths = new string[5];
        public string name;

        public ulong rootNodeAddr;
        public int id;

        public Vector2 screenPos;
        public Vector2 screenSize;
    }

    public class UIStateTriadPrep
    {
        public string[] rules = new string[4];
        public string npc;

        public Vector2 screenPos;
        public Vector2 screenSize;

        public List<UIStateTriadPrepDeck> decks = new();
    }
}
