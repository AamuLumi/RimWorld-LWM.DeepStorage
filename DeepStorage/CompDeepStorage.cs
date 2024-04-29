using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using System.Linq;
//using HarmonyLib;
//using System.Reflection;
//using System.Reflection.Emit; // for OpCodes in Harmony Transpiler
using UnityEngine;
using static LWM.DeepStorage.Utils.DBF; // trace utils


namespace LWM.DeepStorage
{
    public class CompDeepStorage : ThingComp, IExposable, IHoldMultipleThings.IHoldMultipleThings, IRenameable {
            public string label = "";
            public string RenamableLabel
            {
              get => this.buildingLabel.NullOrEmpty() ? this.BaseLabel : this.buildingLabel;
              set => this.label = value;
            }
        
            public string BaseLabel => this.parent.def.label.CapitalizeFirst();
        
            public string InspectLabel => this.RenamableLabel;
        public override IEnumerable<Gizmo> CompGetGizmosExtra() {
            foreach (Gizmo g in base.CompGetGizmosExtra()) {
                yield return g;
            }
            foreach (Gizmo g in DSStorageGroupUtility.GetDSStorageGizmos())
                yield return g;

            #if DEBUG
            yield return new Command_Toggle {
                defaultLabel="Use RClick Logic",
                defaultDesc="Toggle use of custom Right Click logic",
                isActive=(()=>Settings.useDeepStorageRightClickLogic),
                toggleAction=delegate() {
                    Settings.useDeepStorageRightClickLogic=!Settings.useDeepStorageRightClickLogic;
                }
            };
            yield return new Command_Action {
                defaultLabel="Items in Region",
                action=delegate() {
                    Log.Warning("ListerThings for "+parent+" (at region at position "+parent.Position+")");
                    foreach (var t in parent.Position.GetRegion(parent.Map).ListerThings
                             .ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.HaulableEver))) {
                        Log.Message("  "+t);
                    }
                }
            };
            #endif

            #if false
            yield return new Command_Action {
                defaultLabel="Y-=.1",
                action=delegate () {
                    y-=0.1f;
                    Messages.Message("Offset: "+y,MessageTypeDefOf.NeutralEvent);
                }
            };
            yield return new Command_Action {
                defaultLabel="Y+=.1",
                action=delegate () {
                    y+=0.1f;
                    Messages.Message("Offset: "+y,MessageTypeDefOf.NeutralEvent);
                }
            };
            #endif

