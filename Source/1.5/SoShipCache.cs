﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using SaveOurShip2;
using UnityEngine;
using RimWorld;

namespace SaveOurShip2
{
    public class SoShipCache
    {
        //td no function on ship parts if no bridge (tur,eng,sen)
        //functionality:
        //post load or after ship spawn: rebuild cache
        //shipmove: if moved vec - offset/rot area, if diff map rem from curr, add to target

        //on shipPart added
        //possible merge: ooc - add all parts to this ship / ic - same but shrink destroyedarea

        //on shipPart removed
        //possible split: ooc - check for bridge, ic - detach, ooc - RebuildCache for each dcon bridge, -1 to rest

        //other buildings: +-for count, mass if on shipPart

        public HashSet<IntVec3> Area = new HashSet<IntVec3>(); //shipParts add to area, removed in despawn, detach
        public HashSet<Building> Parts = new HashSet<Building>(); //shipParts only
        public HashSet<Building> Buildings = new HashSet<Building>(); //all on ship parts, even partially
        //for rebuild after battle, reset before combat, rebuild gizmo
        public HashSet<Tuple<BuildableDef, IntVec3, Rot4>> BuildingsDestroyed = new HashSet<Tuple<BuildableDef, IntVec3, Rot4>>();
        public HashSet<Building> BuildingsNonRot = new HashSet<Building>();
        public List<CompEngineTrail> Engines = new List<CompEngineTrail>();
        public List<CompRCSThruster> RCSs = new List<CompRCSThruster>();
        public List<CompShipHeatPurge> HeatPurges = new List<CompShipHeatPurge>();
        public List<CompShipCombatShield> Shields = new List<CompShipCombatShield>();
        public List<CompCryptoLaunchable> Pods = new List<CompCryptoLaunchable>();
        public List<Building_ShipBridge> Bridges = new List<Building_ShipBridge>();
        public List<Building_ShipBridge> AICores = new List<Building_ShipBridge>();
        public HashSet<Building_ShipTurret> Turrets = new HashSet<Building_ShipTurret>();
        public List<Building_ShipAdvSensor> Sensors = new List<Building_ShipAdvSensor>();
        public List<CompHullFoamDistributor> FoamDistributors = new List<CompHullFoamDistributor>();
        public List<CompShipLifeSupport> LifeSupports = new List<CompShipLifeSupport>();
        public List<Pawn> PawnsOnShip => Map.mapPawns.AllPawns.Where(p => Area.Contains(p.Position)).ToList();
        private Map map;
        public Map Map
        {
            get { return map; }
            set
            {
                map = value;
                if (map == null)
                    mapComp = null;
                else
                    mapComp = map.GetComponent<ShipHeatMapComp>();
            }
        }
        public ShipHeatMapComp mapComp;
        public Building_ShipBridge Core; //main bridge
        public int Index = -1;
        private string name;
        public string Name
        {
            set
            {
                foreach (Building_ShipBridge b in Bridges)
                    b.ShipName = value;
                name = value;
            }
            get
            {
                return name;
            }
        }
        public Faction Faction => Buildings?.FirstOrDefault()?.Faction;
        public void Capture(Faction fac)
        {
            foreach (Building building in Buildings)
            {
                if (building.def.CanHaveFaction)
                    building.SetFaction(fac);
            }
        }
        //threat
        public int BuildingCount = 0;
        public int BuildingCountAtCombatStart = 0;
        public int ThreatRaw = 0;
        public int Threat => ThreatRaw + Mass / 100;
        public float ThreatCurrent; //updates on ActualThreatPerSegment
        public float[] ThreatPerSegment = new[] { 1f, 1f, 1f, 1f };
        public float[] ActualThreatPerSegment()
        {
            ThreatCurrent = 0;
            float[] actualThreatPerSegment = ThreatPerSegment;
            foreach (var turret in Turrets)
            {
                int threat = turret.heatComp.Props.threat;
                var torp = turret.TryGetComp<CompChangeableProjectilePlural>();
                var fuel = turret.TryGetComp<CompRefuelable>();
                if (!turret.Active || turret.SpinalHasNoAmps || (torp != null && !torp.Loaded) || (fuel != null && fuel.Fuel == 0f)) //turrets that cant fire due to ammo
                {
                    if (turret.heatComp.Props.maxRange > 150) //long
                    {
                        actualThreatPerSegment[0] -= threat / 6f;
                        actualThreatPerSegment[1] -= threat / 4f;
                        actualThreatPerSegment[2] -= threat / 2f;
                        actualThreatPerSegment[3] -= threat;
                    }
                    else if (turret.heatComp.Props.maxRange > 100) //med
                    {
                        actualThreatPerSegment[0] -= threat / 4f;
                        actualThreatPerSegment[1] -= threat / 2f;
                        actualThreatPerSegment[2] -= threat;
                    }
                    else if (turret.heatComp.Props.maxRange > 50) //short
                    {
                        actualThreatPerSegment[0] -= threat / 2f;
                        actualThreatPerSegment[1] -= threat;
                    }
                    else //cqc
                        actualThreatPerSegment[0] -= threat;
                }
                else //turrets that can fire
                {
                    ThreatCurrent += threat;
                }
            }
            return actualThreatPerSegment;
        }
        //movement
        public int Mass = 0;
        public int EngineMass = 0;
        public int MassSum => Mass + EngineMass;
        public float MassActual => Mathf.Pow(MassSum, 1.2f) / 14;
        public float MaxTakeoff = 0;
        public float ThrustRaw = 0;
        public float ThrustRatio => 14 * ThrustRaw * 500f / Mathf.Pow(MassSum, 1.2f);
        public int Rot => Engines.First().parent.Rotation.AsInt;
        public bool IsWreck => Core == null; //not a real ship
        public bool IsStuck => IsWreck || Bridges.All(b => b.TacCon) || Engines.NullOrEmpty(); //ship but cant move on its own
        public bool CanFire() //ship has any engine that can fire
        {
            if (IsStuck)
                return false;
            if (Engines.Any(e => e.CanFire()))
                return true;
            return false;
        }
        public bool CanMove() //ship can move if it can fire and is aligned to map rot
        {
            if (IsStuck)
                return false;
            if (Engines.Any(e => e.CanFire(new Rot4(mapComp.EngineRot))))
                return true;
            return false;
        }
        public bool IsStuckAndNotAssisted() //is the stuck ship docked to a ship that can move
        {
            if (IsStuck)
            {
                if (mapComp.Docked.Any())
                {
                    foreach (int i in DockedTo())
                    {
                        var ship = mapComp.ShipsOnMapNew[i];
                        if (ship.CanMove())
                            return false;
                    }
                }
                return true;
            }
            return false;
        }

