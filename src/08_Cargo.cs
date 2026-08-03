// ============================================================================
//  INVENTORY, EJECTION, UNLOADING
// ============================================================================

const string TYPE_ORE = "MyObjectBuilder_Ore";
const string SUB_STONE = "Stone";
const string SUB_ICE = "Ice";

/// <summary>
/// Volumes, power and gas. Cheap enough to run every tick.
///
/// Deliberately does NOT enumerate items. CurrentVolume is a single property
/// read, whereas GetItems fills a list per inventory — and a ship with ten
/// drills and five containers was doing fifteen of those six times a second for
/// a number that only feeds a one-hertz decision. Ore counting lives in
/// SampleOre and runs far less often.
/// </summary>
void SampleInventories()
{
    double vol = 0, maxVol = 0;
    peakInventoryFill = 0;

    for (int i = 0; i < cargo.Count; i++)
        AccumulateVolume(cargo[i].GetInventory(0), ref vol, ref maxVol);

    // Drill inventories count too. On a ship without conveyors they are the
    // only storage there is, and on one with conveyors they are the buffer that
    // tells us whether ore is still arriving.
    for (int i = 0; i < drills.Count; i++)
        AccumulateVolume(drills[i].GetInventory(0), ref vol, ref maxVol);

    cargoFill = maxVol > 0 ? vol / maxVol : 0;
    cargoVolume = vol;

    if (peakInventoryFill >= 0.98) peakFullTicks++; else peakFullTicks = 0;

    // ---- Power ------------------------------------------------------------
    double stored = 0, capacity = 0;
    for (int i = 0; i < batteries.Count; i++)
    {
        IMyBatteryBlock b = batteries[i];
        if (!b.IsFunctional) continue;
        stored += b.CurrentStoredPower;
        capacity += b.MaxStoredPower;
    }
    batteryFill = capacity > 0 ? stored / capacity : 1.0;

    // ---- Hydrogen ---------------------------------------------------------
    double gas = 0; int tanks = 0;
    for (int i = 0; i < hydrogenTanks.Count; i++)
    {
        if (!hydrogenTanks[i].IsFunctional) continue;
        gas += hydrogenTanks[i].FilledRatio;
        tanks++;
    }
    // No tanks means no hydrogen dependency, so report full rather than empty —
    // otherwise an ion-only ship would refuse to ever leave the dock.
    hydrogenFill = tanks > 0 ? gas / tanks : 1.0;
}

void AccumulateVolume(IMyInventory inv, ref double vol, ref double maxVol)
{
    if (inv == null) return;
    double cur = (double)inv.CurrentVolume, max = (double)inv.MaxVolume;
    vol += cur;
    maxVol += max;
    if (max > 0) peakInventoryFill = Math.Max(peakInventoryFill, cur / max);
}

/// <summary>
/// Kilograms of valuable ore aboard. The expensive half of inventory sampling,
/// so it runs at about 1 Hz rather than every tick. Adaptive depth already
/// requires sixty dry ticks before it acts, so a second of lag changes nothing;
/// callers that need an exact figure at a shaft boundary call this directly.
/// </summary>
void SampleOre()
{
    // Uranium, while we are already walking inventories.
    uraniumKg = 0;
    for (int i = 0; i < reactors.Count; i++)
    {
        IMyInventory inv = reactors[i].GetInventory(0);
        if (inv == null) continue;
        itemScratch.Clear();
        inv.GetItems(itemScratch);
        for (int k = 0; k < itemScratch.Count; k++)
            if (itemScratch[k].Type.SubtypeId == "Uranium") uraniumKg += (double)itemScratch[k].Amount;
    }

    double ore = 0;
    for (int i = 0; i < cargo.Count; i++)
        AccumulateOre(cargo[i].GetInventory(0), ref ore);
    for (int i = 0; i < drills.Count; i++)
        AccumulateOre(drills[i].GetInventory(0), ref ore);
    oreAboard = ore;
}

void AccumulateOre(IMyInventory inv, ref double ore)
{
    if (inv == null) return;
    itemScratch.Clear();
    inv.GetItems(itemScratch);
    for (int i = 0; i < itemScratch.Count; i++)
        if (IsValuableOre(itemScratch[i].Type)) ore += (double)itemScratch[i].Amount;
}

