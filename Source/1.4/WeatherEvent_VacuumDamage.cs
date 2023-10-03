﻿using System;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Collections.Generic;
using System.Linq;
using SaveOurShip2;
using Verse.AI;

namespace RimWorld
{
    public class WeatherEvent_VacuumDamage : WeatherEvent
    {
        public override bool Expired {
            get {
                return true;
            }
        }
        public WeatherEvent_VacuumDamage(Map map) : base(map)
        {
        }
        public override void WeatherEventTick()
        {
        }
        public override void FireEvent()
        {
            List<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
            List<Pawn> pawnsToDamage = new List<Pawn>();
            List<Pawn> pawnsToSuffocate = new List<Pawn>();
            foreach (Pawn pawn in allPawns.Where(p => !p.Dead))
            {
                byte eva = ShipInteriorMod2.EVAlevel(pawn);
                if (eva > 3)
                    continue;
                Room room = pawn.Position.GetRoom(map);
                if (ShipInteriorMod2.ExposedToOutside(room))
                {
                    RunFromVacuum(pawn);
                    if (eva == 3)//has inactive bubble
                    {
                        eva = ActivateBubble(pawn);
                    }
                    else if (eva == 2)//has skin
                        pawnsToSuffocate.Add(pawn);
                    else
                        pawnsToDamage.Add(pawn);
                }
                //SC else if (eva != 1 && !map.GetComponent<ShipHeatMapComp>().VecHasEVA(pawn.Position)) //in ship, no air
                else if (eva != 1 && !map.GetComponent<ShipHeatMapComp>().LifeSupports.Any(s => s.active)) //in ship, no air
                {
					if (eva == 3)
					{
						eva = ActivateBubble(pawn);
					}
					else
						pawnsToSuffocate.Add(pawn);
                }
            }
            foreach (Pawn thePawn in pawnsToDamage)
            {
                if (thePawn.InBed() && thePawn.CurrentBed() is Building_SpaceCrib crib)
                {
                    crib.UpdateState(true);
                    continue;
                }
                thePawn.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed("VacuumDamage"), 1));
                HealthUtility.AdjustSeverity(thePawn, ResourceBank.HediffDefOf.SpaceHypoxia, 0.025f);
            }
            foreach (Pawn thePawn in pawnsToSuffocate)
            {
                if (thePawn.InBed() && thePawn.CurrentBed() is Building_SpaceCrib crib)
                {
                    crib.UpdateState(true);
                    continue;
                }
                HealthUtility.AdjustSeverity(thePawn, ResourceBank.HediffDefOf.SpaceHypoxia, 0.0125f);
            }
        }
        public byte ActivateBubble(Pawn pawn)
        {
            foreach (Apparel app in pawn.apparel.WornApparel)
            {
                if (app.def == ResourceBank.ThingDefOf.Apparel_SpaceSurvivalBelt)
                {
                    pawn.health.AddHediff(ResourceBank.HediffDefOf.SpaceBeltBubbleHediff);
                    pawn.apparel.Remove(app);
                    pawn.apparel.Wear((Apparel)ThingMaker.MakeThing(ResourceBank.ThingDefOf.Apparel_SpaceSurvivalBeltDummy),
                        false, true);
                    GenExplosion.DoExplosion(pawn.Position, pawn.Map, 1, DamageDefOf.Smoke, null, -1, -1f, null, null,
                        null, null, null, 1f);
                    Find.World.GetComponent<PastWorldUWO2>().PawnsInSpaceCache[pawn.thingIDNumber] = 4;
                    break;
                }
            }
            return 4;
        }
        public void RunFromVacuum(Pawn pawn)
        {
            //find first nonvac area and run to it - enemy only
            var mapComp = pawn.Map.GetComponent<ShipHeatMapComp>();
            if (pawn.Faction != Faction.OfPlayer && !pawn.Downed && pawn.CurJob.def != DefDatabase<JobDef>.GetNamed("FleeVacuum"))
            {
                Predicate<Thing> otherValidator = delegate (Thing t)
                {
                    return t is Building_ShipAirlock && !((Building_ShipAirlock)t).Outerdoor();
                };
                Thing b = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(ResourceBank.ThingDefOf.ShipAirlock), PathEndMode.Touch, TraverseParms.For(pawn), 99f, otherValidator);
                Job Flee = new Job(DefDatabase<JobDef>.GetNamed("FleeVacuum"), b);
                pawn.jobs.StartJob(Flee, JobCondition.InterruptForced);
            }
        }
    }
}

