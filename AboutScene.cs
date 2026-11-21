namespace ClaudeUsage;

/// <summary>
/// The little title screen in the About window, a nod to BS3D (a 3D bubble shooter by the same author): its name in
/// pixel letters, and beside it a cluster of coloured balls hanging from a glass plate above a round stone island. The
/// camera circles the island, the cannon on it fires, and the three balls of one colour drop away and bounce off. Each
/// of the four shots hits another side of the cluster; after the fourth the camera has gone round once and it loops.
/// <para>
/// Drawn by a tiny software renderer in plain C# (no UI types, so the WinForms and the Avalonia About window show the
/// same pixels) into a low-resolution buffer that the window scales up by a whole number. Retro on purpose: a small
/// palette, ordered dithering instead of smooth shading and transparency, and a dark outline around every ball.
/// </para>
/// </summary>
public sealed class AboutScene
{
    /// <summary>The proportions the scene is composed for; <see cref="Fit"/> keeps them.</summary>
    public const int DesignWidth = 120;
    public const int DesignHeight = 64;

    /// <summary>The narrowest buffer <see cref="Fit"/> chooses, so the letters and the island never crowd each other.</summary>
    private const int MinimumWidth = 112;

    private const double Period = 4.0;          // one shot
    private const double Loop = 4 * Period;     // four shots, one turn of the camera
    private const double FireAt = 1.0;          // within a shot: the cannon fires
    private const double FlightTime = 0.4;      // the ball's way up
    private const double HitAt = FireAt + FlightTime;
    private const double DropAt = HitAt + 0.36; // the matched three blink until then, then fall
    private const double FadeAt = 1.3;          // after the drop, the fallen balls thin out
    private const double FadeTime = 0.35;
    private const double RegrowTime = 0.45;     // at the end of a shot the cluster grows back
    private const double Gravity = 16;

    private const double BallRadius = 0.5;
    private const double IslandRadius = 2.7;
    private const double IslandDepth = 0.4;
    private const double PlateHalf = 2.2;
    private const double BarrelLength = 1.4;
    private const double BreechRadius = 0.46;                  // the barrel tapers from here to the muzzle
    private const double MuzzleRadius = 0.36;
    private static readonly V3 Pivot = new(0, 5.1, 0);         // the plate's centre: the cluster swings about it
    private static readonly V3 CannonPivot = new(0, 0.25, 0);
    private static readonly V3 Dome = new(0, -0.85, 0);        // the cannon's turret: the top of a ball sunk in the island
    private const double DomeRadius = 1.35;

    private const double CameraDistance = 12.5;
    private const double CameraHeight = 4.1;
    private const double LookAtHeight = 2.5;
    private const double HalfFieldOfView = 0.31;               // radians, vertical
    private const double IslandCenterX = 0.69;                 // where the island stands, as a share of the width

    // Ball colours from the BS3D logo; each ramp is outline, shadow, base, light.
    private static readonly int[] Red = Ramp(0x6E121E, 0xB22230, 0xE23E42, 0xFF8478);
    private static readonly int[] Blue = Ramp(0x122C70, 0x225CBC, 0x4096EC, 0x96CEFF);
    private static readonly int[] Yellow = Ramp(0x824A00, 0xD68C0A, 0xFCC428, 0xFFEC8C);
    private static readonly int[] Green = Ramp(0x105422, 0x248E34, 0x54C448, 0xAAEC82);
    private static readonly int[][] BallColors = { Red, Blue, Yellow, Green };
    private static readonly int[][] BlinkColors = BallColors.Select(r => new[] { r[0], r[3], Highlight, Highlight }).ToArray();
    private const int Highlight = unchecked((int)0xFFFFFFFF);

    // Light direction in view space (x right, y up, z towards the viewer): from the upper left, in front.
    private static readonly V3 Light = new V3(-0.55, 0.65, 0.55).Normalized();
    private static readonly V3 HalfVector = (Light + new V3(0, 0, 1)).Normalized();

    private static readonly float[] BayerMatrix =
    {
        0, 8, 2, 10,
        12, 4, 14, 6,
        3, 11, 1, 9,
        15, 7, 13, 5,
    };