/// <summary>Ore that is worth carrying home. Stone is not.</summary>
static bool IsValuableOre(MyItemType t)
{
    return t.TypeId == TYPE_ORE && t.SubtypeId != SUB_STONE;
}

/// <summary>
/// Total mass moved through the drills this shaft, including whatever the
/// conveyors already pulled back into cargo. Using drill contents alone would
/// under-report badly on a well-conveyored ship.
/// </summary>
double ShaftOreSoFar()
{
    return Math.Max(0.0, oreAboard - shaftStartOre);
}

/// <summary>
/// Out of usable room. Aggregate fill is the normal signal, but a single
/// brimming inventory also counts: a drill with no conveyor to anywhere fills
/// up and silently stops collecting while total fill still reads ten per cent,
/// so the ship would keep grinding away collecting nothing. PAM solves this by
/// balancing contents between drills; refusing to keep mining is cheaper and
/// fails in the safe direction.
/// </summary>
/// <summary>Consecutive ticks with a single inventory brimming. Counted in
/// SampleInventories, which runs exactly once per tick — counting it inside the
/// property would multiply it by however many callers happened to read it.</summary>
int peakFullTicks;

bool CargoFull
{
    get
    {
        if (cargoFill >= cargoFullAt) return true;
        // A single full inventory is the unconveyored-drill case and is real, but
        // it also shows for one tick whenever a conveyor is mid-transfer.
        // Requiring it to persist for a second stops a shaft being abandoned on
        // a transient.
        return peakFullTicks > 6;
    }
}

// ---------------------------------------------------------------------------
//  EJECTION
// ---------------------------------------------------------------------------

/// <summary>
/// Dump worthless mass overboard. This roughly triples time-on-site: stone is
/// the overwhelming majority of what a drill picks up, and hauling it home is
/// pure waste.
/// </summary>
/// <returns>True when there is nothing left worth ejecting.</returns>
bool EjectWaste()
{
    if (ejectMode == EjectMode.Off || ejectors.Count == 0) return true;

    bool movedAny = false;

    for (int e = 0; e < ejectors.Count; e++)
    {
        IMyShipConnector ej = ejectors[e];
        if (!ej.IsFunctional) continue;
        IMyInventory dst = ej.GetInventory(0);
        if (dst == null) continue;

        if (PushWasteInto(dst)) movedAny = true;
        if (BudgetTight(0.75)) break;   // finish next tick rather than overrun
    }

    // Done when the ejectors have drained and we found nothing new to add.
    if (!movedAny)
    {
        for (int e = 0; e < ejectors.Count; e++)
        {
            IMyInventory inv = ejectors[e].GetInventory(0);
            if (inv != null && (double)inv.CurrentVolume > 0.001) return false;
        }
        return true;
    }
    return false;
}

bool PushWasteInto(IMyInventory dst)
{
    bool moved = false;
    for (int i = 0; i < cargo.Count; i++)
        if (DrainWaste(cargo[i].GetInventory(0), dst)) moved = true;
    for (int i = 0; i < drills.Count; i++)
        if (DrainWaste(drills[i].GetInventory(0), dst)) moved = true;
    return moved;
}

bool DrainWaste(IMyInventory src, IMyInventory dst)
{
    if (src == null || dst == null || src == dst) return false;
    if (!src.IsConnectedTo(dst)) return false;

    itemScratch.Clear();
    src.GetItems(itemScratch);

    bool moved = false;
    // Iterate backwards: transferring removes items and shifts every index
    // above it, which silently skips entries if you walk forwards.
    for (int i = itemScratch.Count - 1; i >= 0; i--)
    {
        if (!IsWaste(itemScratch[i].Type)) continue;
        if (src.TransferItemTo(dst, i, null, true, null)) moved = true;
    }
    return moved;
}

bool IsWaste(MyItemType t)
{
    if (t.TypeId != TYPE_ORE) return false;
    if (t.SubtypeId == SUB_STONE) return true;
    if (t.SubtypeId == SUB_ICE && ejectMode == EjectMode.StoneAndIce) return true;
    return false;
}


