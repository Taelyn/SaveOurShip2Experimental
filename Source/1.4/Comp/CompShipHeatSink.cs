﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;
using SaveOurShip2;
using RimworldMod;

namespace RimWorld
{
    [StaticConstructorOnStartup]
    public class CompShipHeatSink : CompShipHeat
    {
        public static readonly float HeatPushMult = 15f; //ratio modifier
        public float heatStored; //used only when a HB is not on a net
        public bool inSpace;
        public CompPower powerComp;
        public bool Disabled = false;
        ShipHeatMapComp mapComp;
        IntVec3 pos; //needed because no predestroy
        Map map; //needed because no predestroy

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.TryGetComp<CompPower>();
            inSpace = this.parent.Map.IsSpace();
            pos = this.parent.Position;
            map = this.parent.Map;
            mapComp = this.map.GetComponent<ShipHeatMapComp>();
        }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            float heat = this.Props.heatCapacity * HeatPushMult * RatioInNetwork();
            pos = pos.GetRoom(map).Cells.FirstOrDefault();
            GenTemperature.PushHeat(pos, map, heat);
            base.PostDestroy(mode, previousMap);
        }
        public override void PostDeSpawn(Map map)
        {
            base.PostDeSpawn(map);
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            //save heat to sinks on save, value clamps
            if (myNet != null && Scribe.mode == LoadSaveMode.Saving)
            {
                heatStored = Props.heatCapacity * RatioInNetwork();
                if (heatStored < 0)
                    heatStored = 0;
                else if (heatStored > Props.heatCapacity)
                    heatStored = Props.heatCapacity;
            }
            Scribe_Values.Look<float>(ref heatStored, "heatStored");
        }
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || parent.Destroyed || myNet == null)
            {
                return;
            }
            if (this.parent.IsHashIntervalTick(120))
            {
                if (Props.heatVent > 0 && !Props.antiEntropic) //radiates to space
                {
                    IsDisabled();
                    if (!Disabled)
                    {
                        if (map.IsSpace())
                            RemHeatFromNetwork(Props.heatVent);
                        else
                        {
                            //higher outdoor temp, push less heat out
                            float heat = Props.heatVent * GenMath.LerpDoubleClamped(0, 100, 1, 0, map.mapTemperature.OutdoorTemp);
                            RemHeatFromNetwork(heat);
                        }
                    }
                }
                if (myNet.StorageUsed > 0)
                {
                    float ratio = RatioInNetwork();
                    if (ratio > 0.90f)
                    {
                        this.parent.TakeDamage(new DamageInfo(DamageDefOf.Burn, 10));
                    }
                    if (Props.antiEntropic) //convert heat
                    {
                        if (powerComp != null && powerComp.PowerNet != null && powerComp.PowerNet.batteryComps.Count > 0)
                        {
                            IEnumerable<CompPowerBattery> batteries = powerComp.PowerNet.batteryComps.Where(b => b.StoredEnergy <= b.Props.storedEnergyMax - 1);
                            if (batteries.Any())
                            {
                                batteries.RandomElement().AddEnergy(2);
                                RemHeatFromNetwork(Props.heatVent);
                            }
                        }
                    }
                    else if (inSpace && ShipInteriorMod2.ExposedToOutside(this.parent.GetRoom()))
                        return;
                    else //push heat to room
                    {
                        if (RemHeatFromNetwork(Props.heatLoss))
                            GenTemperature.PushHeat(this.parent, Props.heatLoss * HeatPushMult * ratio);
                    }
                }
            }
        }
        private void IsDisabled()
        {
            if (!mapComp.InCombat && mapComp.Cloaks.Any(c => c.active))
            {
                Disabled = false;
            }
            Disabled = false;
        }
        public override string CompInspectStringExtra()
        {
            string toReturn = base.CompInspectStringExtra();// = "Stored heat: " + Mathf.Round(heatStored)+"/"+Props.heatCapacity;
            if (Disabled)
            {
                toReturn += "\n<color=red>Cannot vent: Cloaked</color>";
            }
            return toReturn;
        }
    }
}
