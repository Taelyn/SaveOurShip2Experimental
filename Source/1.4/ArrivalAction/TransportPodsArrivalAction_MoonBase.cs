﻿using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimWorld
{
    class TransportPodsArrivalAction_MoonBase : TransportPodsArrivalAction
    {
        MapParent map;

        public TransportPodsArrivalAction_MoonBase()
        {

        }

        public TransportPodsArrivalAction_MoonBase(MapParent moonMap)
        {
            map = moonMap;
        }

        public override void Arrived(List<ActiveDropPodInfo> pods, int tile)
        {
			Thing lookTarget = TransportPodsArrivalActionUtility.GetLookTarget(pods);
			Messages.Message("MessageTransportPodsArrived".Translate(), lookTarget, MessageTypeDefOf.TaskCompletion);
            // PawnsArrivalModeDefOf.EdgeDrop.Worker.TravelingTransportPodsArrived(pods, GetOrGenerateMapUtility.GetOrGenerateMap(map.Tile, new IntVec3(250, 1, 250), map.def));
            PawnsArrivalModeDefOf.EdgeDrop.Worker.TravelingTransportPodsArrived(pods, GetOrGenerateMapUtility.GetOrGenerateMap(map.Tile, new IntVec3(400, 1, 400), map.def));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look<MapParent>(ref map, "Map");
        }
    }
}
