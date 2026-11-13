using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MiniExcelLibs;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using GdiFont = System.Drawing.Font;
using GdiPen = System.Drawing.Pen;

namespace CrackMeasurePro
{
    public readonly record struct ProfileSample(float Sx, float Sy, float WidthPx, float P1x, float P1y, float P2x, float P2y);

    public sealed class CrackRecord
    {
        public int Id { get; set; }
        public Rect BBox { get; set; }
        public List<Point2f> SkelPts { get; set; } = new();
        public List<ProfileSample> ProfilePts { get; set; } = new();
        public Point2f WmaxPt { get; set; }
        public double AngleDeg { get; set; }
        public string Orientation { get; set; } = "-";
        public double Conf { get; set; }
        public int AreaPx { get; set; }
        public double LenPx { get; set; }
        public double AvgWPx { get; set; }
        public double MaxWPx { get; set; }
        public int Branches { get; set; }
        public string Unit { get; set; } = "px";
        public double LenVal { get; set; }
        public double AvgWVal { get; set; }
        public double MaxWVal { get; set; }
        public double AreaVal { get; set; }
    }

    public sealed class ImageSummary
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string Mode { get; set; } = "";
        public double ProbThresh { get; set; }
        public double MmPerPx { get; set; }
        public int BridgePx { get; set; }
        public string Unit { get; set; } = "px";
        public int CrackCount { get; set; }
        public double TotalLenVal { get; set; }
        public double MaxWVal { get; set; }
        public double AvgWVal { get; set; }
        public int TotalAreaPx { get; set; }
        public double TotalAreaVal { get; set; }
        public int TotalBranches { get; set; }
    }

    public sealed class QueueItem
    {
        public string Id { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Status { get; set; } = "待检测";
        public ImageSummary? Summary { get; set; }
        public List<CrackRecord> Cracks { get; set; } = new();
        public List<CvPoint> BranchPts { get; set; } = new();
    }

    public sealed class CacheEntry : IDisposable
    {
        public Mat? OrigImg { get; set; }
        public Mat? ProbU8 { get; set; }
        public string ModeTag { get; set; } = "推理";
        public Mat? OverlayImg { get; set; }
        public Mat? MaskImg { get; set; }
        public void Dispose() { OrigImg?.Dispose(); ProbU8?.Dispose(); OverlayImg?.Dispose(); MaskImg?.Dispose(); }
    }

    public sealed class AnalysisOutput
    {
        public required Mat OverlayImg { get; init; }
        public required Mat MaskImg { get; init; }
        public required List<CvPoint> BranchPts { get; init; }
        public required ImageSummary Summary { get; init; }
        public required List<CrackRecord> Cracks { get; init; }
    }

    public sealed class LabelLayout
    {
        public int Id { get; init; }
        public RectangleF BBox { get; init; }
        public RectangleF Rect { get; init; }
        public string Text { get; init; } = "";
        public (float X1, float Y1, float X2, float Y2)? Leader { get; init; }
    }

    internal static class MatBuf
    {
        public static byte[] GetBytes(Mat m)
        {
            int h = m.Rows, rowBytes = m.Cols * m.Channels();
            byte[] dst = new byte[h * rowBytes];
            long step = m.Step();
            if (step == rowBytes) Marshal.Copy(m.Data, dst, 0, dst.Length);
            else for (int y = 0; y < h; y++) Marshal.Copy(m.Data + (nint)(y * step), dst, y * rowBytes, rowBytes);
            return dst;
        }

        public static float[] GetFloats(Mat m)
        {
            int h = m.Rows, rowElems = m.Cols * m.Channels();
            float[] dst = new float[h * rowElems];
            long stepBytes = m.Step();
            if (stepBytes == rowElems * sizeof(float)) Marshal.Copy(m.Data, dst, 0, dst.Length);
            else for (int y = 0; y < h; y++) Marshal.Copy(m.Data + (nint)(y * stepBytes), dst, y * rowElems, rowElems);
            return dst;
        }

        public static Mat FromBytes(int h, int w, byte[] src)
        {
            Mat m = new(h, w, MatType.CV_8UC1);
            long step = m.Step();
            if (step == w) Marshal.Copy(src, 0, m.Data, src.Length);
            else for (int y = 0; y < h; y++) Marshal.Copy(src, y * w, m.Data + (nint)(y * step), w);
            return m;
        }

        public static Mat FromFloats(int h, int w, float[] src)
        {
            Mat m = new(h, w, MatType.CV_32FC1);
            long stepBytes = m.Step();
            if (stepBytes == w * sizeof(float)) Marshal.Copy(src, 0, m.Data, src.Length);
            else for (int y = 0; y < h; y++) Marshal.Copy(src, y * w, m.Data + (nint)(y * stepBytes), w);
            return m;
        }
    }

    public static class ImgCodec
    {
        public static readonly string[] SupportedExts = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp" };

        public static string ResolveAsset(string fileName)
        {
            string exeDir = AppContext.BaseDirectory;
            string p1 = Path.Combine(exeDir, fileName);
            if (File.Exists(p1)) return p1;
            string cwd = Directory.GetCurrentDirectory();
            string p2 = Path.Combine(cwd, fileName);
            if (File.Exists(p2)) return p2;
            string p2Models = Path.Combine(cwd, "models", fileName);
            if (File.Exists(p2Models)) return p2Models;
            DirectoryInfo? dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string pUp = Path.Combine(dir.FullName, fileName);
                if (File.Exists(pUp)) return pUp;
                string pUpModels = Path.Combine(dir.FullName, "models", fileName);
                if (File.Exists(pUpModels)) return pUpModels;
            }
            return p1;
        }

        public static Mat? ReadBgr(string filePath)
        {
            try
            {
                byte[] raw = File.ReadAllBytes(filePath);
                if (raw.Length == 0) return null;
                Mat m = Cv2.ImDecode(raw, ImreadModes.Color);
                return m.Empty() ? null : m;
            }
            catch { return null; }
        }

        public static string WriteImg(string savePath, Mat img)
        {
            string ext = Path.GetExtension(savePath).ToLowerInvariant();
            if (!SupportedExts.Contains(ext)) { ext = ".jpg"; savePath += ext; }
            int[] prms = (ext == ".jpg" || ext == ".jpeg") ? new[] { (int)ImwriteFlags.JpegQuality, 95 } : Array.Empty<int>();
            if (!Cv2.ImEncode(ext, img, out byte[] buf, prms)) throw new InvalidOperationException($"图像编码失败: {ext}");
            File.WriteAllBytes(savePath, buf);
            return savePath;
        }

        public static byte[]? CompressMask(Mat maskU8) => Cv2.ImEncode(".png", maskU8, out byte[] buf) ? buf : null;

        public static Mat? DecompressMask(byte[]? pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0) return null;
            Mat m = Cv2.ImDecode(pngBytes, ImreadModes.Grayscale);
            return m.Empty() ? null : m;
        }

        public static string ToJpegBase64(Mat imgBgr, int maxSide = 960, int quality = 88)
        {
            Mat work = imgBgr; bool cloned = false;
            try
            {
                int h = imgBgr.Rows, w = imgBgr.Cols;
                if (Math.Max(h, w) > maxSide)
                {
                    double s = (double)maxSide / Math.Max(h, w);
                    work = new Mat(); cloned = true;
                    Cv2.Resize(imgBgr, work, new CvSize(Math.Max(1, (int)(w * s)), Math.Max(1, (int)(h * s))), 0, 0, InterpolationFlags.Area);
                }
                Cv2.ImEncode(".jpg", work, out byte[] buf, new[] { (int)ImwriteFlags.JpegQuality, quality });
                return Convert.ToBase64String(buf);
            }
            finally { if (cloned) work.Dispose(); }
        }
    }

    public static class SkeletonEngine
    {
        private static readonly bool[] LutZs0 = new bool[256], LutZs1 = new bool[256], LutStair0 = new bool[256], LutStair1 = new bool[256];
        private static readonly byte[] LutDeg = new byte[256], LutCn = new byte[256];

        static SkeletonEngine()
        {
            for (int c = 0; c < 256; c++)
            {
                int p2 = (c >> 0) & 1, p3 = (c >> 1) & 1, p4 = (c >> 2) & 1, p5 = (c >> 3) & 1;
                int p6 = (c >> 4) & 1, p7 = (c >> 5) & 1, p8 = (c >> 6) & 1, p9 = (c >> 7) & 1;
                int[] ring = { p2, p3, p4, p5, p6, p7, p8, p9 };
                int b = ring.Sum(), cn = 0;
                for (int i = 0; i < 8; i++) if (ring[i] == 0 && ring[(i + 1) & 7] == 1) cn++;
                LutDeg[c] = (byte)b; LutCn[c] = (byte)cn;
                if (cn == 1 && b >= 2 && b <= 6)
                {
                    if (p2 * p4 * p6 == 0 && p4 * p6 * p8 == 0) LutZs0[c] = true;
                    if (p2 * p4 * p8 == 0 && p2 * p6 * p8 == 0) LutZs1[c] = true;
                }
                LutStair0[c] = (p2 == 1 && p4 == 1 && (p3 | p6 | p7 | p8) == 0) || (p2 == 1 && p8 == 1 && (p9 | p4 | p5 | p6) == 0);
                LutStair1[c] = (p6 == 1 && p4 == 1 && (p5 | p2 | p8 | p9) == 0) || (p6 == 1 && p8 == 1 && (p7 | p2 | p3 | p4) == 0);
            }
        }

        private static int EncodeRing(byte[] pad, int idx, int stride) =>
            pad[idx - stride] | (pad[idx - stride + 1] << 1) | (pad[idx + 1] << 2) | (pad[idx + stride + 1] << 3) |
            (pad[idx + stride] << 4) | (pad[idx + stride - 1] << 5) | (pad[idx - 1] << 6) | (pad[idx - stride - 1] << 7);

        private static void RemoveStairs(byte[] pad, int h, int w, int stride, List<int> toClear)
        {
            foreach (bool[] lut in new[] { LutStair0, LutStair1 })
            {
                toClear.Clear();
                for (int y = 1; y <= h; y++)
                {
                    int row = y * stride;
                    for (int x = 1; x <= w; x++) if (pad[row + x] == 1 && lut[EncodeRing(pad, row + x, stride)]) toClear.Add(row + x);
                }
                foreach (int idx in toClear) pad[idx] = 0;
            }
        }

        public static byte[] FastThinAndClean(byte[] maskBin, int h, int w)
        {
            int pw = w + 2, ph = h + 2;
            byte[] pad = new byte[ph * pw];
            for (int y = 0; y < h; y++)
            {
                int srcOff = y * w, dstOff = (y + 1) * pw + 1;
                for (int x = 0; x < w; x++) pad[dstOff + x] = maskBin[srcOff + x] > 0 ? (byte)1 : (byte)0;
            }
            List<int> toClear = new(Math.Max(64, (h * w) / 8));
            while (true)
            {
                bool changed = false;
                foreach (bool[] lut in new[] { LutZs0, LutZs1 })
                {
                    toClear.Clear();
                    for (int y = 1; y <= h; y++)
                    {
                        int row = y * pw;
                        for (int x = 1; x <= w; x++) if (pad[row + x] == 1 && lut[EncodeRing(pad, row + x, pw)]) toClear.Add(row + x);
                    }
                    if (toClear.Count > 0) { foreach (int idx in toClear) pad[idx] = 0; changed = true; }
                }
                if (!changed) break;
            }
            RemoveStairs(pad, h, w, pw, toClear);
            byte[] outSkel = new byte[h * w];
            for (int y = 0; y < h; y++) Array.Copy(pad, (y + 1) * pw + 1, outSkel, y * w, w);
            return outSkel;
        }

        private static (byte[] Deg, byte[] Cn) NeighborStats(byte[] skel, int h, int w)
        {
            int pw = w + 2, ph = h + 2;
            byte[] pad = new byte[ph * pw];
            for (int y = 0; y < h; y++) Array.Copy(skel, y * w, pad, (y + 1) * pw + 1, w);
            byte[] deg = new byte[h * w], cn = new byte[h * w];
            for (int y = 0; y < h; y++)
            {
                int pRow = (y + 1) * pw + 1, oRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (pad[pRow + x] == 0) continue;
                    int code = EncodeRing(pad, pRow + x, pw);
                    deg[oRow + x] = LutDeg[code]; cn[oRow + x] = LutCn[code];
                }
            }
            return (deg, cn);
        }

        private static float SampleBilinear(float[] field, int h, int w, float x, float y)
        {
            if (x < 0f || y < 0f || x > w - 1f || y > h - 1f) return 0f;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y), x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float fx = x - x0, fy = y - y0;
            int r0 = y0 * w, r1 = y1 * w;
            return (field[r0 + x0] * (1f - fx) + field[r0 + x1] * fx) * (1f - fy) + (field[r1 + x0] * (1f - fx) + field[r1 + x1] * fx) * fy;
        }

        private static float MarchRaySingle(float[] field, int h, int w, float x0, float y0, float dx, float dy, float v0, float isoLevel, float dt, int nSteps)
        {
            float prev = v0;
            for (int s = 1; s <= nSteps; s++)
            {
                float dist = s * dt, curr = SampleBilinear(field, h, w, x0 + dx * dist, y0 + dy * dist);
                if (curr < isoLevel) return (s - 1 + Math.Clamp((prev - isoLevel) / MathF.Max(prev - curr, 1e-6f), 0f, 1f)) * dt;
                prev = curr;
            }
            return nSteps * dt;
        }

        private static (double AngleDeg, string Orient) ClassifyOrientation(List<int> xs, List<int> ys, int branchCount)
        {
            int n = xs.Count;
            if (n < 3) return (0.0, "点状微裂");
            double mx = xs.Average(), my = ys.Average(), cxx = 0, cyy = 0, cxy = 0;
            for (int i = 0; i < n; i++) { double dx = xs[i] - mx, dy = ys[i] - my; cxx += dx * dx; cyy += dy * dy; cxy += dx * dy; }
            cxx /= n; cyy /= n; cxy /= n;
            double diff = cxx - cyy, hypot = Math.Sqrt(diff * diff + 4.0 * cxy * cxy);
            double lMax = Math.Max(0.5 * (cxx + cyy + hypot), 1e-6), lMin = Math.Max(0.5 * (cxx + cyy - hypot), 1e-6);
            double theta = 0.5 * Math.Atan2(2.0 * cxy, diff);
            double deg = ((-Math.Atan2(Math.Sin(theta), Math.Cos(theta)) * 180.0 / Math.PI) % 180.0 + 180.0) % 180.0;
            string orient = (branchCount >= 3 && (lMax / lMin) < 3.5) ? "网状/龟裂" : (deg < 22.5 || deg >= 157.5) ? "水平横向" : (deg >= 67.5 && deg < 112.5) ? "竖直纵向" : "斜向裂缝";
            return (deg, orient);
        }

        public static (List<CvPoint> Branches, double AvgW, double MaxW, double LenPx, List<Point2f> SkelPts, List<ProfileSample> Profiles, Point2f WmaxPt, double AngleDeg, string Orient)
            AnalyzeComponent(Mat roiMask, Mat? probRoi, double probThresh, int spurLen = 6)
        {
            int h = roiMask.Rows, w = roiMask.Cols;
            byte[] rawMask = MatBuf.GetBytes(roiMask);
            if (!rawMask.Any(v => v > 0)) return (new(), 0, 0, 0, new(), new(), new Point2f(0, 0), 0, "-");

            using Mat padMask = new(h + 2, w + 2, MatType.CV_8UC1, Scalar.All(0));
            using (Mat inner = new(padMask, new Rect(1, 1, w, h))) Cv2.Threshold(roiMask, inner, 0, 1, ThresholdTypes.Binary);
            using Mat distFull = new();
            Cv2.DistanceTransform(padMask, distFull, DistanceTypes.L2, DistanceTransformMasks.Mask5);
            using Mat distRoi = new(distFull, new Rect(1, 1, w, h));
            float[] distArr = MatBuf.GetFloats(distRoi);

            byte[] pruned = FastThinAndClean(rawMask, h, w);
            byte[] backup = (byte[])pruned.Clone();

            for (int iter = 0; iter < 3; iter++)
            {
                var (deg, cn) = NeighborStats(pruned, h, w);
                if (!pruned.Where((v, idx) => v > 0 && cn[idx] >= 3).Any()) break;
                List<List<(int Y, int X)>> spurs = new();
                for (int ey = 0; ey < h; ey++)
                {
                    for (int ex = 0; ex < w; ex++)
                    {
                        if (pruned[ey * w + ex] == 0 || deg[ey * w + ex] != 1) continue;
                        List<(int Y, int X)> path = new() { (ey, ex) };
                        int cy = ey, cx = ex, py = -1, px = -1; bool hit = false;
                        for (int s = 0; s < spurLen; s++)
                        {
                            List<(int Y, int X)> nxt = new(4);
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                    if ((dy != 0 || dx != 0) && cy + dy >= 0 && cy + dy < h && cx + dx >= 0 && cx + dx < w && pruned[(cy + dy) * w + cx + dx] > 0 && (cy + dy != py || cx + dx != px))
                                        nxt.Add((cy + dy, cx + dx));
                            if (nxt.Count == 0) break;
                            if (nxt.Count > 1 || nxt.Any(p => cn[p.Y * w + p.X] >= 3)) { hit = true; break; }
                            py = cy; px = cx; cy = nxt[0].Y; cx = nxt[0].X; path.Add(nxt[0]);
                        }
                        if (hit && path.Count <= spurLen) spurs.Add(path);
                    }
                }
                if (spurs.Count == 0) break;
                spurs.Sort((a, b) => a.Count.CompareTo(b.Count));
                bool removed = false;
                foreach (var path in spurs)
                {
                    var (_, cnNow) = NeighborStats(pruned, h, w);
                    var (ty, tx) = path[^1];
                    HashSet<(int, int)> pset = new(path);
                    bool touches = false;
                    for (int dy = -1; dy <= 1 && !touches; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((dy != 0 || dx != 0) && ty + dy >= 0 && ty + dy < h && tx + dx >= 0 && tx + dx < w && pruned[(ty + dy) * w + tx + dx] > 0 && cnNow[(ty + dy) * w + tx + dx] >= 3 && !pset.Contains((ty + dy, tx + dx)))
                            { touches = true; break; }
                    if (touches) { foreach (var (ry, rx) in path) pruned[ry * w + rx] = 0; removed = true; }
                }
                if (!removed) break;
                int pw = w + 2, ph = h + 2;
                byte[] pad = new byte[ph * pw];
                for (int y = 0; y < h; y++) Array.Copy(pruned, y * w, pad, (y + 1) * pw + 1, w);
                RemoveStairs(pad, h, w, pw, new List<int>());
                for (int y = 0; y < h; y++) Array.Copy(pad, (y + 1) * pw + 1, pruned, y * w, w);
            }
            if (!pruned.Any(v => v > 0)) pruned = backup;

            var (_, cnFin) = NeighborStats(pruned, h, w);
            byte[] rawJunc = new byte[h * w]; bool hasJunc = false;
            for (int i = 0; i < pruned.Length; i++) if (pruned[i] > 0 && cnFin[i] >= 3) { rawJunc[i] = 255; hasJunc = true; }
            List<CvPoint> branchPts = new();
            if (hasJunc)
            {
                using Mat jMat = MatBuf.FromBytes(h, w, rawJunc);
                using Mat merged = new(), k3 = Cv2.GetStructuringElement(MorphShapes.Rect, new CvSize(3, 3));
                Cv2.Dilate(jMat, merged, k3, null, 1, BorderTypes.Constant, Scalar.All(0));
                using Mat lbls = new(), stats = new(), cents = new();
                int nLbl = Cv2.ConnectedComponentsWithStats(merged, lbls, stats, cents, PixelConnectivity.Connectivity8);
                for (int i = 1; i < nLbl; i++) branchPts.Add(new CvPoint((int)Math.Round(cents.At<double>(i, 0)), (int)Math.Round(cents.At<double>(i, 1))));
            }

            List<int> xs = new(), ys = new();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (pruned[y * w + x] > 0) { xs.Add(x); ys.Add(y); }

            List<Point2f> skelPts = new(); List<ProfileSample> profilePts = new();
            Point2f wmaxPt = new(0, 0); double maxW = 0, avgW = 0, angleDeg = 0; string orient = "-";
            int count = xs.Count;

            if (count > 0)
            {
                float[] softField = new float[h * w]; float isoLvl;
                if (probRoi != null && probRoi.Rows == h && probRoi.Cols == w)
                {
                    byte[] pBytes = MatBuf.GetBytes(probRoi);
                    isoLvl = (float)Math.Clamp(probThresh, 0.10, 0.90);
                    for (int i = 0; i < softField.Length; i++) softField[i] = rawMask[i] > 0 ? MathF.Max(pBytes[i] / 255f, isoLvl + 0.04f) : MathF.Min(pBytes[i] / 255f, isoLvl - 0.04f);
                }
                else
                {
                    isoLvl = 0.5f;
                    for (int i = 0; i < softField.Length; i++) softField[i] = rawMask[i] > 0 ? 1f : 0f;
                }

                float[] nxArr = new float[count], nyArr = new float[count], dPos = new float[count], dNeg = new float[count], dTot = new float[count];
                float maxLocalR = 0f; double sumLocalR = 0.0;
                for (int i = 0; i < count; i++) { float r = distArr[ys[i] * w + xs[i]]; if (r > maxLocalR) maxLocalR = r; sumLocalR += r; }
                float maxSearch = Math.Clamp(maxLocalR * 2.2f + 2.5f, 3f, Math.Min(Math.Max(h, w) + 2f, 256f));
                float dt = maxSearch <= 32f ? 0.25f : 0.5f;
                int nSteps = Math.Max(4, (int)MathF.Ceiling(maxSearch / dt));

                Parallel.For(0, count, i =>
                {
                    int cx = xs[i], cy = ys[i], cnt = 0;
                    double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
                    for (int wy = Math.Max(0, cy - 4); wy <= Math.Min(h - 1, cy + 4); wy++)
                    {
                        int rOff = wy * w;
                        for (int wx = Math.Max(0, cx - 4); wx <= Math.Min(w - 1, cx + 4); wx++)
                        {
                            if (pruned[rOff + wx] == 0) continue;
                            cnt++; sx += wx; sy += wy; sxx += wx * wx; syy += wy * wy; sxy += wx * wy;
                        }
                    }
                    float nx = 0f, ny = 1f;
                    if (cnt > 1)
                    {
                        double mx = sx / cnt, my = sy / cnt;
                        double theta = 0.5 * Math.Atan2(2.0 * (sxy / cnt - mx * my), (sxx / cnt - mx * mx) - (syy / cnt - my * my));
                        nx = (float)(-Math.Sin(theta)); ny = (float)Math.Cos(theta);
                    }
                    nxArr[i] = nx; nyArr[i] = ny;
                    float v0 = softField[cy * w + cx];
                    float dp = MarchRaySingle(softField, h, w, cx, cy, nx, ny, v0, isoLvl, dt, nSteps);
                    float dn = MarchRaySingle(softField, h, w, cx, cy, -nx, -ny, v0, isoLvl, dt, nSteps);
                    float tot = Math.Clamp(dp + dn, 0.45f, distArr[cy * w + cx] * 2f + 1.15f);
                    float fix = tot / MathF.Max(dp + dn, 1e-6f);
                    dPos[i] = dp * fix; dNeg[i] = dn * fix; dTot[i] = tot;
                });

                bool[] validMask = Enumerable.Repeat(true, count).ToArray();
                if (branchPts.Count > 0 && count > 5)
                {
                    double exclR2 = Math.Pow(Math.Max(3.0, (sumLocalR / count) * 1.6 + 1.5), 2);
                    for (int i = 0; i < count; i++)
                        foreach (var bp in branchPts) if ((xs[i] - bp.X) * (xs[i] - bp.X) + (ys[i] - bp.Y) * (ys[i] - bp.Y) <= exclR2) { validMask[i] = false; break; }
                    if (!validMask.Any(v => v)) Array.Fill(validMask, true);
                }

                int bestIdx = 0; float bestVal = -1f; double sumValid = 0; int nValid = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!validMask[i]) continue;
                    sumValid += dTot[i]; nValid++;
                    if (dTot[i] > bestVal) { bestVal = dTot[i]; bestIdx = i; }
                }
                maxW = bestVal; avgW = nValid > 0 ? sumValid / nValid : 0.0;
                wmaxPt = new Point2f(xs[bestIdx] + 0.5f * nxArr[bestIdx] * (dPos[bestIdx] - dNeg[bestIdx]), ys[bestIdx] + 0.5f * nyArr[bestIdx] * (dPos[bestIdx] - dNeg[bestIdx]));

                int stepS = Math.Max(1, count / 45);
                for (int i = 0; i < count; i += stepS) skelPts.Add(new Point2f(xs[i], ys[i]));
                int stepP = Math.Max(1, count / 180);
                SortedSet<int> pIdx = new() { bestIdx };
                for (int i = 0; i < count; i += stepP) pIdx.Add(i);
                foreach (int i in pIdx)
                    profilePts.Add(new ProfileSample(xs[i], ys[i], dTot[i], xs[i] - nxArr[i] * dNeg[i], ys[i] - nyArr[i] * dNeg[i], xs[i] + nxArr[i] * dPos[i], ys[i] + nyArr[i] * dPos[i]));
                (angleDeg, orient) = ClassifyOrientation(xs, ys, branchPts.Count);
            }

            int ortho = 0, diag = 0;
            for (int y = 0; y < h; y++) { int r0 = y * w; for (int x = 0; x < w - 1; x++) if (pruned[r0 + x] > 0 && pruned[r0 + x + 1] > 0) ortho++; }
            for (int y = 0; y < h - 1; y++)
            {
                int r0 = y * w, r1 = (y + 1) * w;
                for (int x = 0; x < w; x++) if (pruned[r0 + x] > 0 && pruned[r1 + x] > 0) ortho++;
                for (int x = 0; x < w - 1; x++)
                {
                    if (pruned[r0 + x] > 0 && pruned[r1 + x + 1] > 0 && pruned[r0 + x + 1] == 0 && pruned[r1 + x] == 0) diag++;
                    if (pruned[r0 + x + 1] > 0 && pruned[r1 + x] > 0 && pruned[r0 + x] == 0 && pruned[r1 + x + 1] == 0) diag++;
                }
            }
            double lenPx = ortho + diag * 1.41421356;
            if (lenPx == 0.0 && count > 0) lenPx = count;
            return (branchPts, avgW, maxW, lenPx, skelPts, profilePts, wmaxPt, angleDeg, orient);
        }
    }

    public sealed class OnnxSegmenter : IDisposable
    {
        public const int PatchSize = 512;
        private InferenceSession? _session;
        private string _inputName = "";
        public bool IsNhwc { get; private set; }
        public bool DynBatch { get; private set; }
        public string DeviceLabel { get; private set; } = "未加载";
        public bool IsReady => _session != null;
        private static readonly float[] Hanning2D = BuildHanning2D(PatchSize);

        private static float[] BuildHanning2D(int sz)
        {
            float[] w1d = new float[sz], w2d = new float[sz * sz];
            for (int i = 0; i < sz; i++) w1d[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * (i + 1) / (sz + 1)));
            for (int y = 0; y < sz; y++) for (int x = 0; x < sz; x++) w2d[y * sz + x] = w1d[y] * w1d[x];
            return w2d;
        }

        public void LoadModel(string onnxPath)
        {
            _session?.Dispose();
            bool usedGpu = false;
            try
            {
                var gpuOpts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
                gpuOpts.AppendExecutionProvider_DML(0);
                _session = new InferenceSession(onnxPath, gpuOpts);
                usedGpu = true; DeviceLabel = "GPU 加速 (DirectML)";
            }
            catch
            {
                int threads = Math.Max(2, Environment.ProcessorCount / 2);
                _session = new InferenceSession(onnxPath, new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = threads });
                DeviceLabel = $"CPU ({threads}线程+LUT并行)";
            }
            var meta = _session.InputMetadata.First();
            _inputName = meta.Key; int[] dims = meta.Value.Dimensions;
            IsNhwc = dims.Length == 4 && dims[3] == 3;
            DynBatch = dims.Length == 4 && dims[0] <= 0;
            if (usedGpu)
            {
                int[] warmDims = IsNhwc ? new[] { 1, PatchSize, PatchSize, 3 } : new[] { 1, 3, PatchSize, PatchSize };
                using var res = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, new DenseTensor<float>(new float[PatchSize * PatchSize * 3], warmDims)) });
            }
        }

        private List<float[]> RunBatch(List<Mat> patchesBgr, int sz = PatchSize)
        {
            if (_session == null) throw new InvalidOperationException("模型尚未初始化");
            if (DynBatch && patchesBgr.Count > 1)
            {
                try
                {
                    int bCount = patchesBgr.Count;
                    float[] blob = new float[bCount * sz * sz * 3];
                    for (int b = 0; b < bCount; b++) FillBlob(patchesBgr[b], sz, blob, b * sz * sz * 3);
                    int[] shape = IsNhwc ? new[] { bCount, sz, sz, 3 } : new[] { bCount, 3, sz, sz };
                    using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, new DenseTensor<float>(blob, shape)) });
                    return ParseProbBatch(results.First().AsTensor<float>(), bCount, sz, sz);
                }
                catch { DynBatch = false; }
            }

            List<float[]> outs = new(patchesBgr.Count);
            int[] singleShape = IsNhwc ? new[] { 1, sz, sz, 3 } : new[] { 1, 3, sz, sz };
            foreach (Mat patch in patchesBgr)
            {
                float[] blob = new float[sz * sz * 3];
                FillBlob(patch, sz, blob, 0);
                using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, new DenseTensor<float>(blob, singleShape)) });
                outs.Add(ParseProbBatch(results.First().AsTensor<float>(), 1, sz, sz)[0]);
            }
            return outs;
        }

        private void FillBlob(Mat bgr, int sz, float[] dst, int offset)
        {
            Mat work = bgr; bool resized = false;
            try
            {
                if (bgr.Rows != sz || bgr.Cols != sz) { work = new Mat(); resized = true; Cv2.Resize(bgr, work, new CvSize(sz, sz), 0, 0, InterpolationFlags.Linear); }
                byte[] raw = MatBuf.GetBytes(work);
                int plane = sz * sz; const float inv255 = 1f / 255f;
                if (IsNhwc)
                {
                    for (int i = 0; i < plane; i++) { int s = i * 3, d = offset + i * 3; dst[d] = raw[s + 2] * inv255; dst[d + 1] = raw[s + 1] * inv255; dst[d + 2] = raw[s] * inv255; }
                }
                else
                {
                    int offR = offset, offG = offset + plane, offB = offset + plane * 2;
                    for (int i = 0; i < plane; i++) { int s = i * 3; dst[offR + i] = raw[s + 2] * inv255; dst[offG + i] = raw[s + 1] * inv255; dst[offB + i] = raw[s] * inv255; }
                }
            }
            finally { if (resized) work.Dispose(); }
        }

        private static List<float[]> ParseProbBatch(Tensor<float> rawTensor, int batch, int tw, int th)
        {
            int[] dims = rawTensor.Dimensions.ToArray(); float[] flat = rawTensor.ToArray();
            int h = dims.Length == 4 ? (dims[1] <= 4 ? dims[2] : dims[1]) : dims[^2];
            int w = dims.Length == 4 ? (dims[1] <= 4 ? dims[3] : dims[2]) : dims[^1];
            int plane = h * w;
            bool twoChNchw = dims.Length == 4 && dims[1] == 2, twoChNhwc = dims.Length == 4 && dims[3] == 2;
            List<float[]> results = new(batch);
            for (int b = 0; b < batch; b++)
            {
                float[] c0 = new float[plane], c1 = new float[plane], prob = new float[plane];
                bool hasTwo = twoChNchw || twoChNhwc;
                if (twoChNchw) { Array.Copy(flat, b * 2 * plane, c0, 0, plane); Array.Copy(flat, (b * 2 + 1) * plane, c1, 0, plane); }
                else if (twoChNhwc) { int bi = b * plane * 2; for (int i = 0; i < plane; i++) { c0[i] = flat[bi + i * 2]; c1[i] = flat[bi + i * 2 + 1]; } }
                else Array.Copy(flat, b * plane, c1, 0, plane);

                bool isLogit = c1.Min() < -1e-3f || c1.Max() > 1.001f;
                if (hasTwo && isLogit)
                    for (int i = 0; i < plane; i++) { float mx = MathF.Max(c0[i], c1[i]), e0 = MathF.Exp(Math.Clamp(c0[i] - mx, -50f, 50f)), e1 = MathF.Exp(Math.Clamp(c1[i] - mx, -50f, 50f)); prob[i] = Math.Clamp(e1 / (e0 + e1 + 1e-8f), 0f, 1f); }
                else if (isLogit)
                    for (int i = 0; i < plane; i++) prob[i] = Math.Clamp(1f / (1f + MathF.Exp(-Math.Clamp(c1[i], -50f, 50f))), 0f, 1f);
                else
                    for (int i = 0; i < plane; i++) prob[i] = Math.Clamp(c1[i], 0f, 1f);

                if (w != tw || h != th) { using Mat srcM = MatBuf.FromFloats(h, w, prob), dstM = new(); Cv2.Resize(srcM, dstM, new CvSize(tw, th), 0, 0, InterpolationFlags.Linear); prob = MatBuf.GetFloats(dstM); }
                results.Add(prob);
            }
            return results;
        }

        public (Mat ProbU8, string ModeTag) InferProbU8(Mat imgBgr, bool tileMode = true, int tileSize = PatchSize, double overlap = 0.25)
        {
            int origH = imgBgr.Rows, origW = imgBgr.Cols;
            if (!tileMode || (origH <= tileSize && origW <= tileSize))
            {
                double scale = Math.Min((double)tileSize / origW, (double)tileSize / origH);
                int nw = Math.Max(1, (int)Math.Round(origW * scale)), nh = Math.Max(1, (int)Math.Round(origH * scale)), pt = (tileSize - nh) / 2, pl = (tileSize - nw) / 2;
                using Mat canvas = new(tileSize, tileSize, MatType.CV_8UC3, new Scalar(128, 128, 128));
                using (Mat roi = new(canvas, new Rect(pl, pt, nw, nh))) Cv2.Resize(imgBgr, roi, new CvSize(nw, nh), 0, 0, InterpolationFlags.Linear);
                float[] sqProb = RunBatch(new List<Mat> { canvas }, tileSize)[0];
                using Mat sqMat = MatBuf.FromFloats(tileSize, tileSize, sqProb), validSub = new(sqMat, new Rect(pl, pt, nw, nh)), fullF32 = new();
                Cv2.Resize(validSub, fullF32, new CvSize(origW, origH), 0, 0, InterpolationFlags.Linear);
                Mat outU8 = new(); fullF32.ConvertTo(outU8, MatType.CV_8UC1, 255.0);
                return (outU8, "等比缩放推理");
            }

            int stride = Math.Max(64, (int)(tileSize * (1.0 - overlap))), padH = Math.Max(0, tileSize - origH), padW = Math.Max(0, tileSize - origW);
            Mat work = imgBgr; bool padded = false;
            if (padH > 0 || padW > 0) { work = new Mat(); padded = true; Cv2.CopyMakeBorder(imgBgr, work, 0, padH, 0, padW, BorderTypes.Constant, new Scalar(128, 128, 128)); }
            try
            {
                int H = work.Rows, W = work.Cols;
                List<int> ys = new(), xs = new();
                for (int y = 0; y <= H - tileSize; y += stride) ys.Add(y); if (ys[^1] + tileSize < H) ys.Add(H - tileSize);
                for (int x = 0; x <= W - tileSize; x += stride) xs.Add(x); if (xs[^1] + tileSize < W) xs.Add(W - tileSize);
                var coords = (from y in ys from x in xs select (Y: y, X: x)).ToList();
                float[] probAcc = new float[H * W], weightAcc = new float[H * W];
                int step = DynBatch ? 4 : 1;
                for (int i = 0; i < coords.Count; i += step)
                {
                    int take = Math.Min(step, coords.Count - i);
                    List<Mat> patches = new(take);
                    for (int k = 0; k < take; k++) patches.Add(new Mat(work, new Rect(coords[i + k].X, coords[i + k].Y, tileSize, tileSize)));
                    List<float[]> maps = RunBatch(patches, tileSize);
                    foreach (Mat p in patches) p.Dispose();
                    for (int k = 0; k < take; k++)
                    {
                        var (cy, cx) = coords[i + k]; float[] pm = maps[k];
                        for (int py = 0; py < tileSize; py++)
                        {
                            int dr = (cy + py) * W + cx, sr = py * tileSize;
                            for (int px = 0; px < tileSize; px++) { float wv = Hanning2D[sr + px]; probAcc[dr + px] += pm[sr + px] * wv; weightAcc[dr + px] += wv; }
                        }
                    }
                }
                byte[] probBytes = new byte[origH * origW];
                for (int y = 0; y < origH; y++)
                    for (int x = 0; x < origW; x++)
                        probBytes[y * origW + x] = (byte)Math.Round(Math.Clamp(probAcc[y * W + x] / MathF.Max(weightAcc[y * W + x], 1e-6f), 0f, 1f) * 255f);
                return (MatBuf.FromBytes(origH, origW, probBytes), "高清切片推理");
            }
            finally { if (padded) work.Dispose(); }
        }

        public void Dispose() => _session?.Dispose();
    }

    public static class PostProcessor
    {
        public static ImageSummary BuildSummary(int w, int h, string mode, double probThresh, double mmPerPx, int bridgePx, List<CrackRecord> cracks, int totalBranches)
        {
            string unit = mmPerPx > 0 ? "mm" : "px"; double factor = mmPerPx > 0 ? mmPerPx : 1.0;
            foreach (var c in cracks) { c.Unit = unit; c.LenVal = c.LenPx * factor; c.AvgWVal = c.AvgWPx * factor; c.MaxWVal = c.MaxWPx * factor; c.AreaVal = c.AreaPx * factor * factor; }
            double totLen = cracks.Sum(c => c.LenPx), maxW = cracks.Count > 0 ? cracks.Max(c => c.MaxWPx) : 0.0;
            double avgW = totLen > 0 ? cracks.Sum(c => c.AvgWPx * c.LenPx) / totLen : 0.0;
            int totArea = cracks.Sum(c => c.AreaPx);
            return new ImageSummary { Width = w, Height = h, Mode = mode, ProbThresh = probThresh, MmPerPx = mmPerPx, BridgePx = bridgePx, Unit = unit, CrackCount = cracks.Count, TotalLenVal = totLen * factor, MaxWVal = maxW * factor, AvgWVal = avgW * factor, TotalAreaPx = totArea, TotalAreaVal = totArea * factor * factor, TotalBranches = totalBranches };
        }

        public static Mat RenderCleanOverlay(Mat origBgr, Mat globalMask, List<CvPoint> branchPts, List<Point2f>? wmaxPts)
        {
            int h = origBgr.Rows, w = origBgr.Cols; Mat canvas = origBgr.Clone();
            if (Cv2.CountNonZero(globalMask) > 0)
            {
                using Mat red = new(origBgr.Size(), MatType.CV_8UC3, new Scalar(0, 0, 255)), blended = new();
                Cv2.AddWeighted(canvas, 0.46, red, 0.54, 0, blended);
                blended.CopyTo(canvas, globalMask);
                Cv2.FindContours(globalMask, out CvPoint[][] contours, out _, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);
                Cv2.DrawContours(canvas, contours, -1, new Scalar(0, 255, 0), 1, LineTypes.AntiAlias);
            }
            int r = Math.Clamp((int)Math.Round(Math.Min(w, h) / 140.0), 3, 6);
            foreach (var pt in branchPts) { Cv2.Circle(canvas, pt, r, new Scalar(255, 190, 0), -1, LineTypes.AntiAlias); Cv2.Circle(canvas, pt, r + 1, new Scalar(0, 0, 0), 1, LineTypes.AntiAlias); }
            if (wmaxPts != null)
            {
                int rt = Math.Clamp((int)Math.Round(Math.Min(w, h) / 95.0), 5, 9);
                foreach (var wp in wmaxPts)
                {
                    int ix = (int)Math.Round(wp.X), iy = (int)Math.Round(wp.Y);
                    Cv2.Circle(canvas, new CvPoint(ix, iy), rt, new Scalar(255, 255, 255), 2, LineTypes.AntiAlias);
                    Cv2.Circle(canvas, new CvPoint(ix, iy), rt, new Scalar(203, 0, 255), 1, LineTypes.AntiAlias);
                    Cv2.Line(canvas, new CvPoint(ix - rt - 3, iy), new CvPoint(ix + rt + 3, iy), new Scalar(255, 0, 255), 1, LineTypes.AntiAlias);
                    Cv2.Line(canvas, new CvPoint(ix, iy - rt - 3), new CvPoint(ix, iy + rt + 3), new Scalar(255, 0, 255), 1, LineTypes.AntiAlias);
                }
            }
            return canvas;
        }

        public static AnalysisOutput AnalyzeFromMask(Mat imgBgr, Mat maskU8, Mat? probU8, string modeTag, double probThresh, double mmPerPx, int minAreaPx, int bridgePx, bool showWmax = true)
        {
            int h = imgBgr.Rows, w = imgBgr.Cols;
            using Mat bin01 = new(), labels = new(), stats = new(), cents = new();
            Cv2.Threshold(maskU8, bin01, 0, 1, ThresholdTypes.Binary);
            int nLabels = Cv2.ConnectedComponentsWithStats(bin01, labels, stats, cents, PixelConnectivity.Connectivity8);
            Mat globalMask = new(h, w, MatType.CV_8UC1, Scalar.All(0));
            List<CvPoint> globalBranches = new(); List<CrackRecord> cracks = new();
            int cid = 0;

            for (int lbl = 1; lbl < nLabels; lbl++)
            {
                int areaPx = stats.At<int>(lbl, (int)ConnectedComponentsTypes.Area);
                if (areaPx < minAreaPx) continue;
                cid++;
                int x1 = stats.At<int>(lbl, (int)ConnectedComponentsTypes.Left), y1 = stats.At<int>(lbl, (int)ConnectedComponentsTypes.Top);
                int rw = stats.At<int>(lbl, (int)ConnectedComponentsTypes.Width), rh = stats.At<int>(lbl, (int)ConnectedComponentsTypes.Height);
                Rect roiRect = new(x1, y1, rw, rh);
                using Mat lblSub = new(labels, roiRect), roiMask = new();
                Cv2.Compare(lblSub, new Scalar(lbl), roiMask, CmpTypes.EQ);
                using (Mat gmSub = new(globalMask, roiRect)) Cv2.BitwiseOr(gmSub, roiMask, gmSub);

                Mat? probSub = probU8 != null ? new Mat(probU8, roiRect) : null;
                try
                {
                    var topo = SkeletonEngine.AnalyzeComponent(roiMask, probSub, probThresh);
                    double conf = Math.Clamp(probSub != null ? Cv2.Mean(probSub, roiMask).Val0 / 255.0 : 1.0, 0.01, 1.0);
                    foreach (var bp in topo.Branches) globalBranches.Add(new CvPoint(x1 + bp.X, y1 + bp.Y));
                    cracks.Add(new CrackRecord
                    {
                        Id = cid, BBox = roiRect,
                        SkelPts = topo.SkelPts.Select(p => new Point2f(x1 + p.X, y1 + p.Y)).ToList(),
                        ProfilePts = topo.Profiles.Select(p => new ProfileSample(x1 + p.Sx, y1 + p.Sy, p.WidthPx, x1 + p.P1x, y1 + p.P1y, x1 + p.P2x, y1 + p.P2y)).ToList(),
                        WmaxPt = new Point2f(x1 + topo.WmaxPt.X, y1 + topo.WmaxPt.Y),
                        AngleDeg = topo.AngleDeg, Orientation = topo.Orient, Conf = conf, AreaPx = areaPx,
                        LenPx = topo.LenPx, AvgWPx = topo.AvgW, MaxWPx = topo.MaxW, Branches = topo.Branches.Count
                    });
                }
                finally { probSub?.Dispose(); }
            }

            Mat overlay = RenderCleanOverlay(imgBgr, globalMask, globalBranches, showWmax ? cracks.Select(c => c.WmaxPt).ToList() : null);
            return new AnalysisOutput { OverlayImg = overlay, MaskImg = globalMask, BranchPts = globalBranches, Summary = BuildSummary(w, h, modeTag, probThresh, mmPerPx, bridgePx, cracks, globalBranches.Count), Cracks = cracks };
        }

        public static AnalysisOutput ProcessFromProb(Mat imgBgr, Mat probU8, string modeTag, double probThresh, double mmPerPx, int minAreaPx, int bridgePx, bool showWmax = true)
        {
            using Mat rawMask = new();
            Cv2.Threshold(probU8, rawMask, (int)Math.Round(probThresh * 255.0) - 1, 255, ThresholdTypes.Binary);
            int k = Math.Max(1, bridgePx);
            if (k > 1)
            {
                using Mat closed = new(), kern = Cv2.GetStructuringElement(MorphShapes.Ellipse, new CvSize(k, k));
                Cv2.MorphologyEx(rawMask, closed, MorphTypes.Close, kern);
                return AnalyzeFromMask(imgBgr, closed, probU8, modeTag, probThresh, mmPerPx, minAreaPx, k, showWmax);
            }
            return AnalyzeFromMask(imgBgr, rawMask, probU8, modeTag, probThresh, mmPerPx, minAreaPx, k, showWmax);
        }

        private static bool Overlaps(RectangleF a, RectangleF b, float pad = 4f) =>
            !(a.Right + pad < b.Left || a.Left - pad > b.Right || a.Bottom + pad < b.Top || a.Top - pad > b.Bottom);

        public static List<LabelLayout> ComputeLabelLayout(List<CrackRecord> cracks, string labelStyle, Func<string, SizeF> measureFn, float canvasW, float canvasH, float scale = 1f, float ox = 0f, float oy = 0f)
        {
            List<LabelLayout> layouts = new();
            if (labelStyle == "none" || cracks.Count == 0) return layouts;
            List<RectangleF> occupied = new();
            foreach (var c in cracks)
            {
                float x1 = c.BBox.X * scale + ox, y1 = c.BBox.Y * scale + oy, x2 = c.BBox.Right * scale + ox, y2 = c.BBox.Bottom * scale + oy;
                string txt = labelStyle == "full" ? $"#{c.Id} [{c.Orientation}] L:{c.LenVal:F1}{c.Unit} W:{c.AvgWVal:F2}{c.Unit} Wmax:{c.MaxWVal:F2}{c.Unit}" : $"#{c.Id}";
                SizeF sz = measureFn(txt); float bw = sz.Width + 12f, bh = sz.Height + 6f;
                var pts = c.SkelPts.Select(p => new PointF(p.X * scale + ox, p.Y * scale + oy)).ToList();
                PointF topPt = pts.Count > 0 ? pts.OrderBy(p => p.Y).First() : new PointF((x1 + x2) * 0.5f, y1);
                List<(float X, float Y)> cands = new() { (topPt.X - bw * 0.5f, y1 - bh - 6f), (x1, y1 - bh - 6f), (x1 + 4f, y1 + 4f), (x2 - bw, y1 - bh - 6f), (x1, y2 + 6f) };
                for (int s = 1; s < 12; s++) { cands.Add((x1 + 4f, y1 + 4f + s * (bh + 4f))); cands.Add((x1, y1 - bh - 6f - s * (bh + 4f))); }

                RectangleF? chosen = null;
                float maxX = Math.Max(ox + 3f, ox + canvasW - bw - 3f), maxY = Math.Max(oy + 3f, oy + canvasH - bh - 3f);
                foreach (var (cx, cy) in cands)
                {
                    var rect = new RectangleF(Math.Clamp(cx, ox + 3f, maxX), Math.Clamp(cy, oy + 3f, maxY), bw, bh);
                    if (!occupied.Any(old => Overlaps(rect, old))) { chosen = rect; break; }
                }
                RectangleF box = chosen ?? new RectangleF(Math.Clamp(x1 + 4f, ox + 3f, maxX), Math.Clamp(y1 + 4f, oy + 3f, maxY), bw, bh);
                occupied.Add(box);
                float tcx = box.Left + box.Width * 0.5f, tcy = box.Top + box.Height * 0.5f;
                PointF nearPt = pts.Count > 0 ? pts.OrderBy(p => (p.X - tcx) * (p.X - tcx) + (p.Y - tcy) * (p.Y - tcy)).First() : topPt;
                float ex = Math.Clamp(nearPt.X, box.Left, box.Right), ey = Math.Clamp(nearPt.Y, box.Top, box.Bottom);
                double dist = Math.Sqrt((nearPt.X - ex) * (nearPt.X - ex) + (nearPt.Y - ey) * (nearPt.Y - ey));
                layouts.Add(new LabelLayout { Id = c.Id, BBox = new RectangleF(x1, y1, x2 - x1, y2 - y1), Rect = box, Text = txt, Leader = (dist > 6.0 && dist < 140.0) ? (ex, ey, nearPt.X, nearPt.Y) : null });
            }
            return layouts;
        }

        public static Mat RenderHighDpiExport(Mat cleanOverlayBgr, List<CrackRecord> cracks, string labelStyle = "compact")
        {
            int h = cleanOverlayBgr.Rows, w = cleanOverlayBgr.Cols;
            float scale = Math.Min(w, h) < 520 ? 2.0f : (Math.Min(w, h) < 800 ? 1.5f : 1.0f);
            Mat work = scale > 1.0f ? new Mat() : cleanOverlayBgr.Clone();
            if (scale > 1.0f) Cv2.Resize(cleanOverlayBgr, work, new CvSize((int)Math.Round(w * scale), (int)Math.Round(h * scale)), 0, 0, InterpolationFlags.Lanczos4);
            if (labelStyle == "none" || cracks.Count == 0) return work;

            using Bitmap bmp = BitmapConverter.ToBitmap(work);
            work.Dispose();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using GdiFont font = new("Microsoft YaHei UI", Math.Clamp(Math.Min(bmp.Width, bmp.Height) / 48f, 13f, 23f), FontStyle.Bold, GraphicsUnit.Pixel);
                using GdiPen boxPen = new(Color.FromArgb(215, 255, 225, 0), Math.Max(1f, scale)), leaderPen = new(Color.FromArgb(235, 255, 225, 0), 2f), tagBorder = new(Color.FromArgb(245, 255, 215, 0), 1.2f);
                using SolidBrush tagFill = new(Color.FromArgb(215, 15, 23, 42)), yellowFill = new(Color.FromArgb(255, 255, 225, 0));
                foreach (var it in ComputeLabelLayout(cracks, labelStyle, s => g.MeasureString(s, font), bmp.Width, bmp.Height, scale))
                {
                    if (labelStyle == "full") g.DrawRectangle(boxPen, it.BBox.X, it.BBox.Y, it.BBox.Width, it.BBox.Height);
                    if (it.Leader is var (lx1, ly1, lx2, ly2)) { g.DrawLine(leaderPen, lx1, ly1, lx2, ly2); g.FillEllipse(yellowFill, lx2 - 2.5f, ly2 - 2.5f, 5f, 5f); }
                    g.FillRectangle(tagFill, it.Rect); g.DrawRectangle(tagBorder, it.Rect.X, it.Rect.Y, it.Rect.Width, it.Rect.Height);
                    g.DrawString(it.Text, font, Brushes.White, it.Rect.X + 5f, it.Rect.Y + 3f);
                }
            }
            return BitmapConverter.ToMat(bmp);
        }
    }

    public static class ReportExporter
    {
        public static void ExportDxf(string savePath, ImageSummary s, List<CrackRecord> cracks, Mat? maskImg)
        {
            int H = s.Height, W = s.Width; double k = s.MmPerPx > 0 ? s.MmPerPx : 1.0, fw = W * k, fh = H * k, txtH = Math.Max(0.8 * k * Math.Max(W, H) / 120.0, 1.5 * k);
            string u = s.Unit; var ic = CultureInfo.InvariantCulture; string F(double v) => v.ToString("F4", ic);
            StringBuilder sb = new(16384);
            foreach (string ln in new[] { "0","SECTION","2","HEADER","9","$ACADVER","1","AC1009","0","ENDSEC","0","SECTION","2","TABLES","0","TABLE","2","LAYER","70","4","0","LAYER","2","IMAGE_FRAME","70","0","62","7","6","CONTINUOUS","0","LAYER","2","CRACK_CONTOUR","70","0","62","3","6","CONTINUOUS","0","LAYER","2","CRACK_SKELETON","70","0","62","1","6","CONTINUOUS","0","LAYER","2","CRACK_WMAX","70","0","62","2","6","CONTINUOUS","0","ENDTAB","0","ENDSEC","0","SECTION","2","ENTITIES" })
                sb.AppendLine(ln);

            void AddPoly(string layer, IList<(double X, double Y)> pts, bool closed)
            {
                if (pts.Count < 2) return;
                sb.AppendLine("0").AppendLine("POLYLINE").AppendLine("8").AppendLine(layer).AppendLine("66").AppendLine("1").AppendLine("70").AppendLine(closed ? "1" : "0");
                foreach (var (px, py) in pts) sb.AppendLine("0").AppendLine("VERTEX").AppendLine("8").AppendLine(layer).AppendLine("10").AppendLine(F(px)).AppendLine("20").AppendLine(F(py)).AppendLine("30").AppendLine("0.0");
                sb.AppendLine("0").AppendLine("SEQEND");
            }
            AddPoly("IMAGE_FRAME", new[] { (0.0, 0.0), (fw, 0.0), (fw, fh), (0.0, fh) }, true);
            if (maskImg != null && Cv2.CountNonZero(maskImg) > 0)
            {
                Cv2.FindContours(maskImg, out CvPoint[][] cnts, out _, RetrievalModes.List, ContourApproximationModes.ApproxTC89KCOS);
                foreach (var cnt in cnts)
                {
                    if (cnt.Length < 3) continue;
                    var approx = Cv2.ApproxPolyDP(cnt, 0.65, true);
                    if (approx.Length >= 3) AddPoly("CRACK_CONTOUR", approx.Select(p => (p.X * k, (H - p.Y) * k)).ToList(), true);
                }
            }
            foreach (var c in cracks)
            {
                for (int i = 0; i < c.ProfilePts.Count; i += 3)
                {
                    var pf = c.ProfilePts[i];
                    sb.AppendLine("0").AppendLine("LINE").AppendLine("8").AppendLine("CRACK_SKELETON").AppendLine("10").AppendLine(F(pf.P1x * k)).AppendLine("20").AppendLine(F((H - pf.P1y) * k)).AppendLine("30").AppendLine("0.0").AppendLine("11").AppendLine(F(pf.P2x * k)).AppendLine("21").AppendLine(F((H - pf.P2y) * k)).AppendLine("31").AppendLine("0.0");
                }
                double wx = c.WmaxPt.X * k, wy = (H - c.WmaxPt.Y) * k, rad = Math.Max(c.MaxWVal * 0.75, txtH * 0.35);
                sb.AppendLine("0").AppendLine("CIRCLE").AppendLine("8").AppendLine("CRACK_WMAX").AppendLine("10").AppendLine(F(wx)).AppendLine("20").AppendLine(F(wy)).AppendLine("30").AppendLine("0.0").AppendLine("40").AppendLine(F(rad));
                sb.AppendLine("0").AppendLine("TEXT").AppendLine("8").AppendLine("CRACK_WMAX").AppendLine("10").AppendLine(F(wx + rad * 1.3)).AppendLine("20").AppendLine(F(wy)).AppendLine("30").AppendLine("0.0").AppendLine("40").AppendLine(F(txtH)).AppendLine("1").AppendLine(string.Create(ic, $"#{c.Id} Wmax={c.MaxWVal:F3}{u} AvgW={c.AvgWVal:F3}{u} L={c.LenVal:F1}{u}"));
            }
            sb.AppendLine("0").AppendLine("ENDSEC").AppendLine("0").AppendLine("EOF");
            File.WriteAllText(savePath, sb.ToString(), Encoding.ASCII);
        }

        public static void ExportExcel(string savePath, IList<QueueItem> records)
        {
            var sumList = new List<Dictionary<string, object?>>(); var detList = new List<Dictionary<string, object?>>(); int seq = 0;
            for (int i = 0; i < records.Count; i++)
            {
                var rec = records[i]; var s = rec.Summary!;
                sumList.Add(new Dictionary<string, object?> { ["序号"] = i + 1, ["图像文件名"] = rec.FileName, ["分辨率"] = $"{s.Width}x{s.Height}", ["推理模式"] = s.Mode, ["概率阈值"] = Math.Round(s.ProbThresh, 2), ["比例尺(mm/px)"] = Math.Round(s.MmPerPx, 6), ["单位"] = s.Unit, ["裂缝条数"] = s.CrackCount, ["总长(∑L)"] = Math.Round(s.TotalLenVal, 2), ["最大宽(Wmax)"] = Math.Round(s.MaxWVal, 3), ["加权均宽(Wavg)"] = Math.Round(s.AvgWVal, 3), ["像素面积(px²)"] = s.TotalAreaPx, ["换算面积"] = Math.Round(s.TotalAreaVal, 2), ["分叉总数"] = s.TotalBranches, ["文件路径"] = rec.FilePath });
                foreach (var c in rec.Cracks)
                {
                    seq++;
                    detList.Add(new Dictionary<string, object?> { ["序号"] = seq, ["图像文件名"] = rec.FileName, ["裂缝编号"] = $"#{c.Id}", ["走向分类"] = c.Orientation, ["主倾角(°)"] = Math.Round(c.AngleDeg, 1), ["置信度"] = Math.Round(c.Conf, 4), ["单位"] = c.Unit, ["骨架长度(L)"] = Math.Round(c.LenVal, 2), ["平均宽(W)"] = Math.Round(c.AvgWVal, 3), ["最大宽(Wmax)"] = Math.Round(c.MaxWVal, 3), ["Wmax坐标(X,Y)"] = $"({c.WmaxPt.X:F1},{c.WmaxPt.Y:F1})", ["分叉数(B)"] = c.Branches, ["像素面积(px²)"] = c.AreaPx, ["换算面积"] = Math.Round(c.AreaVal, 2), ["外接框"] = $"({c.BBox.X},{c.BBox.Y},{c.BBox.Right},{c.BBox.Bottom})" });
                }
            }
            if (File.Exists(savePath)) File.Delete(savePath);
            MiniExcel.SaveAs(savePath, new Dictionary<string, object> { ["图像汇总"] = sumList, ["裂缝明细"] = detList });
        }

        public static void ExportInspectionReport(string savePath, string project, string location, string inspector, double warnLimit, IList<(QueueItem Item, string OrigB64, string ResB64)> items)
        {
            bool asWord = savePath.EndsWith(".doc", StringComparison.OrdinalIgnoreCase);
            string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), anyUnit = items.Count > 0 ? items[0].Item.Summary!.Unit : "px";
            int totImgs = items.Count, totCracks = items.Sum(x => x.Item.Summary!.CrackCount), overCnt = items.Sum(x => x.Item.Cracks.Count(c => c.MaxWVal >= warnLimit));
            double globalWmax = items.Count > 0 ? items.Max(x => x.Item.Summary!.MaxWVal) : 0.0;
            var mimeParts = new List<(string Uri, string B64)>(); var cards = new StringBuilder();
            static string Esc(string v) => WebUtility.HtmlEncode(v);

            for (int i = 0; i < items.Count; i++)
            {
                var (it, origB64, resB64) = items[i]; var s = it.Summary!; string u = s.Unit, scTxt = s.MmPerPx > 0 ? $"{s.MmPerPx:F5} mm/px" : "未标定 (px)";
                string uOrig = asWord ? $"file:///C:/rep/raw_{i + 1}.jpg" : $"data:image/jpeg;base64,{origB64}", uRes = asWord ? $"file:///C:/rep/res_{i + 1}.jpg" : $"data:image/jpeg;base64,{resB64}";
                if (asWord) { mimeParts.Add((uOrig, origB64)); mimeParts.Add((uRes, resB64)); }

                cards.Append($"<div class='blk'><h3>{i + 1}. 样本：{Esc(it.FileName)}</h3><div class='bar'>尺寸：{s.Width}×{s.Height} | 模式：{Esc(s.Mode)} | 阈值：{s.ProbThresh:F2} | 比例尺：{scTxt} | 检出：<b>{s.CrackCount}</b> 条 | 总长：<b>{s.TotalLenVal:F2} {u}</b> | Wmax：<b style='color:#dc2626'>{s.MaxWVal:F3} {u}</b></div><table class='grid'><tr><td width='50%'><img src='{uOrig}'><div class='cap'>原始图像</div></td><td width='50%'><img src='{uRes}'><div class='cap'>亚像素法向测量与 Wmax 靶标标注 (⊕为最宽处)</div></td></tr></table><table class='grid'><thead><tr><th>编号</th><th>走向</th><th>置信度</th><th>长度 L</th><th>均宽 W</th><th>最大宽 Wmax</th><th>分叉</th><th>面积</th><th>评估</th></tr></thead><tbody>");
                if (it.Cracks.Count == 0) cards.Append("<tr><td colspan='9' style='color:#16a34a'>未检出明显裂缝</td></tr>");
                else foreach (var c in it.Cracks)
                {
                    bool over = c.MaxWVal >= warnLimit;
                    cards.Append($"<tr{(over ? " style='background:#fef2f2'" : "")}><td>#{c.Id}</td><td>{Esc(c.Orientation)} ({c.AngleDeg:F0}°)</td><td>{c.Conf:F2}</td><td>{c.LenVal:F2} {u}</td><td>{c.AvgWVal:F3} {u}</td><td><b>{c.MaxWVal:F3} {u}</b></td><td>{c.Branches}</td><td>{c.AreaVal:F1} {u}²</td><td>{(over ? $"<b style='color:#dc2626'>超限(≥{warnLimit}{u})</b>" : "<span style='color:#16a34a'>受控</span>")}</td></tr>");
                }
                cards.Append("</tbody></table></div>");
            }

            string conclusion = overCnt > 0 ? $"<b style='color:#dc2626'>存在 {overCnt} 条裂缝最大宽度达({warnLimit} {anyUnit})。</b>" : $"<b style='color:#16a34a'>全部样本未发现达到预警限值 ({warnLimit} {anyUnit}) 的超限裂缝，整体受控。</b>";
            string css = "body{font-family:'Microsoft YaHei',sans-serif;background:#f8fafc;color:#1e293b;margin:0;padding:20px}.sheet{max-width:1040px;margin:0 auto;background:#fff;padding:30px 36px;box-shadow:0 3px 12px rgba(0,0,0,.06)}h1{text-align:center;border-bottom:3px solid #1e3a8a;padding-bottom:10px;font-size:23px}h3{color:#1e3a8a;border-left:4px solid #2563eb;padding-left:10px;margin-top:26px}.grid{width:100%;border-collapse:collapse;margin:10px 0}.grid th{background:#1e3a8a;color:#fff;padding:7px;font-size:12.5px;border:1px solid #1e3a8a}.grid td{border:1px solid #cbd5e1;padding:7px;font-size:12px;text-align:center}.grid img{max-width:100%;max-height:350px;height:auto}.lbl{background:#f1f5f9;font-weight:bold;width:15%}.bar{background:#f8fafc;padding:8px 12px;font-size:13px;color:#475569}.cap{font-size:12px;color:#64748b;margin-top:4px}.blk{page-break-inside:avoid;margin-bottom:24px}.act{max-width:1040px;margin:0 auto 10px;text-align:right}.act button{background:#2563eb;color:#fff;border:0;padding:8px 16px;border-radius:4px;cursor:pointer;font-weight:bold}@media print{body{background:#fff;padding:0}.sheet{box-shadow:none;padding:0}.act{display:none}}";
            string html = $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>{Esc(project)}</title><style>{css}</style></head><body>{(asWord ? "" : "<div class='act'><button onclick='window.print()'>🖨 打印报告 / 另存为 PDF</button></div>")}<div class='sheet'><h1>表面裂缝智能分割与精细测量报告</h1><table class='grid'><tr><td class='lbl'>工程名称</td><td>{Esc(project)}</td><td class='lbl'>检测部位</td><td>{Esc(location)}</td></tr><tr><td class='lbl'>检测人员</td><td>{Esc(inspector)}</td><td class='lbl'>生成时间</td><td>{nowStr}</td></tr><tr><td class='lbl'>样本统计</td><td>{totImgs} 张图像 (共 {totCracks} 条裂缝)</td><td class='lbl'>全局 Wmax</td><td><b style='color:#dc2626'>{globalWmax:F3} {anyUnit}</b></td></tr><tr><td class='lbl'>综合结论</td><td colspan='3'>{conclusion}</td></tr></table>{cards}</div></body></html>";

            if (!asWord) { File.WriteAllText(savePath, html, Encoding.UTF8); return; }
            const string boundary = "----CRACK_REPORT_PART_2026----";
            StringBuilder mhtml = new();
            mhtml.AppendLine("MIME-Version: 1.0").AppendLine($"Content-Type: multipart/related; boundary=\"{boundary}\"").AppendLine();
            mhtml.AppendLine($"--{boundary}").AppendLine("Content-Type: text/html; charset=\"utf-8\"").AppendLine("Content-Transfer-Encoding: base64").AppendLine();
            mhtml.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(html), Base64FormattingOptions.InsertLineBreaks));
            foreach (var (uri, b64) in mimeParts) mhtml.AppendLine($"--{boundary}").AppendLine("Content-Type: image/jpeg").AppendLine("Content-Transfer-Encoding: base64").AppendLine($"Content-Location: {uri}").AppendLine().AppendLine(b64);
            mhtml.AppendLine($"--{boundary}--");
            File.WriteAllText(savePath, mhtml.ToString(), Encoding.UTF8);
        }
    }
}