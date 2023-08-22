﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using SaveOurShip2;

namespace RimWorld
{
    class MinifiedThingShipMove : MinifiedThing
    {
        public Building shipRoot;
        public IntVec3 bottomLeftPos;
        public byte shipRotNum;
        public bool includeRock;
        public Map targetMap = null;
        public Faction fac = null;

        public override void Tick()
        {
            base.Tick();
            if (Find.Selector.SelectedObjects.Count > 1 || !Find.Selector.SelectedObjects.Contains(this))
            {
                if (InstallBlueprintUtility.ExistingBlueprintFor(this) != null)
                    ShipInteriorMod2.MoveShip(shipRoot, targetMap, InstallBlueprintUtility.ExistingBlueprintFor(this).Position - bottomLeftPos, fac, shipRotNum, includeRock);
                if(!this.Destroyed)
                    this.Destroy(DestroyMode.Vanish);
            }
        }

        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (this.Graphic is Graphic_Single)
            {
                this.Graphic.Draw(drawLoc, Rot4.North, this, 0f);
                return;
            }
            this.Graphic.Draw(drawLoc, Rot4.South, this, 0f);
        }

        public override string GetInspectString()
        {
            return TranslatorFormattedStringExtensions.Translate("ShipMoveDesc");
        }
    }
}
