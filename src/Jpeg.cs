// JPEG для BLP1: декодер baseline (любое число каналов, прореживание, рестарт-маркеры) и кодер 4 каналов 1x1
// со стандартными таблицами libjpeg — как Pillow в tools/icons/blp1.py (CMYK без Adobe APP14, все каналы на таблице яркости).
// Каналы BLP (инвертированные B, G, R, A) не преобразуются: пиксели читаются и пишутся как есть.
using System;
using System.Collections.Generic;

static class Jpeg
{
    static readonly int[] ZZ = { 0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63 };
    static readonly int[] StdLuma = { 16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62, 18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99 };
    static readonly byte[] DcBits = { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
    static readonly byte[] DcVals = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
    static readonly byte[] AcBits = { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d };
    static readonly byte[] AcVals = {
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08, 0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
        0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
        0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
        0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
        0xf9, 0xfa };

    // K[x*8+u] = 0.5 * c(u) * cos((2x+1)u*pi/16) — ортонормированное ДКП, обратное = транспонированное
    static readonly float[] K = MakeK();
    static float[] MakeK()
    {
        float[] k = new float[64];
        for (int x = 0; x < 8; x++)
            for (int u = 0; u < 8; u++)
                k[x * 8 + u] = (float)(0.5 * (u == 0 ? Math.Sqrt(0.5) : 1.0) * Math.Cos((2 * x + 1) * u * Math.PI / 16));
        return k;
    }

    // таблица libjpeg для качества q (естественный порядок)
    public static int[] Table(int q)
    {
        q = Math.Max(1, Math.Min(100, q));
        int s = q < 50 ? 5000 / q : 200 - 2 * q;
        int[] t = new int[64];
        for (int i = 0; i < 64; i++) t[i] = Math.Max(1, Math.Min(255, (StdLuma[i] * s + 50) / 100));
        return t;
    }

    // примерное качество (1..100) по первой таблице квантования, как blp1.jpeg_quality; -1 — таблицы нет
    public static int Quality(byte[] d)
    {
        int i = 2;
        while (i + 4 <= d.Length && d[i] == 0xFF)
        {
            int m = d[i + 1];
            if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            if (m == 0xDA) break;
            int len = (d[i + 2] << 8) | d[i + 3];
            if (m == 0xDB && i + 4 + 65 <= d.Length)
            {
                int pq = d[i + 4] >> 4, p = i + 5, tot = 0;
                for (int k = 0; k < 64; k++) tot += pq == 0 ? d[p++] : ((d[p++] << 8) | d[p++]);
                int best = -1, bd = int.MaxValue;
                for (int q = 1; q <= 100; q++) { int s = 0; foreach (int v in Table(q)) s += v; int df = Math.Abs(s - tot); if (df < bd) { bd = df; best = q; } }
                return best;
            }
            i += 2 + len;
        }
        return -1;
    }

    // ---------- декодер ----------
    class Huff
    {
        int[] maxc = new int[17], valp = new int[17], minc = new int[17]; byte[] v;
        public Huff(int[] cnt, byte[] vals)
        {
            v = vals; int code = 0, k = 0;
            for (int l = 1; l <= 16; l++)
            {
                valp[l] = k; minc[l] = code; code += cnt[l]; k += cnt[l];
                maxc[l] = cnt[l] > 0 ? code - 1 : -1; code <<= 1;
            }
        }
        public int Decode(Bits r)
        {
            int c = 0;
            for (int l = 1; l <= 16; l++)
            {
                c = (c << 1) | r.Bit();
                if (maxc[l] >= 0 && c <= maxc[l]) return v[valp[l] + c - minc[l]];
            }
            throw new Exception("испорченный код Хаффмана");
        }
    }

    class Bits
    {
        byte[] d; public int Pos; int buf, cnt;
        public Bits(byte[] d, int pos) { this.d = d; Pos = pos; }
        public int Bit()
        {
            if (cnt == 0)
            {
                buf = 0;
                if (Pos < d.Length)
                {
                    buf = d[Pos];
                    if (buf == 0xFF)
                    {
                        int nx = Pos + 1 < d.Length ? d[Pos + 1] : 0xD9;
                        if (nx == 0) Pos += 2; else buf = 0; // маркер — дальше данных нет
                    }
                    else Pos++;
                }
                cnt = 8;
            }
            cnt--;
            return (buf >> cnt) & 1;
        }
        public int Recv(int s) { int v = 0; for (int i = 0; i < s; i++) v = (v << 1) | Bit(); return v; }
        public void Restart()
        {
            cnt = 0;
            while (Pos + 1 < d.Length && !(d[Pos] == 0xFF && d[Pos + 1] >= 0xD0 && d[Pos + 1] <= 0xD7)) Pos++;
            Pos += 2;
        }
    }

    static int Ext(int v, int s) { return s == 0 ? 0 : v < (1 << (s - 1)) ? v - (1 << s) + 1 : v; }

    // каналы в полном разрешении: P[c][y*W+x]
    public static byte[][] Decode(byte[] d, out int W, out int H)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) throw new Exception("не JPEG");
        int[][] qt = new int[4][]; Huff[] dch = new Huff[4], ach = new Huff[4];
        int N = 0, ri = 0; int[] cid = null, hs = null, vs = null, tq = null;
        W = H = 0;
        int i = 2;
        while (true)
        {
            if (i + 4 > d.Length || d[i] != 0xFF) throw new Exception("нет скана");
            int m = d[i + 1];
            if (m == 0xFF) { i++; continue; }
            if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            int len = (d[i + 2] << 8) | d[i + 3], s = i + 4, e = i + 2 + len;
            if (e > d.Length) throw new Exception("маркер за концом");
            if (m == 0xDB)
                for (int p = s; p < e;)
                {
                    int pq = d[p] >> 4, t = d[p] & 3; p++;
                    int[] tb = new int[64];
                    for (int k = 0; k < 64; k++) tb[ZZ[k]] = pq == 0 ? d[p++] : ((d[p++] << 8) | d[p++]);
                    qt[t] = tb;
                }
            else if (m == 0xC0 || m == 0xC1)
            {
                if (d[s] != 8) throw new Exception("не 8 бит");
                H = (d[s + 1] << 8) | d[s + 2]; W = (d[s + 3] << 8) | d[s + 4]; N = d[s + 5];
                cid = new int[N]; hs = new int[N]; vs = new int[N]; tq = new int[N];
                for (int c = 0; c < N; c++)
                {
                    cid[c] = d[s + 6 + c * 3]; hs[c] = Math.Max(1, d[s + 7 + c * 3] >> 4); vs[c] = Math.Max(1, d[s + 7 + c * 3] & 15); tq[c] = d[s + 8 + c * 3] & 3;
                }
            }
            else if (m >= 0xC2 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC)
                throw new Exception("SOF" + (m - 0xC0) + " (прогрессивный/арифметический) не поддерживается");
            else if (m == 0xC4)
                for (int p = s; p < e;)
                {
                    int tc = d[p] >> 4, th = d[p] & 3; p++;
                    int[] cnt = new int[17]; int tot = 0;
                    for (int k = 1; k <= 16; k++) { cnt[k] = d[p++]; tot += cnt[k]; }
                    byte[] v = new byte[tot]; Array.Copy(d, p, v, 0, tot); p += tot;
                    if (tc == 0) dch[th] = new Huff(cnt, v); else ach[th] = new Huff(cnt, v);
                }
            else if (m == 0xDD) ri = (d[s] << 8) | d[s + 1];
            else if (m == 0xDA)
            {
                if (N == 0 || W == 0 || H == 0) throw new Exception("скан до SOF");
                int ns = d[s];
                if (ns != N) throw new Exception("несколько сканов не поддерживается");
                int[] order = new int[ns], td = new int[ns], ta = new int[ns];
                for (int k = 0; k < ns; k++)
                {
                    int id = d[s + 1 + k * 2]; order[k] = Array.IndexOf(cid, id);
                    if (order[k] < 0) throw new Exception("неизвестный канал");
                    td[k] = d[s + 2 + k * 2] >> 4; ta[k] = d[s + 2 + k * 2] & 15;
                }
                return Scan(d, e, N, W, H, hs, vs, tq, qt, dch, ach, order, td, ta, ri);
            }
            else if (m == 0xD9) throw new Exception("нет скана");
            i = e;
        }
    }

    static byte[][] Scan(byte[] d, int pos, int N, int W, int H, int[] hs, int[] vs, int[] tq, int[][] qt, Huff[] dch, Huff[] ach,
        int[] order, int[] td, int[] ta, int ri)
    {
        if (N == 1) { hs[0] = vs[0] = 1; }
        int hm = 1, vm = 1;
        for (int c = 0; c < N; c++) { hm = Math.Max(hm, hs[c]); vm = Math.Max(vm, vs[c]); }
        int mcx = (W + 8 * hm - 1) / (8 * hm), mcy = (H + 8 * vm - 1) / (8 * vm);
        int[] pw = new int[N], ph = new int[N]; byte[][] pl = new byte[N][];
        for (int c = 0; c < N; c++)
        {
            if (qt[tq[c]] == null) throw new Exception("нет таблицы квантования");
            pw[c] = mcx * hs[c] * 8; ph[c] = mcy * vs[c] * 8; pl[c] = new byte[pw[c] * ph[c]];
        }
        Bits r = new Bits(d, pos);
        int[] pred = new int[N];
        float[] blk = new float[64], tmp = new float[64];
        int total = mcx * mcy, n = 0;
        for (int my = 0; my < mcy; my++)
            for (int mx = 0; mx < mcx; mx++)
            {
                if (ri > 0 && n > 0 && n % ri == 0) { r.Restart(); Array.Clear(pred, 0, N); }
                n++;
                for (int k = 0; k < order.Length; k++)
                {
                    int c = order[k]; Huff dh = dch[td[k]], ah = ach[ta[k]]; int[] q = qt[tq[c]];
                    if (dh == null || ah == null) throw new Exception("нет таблицы Хаффмана");
                    for (int v = 0; v < vs[c]; v++)
                        for (int h = 0; h < hs[c]; h++)
                        {
                            Array.Clear(blk, 0, 64);
                            int t = dh.Decode(r);
                            pred[c] += t == 0 ? 0 : Ext(r.Recv(t), t);
                            blk[0] = pred[c] * q[0];
                            for (int z = 1; z < 64;)
                            {
                                int rs = ah.Decode(r), rr = rs >> 4, ss = rs & 15;
                                if (ss == 0) { if (rr == 15) { z += 16; continue; } break; }
                                z += rr; if (z > 63) break;
                                blk[ZZ[z]] = Ext(r.Recv(ss), ss) * q[ZZ[z]]; z++;
                            }
                            Idct(blk, tmp);
                            int ox = (mx * hs[c] + h) * 8, oy = (my * vs[c] + v) * 8; byte[] P = pl[c]; int st = pw[c];
                            for (int y = 0; y < 8; y++)
                                for (int x = 0; x < 8; x++)
                                {
                                    int val = (int)Math.Round(blk[y * 8 + x] + 128);
                                    P[(oy + y) * st + ox + x] = (byte)(val < 0 ? 0 : val > 255 ? 255 : val);
                                }
                        }
                }
            }
        // в полное разрешение
        byte[][] res = new byte[N][];
        for (int c = 0; c < N; c++)
        {
            byte[] o = new byte[W * H], P = pl[c];
            for (int y = 0; y < H; y++)
            {
                int sy = y * vs[c] / vm;
                for (int x = 0; x < W; x++) o[y * W + x] = P[sy * pw[c] + x * hs[c] / hm];
            }
            res[c] = o;
        }
        return res;
    }

    static void Idct(float[] b, float[] t)
    {
        // t[v][x] = sum_u K[x,u] F[v][u];  out[y][x] = sum_v K[y,v] t[v][x]
        for (int v = 0; v < 8; v++)
            for (int x = 0; x < 8; x++)
            {
                float s = 0; for (int u = 0; u < 8; u++) s += K[x * 8 + u] * b[v * 8 + u];
                t[v * 8 + x] = s;
            }
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                float s = 0; for (int v = 0; v < 8; v++) s += K[y * 8 + v] * t[v * 8 + x];
                b[y * 8 + x] = s;
            }
    }

    // ---------- кодер ----------
    static void Codes(byte[] bits, byte[] vals, out int[] co, out int[] si)
    {
        co = new int[256]; si = new int[256];
        int code = 0, k = 0;
        for (int l = 1; l <= 16; l++)
        {
            for (int j = 0; j < bits[l - 1]; j++) { co[vals[k]] = code; si[vals[k]] = l; code++; k++; }
            code <<= 1;
        }
    }

    class Out
    {
        public List<byte> B = new List<byte>(); int acc, n;
        public void Put(int code, int size)
        {
            for (int i = size - 1; i >= 0; i--)
            {
                acc = (acc << 1) | ((code >> i) & 1);
                if (++n == 8) { B.Add((byte)acc); if (acc == 0xFF) B.Add(0); acc = 0; n = 0; }
            }
        }
        public void Flush() { while (n != 0) Put(1, 1); }
    }

    static int Cat(int v) { v = Math.Abs(v); int s = 0; while (v > 0) { s++; v >>= 1; } return s; }

    // P[c][y*W+x], 1x1 у всех каналов; без APP-маркеров (как blp1._jpeg после _strip_app14)
    public static byte[] Encode(byte[][] P, int W, int H, int quality)
    {
        int N = P.Length;
        int[] q = Table(quality);
        int[] dco, dsi, aco, asi;
        Codes(DcBits, DcVals, out dco, out dsi); Codes(AcBits, AcVals, out aco, out asi);
        List<byte> h = new List<byte> { 0xFF, 0xD8 };
        h.AddRange(new byte[] { 0xFF, 0xDB, 0, 67, 0 }); for (int k = 0; k < 64; k++) h.Add((byte)q[ZZ[k]]);
        int sl = 8 + 3 * N;
        h.AddRange(new byte[] { 0xFF, 0xC0, (byte)(sl >> 8), (byte)sl, 8, (byte)(H >> 8), (byte)H, (byte)(W >> 8), (byte)W, (byte)N });
        for (int c = 0; c < N; c++) { h.Add((byte)(c + 1)); h.Add(0x11); h.Add(0); }
        Dht(h, 0x00, DcBits, DcVals); Dht(h, 0x10, AcBits, AcVals);
        int ss = 6 + 2 * N;
        h.AddRange(new byte[] { 0xFF, 0xDA, (byte)(ss >> 8), (byte)ss, (byte)N });
        for (int c = 0; c < N; c++) { h.Add((byte)(c + 1)); h.Add(0x00); }
        h.AddRange(new byte[] { 0, 63, 0 });

        Out o = new Out(); o.B = h;
        int[] pred = new int[N];
        float[] f = new float[64], t = new float[64];
        int bx = (W + 7) / 8, by = (H + 7) / 8;
        for (int my = 0; my < by; my++)
            for (int mx = 0; mx < bx; mx++)
                for (int c = 0; c < N; c++)
                {
                    byte[] p = P[c];
                    for (int y = 0; y < 8; y++)
                    {
                        int sy = Math.Min(my * 8 + y, H - 1);
                        for (int x = 0; x < 8; x++) f[y * 8 + x] = p[sy * W + Math.Min(mx * 8 + x, W - 1)] - 128f;
                    }
                    // t[y][u] = sum_x K[x,u] f[y][x];  F[v][u] = sum_y K[y,v] t[y][u]
                    for (int y = 0; y < 8; y++)
                        for (int u = 0; u < 8; u++)
                        {
                            float s = 0; for (int x = 0; x < 8; x++) s += K[x * 8 + u] * f[y * 8 + x];
                            t[y * 8 + u] = s;
                        }
                    int[] z = new int[64];
                    for (int v = 0; v < 8; v++)
                        for (int u = 0; u < 8; u++)
                        {
                            float s = 0; for (int y = 0; y < 8; y++) s += K[y * 8 + v] * t[y * 8 + u];
                            z[v * 8 + u] = (int)Math.Round(s / q[v * 8 + u], MidpointRounding.AwayFromZero);
                        }
                    int diff = z[0] - pred[c]; pred[c] = z[0];
                    int dc = Cat(diff); o.Put(dco[dc], dsi[dc]); if (dc > 0) o.Put(diff < 0 ? diff - 1 : diff, dc);
                    int run = 0;
                    for (int k = 1; k < 64; k++)
                    {
                        int v = z[ZZ[k]];
                        if (v == 0) { run++; continue; }
                        while (run > 15) { o.Put(aco[0xF0], asi[0xF0]); run -= 16; }
                        int s = Cat(v), sym = (run << 4) | s;
                        o.Put(aco[sym], asi[sym]); o.Put(v < 0 ? v - 1 : v, s);
                        run = 0;
                    }
                    if (run > 0) o.Put(aco[0], asi[0]);
                }
        o.Flush();
        o.B.Add(0xFF); o.B.Add(0xD9);
        return o.B.ToArray();
    }

    static void Dht(List<byte> h, int tc, byte[] bits, byte[] vals)
    {
        int l = 3 + 16 + vals.Length;
        h.AddRange(new byte[] { 0xFF, 0xC4, (byte)(l >> 8), (byte)l, (byte)tc });
        h.AddRange(bits); h.AddRange(vals);
    }
}
