﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimWorld
{
    [StaticConstructorOnStartup]
    class CompArcholifeCosmetics : ThingComp
    {
        public int whichVersion = 0;

        public static Dictionary<string, Graphic[]> graphics = new Dictionary<string, Graphic[]>();

        public static Dictionary<ThingDef, CompProperties_ArcholifeCosmetics> GraphicsToResolve = new Dictionary<ThingDef, CompProperties_ArcholifeCosmetics>();

        public CompProperties_ArcholifeCosmetics Props
        {
            get
            {
                return (CompProperties_ArcholifeCosmetics)this.props;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref whichVersion, "version");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Command_Action setVersion = new Command_Action
            {
                action = delegate
                {
                    List<FloatMenuOption> list = new List<FloatMenuOption>();
                    for (int index = 0; index < Props.names.Count; index++)
                    {
                        list.Add(new FloatMenuOption(Props.names[index], delegate { ChangeAnimalGraphics(parent, Props, this, true); }));
                    }
                    Find.WindowStack.Add(new FloatMenuWithCallback(list));
                },
                icon = (Texture2D)graphics[parent.def.defName][whichVersion].MatSouth.mainTexture,
                defaultLabel = TranslatorFormattedStringExtensions.Translate("ArcholifeChangeSkin"),
                defaultDesc = TranslatorFormattedStringExtensions.Translate("ArcholifeChangeSkinDesc")
            };
            List<Gizmo> toReturn = new List<Gizmo>();
            toReturn.Add(setVersion);
            return toReturn;
        }

        public static void ChangeAnimalGraphics(ThingWithComps parent, CompProperties_ArcholifeCosmetics Props, CompArcholifeCosmetics cosmetics, bool triggeredByChange = false)
        {
            if (triggeredByChange)
                cosmetics.whichVersion = FloatMenuWithCallback.whichOptionWasChosen;
            Pawn pawn = (Pawn)parent;
            Vector2 drawSize;
            if (pawn.Drawer.renderer.graphics.nakedGraphic==null)
                drawSize = pawn.ageTracker.CurKindLifeStage.bodyGraphicData.Graphic.drawSize;
            else
                drawSize = pawn.Drawer.renderer.graphics.nakedGraphic.drawSize;
            pawn.Drawer.renderer.graphics.nakedGraphic = graphics[parent.def.defName][cosmetics.whichVersion];
            pawn.Drawer.renderer.graphics.nakedGraphic.drawSize = drawSize;
        }
    }
}