            // I left this lovely testing code in - oops.
            //yield return new Command_Action
            //{
            //    defaultLabel = "Minus One",
            //    action=delegate ()
            //    {
            //        foreach (var cl in parent.GetSlotGroup().CellsList)
            //        {
            //            foreach (Thing t in parent.Map.thingGrid.ThingsAt(cl))
            //            {
            //                if (t.def.category == ThingCategory.Item)
            //                {
            //                    if (t.stackCount > 1)
            //                    {
            //                        Log.Warning("Lowering " + t.ToString());
            //                        t.stackCount--;
            //                    }
            //                }// item
            //            }// each thing
            //        }// each cell
            //    },// end action
            //};
        }

        public override string TransformLabel(string label) {
            if (this.buildingLabel=="") return label;
            return buildingLabel;
        }

        // If the player has renamed the item, show the old name (e.g., label)
        //   in the window so player can see "Oh, it's a masterwork uranium shelf" etc
        public override string CompInspectStringExtra() {
            if (this.buildingLabel=="") {
                return null;
            }
            // in case you could not tell, I was tired when I wrote this:
            string s=buildingLabel;
            buildingLabel="";
            string origLabel=parent.Label;
            buildingLabel=s;
            return origLabel;
        }

        public virtual int TimeStoringTakes(Map map, IntVec3 cell, Pawn pawn) {
            if (CdsProps.minTimeStoringTakes <0) {
                // Basic version
                return ((Properties)this.props).timeStoringTakes;
            }
            Thing thing=pawn?.carryTracker?.CarriedThing;
            if (thing==null) {
                Log.Error("LWM.DeepStorage: null CarriedThing");
                return 0;
            }
            // having a minTimeStoringTakes, adjusted:
            // TODO: additionTimeEachDef
            int t= CdsProps.minTimeStoringTakes;
            var l=map.thingGrid.ThingsListAtFast(cell).FindAll(x=>x.def.EverStorable(false));
            // Do I count storing thing as a separate def?
            bool thingToPlaceIsDifferentFromAnythingThere = l.Count > 0;
            // additional Time for Each Stack:
            for (int i=0; i<l.Count; i++) {
                t+= CdsProps.additionalTimeEachStack;
                if (CdsProps.additionalTimeEachDef>0 &&
                    l[i].CanStackWith(thing)) {
                    // some defs cannot stack with themselves (esp under other mods,
                    //   for example, common sense doesn't allow meals with and w/o
                    //   insect meat to stack)
                    // Note: As far as I know, this works for items with stack sizes of 1, too.
                    thingToPlaceIsDifferentFromAnythingThere=false;
                }
            }
            // additional Time for Each Def (really for each thing that doesn't stack)
            if (CdsProps.additionalTimeEachDef>0) {
                if (thingToPlaceIsDifferentFromAnythingThere) t+= CdsProps.additionalTimeEachDef;
                // l2=l mod CanStackWith()
                // That is, l2 is a maximal list of objects that cannot stack with each other from l.
                // That is, l2 is l with all things that can stack together reduced to one item.
                List<Thing> l2=new List<Thing>(l);
                int i=0;
                for (; i<l2.Count; i++) {
                    int j=i+1;
                    while (j<l2.Count) {
                        if (l2[i].CanStackWith(l2[j])) {
                            l2.RemoveAt(j);
                        } else {
                            j++;
                        }
                    }
                }
                // now l2 is prepared
                if (l2.Count > 1) {
                    t+=(CdsProps.additionalTimeEachDef*(l2.Count-1));
                }
            }
            // additional Time Stack Size
            if (Settings.storingTimeConsidersStackSize && CdsProps.additionalTimeStackSize>0f) {
                float factor=1f;
                if (thing.def.smallVolume || // if it's small (silver, gold)
                    (   // or on the list (compost for Fertile Fields?)
                        (!CdsProps.quickStoringItems.NullOrEmpty()) &&
                        CdsProps.quickStoringItems.Contains(thing.def)
                        )
                    ) {
                    factor=0.05f;
                }
                t+=(int)(CdsProps.additionalTimeStackSize *
                    pawn.carryTracker.CarriedThing.stackCount *
                    factor);
            }
            return t;
        } // end TimeStoringTakes

        public override void Initialize(CompProperties props) {
            base.Initialize(props);
            /*******  Initialize local variables                                      *******/
            //this.maxNumberStacks = CdsProps.maxNumberStacks; //null->use CdsProps!

            /*******  For only one limiting stat: (mass, or bulk for CombatExtended)  *******/
            if (((Properties)props).altStat != null) stat = ((Properties)props).altStat;
            if (((Properties)props).maxTotalMass > 0f) //for floating arithmetic, just to be safe
                limitingTotalFactorForCell = ((Properties)props).maxTotalMass + .0001f;
            if (((Properties)props).maxMassOfStoredItem > 0f)
                limitingFactorForItem = ((Properties)props).maxMassOfStoredItem + .0001f;
            /*******  Viable approach if anyone ever wants to limit storage based on >1 stat:
            if (((Properties)props).maxMassOfStoredItem > 0f) {
                statForStoredItem[0] = StatDefOf.Mass;
                maxStatOfStoredItem[0] = ((Properties)props).maxMassOfStoredItem;
            }
            if (((Properties)props).maxTotalMass > 0f) {
                statToTotal[0] = StatDefOf.Mass;
                maxTotalStat[0] = ((Properties)props).maxTotalMass;
            }
            */
        }
        // Tell the DSMapComponent that something has changed and any storage info for the
        //   unit may now be incorrect
        public void DirtyMapCache()
        {
            if (parent is Building_Storage && parent.Spawned)
            {
                MapComponentDS dsm = parent.Map.GetComponent<MapComponentDS>();
                foreach (var cell in (parent as Building_Storage).AllSlotCells())
                    dsm.DirtyCache(cell);
            }
        }

        // IMPORTANT NOTE: some of the following logic is in the patch for TryFindBestBetterStoreCellFor
        //   (ShouldRemoveFrom logic).  TODO: it should probably be here

        public virtual int CapacityToStoreThingAt(Thing thing, Map map, IntVec3 cell) {
            return map.GetComponent<MapComponentDS>().CapacityToStoreItemAt(this, thing, cell);
        }
        public virtual int CapacityToStoreThingAtDirect(Thing thing, Map map, IntVec3 cell) {
            Utils.Warn(CheckCapacity, "Checking Capacity to store "+thing.stackCount+thing+" at "
                       +(map?.ToString()??"NULL MAP")+" "+cell);
            int capacity = 0;
            /* First test, is it even light enough to go in this DS? */
            //      No rocket launchers in jewelry boxes?
            if (this.limitingFactorForItem > 0f) {
                if (thing.GetStatValue(this.stat) > this.limitingFactorForItem) {
                    Utils.Warn(CheckCapacity, "  Cannot store because "+stat+" of "
                               +thing.GetStatValue(stat)+" > limit of "+limitingFactorForItem);
                    return 0;
                }
            }
            float totalWeightStoredHere=0f;  //mass, or bulk, etc.

            List<Thing> list = map.thingGrid.ThingsListAt(cell);
            var stacksStoredHere=0;
            for (int i=0; i<list.Count;i++) {
                Thing thingInStorage = list[i];
                if (thingInStorage.def.EverStorable(false)) { // an "item" we care about
                    stacksStoredHere+=1;
                    Utils.Mess(CheckCapacity, "  Checking against "+thingInStorage.stackCount+thingInStorage);
                    if (this.limitingTotalFactorForCell > 0f) {
                        totalWeightStoredHere +=thingInStorage.GetStatValue(this.stat)*thingInStorage.stackCount;
                        Utils.Mess(CheckCapacity, "    "+stat+" increased to "+totalWeightStoredHere+ " / "+
                                   limitingTotalFactorForCell);
                        if (totalWeightStoredHere > this.limitingTotalFactorForCell &&
                            stacksStoredHere >= this.MinNumberStacks) {
                            Utils.Warn(CheckCapacity, "  "+thingInStorage.stackCount+thingInStorage+" already over mass!");
                            return 0;
                        }
                    }
                    if (thingInStorage==thing) {
                        Utils.Mess(CheckCapacity, "Found Item!");
                        if (stacksStoredHere > MaxNumberStacks) {
                            // It's over capacity :(
                            Utils.Warn(CheckCapacity, "  But all stacks already taken: "+(stacksStoredHere-1)+" / "+MaxNumberStacks);
                            return 0;
                        }
                        return thing.stackCount;//todo: wrong? mass?
                    }
                    if (thingInStorage.CanStackWith(thing)) {
                        if (thingInStorage.stackCount < thingInStorage.def.stackLimit) {
                            Utils.Warn(CheckCapacity, "  has stackCount of only "+thingInStorage.stackCount+
                                       " so it can hold more");
                            capacity += thingInStorage.def.stackLimit - thingInStorage.stackCount;
                        }
                    }
                    //if (stacksStoredHere >= maxNumberStacks) break; // may be more stacks with empty space?
                } // item
            } // end of cell's contents...
            // Count empty spaces:
            if (this.limitingTotalFactorForCell > 0f) {
                if (stacksStoredHere <= MinNumberStacks) {
                    capacity+=(MinNumberStacks-stacksStoredHere)*thing.def.stackLimit;
                    Utils.Mess(CheckCapacity, "Adding capacity for minNumberStacks: "+stacksStoredHere+"/"+
                               MinNumberStacks+" - capacity now: "+capacity);
                    totalWeightStoredHere+=(MinNumberStacks-stacksStoredHere)*thing.GetStatValue(this.stat)*thing.def.stackLimit;
                    stacksStoredHere=MinNumberStacks;
                }
                // reuse variable totalWeightStoredHere as totalWeightStorableHere
                totalWeightStoredHere = this.limitingTotalFactorForCell - totalWeightStoredHere;
                if (totalWeightStoredHere <= 0f) {
                    Utils.Mess(CheckCapacity, "No storage available by mass: above total by "+totalWeightStoredHere);
                    if (stacksStoredHere > this.MinNumberStacks) return 0;
                    Utils.Mess(CheckCapacity, "  but minNumberStacks not passed, so available capacity is "+capacity);
                    return capacity;
                }
                if (stacksStoredHere < MaxNumberStacks) {
                    capacity+=Math.Min(
                        ((MaxNumberStacks-stacksStoredHere)*thing.def.stackLimit),   // capacity available by count
                        ((int)(totalWeightStoredHere/thing.GetStatValue(this.stat))) // capacity available by weight
                        );
                }
                Utils.Mess(CheckCapacity, "Total available mass for additional storage: "
                           +totalWeightStoredHere+"; final capacity: "+capacity);
                return capacity;
            }
            if (this.MaxNumberStacks > stacksStoredHere) {
                Utils.Mess(CheckCapacity, ""+(MaxNumberStacks-stacksStoredHere)+" free stacks: adding to available capacity");
                capacity+=(this.MaxNumberStacks-stacksStoredHere)*thing.def.stackLimit;
            }
            Utils.Mess(CheckCapacity, "Available capacity: "+capacity);
            return capacity;
        }
        /************************** IHoldMultipleThings interface ************************/
        /* For compatibility with Mehni's PickUpAndHaul                                  */
        /* (Note: without mass-limits factored in, 1.4's vanilla code handles everything *
         *        without deep storage having to do a thing!                             */
        // NOTE: Maybe for Multiplayer makes sense to patch this?
        public bool CapacityAt(Thing thing, IntVec3 cell, Map map, out int capacity) {
            capacity = map.GetComponent<MapComponentDS>().CapacityToStoreItemAt(this, thing, cell);
//            capacity = this.CapacityToStoreThingAt(thing, map, cell);
            if (capacity > 0) return true;
            return false;
        }
        // (I still think this is a stupid name)
        public bool StackableAt(Thing thing, IntVec3 cell, Map map) {
            return map.GetComponent<MapComponentDS>().CanStoreItemAt(this, thing, cell);
            //return this.CapacityToStoreThingAt(thing,map,cell) > 0;
        }
        /*********************************************************************************/
        public override void PostExposeData() { // why not call it "ExposeData" anyway?
            Scribe_Values.Look<string>(ref buildingLabel, "LWM_DS_DSU_label", "", false);
            Scribe_Values.Look<int?>(ref maxNumberStacks, "LWM_DS_DSU_maxNumberStacks", null, false);
        }
        public void ExposeData() // Because ExposeData is a special IExposable thing!
        {
            PostExposeData();
        }
        /********************** properties **********************/
        public Properties CdsProps  // b/c I hate typing :p
        {
            get {
                return ((Properties)this.props);
            }
        }
        public int MinNumberStacks
        {
            get {
                return ((Properties)this.props).minNumberStacks;
            }
        }
        public int MaxNumberStacks
        {
            get {
                return maxNumberStacks ?? ((Properties)this.props).maxNumberStacks;
            }
            set {
                int? newValue = (value == CdsProps.maxNumberStacks ? (int?)null : (int?)value);
                if ((parent is IStorageGroupMember storage) && storage.Group != null)
                {
                    foreach (var c in DSStorageGroupUtility.GetDSCompsFromGroup(storage.Group))
                    {
                        c.SetMaxNumberStacksDirect(newValue);
                    }
                }
                else SetMaxNumberStacksDirect(newValue);
            }
        }
        [Multiplayer.API.SyncMethod]
        public void SetMaxNumberStacksDirect(int? n)
        {
            this.maxNumberStacks = n;
            this.DirtyMapCache();
        }

        public virtual bool ShowContents
        {
            get {
                return ((Properties)this.props).showContents;
            }
        }

        public void SetLabel(string newLabel)
        {
            if (newLabel == null) newLabel = "";
            if ((parent is IStorageGroupMember storage) && storage.Group != null)
            {
                foreach (var c in DSStorageGroupUtility.GetDSCompsFromGroup(storage.Group)) {
                    c.SetLabelDirect(newLabel);
                }
            }
            else SetLabelDirect(newLabel);
        }
        [Multiplayer.API.SyncMethod]
        private void SetLabelDirect(string newLabel)
        {
            buildingLabel = newLabel;
        }

        [Multiplayer.API.SyncMethod]
        public virtual void ResetSettings()
        {
            this.buildingLabel = "";
            this.maxNumberStacks = null;
            this.DirtyMapCache();
        }

        public void CopySettingsFrom(CompDeepStorage other)
        {
            SetLabelDirect(other.buildingLabel);
            SetMaxNumberStacksDirect(other.maxNumberStacks);
        }

        public string buildingLabel="";

        /////////////// Storage Data ////////////////
        private int? maxNumberStacks;

        public StatDef stat = StatDefOf.Mass;
        /*******  For only one limiting stat: (mass, or bulk for CombatExtended)  *******/
        public float limitingFactorForItem = 0f;
        public float limitingTotalFactorForCell = 0f;
        /*******  Viable approach if anyone ever wants to limit storage based on >1 stat:
         *          We can revisit this is anyone ever requests it
         *          (this approach would need a for loop in _CanCarryItemsTo.cs, etc)
        public float[] maxStatOfStoredItem = { };
        public StatDef[] statForStoredItem = { };
        public float[] maxTotalStat = { };
        public StatDef[] statToTotal = { };
        */


    } // end CompDeepStorage




}