// ---------------------------------------------------------------------------
//  UNLOADING AT BASE
// ---------------------------------------------------------------------------

/// <summary>
/// Push everything into base storage. Returns true when the ship is empty (or
/// as empty as it is going to get, if the base is full).
/// </summary>
bool UnloadToBase()
{
    if (dockConnector == null || dockConnector.Status != MyShipConnectorStatus.Connected)
        return false;

    // Base containers: reachable through the terminal system while docked, but
    // explicitly not part of our own construct.
    //
    // Typed rather than IMyTerminalBlock-with-a-cast, because the untyped form
    // runs the predicate against every block on the base — lights, conveyors,
    // catwalks, all of it — and this is called at 6 Hz for as long as the ship is
    // docked. On a large station that is the single most expensive thing the
    // script does, and it scales with a build the script does not control.
    blockScratch.Clear();
    var baseCargo = new List<IMyCargoContainer>();
    GridTerminalSystem.GetBlocksOfType(baseCargo, b => !b.IsSameConstructAs(Me));
    for (int i = 0; i < baseCargo.Count; i++) blockScratch.Add(baseCargo[i]);

    // The far connector is a valid destination in its own right and is the only
    // one that exists on a base whose storage sits behind a sorter.
    IMyShipConnector far = dockConnector.OtherConnector;
    if (far != null) blockScratch.Add(far);

    if (blockScratch.Count == 0)
    {
        // Nothing to push into. Not a fault — some bases just want the ship to
        // sit there while a sorter drains it.
        return cargoFill < 0.02;
    }

    bool moved = false;
    bool anyRoom = false;
    for (int d = 0; d < blockScratch.Count; d++)
    {
        IMyInventory dst = blockScratch[d].GetInventory(0);
        if (dst == null) continue;
        if ((double)dst.CurrentVolume >= (double)dst.MaxVolume * 0.99) continue;
        anyRoom = true;

        for (int i = 0; i < cargo.Count; i++)
            if (DrainAll(cargo[i].GetInventory(0), dst)) moved = true;
        for (int i = 0; i < drills.Count; i++)
            if (DrainAll(drills[i].GetInventory(0), dst)) moved = true;

        if (BudgetTight(0.75)) return false;
    }

    if (moved) SampleInventories();
    if (cargoFill < 0.02) return true;

    // Nowhere left to put it. This method's contract is "empty, or as empty as
    // it is going to get" — but with every base container full it returned false
    // forever, the ship sat on the connector, and the watchdog faulted it. A full
    // base is an ordinary situation an operator can see and fix; it should not
    // need a fault cleared afterwards.
    if (!anyRoom) return true;
    return false;
}

bool DrainAll(IMyInventory src, IMyInventory dst)
{
    if (src == null || dst == null || src == dst) return false;
    if (!src.IsConnectedTo(dst)) return false;

    itemScratch.Clear();
    src.GetItems(itemScratch);

    bool moved = false;
    for (int i = itemScratch.Count - 1; i >= 0; i--)
        if (src.TransferItemTo(dst, i, null, true, null)) moved = true;
    return moved;
}

// ---------------------------------------------------------------------------
//  SERVICING
// ---------------------------------------------------------------------------

/// <summary>Put batteries on recharge while docked, back to auto when leaving.</summary>
void SetBatteryCharging(bool charging)
{
    for (int i = 0; i < batteries.Count; i++)
    {
        IMyBatteryBlock b = batteries[i];
        if (!b.IsFunctional) continue;
        ChargeMode want = charging ? ChargeMode.Recharge : ChargeMode.Auto;
        if (b.ChargeMode != want) b.ChargeMode = want;
    }
}

/// <summary>Let base hydrogen flow into our tanks while docked.</summary>
void SetTanksFilling(bool filling)
{
    for (int i = 0; i < hydrogenTanks.Count; i++)
    {
        IMyGasTank t = hydrogenTanks[i];
        if (!t.IsFunctional) continue;
        // Stockpile pulls gas in and refuses to give any back, which is exactly
        // what we want at the pump and exactly wrong once we undock.
        if (t.Stockpile != filling) t.Stockpile = filling;
    }
}