        public float ThrustToWeight() //actual combat t/w, calcs in docked
        {
            if (mapComp.Docked.NullOrEmpty())
            {
                //Log.Message(Index + " - 1 - " + EnginePower() / Mathf.Pow(BuildingCount, 1.1f));
                return EnginePower() / Mathf.Pow(MassSum, 1.2f); //Mathf.Pow(BuildingCount, 1.1f);
            }

            float p = EnginePower();
            int c = MassSum;
            foreach (int i in DockedTo())
            {
                var ship = mapComp.ShipsOnMapNew[i];
                p += ship.EnginePower();
                c += ship.MassSum;
            }
            //Log.Message(Index + " - 2 - " + p / Mathf.Pow(c, 1.1f));
            return p / Mathf.Pow(c, 1.2f);
        }
        public float EnginePower()
        {
            if (IsStuck)
                return 0;
            float enginePower = 0;
            foreach (var engine in Engines)
            {
                if (engine.CanFire(new Rot4(mapComp.EngineRot)))
                {
                    enginePower += engine.Thrust;
                }
            }
            return enginePower;
        }
        public HashSet<int> DockedTo() //other ships docked to this
        {
            HashSet<int> dockedTo = new HashSet<int>();
            foreach (Building_ShipAirlock b in mapComp.Docked)
            {
                int i = mapComp.ShipIndexOnVec(b.Position);
                int i2 = mapComp.ShipIndexOnVec(b.dockedTo.Position);
                if (i == Index && i2 != Index) //dock on this, get docked
                {
                    dockedTo.Add(i2);
                }
                if (i2 == Index && i != Index) //reverse
                {
                    dockedTo.Add(i);
                }
            }
            return dockedTo;
        }
        public void MoveAtThrustToWeight(float thrust)
        {
            thrust *= Mathf.Pow(MassSum, 1.2f);
            List<CompEngineTrail> list = new List<CompEngineTrail>();
            list = Engines.Where(e => e.CanFire(new Rot4(mapComp.EngineRot))).OrderBy(e => e.Thrust).ThenBy(e => e.Props.energy).ThenBy(e => e.Props.reactionless).ToList();
            int i = 0;
            foreach (CompEngineTrail engine in list)
            {
                //Log.Message(Index + " " + engine);
                i += engine.Thrust;
                engine.On();
                if (i > thrust)
                    return;
            }
        }
        public void EnginesOff()
        {
            foreach (var engine in Engines)
            {
                engine.Off();
            }
        }
        //shipmove
        public byte ForceRePower = 0; //0 - no, 1 - same map, 2 - different map
        public void ForceRePowerOnTick()
        {
            if (Core?.PowerComp?.PowerNet == null)
            {
                ForceRePower = 0;
                return;
            }

            //Log.Message("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " ForceRePower mode: " + ForceRePower);
            if (ForceRePower == 2) //reconnect
            {
                List<CompPower> pComps = new List<CompPower>();
                foreach (CompPower p in Core.PowerComp.PowerNet.connectors)
                {
                    pComps.Add(p);
                }
                foreach (CompPower p in pComps)
                {
                    p.TryManualReconnect();
                }
            }
            //repower
            foreach (CompPowerTrader p in Core.PowerComp.PowerNet.powerComps)
            {
                if (!p.PowerOn && FlickUtility.WantsToBeOn(p.parent) && !p.parent.IsBrokenDown())
                {
                    PowerNet.partsWantingPowerOn.Add(p);
                }
            }
            foreach (CompPowerTrader p in PowerNet.partsWantingPowerOn)
            {
                p.PowerOn = true;
            }
            ForceRePower = 0;
        }
        public void CreateShipSketchIfFuelPct(float fuelPercentNeeded, Map map, byte rot = 0, bool atmospheric = false)
        {
            if (HasPilotRCSAndFuel(fuelPercentNeeded, atmospheric))
                CreateShipSketch(map, rot, atmospheric);
        }
        public bool HasPilotRCSAndFuel(float fuelPercentNeeded, bool atmospheric)
        {
            if (!HasMannedBridge())
            {
                Messages.Message(TranslatorFormattedStringExtensions.Translate("ShipInsideMoveFailPilot"), Core, MessageTypeDefOf.NeutralEvent);
                return false;
            }
            if (!HasRCSAndFuel(fuelPercentNeeded, atmospheric))
                return false;
            return true;
        }
        public bool HasMannedBridge()
        {
            bool hasPilot = false;
            foreach (Building_ShipBridge bridge in Bridges) //first bridge with pilot/AI
            {
                if (!hasPilot && bridge.powerComp.PowerOn)
                {
                    if (bridge.mannableComp == null || (bridge.mannableComp != null && bridge.mannableComp.MannedNow))
                    {
                        hasPilot = true;
                        return true;
                    }
                }
            }
            return false;
        }
        public bool HasRCSAndFuel(float fuelPercentNeeded, bool atmospheric)
        {
            float fuelNeeded = MassActual;
            Log.Message("Mass: " + MassActual + " fuel req: " + fuelNeeded * fuelPercentNeeded + " RCS: " + RCSs.Count);
            if (atmospheric && ((1000 < fuelNeeded && RCSs.Count * 2000 < fuelNeeded) || Engines.Any(e => e.Props.reactionless))) //2k weight/RCS to move
            {
                Messages.Message(TranslatorFormattedStringExtensions.Translate("ShipInsideMoveFailRCS", 1 + (fuelNeeded / 2000)), Core, MessageTypeDefOf.NeutralEvent);
                return false;
            }
            fuelNeeded *= fuelPercentNeeded;
            if (FuelNeeded(atmospheric) < fuelNeeded)
            {
                Messages.Message(TranslatorFormattedStringExtensions.Translate("ShipInsideMoveFailFuel", fuelNeeded), Core, MessageTypeDefOf.NeutralEvent);
                return false;
            }
            return true;
        }
        public float FuelNeeded(bool atmospheric)
        {
            float fuelHad = 0f;
            foreach (CompEngineTrail engine in Engines.Where(e => !e.Props.energy && (!atmospheric || e.Props.takeOff)))
            {
                fuelHad += engine.refuelComp.Fuel;
                if (engine.refuelComp.Props.fuelFilter.AllowedThingDefs.Contains(ResourceBank.ThingDefOf.ShuttleFuelPods))
                    fuelHad += engine.refuelComp.Fuel;
            }
            return fuelHad;
        }
        public void CreateShipSketch(Map targetMap, byte rotb = 0, bool atmospheric = false)
        {
            IntVec3 lowestCorner = LowestCorner(rotb, Map);
            Sketch sketch = new Sketch();
            IntVec3 rot = new IntVec3(0, 0, 0);
            foreach (IntVec3 pos in Area)
            {
                if (rotb == 1)
                {
                    rot.x = targetMap.Size.x - pos.z;
                    rot.z = pos.x;
                    sketch.AddThing(ResourceBank.ThingDefOf.Ship_FakeBeam, rot - lowestCorner, Rot4.North);
                }
                else if (rotb == 2)
                {
                    rot.x = targetMap.Size.x - pos.x;
                    rot.z = targetMap.Size.z - pos.z;
                    sketch.AddThing(ResourceBank.ThingDefOf.Ship_FakeBeam, rot - lowestCorner, Rot4.North);
                }
                else
                    sketch.AddThing(ResourceBank.ThingDefOf.Ship_FakeBeam, pos - lowestCorner, Rot4.North);
            }
            MinifiedThingShipMove fakeMover = (MinifiedThingShipMove)new ShipMoveBlueprint(sketch).TryMakeMinified();
            if (IsWreck)
                fakeMover.shipRoot = Buildings.First();
            else
                fakeMover.shipRoot = Core;
            fakeMover.shipRotNum = rotb;
            fakeMover.bottomLeftPos = lowestCorner;
            ShipInteriorMod2.shipOriginMap = Map;
            fakeMover.originMap = Map;
            fakeMover.targetMap = targetMap;
            fakeMover.atmospheric = atmospheric;
            fakeMover.Position = fakeMover.shipRoot.Position;
            fakeMover.SpawnSetup(targetMap, false);
            List<object> selected = new List<object>();
            foreach (object ob in Find.Selector.SelectedObjects)
                selected.Add(ob);
            foreach (object ob in selected)
                Find.Selector.Deselect(ob);
            Current.Game.CurrentMap = targetMap;
            Find.Selector.Select(fakeMover);
            InstallationDesignatorDatabase.DesignatorFor(ResourceBank.ThingDefOf.ShipMoveBlueprint).ProcessInput(null);
        }
        public IntVec3 LowestCorner(byte rotb, Map map)
        {
            IntVec3 lowestCorner = new IntVec3(int.MaxValue, 0, int.MaxValue);
            foreach (IntVec3 v in Area)
            {
                if (v.x < lowestCorner.x)
                    lowestCorner.x = v.x;
                if (v.z < lowestCorner.z)
                    lowestCorner.z = v.z;
            }
            if (rotb == 1)
            {
                int temp = lowestCorner.x;
                lowestCorner.x = map.Size.z - lowestCorner.z;
                lowestCorner.z = temp;
            }
            else if (rotb == 2)
            {
                lowestCorner.x = map.Size.x - lowestCorner.x;
                lowestCorner.z = map.Size.z - lowestCorner.z;
            }
            return lowestCorner;
        }
        public IntVec3 MaximumCorner()
        {
            IntVec3 maxCorner = new IntVec3(0, 0, 0);
            foreach (IntVec3 v in Area)
            {
                if (v.x > maxCorner.x)
                    maxCorner.x = v.x;
                if (v.z > maxCorner.z)
                    maxCorner.z = v.z;
            }
            return maxCorner;
        }
        public IntVec3 Size(out IntVec3 min)
        {
            min = new IntVec3(int.MaxValue, 0, int.MaxValue);
            IntVec3 max = new IntVec3(0, 0, 0);
            foreach (IntVec3 v in Area)
            {
                if (v.x < min.x)
                    min.x = v.x;
                else if (v.x > max.x)
                    max.x = v.x;
                if (v.z < min.z)
                    min.z = v.z;
                else if (v.z > max.z)
                    max.z = v.z;
            }
            IntVec3 size = new IntVec3(max.x - min.x, 0, max.z - min.z);
            Log.Message("Ship size: " + size);
            return size;
        }
        public IntVec3 CenterShipOnMap()
        {
            IntVec3 min;
            IntVec3 size = Size(out min);
            IntVec3 adj = new IntVec3(Map.Size.x / 2, 0, Map.Size.z / 2) - new IntVec3(size.x / 2, 0, size.z / 2);
            //Log.Message("Ship adj: " + adj);
            //Log.Message("Ship pos: " + min);
            //Log.Message("Ship center: " + (adj - min));
            return adj - min;
        }
        public IEnumerable<Building> OuterNonShipWalls()
        {
            foreach (Building b in Buildings.Where(b => !Parts.Contains(b) && b.def.passability == Traversability.Impassable && (b.def.Size.x == 1 || b.def.Size.z == 1)))
            {
                bool air = false;
                bool vac = false;
                foreach (IntVec3 v in GenAdj.CellsAdjacentCardinal(b.Position, Rot4.North, IntVec2.One))
                {
                    Room room = v.GetRoom(Map);
                    if (room == null)
                        continue;
                    if (room.TouchesMapEdge)
                        vac = true;
                    else if (room.ProperRoom && room.OpenRoofCount == 0)
                        air = true;

                    if (air && vac)
                    {
                        yield return b;
                        break;
                    }
                }
            }
        }
        public HashSet<IntVec3> OuterCells()
        {
            HashSet<IntVec3> cells = new HashSet<IntVec3>();
            foreach (IntVec3 vec in Area)
            {
                foreach (IntVec3 v in GenAdj.CellsAdjacentCardinal(vec, Rot4.North, IntVec2.One).Where(v => !Area.Contains(v)))
                {
                    Room room = v.GetRoom(Map);
                    if (room != null && room.TouchesMapEdge)
                        cells.Add(vec);
                }
            }
            return cells;
        }
        public HashSet<IntVec3> BorderCells()
        {
            HashSet<IntVec3> cells = new HashSet<IntVec3>();
            foreach (IntVec3 vec in Area)
            {
                foreach (IntVec3 v in GenAdj.CellsAdjacentCardinal(vec, Rot4.North, IntVec2.One).Where(v => !Area.Contains(v)))
                {
                    Room room = v.GetRoom(Map);
                    if (room != null && room.TouchesMapEdge)
                        cells.Add(v);
                }
            }
            return cells;
        }

