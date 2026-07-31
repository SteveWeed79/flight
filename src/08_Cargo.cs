// ============================================================================
//  INVENTORY, EJECTION, UNLOADING
// ============================================================================

const string TYPE_ORE = "MyObjectBuilder_Ore";
const string SUB_STONE = "Stone";
const string SUB_ICE = "Ice";

/// <summary>Recompute cargo fill, ore aboard, power and gas levels.</summary>
void SampleInventories()
{
    double vol = 0, maxVol = 0, ore = 0;

    for (int i = 0; i < cargo.Count; i++)
        AccumulateInventory(cargo[i].GetInventory(0), ref vol, ref maxVol, ref ore);

    // Drill inventories count too. On a ship without conveyors they are the
    // only storage there is, and on one with conveyors they are the buffer that
    // tells us whether ore is still arriving.
    for (int i = 0; i < drills.Count; i++)
        AccumulateInventory(drills[i].GetInventory(0), ref vol, ref maxVol, ref ore);

    cargoFill = maxVol > 0 ? vol / maxVol : 0;
    cargoVolume = vol;
    oreAboard = ore;

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

void AccumulateInventory(IMyInventory inv, ref double vol, ref double maxVol, ref double ore)
{
    if (inv == null) return;
    vol += (double)inv.CurrentVolume;
    maxVol += (double)inv.MaxVolume;

    itemScratch.Clear();
    inv.GetItems(itemScratch);
    for (int i = 0; i < itemScratch.Count; i++)
    {
        MyInventoryItem it = itemScratch[i];
        if (IsValuableOre(it.Type)) ore += (double)it.Amount;
    }
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

bool CargoFull { get { return cargoFill >= cargoFullAt; } }

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
    blockScratch.Clear();
    GridTerminalSystem.GetBlocksOfType(blockScratch,
        b => b is IMyCargoContainer && !b.IsSameConstructAs(Me));

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
    for (int d = 0; d < blockScratch.Count; d++)
    {
        IMyInventory dst = blockScratch[d].GetInventory(0);
        if (dst == null) continue;
        if ((double)dst.CurrentVolume >= (double)dst.MaxVolume * 0.99) continue;

        for (int i = 0; i < cargo.Count; i++)
            if (DrainAll(cargo[i].GetInventory(0), dst)) moved = true;
        for (int i = 0; i < drills.Count; i++)
            if (DrainAll(drills[i].GetInventory(0), dst)) moved = true;

        if (BudgetTight(0.75)) return false;
    }

    if (moved) SampleInventories();
    return cargoFill < 0.02;
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
    if (hydrogenTanks.Count == 0) return;

    if (hydroSamplePos == Vector3D.Zero)
    {
        hydroSamplePos = shipPos;
        hydroSampleFill = hydrogenFill;
        return;
    }

    double travelled = Vector3D.Distance(shipPos, hydroSamplePos);
    if (travelled < 150.0) return;              // too short to mean anything

    double used = hydroSampleFill - hydrogenFill;
    hydroSamplePos = shipPos;
    hydroSampleFill = hydrogenFill;

    // Refuelling, or a generator outpacing the thrusters. Nothing to learn.
    if (used <= 0) return;

    double rate = used / travelled;

    // Exponential moving average. A single leg through a gravity well is not
    // representative of the whole route, and neither is a lazy drift in space.
    hydroPerMetre = hydroCalibrated ? hydroPerMetre * 0.7 + rate * 0.3 : rate;
    hydroCalibrated = true;
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

/// <summary>True when we have only just enough fuel left to reach the dock.</summary>
bool FuelCriticalForReturn()
{
    double need = FuelToGetHome();
    if (need <= 0) return false;
    // Five points of tank held back for docking manoeuvres on arrival.
    return hydrogenFill < need + 0.05;
}

bool ServiceComplete()
{
    return batteryFill >= resumeBattery && hydrogenFill >= resumeHydrogen;
}