// ---------------------------------------------------------------------------
//  FUEL MODEL
// ---------------------------------------------------------------------------

/// <summary>
/// Learn how much hydrogen this ship burns per metre, by watching it fly.
///
/// No configuration and no assumptions about thruster count: a ship with forty
/// hydrogen thrusters measures a high rate and turns for home early, while a
/// frugal one runs until it is genuinely low. Sampled over long intervals so
/// that hovering, drilling and station-keeping average out.
/// </summary>
void UpdateFuelModel()
{
    if (hydroSamplePos == Vector3D.Zero)
    {
        hydroSamplePos = shipPos;
        hydroSampleFill = hydrogenFill;
        powerSampleFill = batteryFill;
        return;
    }

    // Only sample while actually going somewhere. The rate is hydrogen per metre
    // *travelled*, and this divides by straight-line displacement — so a sample
    // that spans twenty minutes of hovering over the site, or a descent and climb
    // back out of a shaft, charged all of that gas to whatever net distance was
    // left over. The measured rate then read far worse than the route really
    // costs and the ship turned for home early, every time, for good.
    if (state == MinerState.Descending || state == MinerState.Ascending
        || state == MinerState.Selecting || state == MinerState.Approaching)
    {
        hydroSamplePos = shipPos;
        hydroSampleFill = hydrogenFill;
        powerSampleFill = batteryFill;
        return;
    }

    double travelled = Vector3D.Distance(shipPos, hydroSamplePos);
    if (travelled < 150.0) return;              // too short to mean anything

    double used = hydroSampleFill - hydrogenFill;
    double usedPower = powerSampleFill - batteryFill;
    hydroSamplePos = shipPos;
    hydroSampleFill = hydrogenFill;
    powerSampleFill = batteryFill;

    // Exponential moving average, per resource. A single leg through a gravity
    // well is not representative of the whole route, and neither is a lazy drift
    // in space. Negative means refuelling or recharging: nothing to learn.
    if (used > 0 && hydrogenTanks.Count > 0)
    {
        double rate = used / travelled;
        hydroPerMetre = hydroCalibrated ? hydroPerMetre * 0.7 + rate * 0.3 : rate;
        hydroCalibrated = true;
    }

    // Batteries too, because on an ion or atmospheric ship they are the only
    // thing that runs out. Such a ship had no measured return check at all —
    // just the fixed 30% floor, which is far too generous on a short hop and not
    // nearly enough on a long one. Batteries recharge in flight from solar or a
    // reactor, so a negative sample here is normal and simply teaches nothing.
    if (usedPower > 0 && batteries.Count > 0)
    {
        double rate = usedPower / travelled;
        powerPerMetre = powerCalibrated ? powerPerMetre * 0.7 + rate * 0.3 : rate;
        powerCalibrated = true;
    }
}

/// <summary>
/// Fraction of a tank needed to fly the recorded route home from here, with
/// margin. Returns 0 until the burn rate has been measured.
/// </summary>
double FuelToGetHome()
{
    if (!hydroCalibrated || hydrogenTanks.Count == 0) return 0;

    double distance = DistanceHomeAlongPath();
    if (distance <= 0) return 0;

    // 1.6x. The return leg is the loaded one, and a loaded ship burns more than
    // the empty one that measured the rate on the way out.
    return distance * hydroPerMetre * 1.6;
}

/// <summary>Fraction of a full charge needed to fly the route home from here.</summary>
double PowerToGetHome()
{
    if (!powerCalibrated || batteries.Count == 0) return 0;
    double distance = DistanceHomeAlongPath();
    if (distance <= 0) return 0;
    return distance * powerPerMetre * 1.6;
}

/// <summary>True when we have only just enough of anything left to reach the dock.</summary>
bool FuelCriticalForReturn()
{
    // Five points held back for docking manoeuvres on arrival.
    double need = FuelToGetHome();
    if (need > 0 && hydrogenFill < need + 0.05) return true;

    double power = PowerToGetHome();
    if (power > 0 && batteryFill < power + 0.05) return true;

    return false;
}

bool ServiceComplete()
{
    return batteryFill >= resumeBattery && hydrogenFill >= resumeHydrogen;
}
