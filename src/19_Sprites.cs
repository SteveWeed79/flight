// ============================================================================
//  SPRITE DASHBOARD
//
//  LCDs tagged with lcdTag get this instead of monospace text. The centrepiece
//  is the yield heatmap — the survey rendered as colour, which is the whole
//  point of building a survey in the first place. A table of numbers tells you
//  the script is running; a heatmap tells you the ore is all in the north-east
//  corner and your job rectangle is in the wrong place.
//
//  Everything is drawn from SurfaceSize, so it lays out correctly on a square
//  LCD, a wide one, or a cockpit screen without any configuration.
// ============================================================================

// Palette. Deliberately muted except for the data itself, so the eye lands on
// the map rather than the chrome.
static readonly Color C_BG      = new Color(8, 12, 16);
static readonly Color C_PANEL   = new Color(18, 26, 34);
static readonly Color C_INK     = new Color(150, 180, 200);
static readonly Color C_DIM     = new Color(70, 90, 105);
static readonly Color C_ACCENT  = new Color(90, 200, 255);
static readonly Color C_WARN    = new Color(255, 170, 60);
static readonly Color C_BAD     = new Color(255, 80, 70);
static readonly Color C_GOOD    = new Color(120, 230, 140);

void RenderSprites()
{
    for (int i = 0; i < panels.Count; i++)
    {
        try { DrawDashboard(panels[i]); }
        catch { /* a panel destroyed mid-draw must not take the script down */ }
    }
}

void DrawDashboard(IMyTextSurface s)
{
    Vector2 size = s.SurfaceSize;
    // The drawable texture is usually larger than the visible surface; the
    // visible area sits centred inside it. Skip this and everything is drawn
    // offset toward the top-left corner.
    Vector2 origin = (s.TextureSize - size) * 0.5f;

    using (MySpriteDrawFrame frame = s.DrawFrame())
    {
        Fill(frame, origin, size, C_BG);

        float pad = size.Y * 0.03f;
        float headerH = size.Y * 0.13f;

        DrawHeader(frame, origin + new Vector2(pad, pad),
                   new Vector2(size.X - pad * 2, headerH));

        float bodyY = pad * 2 + headerH;
        float bodyH = size.Y - bodyY - pad;

        // Wide panels put the map beside the readouts; tall or square ones
        // stack them, so a cockpit screen stays legible.
        bool wide = size.X > size.Y * 1.4f;
        float mapW = wide ? (size.X - pad * 3) * 0.58f : size.X - pad * 2;
        float mapH = wide ? bodyH : bodyH * 0.62f;

        DrawMapPanel(frame, origin + new Vector2(pad, bodyY), new Vector2(mapW, mapH));

        Vector2 sidePos = wide
            ? origin + new Vector2(pad * 2 + mapW, bodyY)
            : origin + new Vector2(pad, bodyY + mapH + pad);
        Vector2 sideSize = wide
            ? new Vector2(size.X - mapW - pad * 3, bodyH)
            : new Vector2(size.X - pad * 2, bodyH - mapH - pad);

        if (role == Role.Dispatcher) DrawFleetPanel(frame, sidePos, sideSize);
        else DrawShipPanel(frame, sidePos, sideSize);
    }
}

// ---------------------------------------------------------------------------
//  HEADER
// ---------------------------------------------------------------------------

void DrawHeader(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
    Fill(frame, pos, size, C_PANEL);

    bool fault = state == MinerState.Fault;
    Color accent = fault ? C_BAD : C_ACCENT;

    // Accent stripe down the left edge — the fastest "is it healthy" signal on
    // the whole screen, readable from across the room.
    Fill(frame, pos, new Vector2(size.Y * 0.10f, size.Y), accent);

    float fs = size.Y * 0.030f;
    Text(frame, "VEIN", pos + new Vector2(size.Y * 0.28f, size.Y * 0.10f), fs * 1.15f, accent);

    string sub = role == Role.Dispatcher
        ? fleet.Count + " drone" + (fleet.Count == 1 ? "" : "s")
        : (HasDispatcher ? "fleet" : "solo");
    Text(frame, sub, pos + new Vector2(size.Y * 0.28f, size.Y * 0.55f), fs * 0.62f, C_DIM);

    string headline = fault ? faultReason : statusLine;
    if (headline.Length > 34) headline = headline.Substring(0, 33) + "…";
    Text(frame, headline, pos + new Vector2(size.X * 0.30f, size.Y * 0.10f), fs * 0.80f,
         fault ? C_BAD : C_INK);

    if (job.IsSet)
    {
        Text(frame, Fmt(JobProgress() * 100, 0) + "%",
             pos + new Vector2(size.X - size.Y * 0.20f, size.Y * 0.10f), fs * 1.05f, C_INK,
             TextAlignment.RIGHT);

        // Thin progress rule along the bottom of the header.
        float w = size.X - size.Y * 0.40f;
        Vector2 barPos = pos + new Vector2(size.X * 0.30f, size.Y * 0.72f);
        Fill(frame, barPos, new Vector2(w * 0.62f, size.Y * 0.06f), C_DIM);
        Fill(frame, barPos, new Vector2(w * 0.62f * (float)JobProgress(), size.Y * 0.06f), accent);
    }
}

