using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The floor calibration itself: a mapping from "where the ZED says a person is" to
/// "where that is on the projected floor", worked out from pairs of points.
///
/// Both are positions on the same flat floor, so one mapping (a homography) fixes the
/// camera's position, angle, height and scale AND a slightly skewed floor projector, all at once.
///
/// Saved as a small text file in the project folder (next to Assets), so it loads
/// automatically until you calibrate again. No need to edit this script.
/// </summary>
[Serializable]
public class FloorCalibration
{
    public const string DefaultFileName = "floor_calibration.json";

    // The 3x3 mapping, row by row (works on X/Z floor positions).
    public float[] h = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
    public string savedAt = "";
    public int pointsUsed;
    public float averageError;   // metres (floor units)
    public float worstError;

    public bool IsIdentity => h[0] == 1 && h[1] == 0 && h[2] == 0 && h[3] == 0 && h[4] == 1 && h[5] == 0 && h[6] == 0 && h[7] == 0;

    /// <summary>Tracked (ZED) position → floor position. Height (Y) is kept as it is.</summary>
    public Vector3 Map(Vector3 tracked)
    {
        Vector2 p = Map(new Vector2(tracked.x, tracked.z));
        return new Vector3(p.x, tracked.y, p.y);
    }

    public Vector2 Map(Vector2 p)
    {
        float w = h[6] * p.x + h[7] * p.y + h[8];
        if (Mathf.Abs(w) < 1e-6f) w = 1e-6f;
        return new Vector2((h[0] * p.x + h[1] * p.y + h[2]) / w,
                           (h[3] * p.x + h[4] * p.y + h[5]) / w);
    }

    // ---------------- saving / loading ----------------

    public static string PathFor(string fileName) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", string.IsNullOrEmpty(fileName) ? DefaultFileName : fileName));

    public void Save(string fileName)
    {
        savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        File.WriteAllText(PathFor(fileName), JsonUtility.ToJson(this, true));
    }

    /// <summary>Returns null if there's no saved calibration yet.</summary>
    public static FloorCalibration Load(string fileName)
    {
        string path = PathFor(fileName);
        if (!File.Exists(path)) return null;
        try
        {
            FloorCalibration c = JsonUtility.FromJson<FloorCalibration>(File.ReadAllText(path));
            return c != null && c.h != null && c.h.Length == 9 ? c : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("FloorCalibration: couldn't read " + path + ": " + e.Message);
            return null;
        }
    }

    // ---------------- working it out from point pairs ----------------

    /// <summary>
    /// Works out the mapping from pairs of (tracked position, floor position), X/Z only.
    /// Needs at least 4 well-spread pairs; more pairs average out tracking jitter.
    /// Returns null if the points can't give an answer (e.g. all in a line).
    /// </summary>
    public static FloorCalibration Solve(IList<Vector2> tracked, IList<Vector2> floor)
    {
        int n = Mathf.Min(tracked.Count, floor.Count);
        if (n < 4) return null;

        // Normalise both point sets (centre them, scale to ~1) for a stable result.
        double[,] ts = NormaliseTransform(tracked, n);
        double[,] tf = NormaliseTransform(floor, n);

        // Least squares: 8 unknowns, 2 equations per point pair.
        double[,] ata = new double[8, 8];
        double[] atb = new double[8];
        double[] row = new double[8];
        for (int i = 0; i < n; i++)
        {
            Apply(ts, tracked[i].x, tracked[i].y, out double x, out double y);
            Apply(tf, floor[i].x, floor[i].y, out double u, out double v);

            row[0] = x; row[1] = y; row[2] = 1; row[3] = 0; row[4] = 0; row[5] = 0; row[6] = -u * x; row[7] = -u * y;
            Accumulate(ata, atb, row, u);
            row[0] = 0; row[1] = 0; row[2] = 0; row[3] = x; row[4] = y; row[5] = 1; row[6] = -v * x; row[7] = -v * y;
            Accumulate(ata, atb, row, v);
        }

        double[] s = SolveLinear(ata, atb);
        if (s == null) return null;

        double[,] hn = { { s[0], s[1], s[2] }, { s[3], s[4], s[5] }, { s[6], s[7], 1 } };
        double[,] full = Multiply(Invert3(tf), Multiply(hn, ts));
        if (full == null) return null;

        double scale = Math.Abs(full[2, 2]) > 1e-12 ? full[2, 2] : 1;
        FloorCalibration c = new FloorCalibration { pointsUsed = n };
        for (int r = 0; r < 3; r++)
            for (int k = 0; k < 3; k++)
                c.h[r * 3 + k] = (float)(full[r, k] / scale);
        return c;
    }

    private static void Accumulate(double[,] ata, double[] atb, double[] row, double b)
    {
        for (int i = 0; i < 8; i++)
        {
            atb[i] += row[i] * b;
            for (int j = 0; j < 8; j++) ata[i, j] += row[i] * row[j];
        }
    }

    private static double[,] NormaliseTransform(IList<Vector2> pts, int n)
    {
        double cx = 0, cy = 0;
        for (int i = 0; i < n; i++) { cx += pts[i].x; cy += pts[i].y; }
        cx /= n; cy /= n;
        double d = 0;
        for (int i = 0; i < n; i++) d += Math.Sqrt((pts[i].x - cx) * (pts[i].x - cx) + (pts[i].y - cy) * (pts[i].y - cy));
        d /= n;
        double k = d > 1e-9 ? Math.Sqrt(2) / d : 1;
        return new double[,] { { k, 0, -k * cx }, { 0, k, -k * cy }, { 0, 0, 1 } };
    }

    private static void Apply(double[,] m, double x, double y, out double ox, out double oy)
    {
        ox = m[0, 0] * x + m[0, 1] * y + m[0, 2];
        oy = m[1, 0] * x + m[1, 1] * y + m[1, 2];
    }

    // Gaussian elimination with partial pivoting.
    private static double[] SolveLinear(double[,] a, double[] b)
    {
        int n = b.Length;
        double[,] m = (double[,])a.Clone();
        double[] v = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-12) return null;
            if (pivot != col)
            {
                for (int k = 0; k < n; k++) { double t = m[col, k]; m[col, k] = m[pivot, k]; m[pivot, k] = t; }
                double tv = v[col]; v[col] = v[pivot]; v[pivot] = tv;
            }
            for (int r = col + 1; r < n; r++)
            {
                double f = m[r, col] / m[col, col];
                for (int k = col; k < n; k++) m[r, k] -= f * m[col, k];
                v[r] -= f * v[col];
            }
        }
        double[] x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double sum = v[r];
            for (int k = r + 1; k < n; k++) sum -= m[r, k] * x[k];
            x[r] = sum / m[r, r];
        }
        return x;
    }

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        if (a == null || b == null) return null;
        double[,] r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return r;
    }

    private static double[,] Invert3(double[,] m)
    {
        double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
                   - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                   + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        if (Math.Abs(det) < 1e-12) return null;
        double k = 1 / det;
        return new double[,]
        {
            { (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) * k, (m[0, 2] * m[2, 1] - m[0, 1] * m[2, 2]) * k, (m[0, 1] * m[1, 2] - m[0, 2] * m[1, 1]) * k },
            { (m[1, 2] * m[2, 0] - m[1, 0] * m[2, 2]) * k, (m[0, 0] * m[2, 2] - m[0, 2] * m[2, 0]) * k, (m[0, 2] * m[1, 0] - m[0, 0] * m[1, 2]) * k },
            { (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]) * k, (m[0, 1] * m[2, 0] - m[0, 0] * m[2, 1]) * k, (m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]) * k },
        };
    }
}
