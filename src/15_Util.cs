// ============================================================================
//  UTILITIES
//
//  Number formatting and parsing are done by hand throughout. It looks like
//  reinventing the wheel; it is not. Space Engineers runs on the client's own
//  locale, so ToString("F1") produces "12,5" for a large part of the player base
//  and every round trip through a string silently corrupts. Integers and manual
//  assembly have no such failure mode.
// ============================================================================

static double Clamp(double v, double lo, double hi)
{
    if (double.IsNaN(v)) return lo;
    return v < lo ? lo : (v > hi ? hi : v);
}

static double ToDegrees(double radians)
{
    return radians * 180.0 / Math.PI;
}

// ---------------------------------------------------------------------------
//  HUMAN-READABLE FORMATTING
// ---------------------------------------------------------------------------

/// <summary>Fixed-point format with a guaranteed '.' separator in every locale.</summary>
static string Fmt(double v, int decimals)
{
    if (double.IsNaN(v) || double.IsInfinity(v)) return "-";

    bool negative = v < 0;
    v = Math.Abs(v);

    if (decimals <= 0)
    {
        long whole = (long)Math.Round(v);
        return (negative && whole != 0 ? "-" : "") + whole.ToString();
    }

    long scale = 1;
    for (int i = 0; i < decimals; i++) scale *= 10;

    long scaled = (long)Math.Round(v * scale);
    long units = scaled / scale;
    long frac = scaled % scale;

    string fracText = frac.ToString();
    while (fracText.Length < decimals) fracText = "0" + fracText;

    return (negative && scaled != 0 ? "-" : "") + units.ToString() + "." + fracText;
}

/// <summary>Compact mass: kg below a tonne, tonnes above.</summary>
static string FmtMass(double kg)
{
    if (kg < 1000) return Fmt(kg, 0) + "kg";
    return Fmt(kg / 1000.0, 1) + "t";
}

/// <summary>A text progress bar, e.g. [####------] for 0.4.</summary>
static string Bar(double fraction, int width)
{
    fraction = Clamp(fraction, 0.0, 1.0);
    int filled = (int)Math.Round(fraction * width);
    var b = new StringBuilder(width + 2);
    b.Append('[');
    for (int i = 0; i < width; i++) b.Append(i < filled ? '#' : '-');
    b.Append(']');
    return b.ToString();
}

// ---------------------------------------------------------------------------
//  WIRE ENCODING
//
//  Everything on the wire is an integer. Doubles are scaled by 1000, giving
//  millimetre precision on positions, which is far finer than anything we
//  navigate to.
// ---------------------------------------------------------------------------

const double WIRE_SCALE = 1000.0;

static string EncD(double v)
{
    if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
    double scaled = v * WIRE_SCALE;
    // Saturate rather than overflow into a wrapped negative.
    if (scaled > 9.0e18) scaled = 9.0e18;
    if (scaled < -9.0e18) scaled = -9.0e18;
    return ((long)Math.Round(scaled)).ToString();
}

static double DecD(string s)
{
    long v;
    return long.TryParse(s, out v) ? v / WIRE_SCALE : 0.0;
}

static string EncV(Vector3D v)
{
    return EncD(v.X) + "," + EncD(v.Y) + "," + EncD(v.Z);
}

static Vector3D DecV(string s)
{
    if (string.IsNullOrEmpty(s)) return Vector3D.Zero;
    string[] p = s.Split(',');
    if (p.Length < 3) return Vector3D.Zero;
    return new Vector3D(DecD(p[0]), DecD(p[1]), DecD(p[2]));
}

static int ParseInt(string s, int fallback)
{
    int v;
    return int.TryParse(s, out v) ? v : fallback;
}