// ---------------------------------------------------------------------------
//  YIELD HEATMAP
// ---------------------------------------------------------------------------

void DrawMapPanel(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
    Fill(frame, pos, size, C_PANEL);

    if (!job.IsSet || cells.Length == 0)
    {
        Text(frame, "no job set", pos + size * 0.5f, size.Y * 0.06f, C_DIM, TextAlignment.CENTER);
        return;
    }

    float pad = size.Y * 0.05f;
    float labelH = size.Y * 0.10f;
    Vector2 area = new Vector2(size.X - pad * 2, size.Y - pad * 2 - labelH);

    // Square cells, centred, sized to whichever axis runs out first.
    float cell = Math.Min(area.X / job.Width, area.Y / job.Height);
    float gap = cell > 8f ? cell * 0.08f : 0f;
    Vector2 gridSize = new Vector2(cell * job.Width, cell * job.Height);
    Vector2 gridPos = pos + new Vector2(pad, pad) + (area - gridSize) * 0.5f;

    // Normalise colour against the best cell found so far, so the map stays
    // readable whether you are mining iron or platinum.
    float peak = 0.01f;
    for (int i = 0; i < cells.Length; i++)
        if (cells[i].Yield > peak) peak = cells[i].Yield;

    // Sprite count is the real cost here. Past a few hundred the tick budget
    // suffers, so very large jobs draw a coarse summary rather than every cell.
    int step = 1;
    while ((job.Width / step) * (job.Height / step) > 600) step++;

    for (int row = 0; row < job.Height; row += step)
    {
        for (int col = 0; col < job.Width; col += step)
        {
            int idx = job.IndexOf(col, row);
            Vector2 p = gridPos + new Vector2(col * cell, row * cell);
            Fill(frame, p + new Vector2(gap * 0.5f, gap * 0.5f),
                 new Vector2(cell * step - gap, cell * step - gap), CellColor(cells[idx], peak));
        }
    }

    // Where the drones actually are, drawn over the map.
    if (role == Role.Dispatcher)
    {
        foreach (var kv in fleet)
            DrawDroneMarker(frame, gridPos, cell, kv.Value.Position, C_ACCENT);
    }
    else if (activeCell >= 0)
    {
        int col = CellCol(activeCell), row = CellRow(activeCell);
        Vector2 p = gridPos + new Vector2((col + 0.5f) * cell, (row + 0.5f) * cell);
        Sprite(frame, "CircleHollow", p, new Vector2(cell * 1.9f, cell * 1.9f), Color.White);
    }

    Text(frame, "yield map  ·  peak " + Fmt(peak, 1) + " kg/m",
         pos + new Vector2(size.X * 0.5f, size.Y - labelH), size.Y * 0.055f, C_DIM,
         TextAlignment.CENTER);
}

/// <summary>Colour for a cell: state first, then yield as a heat ramp.</summary>
static Color CellColor(YieldCell c, float peak)
{
    switch (c.State)
    {
        case CellState.Unknown: return new Color(24, 34, 44);
        case CellState.Leased:  return new Color(70, 130, 200);
        case CellState.Blocked: return new Color(120, 40, 40);
        case CellState.Barren:  return new Color(40, 36, 32);
    }

    // Rich or Exhausted — colour by how good it actually was.
    float t = peak > 0 ? Clamped(c.Yield / peak) : 0f;

    // Dark red → orange → yellow → green. Ramps through hue rather than
    // brightness so it stays distinguishable on a dim LCD.
    Color hot = t < 0.5f
        ? Lerp(new Color(90, 30, 25), new Color(220, 150, 40), t * 2f)
        : Lerp(new Color(220, 150, 40), new Color(110, 240, 130), (t - 0.5f) * 2f);

    // Worked-out cells are dimmed rather than recoloured, so you can still see
    // where the good ground was after it has been mined.
    if (c.State == CellState.Exhausted) return Dim(hot, 0.45f);
    return hot;
}