        public float WeBeCrashing = 0;
        public void CrashShip()
        {
            foreach (IntVec3 c in OuterCells())
            {
                if (Rand.Chance(WeBeCrashing / 2))
                    GenExplosion.DoExplosion(c, Map, Rand.Range(3.9f, 7.9f), DamageDefOf.Bomb, null, 500);
            }
            foreach (IntVec3 c in Area)
            {
                if (Rand.Chance(WeBeCrashing / 4))
                    GenExplosion.DoExplosion(c, Map, Rand.Range(1.9f, 4.9f), DamageDefOf.Bomb, null, 50);
            }
            //pawns
            foreach (Pawn p in PawnsOnShip)
            {
                int chance = Rand.RangeInclusive(1, 3);
                if (Rand.Chance(WeBeCrashing) && chance == 1)
                    HealthUtility.DamageLegsUntilIncapableOfMoving(p);
                else if (Rand.Chance(WeBeCrashing) && chance == 2)
                    HealthUtility.DamageUntilDowned(p);
            }
            WeBeCrashing = 0;
        }
        //AI
        public void PurgeCheck()
        {
            if (!HeatPurges.Any(purge => purge.purging)) //heatpurge - only toggle when not purging
            {
                var myNet = Core.heatComp.myNet;
                if (HeatPurges.Any(purge => purge.fuelComp.FuelPercentOfMax > 0.2f) && Core != null && myNet != null && myNet.RatioInNetwork > 0.7f) //start purge
                {
                    foreach (CompShipHeatPurge purge in HeatPurges)
                    {
                        purge.StartPurge();
                    }
                }
                else
                {
                    if (Shields.Any(shield => shield.shutDown)) //repower shields
                    {
                        foreach (var shield in Shields)
                        {
                            if (shield.flickComp == null)
                                continue;
                            shield.flickComp.SwitchIsOn = true;
                        }
                    }
                    if (myNet != null && myNet.RatioInNetwork > 0.85f && !myNet.venting)
                        myNet.StartVent();
                }
            }
        }
        //cache
        public IntVec3 BridgeKillVec = IntVec3.Invalid; //system is stupid but here we are
        public bool LastBridgeDied = false; //as above, prevents checking for detach until ship is moved
        public bool PathDirty = true; //unused //td
        public int LastSafePath = -1; //in combat the lowest path -1 that sufered damage
        public List<HashSet<IntVec3>> DetachedShipAreas = new List<HashSet<IntVec3>>();
        public void RebuildCache(Building origin, HashSet<IntVec3> exclude = null) //full rebuild, on load, merge
        {
            if (origin == null || origin.Destroyed)
            {
                Log.Error(map + " SOS2 ship recache: tried merging to null or destroyed origin");
                return;
            }
            Map = origin.Map;
            Index = origin.thingIDNumber;
            int path = -1;
            if (origin is Building_ShipBridge core)
            {
                Core = core;
                name = core.ShipName;
                Core.ShipIndex = Index;
                path = 0;
            }

            HashSet<IntVec3> cellsTodo = new HashSet<IntVec3>();
            HashSet<IntVec3> cellsDone = new HashSet<IntVec3>();
            if (exclude != null)
                cellsDone.AddRange(exclude);
            cellsTodo.AddRange(origin.OccupiedRect());

            //find cells cardinal to all prev.pos, exclude prev.pos, if found part, set corePath to i, shipIndex to core.shipIndex, set corePath
            while (cellsTodo.Count > 0)
            {
                List<IntVec3> current = cellsTodo.ToList();
                foreach (IntVec3 vec in current) //do all of the current corePath
                {
                    mapComp.MapShipCells[vec] = new Tuple<int, int>(Index, path); //add new vec, index, corepath
                    foreach (Thing t in vec.GetThingList(Map))
                    {
                        if (t is Building b)
                        {
                            AddToCache(b);
                        }
                    }
                    cellsTodo.Remove(vec);
                    cellsDone.Add(vec);
                }
                foreach (IntVec3 vec in current) //find next set cardinal to all cellsDone, exclude cellsDone
                {
                    cellsTodo.AddRange(GenAdj.CellsAdjacentCardinal(vec, Rot4.North, IntVec2.One).Where(v => !cellsDone.Contains(v) && mapComp.MapShipCells.ContainsKey(v)));
                }
                if (path > -1)
                    path++;
                //Log.Message("parts at i: "+ current.Count + "/" + i);
            }
            LastSafePath = path;
            PathDirty = false;
            Log.Message("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " Rebuilt cache, Parts: " + Parts.Count + " Buildings: " + Buildings.Count + " Bridges: " + Bridges.Count + " Area: " + Area.Count + " Core: " + Core + " Name: " + Name + " path max: " + LastSafePath);
        }
        public void AddToCache(Building b)
        {
            if (Buildings.Add(b))
            {
                BuildingCount++;
                if (b.def.rotatable == false && b.def.size.x != b.def.size.z)
                {
                    BuildingsNonRot.Add(b);
                }
                var part = b.TryGetComp<CompSoShipPart>();
                if (part != null)
                {
                    if (b.def.building.shipPart)
                    {
                        Parts.Add(b);
                        foreach (IntVec3 v in GenAdj.CellsOccupiedBy(b))
                        {
                            Area.Add(v);
                        }
                        if (part.Props.isPlating)
                        {
                            Mass += 1;
                            return;
                        }
                        else if (b.TryGetComp<CompEngineTrail>() != null)
                        {
                            var refuelable = b.TryGetComp<CompRefuelable>();
                            ThrustRaw += b.TryGetComp<CompEngineTrail>().Thrust;
                            if (refuelable != null)
                            {
                                MaxTakeoff += refuelable.Props.fuelCapacity;
                                if (refuelable.Props.fuelFilter.AllowedThingDefs.Contains(ResourceBank.ThingDefOf.ShuttleFuelPods))
                                    MaxTakeoff += refuelable.Props.fuelCapacity;
                            }
                            EngineMass += b.def.Size.Area * 60;
                            Engines.Add(b.TryGetComp<CompEngineTrail>());
                        }
                        else if (b.TryGetComp<CompRCSThruster>() != null)
                            RCSs.Add(b.GetComp<CompRCSThruster>());
                    }
                    else
                    {
                        if (b.TryGetComp<CompCryptoLaunchable>() != null)
                            Pods.Add(b.GetComp<CompCryptoLaunchable>());
                        else if (b is Building_ShipBridge bridge)
                        {
                            Bridges.Add(bridge);
                            if (b.TryGetComp<CompBuildingConsciousness>() != null)
                                AICores.Add(bridge);
                            if (IsWreck) //bridge placed on wreck, repath
                            {
                                Core = bridge;
                                if (name == null)
                                    name = bridge.ShipName;
                                RebuildCorePath();
                            }
                            bridge.ShipIndex = Index;
                            bridge.ShipName = Name;
                        }
                        else if (b is Building_ShipAdvSensor sensor)
                            Sensors.Add(sensor);
                        else if (b.TryGetComp<CompHullFoamDistributor>() != null)
                            FoamDistributors.Add(b.GetComp<CompHullFoamDistributor>());
                        else if (b.TryGetComp<CompShipLifeSupport>() != null)
                            LifeSupports.Add(b.GetComp<CompShipLifeSupport>());
                    }
                }
                var heatComp = b.TryGetComp<CompShipHeat>();
                if (heatComp != null)
                {
                    ThreatRaw += heatComp.Props.threat;
                    if (b is Building_ShipTurret turret)
                    {
                        Turrets.Add(turret);
                        if (turret.spinalComp != null)
                            turret.SpinalRecalc();

                        int threat = heatComp.Props.threat;
                        if (heatComp.Props.maxRange > 150) //long
                        {
                            ThreatPerSegment[0] += threat / 6f;
                            ThreatPerSegment[1] += threat / 4f;
                            ThreatPerSegment[2] += threat / 2f;
                            ThreatPerSegment[3] += threat;
                        }
                        else if (heatComp.Props.maxRange > 100) //med
                        {
                            ThreatPerSegment[0] += threat / 4f;
                            ThreatPerSegment[1] += threat / 2f;
                            ThreatPerSegment[2] += threat;
                        }
                        else if (heatComp.Props.maxRange > 50) //short
                        {
                            ThreatPerSegment[0] += threat / 2f;
                            ThreatPerSegment[1] += threat;
                        }
                        else //cqc
                            ThreatPerSegment[0] += threat;
                    }
                    else if (heatComp is CompShipHeatPurge purge)
                    {
                        HeatPurges.Add(purge);
                    }
                    else if (heatComp is CompShipCombatShield shield)
                        Shields.Add(shield);
                }
                else if (b.def == ResourceBank.ThingDefOf.ShipSpinalAmplifier)
                    ThreatRaw += 5;
                Mass += b.def.Size.x * b.def.Size.z * 3;
            }
        }
        public void RemoveFromCache(Building b, DestroyMode mode)
        {
            if (Buildings.Remove(b))
            {
                BuildingCount--;
                if (mapComp.ShipMapState == ShipMapState.inCombat && !IsWreck && b.def.blueprintDef != null && (mode == DestroyMode.KillFinalize || mode == DestroyMode.KillFinalizeLeavingsOnly))
                {
                    BuildingsDestroyed.Add(new Tuple<BuildableDef, IntVec3, Rot4>(b.def, b.Position, b.Rotation));
                }
                if (BuildingsNonRot.Contains(b))
                {
                    BuildingsNonRot.Remove(b);
                }
                var part = b.TryGetComp<CompSoShipPart>();
                if (part != null)
                {
                    if (b.def.building.shipPart)
                    {
                        Parts.Remove(b);
                        if (part.Props.isPlating)
                        {
                            Mass -= 1;
                            return;
                        }
                        else if (b.TryGetComp<CompEngineTrail>() != null)
                        {
                            var refuelable = b.TryGetComp<CompRefuelable>();
                            ThrustRaw -= b.TryGetComp<CompEngineTrail>().Thrust;
                            if (refuelable != null)
                            {
                                MaxTakeoff -= refuelable.Props.fuelCapacity;
                                if (refuelable.Props.fuelFilter.AllowedThingDefs.Contains(ResourceBank.ThingDefOf.ShuttleFuelPods))
                                    MaxTakeoff -= refuelable.Props.fuelCapacity;
                            }
                            EngineMass -= b.def.Size.Area * 60;
                            Engines.Remove(b.TryGetComp<CompEngineTrail>());
                        }
                        else if (b.TryGetComp<CompRCSThruster>() != null)
                            RCSs.Remove(b.GetComp<CompRCSThruster>());
                    }
                    else
                    {
                        if (b.TryGetComp<CompCryptoLaunchable>() != null)
                            Pods.Remove(b.GetComp<CompCryptoLaunchable>());
                        else if (b is Building_ShipBridge bridge)
                        {
                            Bridges.Remove(bridge);
                            if (b.TryGetComp<CompBuildingConsciousness>() != null)
                                AICores.Remove(bridge);
                            if (bridge == Core)
                                ReplaceCore();
                            //bridge.ShipIndex = -1;
                            //bridge.ShipName = "destroyed ship";
                        }
                        else if (b is Building_ShipAdvSensor sensor)
                            Sensors.Remove(sensor);
                        else if (b.TryGetComp<CompHullFoamDistributor>() != null)
                            FoamDistributors.Remove(b.GetComp<CompHullFoamDistributor>());
                        else if (b.TryGetComp<CompShipLifeSupport>() != null)
                            LifeSupports.Remove(b.GetComp<CompShipLifeSupport>());
                    }
                }
                var heatComp = b.TryGetComp<CompShipHeat>();
                if (heatComp != null)
                {
                    ThreatRaw -= heatComp.Props.threat;
                    if (b is Building_ShipTurret turret)
                    {
                        Turrets.Remove(turret);
                        int threat = heatComp.Props.threat;
                        if (heatComp.Props.maxRange > 150) //long
                        {
                            ThreatPerSegment[0] -= threat / 6f;
                            ThreatPerSegment[1] -= threat / 4f;
                            ThreatPerSegment[2] -= threat / 2f;
                            ThreatPerSegment[3] -= threat;
                        }
                        else if (heatComp.Props.maxRange > 100) //med
                        {
                            ThreatPerSegment[0] -= threat / 4f;
                            ThreatPerSegment[1] -= threat / 2f;
                            ThreatPerSegment[2] -= threat;
                        }
                        else if (heatComp.Props.maxRange > 50) //short
                        {
                            ThreatPerSegment[0] -= threat / 2f;
                            ThreatPerSegment[1] -= threat;
                        }
                        else //cqc
                            ThreatPerSegment[0] -= threat;
                    }
                    else if (heatComp is CompShipHeatPurge purge)
                    {
                        HeatPurges.Remove(purge);
                    }
                    else if (heatComp is CompShipCombatShield shield)
                        Shields.Remove(shield);
                }
                else if (b.def == ResourceBank.ThingDefOf.ShipSpinalAmplifier)
                    ThreatRaw -= 5;
                Mass -= b.def.Size.x * b.def.Size.z * 3;
            }
        }
        public bool ReplaceCore() //before despawn try find replacer for core
        {
            List<Building_ShipBridge> bridges = Bridges.Where(b => b != Core && !b.Destroyed).ToList();
            if (bridges.Any())
            {
                //core died but replacer exists
                Log.Message("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " ReplaceCore: Replaced primary bridge.");

                Core = BestCoreReplacer(bridges);
                RebuildCorePath();
                return true;
            }
            Log.Message("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " ReplaceCore: Has 0 cores remaining.");
            Core = null;
            ResetCorePath();
            if (mapComp.ShipMapState == ShipMapState.inCombat) //turn into wreck but do not float it
                LastBridgeDied = true;

            if (BridgeKillVec != IntVec3.Invalid)
            {
                Area.Remove(BridgeKillVec);
                mapComp.MapShipCells.Remove(BridgeKillVec);
            }

            if (mapComp.ShipMapState == ShipMapState.inCombat) //if last ship end combat else move to grave
            {
                if (mapComp.ShipsOnMapNew.Values.Any(s => !s.IsWreck))
                    mapComp.ShipsToMove.Add(Index);
                else
                    mapComp.EndBattle(Map, false);
            }
            return false;
        }
        private Building_ShipBridge BestCoreReplacer(List<Building_ShipBridge> bridges)
        {
            //non core, non destroyed bridge closeset to engines
            if (Engines.NullOrEmpty() || bridges.Count() == 1)
                return bridges.First();
            IntVec3 closest = Map.Size;
            Building_ShipBridge best = null;
            foreach (Building_ShipBridge b in bridges)
            {
                if (closest.LengthManhattan > (b.Position - Engines.First().parent.Position).LengthManhattan)
                    best = b;
            }
            return best;
        }
        public void ResetCorePath()
        {
            LastSafePath = -1;
            foreach (IntVec3 vec in Area)
            {
                mapComp.MapShipCells[vec] = new Tuple<int, int>(Index, -1);
            }
        }
        public void RebuildCorePath() //run before combat if PathDirty and in combat after bridge replaced
        {
            var cellsTodo = new HashSet<IntVec3>();
            var cellsDone = new HashSet<IntVec3>();
            cellsTodo.AddRange(Core.OccupiedRect());
            int mergeToIndex = mapComp.MapShipCells[Core.Position].Item1;

            //find parts cardinal to all prev.pos, exclude prev.pos
            int path = 0;
            while (cellsTodo.Count > 0)
            {
                List<IntVec3> current = cellsTodo.ToList();
                foreach (IntVec3 vec in current) //do all of the current corePath
                {
                    mapComp.MapShipCells[vec] = new Tuple<int, int>(mergeToIndex, path);
                    cellsTodo.Remove(vec);
                    cellsDone.Add(vec);
                }
                foreach (IntVec3 vec in current) //find next set cardinal to all cellsDone, exclude cellsDone
                {
                    cellsTodo.AddRange(GenAdj.CellsAdjacentCardinal(vec, Rot4.North, IntVec2.One).Where(v => !cellsDone.Contains(v) && Area.Contains(v)));
                }
                path++;
            }
            LastSafePath = path;
            PathDirty = false;
            Log.Message("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " Rebuilt corePath at " + Core.Position + " path max: " + LastSafePath);
        }
        //detach
        public void Tick()
        {
            foreach (HashSet<IntVec3> area in DetachedShipAreas)
            {
                FloatAndDestroy(area);
            }
            DetachedShipAreas.Clear();
            if (ForceRePower > 0)
            {
                ForceRePowerOnTick();
                if (WeBeCrashing > 0) //bad juju
                {
                    CrashShip();
                }
            }
        }
        public void CheckForDetach(List<IntVec3> areaDestroyed)
        {
            if (areaDestroyed.Count == 1) //simple solutions for 1x1 detach:
            {
                //0 cells attached to areaDestroyed: remove this ship from cache on mapcomp
                //1: no detach
                List<IntVec3> adjCells = GenAdj.CellsAdjacentCardinal(areaDestroyed.First(), Rot4.North, IntVec2.One).Where(v => Area.Contains(v)).ToList();
                if (adjCells.Count < 2)
                    return;
            }
            //now it gets complicated and slower
            //start cells can be any amount and arrangement
            //find cells around
            //for each try to path back to LastSafePath - 1 or lowest index cell, if not possible detach each set separately

            int pathTo = int.MaxValue; //lowest path in startCells
            IntVec3 first = IntVec3.Invalid; //path to this cell
            HashSet<IntVec3> startCells = new HashSet<IntVec3>(); //cells areaDestroyed
            foreach (IntVec3 vec in areaDestroyed) //find first still attached cell around detach area
            {
                foreach (IntVec3 v in GenAdj.CellsAdjacentCardinal(vec, Rot4.North, IntVec2.One).Where(v => !areaDestroyed.Contains(v) && Area.Contains(v)))// && mapComp.MapShipCells[v].Item2 != 0))
                {
                    startCells.Add(v);
                    int vecPath = mapComp.MapShipCells[v].Item2;
                    if (vecPath < pathTo)
                    {
                        pathTo = vecPath;
                        first = v;
                    }
                }
            }
            //log
            /*startCells?.Remove(first);
            string str2 = "SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " CheckForDetach: Pathing to path: " + (LastSafePath - 1) + " or to cell: " + first + " with " + startCells.Count + " cells: ";
            foreach (IntVec3 vec in startCells)
                str2 += vec;
            Log.Warning(str2);
            str2 = "";*/
            //log

            //HashSet<IntVec3> cellsDone = new HashSet<IntVec3>(); //cells that were checked
            HashSet<IntVec3> cellsAttached = new HashSet<IntVec3> { first }; //cells that were checked and are attached
            foreach (IntVec3 setStartCell in startCells)
            {
                if (mapComp.MapShipCells[setStartCell]?.Item2 < LastSafePath)
                {
                    cellsAttached.Add(setStartCell);
                }
                if (cellsAttached.Contains(setStartCell)) //skip already checked cells
                {
                    continue;
                }
                bool detach = true;
                HashSet<IntVec3> cellsDoneInSet = new HashSet<IntVec3>(); //if detach these form new ship else add to attached
                List<IntVec3> cellsToCheckInSet = new List<IntVec3> { setStartCell };
                while (cellsToCheckInSet.Any())
                {
                    IntVec3 current = cellsToCheckInSet.First();
                    cellsToCheckInSet.Remove(current);
                    if (cellsDoneInSet.Add(current)) //extend search range
                    {
                        foreach (IntVec3 v in GenAdj.CellsAdjacentCardinal(current, Rot4.North, IntVec2.One).Where(v => Area.Contains(v) && !areaDestroyed.Contains(v))) //skip non ship, destroyed tiles
                        {
                            //if part with lower corePath found or next to an already attached and checked this set is attached
                            if (cellsAttached.Contains(v) || mapComp.MapShipCells[v].Item2 < LastSafePath)
                            {
                                cellsAttached.AddRange(cellsDoneInSet);
                                detach = false;
                                break;
                            }
                            cellsToCheckInSet.Add(v);
                        }
                    }
                    if (!detach)
                        break;
                }
                if (detach)
                {
                    //Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    string str = "SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " Could not path to: " + (LastSafePath - 1) + " Detaching " + cellsDoneInSet.Count + " cells: ";
                    foreach (IntVec3 vec in cellsDoneInSet)
                        str += vec;
                    Log.Warning(str);

                    Detach(cellsDoneInSet);
                }
                else
                {

                }
            }
        }
        public void Detach(HashSet<IntVec3> detachArea) //if bridge found detach as new ship else as wreck
        {
            Building newCore = null;
            DestroyMode mode = DestroyMode.Vanish;
            if (mapComp.ShipMapState == ShipMapState.inCombat)
                mode = DestroyMode.KillFinalize;

            foreach (IntVec3 vec in detachArea) //clear area, remove buildings, try to find bridge on detached
            {
                foreach (Thing t in vec.GetThingList(Map))
                {
                    if (t is Building b)
                    {
                        if (b is Building_ShipBridge bridge)
                            newCore = bridge;
                        RemoveFromCache(b, mode);
                    }
                }
                Area.Remove(vec);
                mapComp.MapShipCells[vec] = new Tuple<int, int>(-1, -1);
            }
            if (newCore == null) //wreck
            {
                if (mapComp.ShipMapState == ShipMapState.inCombat || mapComp.ShipMapState == ShipMapState.inTransit) //float wrecks except in case the last bridge was destroyed
                {
                    DetachedShipAreas.Add(detachArea);
                    ShipInteriorMod2.AirlockBugFlag = true;
                    foreach (IntVec3 vec in detachArea)
                    {
                        mapComp.MapShipCells.Remove(vec);
                    }
                    ShipInteriorMod2.AirlockBugFlag = false;
                    //remove this ship if nothing remains
                    if (!Area.Any())
                    {
                        Log.Message("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " Area was empty, removing ship");
                        mapComp.ShipsOnMapNew.Remove(Index);
                    }
                    return;
                }
                newCore = (Building)detachArea.First().GetThingList(Map).First(t => t.TryGetComp<CompSoShipPart>() != null);
            }
            Log.Message("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " Detach new ship/wreck with: " + newCore);
            if (mapComp.ShipsOnMapNew.ContainsKey(newCore.thingIDNumber))
            {
                //Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                int newKey = mapComp.ShipsOnMapNew.Keys.Max() + 1000;
                Log.Warning("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " Detach error, shipID " + newCore.thingIDNumber + " already exits! Using fallback index: " + newKey + ", pausing game. Recheck ship area with infestation overlay. If not correct - save and reload!");
                newCore.thingIDNumber = newKey;
            }
            //make new ship
            mapComp.ShipsOnMapNew.Add(newCore.thingIDNumber, new SoShipCache());
            mapComp.ShipsOnMapNew[newCore.thingIDNumber].RebuildCache(newCore);
            if (mapComp.ShipMapState == ShipMapState.inCombat)
            {
                if (mapComp.HasShipMapAI)
                    mapComp.hasAnyPartDetached = true;
                mapComp.ShipsToMove.Add(newCore.thingIDNumber);
            }
            //remove this ship if nothing remains
            if (!Area.Any())
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Log.Message("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " Area is empty!");
                //mapComp.ShipsOnMapNew.Remove(Index);
            }
        }
        private void FloatAndDestroy(HashSet<IntVec3> detachArea)
        {
            //float wreck and destroy
            ShipInteriorMod2.AirlockBugFlag = true;
            HashSet<Thing> toDestroy = new HashSet<Thing>();
            HashSet<Thing> toReplace = new HashSet<Thing>();
            HashSet<Pawn> toKill = new HashSet<Pawn>();
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;

            foreach (IntVec3 vec in detachArea)
            {
                //mapComp.MapShipCells.Remove(vec);
                //Log.Message("Detaching location " + at);
                foreach (Thing t in vec.GetThingList(Map).Where(t => t.def.destroyable && !t.Destroyed))
                {
                    if (t is Pawn p)
                    {
                        if (p.Faction != Faction.OfPlayer && Rand.Chance(0.75f))
                        {
                            toKill.Add(p);
                            toDestroy.Add(t);
                        }
                    }
                    else if (!(t is Blueprint))
                        toDestroy.Add(t);
                    if (t is Building b && b.def.building.shipPart)
                    {
                        toReplace.Add(b);
                    }
                }
                if (vec.x < minX)
                    minX = vec.x;
                if (vec.x > maxX)
                    maxX = vec.x;
                if (vec.z < minZ)
                    minZ = vec.z;
                if (vec.z > maxZ)
                    maxZ = vec.z;
            }
            if (toReplace.Any()) //any shipPart, make a floating wreck
            {
                DetachedShipPart part = (DetachedShipPart)ThingMaker.MakeThing(ResourceBank.ThingDefOf.DetachedShipPart);
                part.Position = new IntVec3(minX, 0, minZ);
                part.xSize = maxX - minX + 1;
                part.zSize = maxZ - minZ + 1;
                part.wreckage = new byte[part.xSize, part.zSize];
                foreach (Thing t in toReplace)
                {
                    var comp = t.TryGetComp<CompSoShipPart>();
                    if (comp.Props.isHull)
                        part.wreckage[t.Position.x - minX, t.Position.z - minZ] = 1;
                    else if (comp.Props.isPlating)
                        part.wreckage[t.Position.x - minX, t.Position.z - minZ] = 2;
                }
                Log.Message("SOS2: ".Colorize(Color.cyan) + map + " Ship ".Colorize(Color.green) + Index + " Spawning floating part at: " + part.Position);
                part.SpawnSetup(Map, false);
            }
            foreach (Pawn p in toKill)
            {
                p.Kill(new DamageInfo(DamageDefOf.Bomb, 100f));
            }
            foreach (Thing t in toDestroy)
            {
                if (t.def.destroyable && !t.Destroyed)
                {
                    try
                    {
                        t.Destroy(DestroyMode.Vanish);
                    }
                    catch (Exception e)
                    {
                        var sb = new StringBuilder();
                        sb.AppendFormat("Wrecking Error on {0}: {1}\n", t.def.label, e.Message);
                        sb.AppendLine(e.StackTrace);
                        Log.Warning(sb.ToString());
                    }
                }
            }
            foreach (IntVec3 c in detachArea)
            {
                Map.terrainGrid.RemoveTopLayer(c, false);
                Map.roofGrid.SetRoof(c, null);
            }
            ShipInteriorMod2.AirlockBugFlag = false;
        }
    }
}