    // 5 x 7 pixel letters, drawn twice the size, each in its colour from the logo.
    private static readonly (string[] Rows, int Color)[] Letters =
    {
        (new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." }, 1),
        (new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." }, 3),
        (new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." }, 2),
        (new[] { "###..", "#..#.", "#...#", "#...#", "#...#", "#..#.", "###.." }, 0),
    };

    public int Width { get; }
    public int Height { get; }

    /// <summary>The last rendered frame, row by row, as 0xAARRGGBB (BGRA in memory: what both UI frameworks take).</summary>
    public int[] Pixels { get; }

    private readonly float[] _depth;
    private readonly List<Sphere> _spheres = new();
    private readonly List<(double X, double Z, double R2)> _shadows = new();
    private readonly V3[] _cluster;         // rest positions relative to the pivot
    private readonly int[] _clusterColor;   // index into BallColors
    private readonly Shot[] _shots;

    // Theme colours
    private readonly int _background, _floorA, _floorB, _floorRim, _floorShadow, _sideLight, _sideDark, _sideEdge;
    private readonly int _glass, _glassEdge, _cable, _letterEdge;
    private readonly int[] _steel, _steelBand, _smoke;

    // Camera of the frame being drawn
    private V3 _eye, _forward, _right, _up;
    private double _focal, _centerX, _centerY;

    public AboutScene(int width, int height, bool dark)
    {
        Width = Math.Max(16, width);
        Height = Math.Max(16, height);
        Pixels = new int[Width * Height];
        _depth = new float[Width * Height];

        // The background is the window's (Theme / AppTheme), so the scene has no visible edge.
        _background = Rgb(dark ? 0x1E1E1E : 0xFFFFFF);
        _floorA = Rgb(dark ? 0x45423E : 0xE6DFD2);
        _floorB = Rgb(dark ? 0x3B3936 : 0xD9D1C2);
        _floorRim = Rgb(dark ? 0x5E5A54 : 0xF4EFE6);
        _floorShadow = Rgb(dark ? 0x2A2928 : 0xBDB4A4);
        _sideLight = Rgb(dark ? 0x36342F : 0xC2B8A6);
        _sideDark = Rgb(dark ? 0x2B2926 : 0xA09684);
        _sideEdge = Rgb(dark ? 0x141414 : 0x766D5F);
        _glass = Rgb(dark ? 0x4A6678 : 0xBCDDF0);
        _glassEdge = Rgb(dark ? 0x6E94AC : 0x8CB6D2);
        _cable = Rgb(dark ? 0x6E6E6E : 0xA6A6A6);
        _letterEdge = Rgb(dark ? 0x0A0A0A : 0x2A2A3A);
        _steel = dark ? Ramp(0x101216, 0x4A505C, 0x737A8A, 0xA8B0C0) : Ramp(0x16181E, 0x3A3E48, 0x5E6472, 0x929AAA);
        _steelBand = dark ? Ramp(0x101216, 0x2C3038, 0x444A56, 0x6A7282) : Ramp(0x16181E, 0x22252C, 0x343842, 0x50566A);
        _smoke = dark ? Ramp(0x3C3C3C, 0x585858, 0x6E6E6E, 0x888888) : Ramp(0x969696, 0xBEBEBE, 0xD7D7D7, 0xEEEEEE);

        (_cluster, _clusterColor, _shots) = BuildCluster();
    }

    /// <summary>
    /// How to show the scene in a box <paramref name="devicePixels"/> wide: a whole-number zoom and the buffer size that
    /// fills the width at that zoom, so that every scene pixel is the same number of screen pixels.
    /// </summary>
    public static (int Zoom, int Width, int Height) Fit(double devicePixels)
    {
        var zoom = Math.Max(1, (int)(devicePixels / MinimumWidth));
        var width = Math.Max(16, (int)(devicePixels / zoom));
        return (zoom, width, (int)Math.Round(width * (double)DesignHeight / DesignWidth));
    }

    /// <summary>Draws the frame at <paramref name="seconds"/> into <see cref="Pixels"/>. Any time works; the scene loops.</summary>
    public void Render(double seconds)
    {
        var t = seconds % Loop;
        if (t < 0) t += Loop;
        var cycle = (int)(t / Period);
        var tc = t - cycle * Period;
        var shot = _shots[cycle];
        var next = _shots[(cycle + 1) % _shots.Length];

        SetCamera(t);
        var sway = Sway(t);
        _spheres.Clear();
        _shadows.Clear();
        AddCluster(shot, tc, sway, t);
        var muzzle = AddCannon(shot, next, tc);
        DrawGround(sway);
        foreach (var s in _spheres) DrawSphere(s);
        DrawCables(sway);
        if (tc >= FireAt && tc < FireAt + 0.09) DrawFlash(muzzle);
        DrawLetters(shot, tc);
    }

    // ---------------------------------------------------------------- the scene

    /// <summary>
    /// An upside-down square pyramid of 30 balls (4 x 4, 3 x 3, 2 x 2, 1), packed like oranges, and on each of its four
    /// sides a pair of touching balls of one colour that a shot completes to three.
    /// </summary>
    private static (V3[] Positions, int[] Colors, Shot[] Shots) BuildCluster()
    {
        var positions = new List<V3>();
        var index = new Dictionary<(int Layer, int J, int K), int>();
        var layerStep = BallRadius * Math.Sqrt(2);
        for (var layer = 0; layer < 4; layer++)
        {
            var n = 4 - layer;
            for (var j = 0; j < n; j++)
            {
                for (var k = 0; k < n; k++)
                {
                    index[(layer, j, k)] = positions.Count;
                    positions.Add(new V3(
                        (j - (n - 1) / 2.0) * 2 * BallRadius,
                        -BallRadius - 0.03 - layer * layerStep,
                        (k - (n - 1) / 2.0) * 2 * BallRadius));
                }
            }
        }

        var s3 = Math.Sqrt(3);
        var s2 = Math.Sqrt(2);
        // In the order the camera passes the sides: +z, +x, -z, -x. Each: a ball of the second layer and one of the third.
        var sides = new (int A, int B, V3 Normal, int Color)[]
        {
            (index[(1, 1, 2)], index[(2, 0, 1)], new V3(0, -1, s2) / s3, 0),
            (index[(1, 2, 1)], index[(2, 1, 1)], new V3(s2, -1, 0) / s3, 1),
            (index[(1, 1, 0)], index[(2, 1, 0)], new V3(0, -1, -s2) / s3, 2),
            (index[(1, 0, 1)], index[(2, 0, 0)], new V3(-s2, -1, 0) / s3, 3),
        };

        // The other balls get the four colours in equal shares, shuffled the same way every time; then any of them that
        // touches a pair in the pair's colour changes colour, so each shot really makes exactly three.
        var colors = new int[positions.Count];
        var paired = sides.SelectMany(s => new[] { s.A, s.B }).ToHashSet();
        var others = Enumerable.Range(0, positions.Count).Where(i => !paired.Contains(i)).ToArray();
        var deck = Enumerable.Range(0, others.Length).Select(i => i % 4).ToArray();
        var seed = 0x9E3779B9u;
        for (var i = deck.Length - 1; i > 0; i--)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            var j = (int)(seed % (uint)(i + 1));
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        for (var i = 0; i < others.Length; i++) colors[others[i]] = deck[i];
        foreach (var side in sides) colors[side.A] = colors[side.B] = side.Color;
        foreach (var i in others)
        {
            for (var attempt = 0; attempt < 4 && sides.Any(s => colors[i] == s.Color && (Touches(i, s.A) || Touches(i, s.B))); attempt++)
            {
                colors[i] = (colors[i] + 1) % 4;
            }
        }
        bool Touches(int a, int b) => (positions[a] - positions[b]).Length < 2.1 * BallRadius;

        var shots = sides.Select(side =>
        {
            // The shot settles in the dip between the pair, touching both.
            var target = (positions[side.A] + positions[side.B]) * 0.5 + side.Normal * (BallRadius * s3);
            var outward = new V3(target.X, 0, target.Z).Normalized();
            return new Shot(side.A, side.B, target, outward, side.Color, (Pivot + target - CannonPivot).Normalized());
        }).ToArray();
        return (positions.ToArray(), colors, shots);
    }

    private void SetCamera(double t)
    {
        // At each shot the camera is a little to the side of the face being hit, so the ball's way up is in view.
        var angle = 0.6 - Math.PI / 2 * (FireAt / Period) + 2 * Math.PI * t / Loop;
        _eye = new V3(CameraDistance * Math.Sin(angle), CameraHeight, CameraDistance * Math.Cos(angle));
        _forward = (new V3(0, LookAtHeight, 0) - _eye).Normalized();
        _right = V3.Cross(_forward, new V3(0, 1, 0)).Normalized();
        _up = V3.Cross(_right, _forward);
        _focal = Height * 0.5 / Math.Tan(HalfFieldOfView);
        _centerX = Width * IslandCenterX;
        _centerY = Height * 0.5;
    }

    /// <summary>
    /// How the plate and the cluster hang: a slow idle sway, and a damped swing after each hit that pushes the bottom
    /// away from the cannon. Every term repeats within <see cref="Loop"/>, so the loop has no seam.
    /// </summary>
    private M3 Sway(double t)
    {
        var w = new V3(
            0.012 * Math.Sin(2 * Math.PI * 3 * t / Loop),
            0,
            0.012 * Math.Sin(2 * Math.PI * 2 * t / Loop + 1.3));
        for (var back = 0; back < 2; back++)
        {
            var cycle = ((int)(t / Period) - back + _shots.Length) % _shots.Length;
            var tau = t % Period + back * Period - HitAt;
            if (tau < 0) continue;
            var o = _shots[cycle].Outward;
            w += new V3(-o.Z, 0, o.X) * (0.11 * Math.Exp(-2.4 * tau) * Math.Sin(2 * Math.PI * 1.4 * tau));
        }
        return M3.Rotation(w);
    }

    private void AddCluster(Shot shot, double tc, M3 sway, double t)
    {
        var regrow = tc > Period - RegrowTime ? EaseOutBack((tc - (Period - RegrowTime)) / RegrowTime) : 0;
        var blinking = tc >= HitAt && tc < DropAt && (int)((tc - HitAt) / 0.06) % 2 == 0;
        for (var i = 0; i < _cluster.Length; i++)
        {
            var matched = i == shot.A || i == shot.B;
            var radius = BallRadius;
            if (matched && tc >= DropAt)
            {
                if (regrow <= 0) continue;
                radius *= regrow;
            }
            var ramp = matched && blinking ? BlinkColors[_clusterColor[i]] : BallColors[_clusterColor[i]];
            AddBall(Pivot + sway * _cluster[i], radius, ramp, 1);
        }

        if (tc >= FireAt && tc < HitAt)
        {
            var muzzle = CannonPivot + shot.Aim * BarrelLength;
            var u = (tc - FireAt) / FlightTime;
            AddBall(muzzle + (Pivot + sway * shot.Target - muzzle) * u, BallRadius, BallColors[shot.Color], 1);
        }
        else if (tc >= HitAt && tc < DropAt)
        {
            AddBall(Pivot + sway * shot.Target, BallRadius, blinking ? BlinkColors[shot.Color] : BallColors[shot.Color], 1);
        }
        else if (tc >= DropAt)
        {
            // The three fall from where they hung at the drop, thrown a little outwards, and bounce on the island
            // (or off its edge).
            var tau = tc - DropAt;
            var alpha = Math.Clamp(1 - (tau - FadeAt) / FadeTime, 0, 1);
            if (alpha <= 0) return;
            var dropSway = Sway(t - tc + DropAt);
            var tangent = new V3(-shot.Outward.Z, 0, shot.Outward.X);
            var color = BallColors[shot.Color];
            AddBall(Fall(Pivot + dropSway * _cluster[shot.A], shot.Outward * 1.1 + tangent * 0.7, tau), BallRadius, color, alpha);
            AddBall(Fall(Pivot + dropSway * _cluster[shot.B], shot.Outward * 0.9 - tangent * 0.6, tau), BallRadius, color, alpha);
            AddBall(Fall(Pivot + dropSway * shot.Target, shot.Outward * 1.5, tau), BallRadius, color, alpha);
        }
    }

    /// <summary>Where a ball dropped with the given velocity is after <paramref name="tau"/> seconds, bouncing on the island.</summary>
    private static V3 Fall(V3 start, V3 velocity, double tau)
    {
        // A fixed step, so a moment always looks the same whatever the frame rate.
        const double step = 1 / 200.0;
        var p = start;
        var v = velocity + new V3(0, 1.1, 0);
        for (var elapsed = 0.0; elapsed < tau; elapsed += step)
        {
            v += new V3(0, -Gravity * step, 0);
            p += v * step;
            if (p.Y < BallRadius && v.Y < 0 && p.X * p.X + p.Z * p.Z < IslandRadius * IslandRadius)
            {
                p = new V3(p.X, BallRadius, p.Z);
                v = new V3(v.X * 0.7, -v.Y * 0.45, v.Z * 0.7);
            }
        }
        return p;
    }

    private void AddBall(V3 center, double radius, int[] ramp, double alpha)
    {
        _spheres.Add(new Sphere(center, radius, ramp, alpha));
        // Light from straight above: the shadow on the island is the ball's footprint, a little smaller.
        var r = radius * 0.95;
        _shadows.Add((center.X, center.Z, r * r));
    }

    /// <summary>The cannon: a steel turret on the island and a barrel of overlapping steel balls, aimed at the shot.</summary>
    private V3 AddCannon(Shot shot, Shot next, double tc)
    {
        var turn = Math.Clamp((tc - (Period - 1.3)) / 0.8, 0, 1);
        var aim = turn > 0 ? (shot.Aim + (next.Aim - shot.Aim) * SmoothStep(turn)).Normalized() : shot.Aim;
        var recoil = tc >= FireAt ? 0.25 * Math.Exp(-7 * (tc - FireAt)) * Math.Min(1, (tc - FireAt) / 0.03) : 0;

        _spheres.Add(new Sphere(Dome, DomeRadius, _steel, 1, Matte: true, AboveFloor: true));
        for (var d = 0.0; d <= BarrelLength + 1e-9; d += 0.1)
        {
            // A darker band at the mouth
            var ramp = d > BarrelLength - 0.25 ? _steelBand : _steel;
            var radius = BreechRadius + (MuzzleRadius - BreechRadius) * d / BarrelLength;
            _spheres.Add(new Sphere(CannonPivot + aim * (d - recoil), radius, ramp, 1, Matte: true));
        }
        var muzzle = CannonPivot + aim * (BarrelLength - recoil);

        // The ball waiting in the mouth: this shot's until it is fired, then the next one's, popping in.
        if (tc < FireAt) _spheres.Add(new Sphere(muzzle, 0.3, BallColors[shot.Color], 1));
        else if (tc >= FireAt + 0.9)
        {
            var grow = EaseOutBack(Math.Min(1, (tc - FireAt - 0.9) / 0.2));
            _spheres.Add(new Sphere(muzzle, 0.3 * grow, BallColors[next.Color], 1));
        }

        // Smoke from the shot: three puffs that drift up and thin out.
        var tau = tc - FireAt;
        if (tau >= 0 && tau < 0.9)
        {
            var side = V3.Cross(aim, new V3(0, 1, 0)).Normalized();
            for (var i = 0; i < 3; i++)
            {
                var offset = side * (0.25 * Math.Sin(i * 2.1 + 0.4)) + new V3(0, (0.3 + 0.25 * i) * tau, 0);
                _spheres.Add(new Sphere(muzzle + aim * (0.25 + (0.5 + 0.3 * i) * tau) + offset, 0.12 + 0.28 * tau, _smoke, 1 - tau / 0.9));
            }
        }
        return muzzle;
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>Per pixel: background, the island's top (tiles, rim, the balls' shadows) and side, and the glass plate.</summary>
    private void DrawGround(M3 sway)
    {
        var inverse = sway.Transposed();
        var localEye = inverse * (_eye - Pivot);
        var lightWorld = (_right * -0.55 + _up * 0.65 - _forward * 0.55).Normalized();
        var r2 = IslandRadius * IslandRadius;
        var rim2 = (IslandRadius - 0.18) * (IslandRadius - 0.18);

        for (var py = 0; py < Height; py++)
        {
            var b = (_centerY - (py + 0.5)) / _focal;
            for (var px = 0; px < Width; px++)
            {
                var a = (px + 0.5 - _centerX) / _focal;
                // Not normalized: its forward component is 1, so the ray parameter is the depth.
                var dir = _forward + _right * a + _up * b;
                var threshold = Bayer(px, py);
                var color = _background;
                var depth = float.PositiveInfinity;

                if (dir.Y < 0)
                {
                    var t = -_eye.Y / dir.Y;
                    var x = _eye.X + dir.X * t;
                    var z = _eye.Z + dir.Z * t;
                    var d2 = x * x + z * z;
                    if (d2 <= r2)
                    {
                        depth = (float)t;
                        color = d2 > rim2 ? _floorRim
                            : InShadow(x, z, threshold) ? _floorShadow
                            : (((int)Math.Floor(x / 0.8) + (int)Math.Floor(z / 0.8)) & 1) == 0 ? _floorA : _floorB;
                    }
                }
                if (float.IsPositiveInfinity(depth))
                {
                    // The island's side, a short cylinder below its top
                    var qa = dir.X * dir.X + dir.Z * dir.Z;
                    var qb = 2 * (_eye.X * dir.X + _eye.Z * dir.Z);
                    var qc = _eye.X * _eye.X + _eye.Z * _eye.Z - r2;
                    var disc = qb * qb - 4 * qa * qc;
                    if (disc >= 0)
                    {
                        var t = (-qb - Math.Sqrt(disc)) / (2 * qa);
                        var y = _eye.Y + dir.Y * t;
                        if (t > 0 && y <= 0 && y >= -IslandDepth)
                        {
                            depth = (float)t;
                            var normal = new V3(_eye.X + dir.X * t, 0, _eye.Z + dir.Z * t) / IslandRadius;
                            var lit = 0.35 + 0.65 * Math.Max(0, V3.Dot(normal, lightWorld));
                            color = y < -IslandDepth + 0.07 ? _sideEdge : lit > threshold ? _sideLight : _sideDark;
                        }
                    }
                }

                // The glass plate, in the cluster's swinging frame: half its pixels for the glass, solid for its edge.
                var localDir = inverse * dir;
                if (Math.Abs(localDir.Y) > 1e-9)
                {
                    var t = -localEye.Y / localDir.Y;
                    if (t > 0 && t < depth)
                    {
                        var x = Math.Abs(localEye.X + localDir.X * t);
                        var z = Math.Abs(localEye.Z + localDir.Z * t);
                        if (x <= PlateHalf && z <= PlateHalf)
                        {
                            if (Math.Max(x, z) > PlateHalf - 0.14)
                            {
                                color = _glassEdge;
                                depth = (float)t;
                            }
                            else if (((px + py) & 1) == 0)
                            {
                                color = _glass;
                                depth = (float)t;
                            }
                        }
                    }
                }

                var i = py * Width + px;
                Pixels[i] = color;
                _depth[i] = depth;
            }
        }
    }

    /// <summary>Whether the floor point is under a ball, with a dithered edge.</summary>
    private bool InShadow(double x, double z, float threshold)
    {
        foreach (var (sx, sz, r2) in _shadows)
        {
            var dx = x - sx;
            var dz = z - sz;
            var d2 = dx * dx + dz * dz;
            if (d2 < r2 * 0.55 || (d2 < r2 && threshold < 0.5)) return true;
        }
        return false;
    }

    /// <summary>A ball as a flat disc with a per-pixel normal and depth: shaded in three steps by ordered dithering, outlined.</summary>
    private void DrawSphere(Sphere s)
    {
        var v = s.Center - _eye;
        var z = V3.Dot(v, _forward);
        if (z < 0.5) return;
        var cx = _centerX + V3.Dot(v, _right) / z * _focal;
        var cy = _centerY - V3.Dot(v, _up) / z * _focal;
        var r = s.Radius * _focal / z;
        if (r < 0.4) return;
        var x0 = Math.Max(0, (int)Math.Floor(cx - r));
        var x1 = Math.Min(Width - 1, (int)Math.Ceiling(cx + r));
        var y0 = Math.Max(0, (int)Math.Floor(cy - r));
        var y1 = Math.Min(Height - 1, (int)Math.Ceiling(cy + r));
        var edge = Math.Max(0, 1 - 1.15 / r);
        var edge2 = edge * edge;

        for (var py = y0; py <= y1; py++)
        {
            var dy = (py + 0.5 - cy) / r;
            for (var px = x0; px <= x1; px++)
            {
                var dx = (px + 0.5 - cx) / r;
                var d2 = dx * dx + dy * dy;
                if (d2 > 1) continue;
                var nz = Math.Sqrt(1 - d2);
                var depth = (float)(z - s.Radius * nz);
                var i = py * Width + px;
                if (depth >= _depth[i]) continue;
                var threshold = Bayer(px, py);
                if (threshold >= s.Alpha) continue;
                if (s.AboveFloor)
                {
                    // The point's height, from its place in view space
                    var height = _eye.Y + (_right.Y * (px + 0.5 - _centerX) + _up.Y * (_centerY - py - 0.5)) / _focal * depth + _forward.Y * depth;
                    if (height < 0) continue;
                }
                _depth[i] = depth;

                if (d2 > edge2)
                {
                    Pixels[i] = s.Ramp[0];
                    continue;
                }
                var diffuse = Math.Max(0, dx * Light.X - dy * Light.Y + nz * Light.Z);
                if (!s.Matte)
                {
                    var h = Math.Max(0, dx * HalfVector.X - dy * HalfVector.Y + nz * HalfVector.Z);
                    var h4 = h * h * h * h;
                    var specular = h4 * h4 * h4 * h4 * h4; // h^20
                    if (specular > 0.6 + threshold * 0.3)
                    {
                        Pixels[i] = Highlight;
                        continue;
                    }
                }
                var shade = 1 + (0.15 + 0.85 * diffuse) * 2;
                var level = (int)shade;
                if (shade - level > threshold) level++;
                Pixels[i] = s.Ramp[Math.Min(3, level)];
            }
        }
    }

    /// <summary>The plate's four cables, up out of the picture.</summary>
    private void DrawCables(M3 sway)
    {
        for (var c = 0; c < 4; c++)
        {
            var corner = new V3((c & 1) == 0 ? PlateHalf * 0.8 : -PlateHalf * 0.8, 0, c < 2 ? PlateHalf * 0.8 : -PlateHalf * 0.8);
            DrawLine(Pivot + sway * corner, Pivot + corner + new V3(0, 6, 0), _cable);
        }
    }

    private void DrawLine(V3 from, V3 to, int color)
    {
        const int steps = 96;
        for (var k = 0; k <= steps; k++)
        {
            var v = from + (to - from) * (k / (double)steps) - _eye;
            var z = V3.Dot(v, _forward);
            if (z < 0.5) continue;
            var px = (int)Math.Floor(_centerX + V3.Dot(v, _right) / z * _focal);
            var py = (int)Math.Floor(_centerY - V3.Dot(v, _up) / z * _focal);
            if (px < 0 || py < 0 || px >= Width || py >= Height) continue;
            var i = py * Width + px;
            if (z < _depth[i]) Pixels[i] = color;
        }
    }

    /// <summary>The muzzle flash: a small star for a couple of frames.</summary>
    private void DrawFlash(V3 muzzle)
    {
        var v = muzzle - _eye;
        var z = V3.Dot(v, _forward);
        if (z < 0.5) return;
        var cx = (int)Math.Floor(_centerX + V3.Dot(v, _right) / z * _focal);
        var cy = (int)Math.Floor(_centerY - V3.Dot(v, _up) / z * _focal);
        for (var d = -2; d <= 2; d++)
        {
            var color = d == 0 ? Highlight : Math.Abs(d) == 1 ? Yellow[3] : Yellow[2];
            Plot(cx + d, cy, color);
            Plot(cx, cy + d, color);
        }
        foreach (var (dx, dy) in new[] { (1, 1), (-1, -1), (1, -1), (-1, 1) }) Plot(cx + dx, cy + dy, Yellow[2]);
    }

    /// <summary>
    /// "BS3D" in pixel letters on the left, twice the size, outlined. A glint runs over them at the start of each shot,
    /// and when three drop the letters hop one after another, the one in the colour cleared the highest.
    /// </summary>
    private void DrawLetters(Shot shot, double tc)
    {
        const int scale = 2, advance = 12, width = 3 * advance + 5 * scale;
        // Centred in the room left of the plate, whose half width is about a quarter of the picture's
        var left = Math.Max(1, (int)((Width * (IslandCenterX - 0.235) - width) / 2));
        var top = Height / 2 - 7 * scale / 2 - 1;
        var glint = (tc - 0.15) * 110 - 10; // a diagonal line x + y = glint, moving right

        for (var pass = 0; pass < 2; pass++)
        {
            for (var l = 0; l < Letters.Length; l++)
            {
                var (rows, colorIndex) = Letters[l];
                var ramp = BallColors[colorIndex];
                var tau = tc - DropAt - 0.07 * l;
                var hop = tau >= 0 && tau < 0.2 ? (colorIndex == shot.Color ? 3 : 2) : 0;
                var x0 = left + l * advance;
                var y0 = top - hop;
                for (var row = 0; row < rows.Length * scale; row++)
                {
                    for (var col = 0; col < 5 * scale; col++)
                    {
                        if (rows[row / scale][col / scale] != '#') continue;
                        var x = x0 + col;
                        var y = y0 + row;
                        if (pass == 0)
                        {
                            // Outline: one pixel all round
                            for (var dy = -1; dy <= 1; dy++)
                            {
                                for (var dx = -1; dx <= 1; dx++) Plot(x + dx, y + dy, _letterEdge);
                            }
                            continue;
                        }
                        var color = row < 4 ? ramp[3] : row < 10 ? ramp[2] : ramp[1];
                        var g = x - left + y - top - glint;
                        if (g >= 0 && g < 3) color = Highlight;
                        Plot(x, y, color);
                    }
                }
            }
        }
    }

    private void Plot(int x, int y, int color)
    {
        if (x >= 0 && y >= 0 && x < Width && y < Height) Pixels[y * Width + x] = color;
    }

    // ---------------------------------------------------------------- helpers

    private static float Bayer(int x, int y) => (BayerMatrix[((y & 3) << 2) | (x & 3)] + 0.5f) / 16f;

    private static int Rgb(int rgb) => unchecked((int)0xFF000000) | rgb;

    private static int[] Ramp(int outline, int shadow, int baseColor, int light) => new[] { Rgb(outline), Rgb(shadow), Rgb(baseColor), Rgb(light) };

    private static double SmoothStep(double x) => x * x * (3 - 2 * x);

    private static double EaseOutBack(double x)
    {
        x = Math.Clamp(x, 0, 1) - 1;
        return 1 + x * x * (2.7 * x + 1.7);
    }

    /// <summary>A ball to draw. Matte: no white highlight. AboveFloor: the part below the island's top is left out.</summary>
    private readonly record struct Sphere(V3 Center, double Radius, int[] Ramp, double Alpha, bool Matte = false, bool AboveFloor = false);

    /// <summary>One shot: the pair it completes (cluster indices), where it settles (relative to the pivot), and where the cannon points.</summary>
    private readonly record struct Shot(int A, int B, V3 Target, V3 Outward, int Color, V3 Aim);

    private readonly record struct V3(double X, double Y, double Z)
    {
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static V3 operator /(V3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
        public static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(Dot(this, this));
        public V3 Normalized() => this / Length;
    }

    /// <summary>A 3 x 3 rotation matrix, rows first.</summary>
    private readonly record struct M3(V3 R0, V3 R1, V3 R2)
    {
        public static V3 operator *(M3 m, V3 v) => new(V3.Dot(m.R0, v), V3.Dot(m.R1, v), V3.Dot(m.R2, v));

        public M3 Transposed() => new(new V3(R0.X, R1.X, R2.X), new V3(R0.Y, R1.Y, R2.Y), new V3(R0.Z, R1.Z, R2.Z));

        /// <summary>The rotation by |w| radians about w (Rodrigues).</summary>
        public static M3 Rotation(V3 w)
        {
            var angle = w.Length;
            if (angle < 1e-9) return new M3(new V3(1, 0, 0), new V3(0, 1, 0), new V3(0, 0, 1));
            var (x, y, z) = (w.X / angle, w.Y / angle, w.Z / angle);
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            var k = 1 - c;
            return new M3(
                new V3(c + x * x * k, x * y * k - z * s, x * z * k + y * s),
                new V3(y * x * k + z * s, c + y * y * k, y * z * k - x * s),
                new V3(z * x * k - y * s, z * y * k + x * s, c + z * z * k));
        }
    }
}