void DrawDroneMarker(MySpriteDrawFrame frame, Vector2 gridPos, float cell, Vector3D world, Color col)
{
    if (job.Spacing <= 0) return;

    Vector3D d = world - job.Origin;
    double x = Vector3D.Dot(d, job.Right) / job.Spacing + (job.Width - 1) * 0.5;
    double y = Vector3D.Dot(d, job.Forward) / job.Spacing + (job.Height - 1) * 0.5;

    // Off the edge of the site — in transit, probably. Not worth drawing.
    if (x < -1 || x > job.Width || y < -1 || y > job.Height) return;

    Vector2 p = gridPos + new Vector2((float)(x + 0.5) * cell, (float)(y + 0.5) * cell);
    Sprite(frame, "Circle", p, new Vector2(cell * 0.7f, cell * 0.7f), col);
}

// ---------------------------------------------------------------------------
//  SIDE PANELS
// ---------------------------------------------------------------------------

void DrawShipPanel(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
    Fill(frame, pos, size, C_PANEL);

    float pad = size.Y * 0.06f;
    float fs = size.Y * 0.055f;
    float y = pos.Y + pad;
    float rowH = size.Y * 0.145f;

    Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, rowH, "CARGO",
          cargoFill, FmtMass(oreAboard) + " ore", GaugeColor(cargoFill, true));
    y += rowH;

    Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, rowH, "POWER",
          batteryFill, Fmt(batteryFill * 100, 0) + "%", GaugeColor(batteryFill, false));
    y += rowH;

    if (hydrogenTanks.Count > 0)
    {
        Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, rowH, "H2",
              hydrogenFill, Fmt(hydrogenFill * 100, 0) + "%", GaugeColor(hydrogenFill, false));
        y += rowH;
    }

    y += pad * 0.5f;

    if (activeCell >= 0)
    {
        string what = (shaftIsProbe ? "probe " : "shaft ") + CellLabel(activeCell);
        Text(frame, what, new Vector2(pos.X + pad, y), fs, C_DIM);
        Text(frame, Fmt(Math.Max(0, shaftDepth), 1) + " m",
             new Vector2(pos.X + size.X - pad, y), fs, C_INK, TextAlignment.RIGHT);
        y += rowH * 0.75f;
    }

    if (maxFlyableMass > 0)
    {
        bool over = shipMass >= maxFlyableMass;
        Text(frame, "lift", new Vector2(pos.X + pad, y), fs, C_DIM);
        Text(frame, FmtMass(shipMass) + " / " + FmtMass(maxFlyableMass),
             new Vector2(pos.X + size.X - pad, y), fs, over ? C_BAD : C_INK, TextAlignment.RIGHT);
        y += rowH * 0.75f;
    }

    Text(frame, "scout", new Vector2(pos.X + pad, y), fs, C_DIM);
    Text(frame, oreModAvailable ? "ore raycast" : "probe map",
         new Vector2(pos.X + size.X - pad, y), fs,
         oreModAvailable ? C_GOOD : C_INK, TextAlignment.RIGHT);
}

void DrawFleetPanel(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
    Fill(frame, pos, size, C_PANEL);

    float pad = size.Y * 0.05f;
    float fs = size.Y * 0.048f;
    float y = pos.Y + pad;

    Text(frame, FmtMass(FleetOreTotal()) + " recovered", new Vector2(pos.X + pad, y), fs * 1.2f, C_GOOD);
    y += size.Y * 0.11f;
    Text(frame, Fmt(FleetMetresTotal(), 0) + " m drilled  ·  " + RemainingCellCount() + " shafts left",
         new Vector2(pos.X + pad, y), fs * 0.85f, C_DIM);
    y += size.Y * 0.10f;

    if (fleet.Count == 0)
    {
        Text(frame, "no drones on channel", new Vector2(pos.X + pad, y), fs, C_WARN);
        return;
    }

    float rowH = Math.Min(size.Y * 0.13f, (size.Y - (y - pos.Y) - pad) / Math.Max(1, fleet.Count));

    foreach (var kv in fleet)
    {
        DroneRecord r = kv.Value;
        if (y + rowH > pos.Y + size.Y) break;      // ran out of panel

        Color dot = r.State == MinerState.Fault ? C_BAD
                  : r.State == MinerState.Idle ? C_DIM : C_GOOD;
        Sprite(frame, "Circle", new Vector2(pos.X + pad + fs * 0.4f, y + rowH * 0.35f),
               new Vector2(fs * 0.55f, fs * 0.55f), dot);

        string name = r.Name.Length > 12 ? r.Name.Substring(0, 12) : r.Name;
        Text(frame, name, new Vector2(pos.X + pad + fs * 1.1f, y), fs, C_INK);
        Text(frame, r.State.ToString(), new Vector2(pos.X + size.X - pad, y), fs * 0.8f,
             C_DIM, TextAlignment.RIGHT);

        // Two hairline bars: cargo above, battery below.
        float barW = size.X - pad * 2 - fs * 1.1f;
        float barX = pos.X + pad + fs * 1.1f;
        float barY = y + rowH * 0.62f;
        Fill(frame, new Vector2(barX, barY), new Vector2(barW, rowH * 0.08f), C_BG);
        Fill(frame, new Vector2(barX, barY), new Vector2(barW * r.CargoFill, rowH * 0.08f), C_ACCENT);
        Fill(frame, new Vector2(barX, barY + rowH * 0.13f), new Vector2(barW, rowH * 0.08f), C_BG);
        Fill(frame, new Vector2(barX, barY + rowH * 0.13f),
             new Vector2(barW * r.Battery, rowH * 0.08f), GaugeColor(r.Battery, false));

        y += rowH;
    }
}

void Gauge(MySpriteDrawFrame frame, Vector2 pos, float w, float h,
           string label, double value, string readout, Color col)
{
    float fs = h * 0.34f;
    Text(frame, label, pos, fs, C_DIM);
    Text(frame, readout, new Vector2(pos.X + w, pos.Y), fs, C_INK, TextAlignment.RIGHT);

    Vector2 barPos = new Vector2(pos.X, pos.Y + h * 0.52f);
    Vector2 barSize = new Vector2(w, h * 0.20f);
    Fill(frame, barPos, barSize, C_BG);
    Fill(frame, barPos, new Vector2(w * Clamped((float)value), barSize.Y), col);
}

/// <summary>Green when healthy, amber then red as it approaches trouble.
/// <paramref name="fullIsBad"/> flips the sense for things like cargo.</summary>
static Color GaugeColor(double v, bool fullIsBad)
{
    double t = fullIsBad ? 1.0 - v : v;
    if (t > 0.5) return C_GOOD;
    if (t > 0.2) return C_WARN;
    return C_BAD;
}

// ---------------------------------------------------------------------------
//  PRIMITIVES
// ---------------------------------------------------------------------------

static void Fill(MySpriteDrawFrame frame, Vector2 pos, Vector2 size, Color col)
{
    frame.Add(new MySprite()
    {
        Type = SpriteType.TEXTURE,
        Data = "SquareSimple",
        Position = pos + size * 0.5f,
        Size = size,
        Color = col,
        Alignment = TextAlignment.CENTER
    });
}

static void Sprite(MySpriteDrawFrame frame, string id, Vector2 centre, Vector2 size, Color col)
{
    frame.Add(new MySprite()
    {
        Type = SpriteType.TEXTURE,
        Data = id,
        Position = centre,
        Size = size,
        Color = col,
        Alignment = TextAlignment.CENTER
    });
}

static void Text(MySpriteDrawFrame frame, string text, Vector2 pos, float scale, Color col,
                 TextAlignment align = TextAlignment.LEFT)
{
    frame.Add(new MySprite()
    {
        Type = SpriteType.TEXT,
        Data = text,
        Position = pos,
        RotationOrScale = scale,
        Color = col,
        Alignment = align,
        FontId = "White"
    });
}

static float Clamped(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
static float Clamped(double v) { return Clamped((float)v); }

static Color Lerp(Color a, Color b, float t)
{
    t = Clamped(t);
    return new Color(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));
}

static Color Dim(Color c, float f)
{
    return new Color((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
}
