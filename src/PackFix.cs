// ChoicePackFix — массовое облегчение пака моделей Warcraft III 1.26 (BLP1 / MDX v800).
// Текстуры: площадь > 512x512 — в 4 раза меньше по стороне, ровно 512x512 — в 2 раза (основным становится
// готовый мип-уровень, без пережатия; после записи размер в JPEG проверяется, при ошибке файл возвращается).
// Частицы (PRE2): эмиттер с пиком > 1000 частиц — частота до ~600 (не больше чем в 4 раза); выброс за раз > 200 —
// до ~150 (не больше чем в 4 раза); модель, где ничего не урезано, но сумма > 1000 — частота всех / до 2;
// затем сумма по модели > 3000 — частота всех / до 2, сумма выброса > 300 — выброс / до 4.
// Размер частиц везде растёт на корень из коэффициента — эффект закрывает ту же площадь меньшим числом частиц.
// Обработанные файлы записываются в _packfix_done.txt в папке и повторно не урезаются (кроме --force).
// Пережатие JPEG (--quality Q, по умолчанию 65, 0 — выкл.): JPEG-BLP с качеством выше Q пересохраняется с качеством Q
// по алгоритму tools/icons/blp1.py (CMYK без APP14, общий заголовок), пиксели и альфа не меняются,
// мип-уровни строятся заново — полная цепочка до 1x1 (512x512 — 10 мипов). Палитровые BLP не трогаются.
// Сборка: build.bat (csc из .NET Framework 4).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

class PackFix
{
    static string Root, Backup, LogPath, DonePath;
    static bool Write, DoTex = true, DoPart = true, Force, Report, MarkDone;
    static int HalfShift = 1; // --half: 1 — в 2 раза по стороне, 2 — в 4 раза
    static int MaxSide;       // --max N: все текстуры больше N по большей стороне — до N (вместо правила площади)
    static int Quality = 65;  // --quality Q: пережать JPEG-BLP с качеством выше Q; 0 — не пережимать
    static bool KeepSize;     // --keep-size: размер текстур не менять (только пережатие)
    static int nRec, nRecLow, nRecErr; static long recBefore, recAfter;
    static HashSet<string> Half = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, string> Done = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    static List<string> Log = new List<string>();
    static int nTex4, nTex2, nTexSkip, nTexBad, nMdx, nEmit;
    static double savedMB;
    static long totalBytes, diskSaved; // общий вес папки и сколько байт на диске освобождено
    static CultureInfo IC = CultureInfo.InvariantCulture;
    // окно (Gui.cs): ход обработки — (просмотрено файлов, всего файлов)
    public static Action<int, int> OnFiles;

#if !GUI
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return Run(args);
    }
#endif

    public static int Run(string[] args)
    {
        // состояние прошлого запуска (в окне Run вызывается много раз)
        Write = Force = Report = MarkDone = false; DoTex = DoPart = true; HalfShift = 1; MaxSide = 0; Quality = 65; KeepSize = false;
        Half.Clear(); Done.Clear(); Log.Clear();
        nTex4 = nTex2 = nTexSkip = nTexBad = nMdx = nEmit = 0; savedMB = 0;
        nRec = nRecLow = nRecErr = 0; recBefore = recAfter = 0; totalBytes = diskSaved = 0;
        if (args.Length == 0 || args[0] == "-h" || args[0] == "--help") { Help(); return 0; }
        Root = Path.GetFullPath(args[0].TrimEnd('\\', '/'));
        if (!Directory.Exists(Root)) { Console.WriteLine("Папка не найдена: " + Root); return 1; }
        Backup = Root + "_backup";
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--write") Write = true;
            else if (a == "--force") Force = true;
            else if (a == "--report") Report = true;
            else if (a == "--mark-done") MarkDone = true;
            else if (a == "--textures-only") DoPart = false;
            else if (a == "--particles-only") DoTex = false;
            else if (a == "--backup" && i + 1 < args.Length) Backup = Path.GetFullPath(args[++i]);
            else if (a == "--half4") HalfShift = 2;
            else if (a == "--keep-size") KeepSize = true;
            else if (a == "--quality" && i + 1 < args.Length && int.TryParse(args[i + 1], out Quality) && Quality >= 0 && Quality <= 100) i++;
            else if (a == "--max" && i + 1 < args.Length && int.TryParse(args[i + 1], out MaxSide) && MaxSide > 0) i++;
            else if (a == "--half" && i + 1 < args.Length) { foreach (string n in args[++i].Split(',')) if (n.Trim() != "") Half.Add(n.Trim()); }
            else { Console.WriteLine("Неизвестный параметр: " + a); Help(); return 1; }
        }
        LogPath = Path.Combine(Root, "_packfix_log.txt");
        DonePath = Path.Combine(Root, "_packfix_done.txt");
        if (File.Exists(DonePath))
            foreach (string l in File.ReadAllLines(DonePath, Encoding.UTF8))
            { int t = l.IndexOf('\t'); if (t > 0) Done[l.Substring(0, t)] = l.Substring(t + 1); }

        if (Report) { DoReport(); return 0; }
        if (MarkDone)
        {
            // уже урезанная папка: все текущие .mdx / .blp считаются обработанными, файлы не меняются
            int c = 0;
            foreach (string p in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext == ".mdx" || ext == ".blp") { Done[Rel(p)] = Stamp(p); c++; }
            }
            File.WriteAllLines(DonePath, Done.Select(kv => kv.Key + "	" + kv.Value), Encoding.UTF8);
            Console.WriteLine("Отмечено как обработанные: " + c + " -> " + DonePath);
            return 0;
        }
        Console.WriteLine((Write ? "ЗАПИСЬ" : "ПРОБНЫЙ ПРОГОН (без --write ничего не меняется)") + ": " + Root);
        if (Write) Console.WriteLine("Резервные копии: " + Backup);
        int nFiles = 0;
        string[] files = Directory.GetFiles(Root, "*", SearchOption.AllDirectories);
        if (OnFiles != null) OnFiles(0, files.Length);
        foreach (string p in files)
        {
            if (OnFiles != null && (++nFiles % 10 == 0 || nFiles == files.Length)) OnFiles(nFiles, files.Length);
            string ext = Path.GetExtension(p).ToLowerInvariant();
            string fn = Path.GetFileName(p);
            if (fn != "_packfix_log.txt" && fn != "_packfix_done.txt") try { totalBytes += new FileInfo(p).Length; } catch { }
            try
            {
                if (ext == ".blp")
                {
                    if (Half.Count > 0) { if (Half.Contains(Path.GetFileName(p))) Blp(p, HalfShift); }
                    else if (DoTex) Blp(p, 0);
                }
                else if (ext == ".mdx" && DoPart && Half.Count == 0) Mdx(p);
            }
            catch (Exception e) { Log.Add("ERR " + Rel(p) + " " + e.Message); }
        }
        string weight = "Общий вес папки: " + MB(totalBytes) + " -> " + MB(totalBytes - diskSaved) + " МБ"
            + (Write ? "" : " (после записи)") + ", меньше на " + MB(diskSaved) + " МБ"
            + (totalBytes > 0 ? " (" + Math.Round(100.0 * diskSaved / totalBytes) + "%)" : "");
        Log.Add(""); Log.Add(weight);
        File.WriteAllLines(LogPath, Log, Encoding.UTF8);
        if (Write) File.WriteAllLines(DonePath, Done.Select(kv => kv.Key + "\t" + kv.Value), Encoding.UTF8);
        Console.WriteLine("Текстуры: /4 — " + nTex4 + ", /2 — " + nTex2 + ", без мипов — " + nTexSkip + ", возвращено из-за ошибки — " + nTexBad
            + " | экономия памяти ~" + Math.Round(savedMB) + " МБ");
        if (Quality > 0)
            Console.WriteLine("Пережато JPEG (q=" + Quality + "): " + nRec + ", " + Math.Round(recBefore / 1048576.0, 1) + " -> " + Math.Round(recAfter / 1048576.0, 1)
                + " МБ на диске | уже не выше q=" + Quality + " — " + nRecLow + ", не удалось — " + nRecErr);
        Console.WriteLine("Модели: " + nMdx + ", эмиттеров: " + nEmit);
        Console.WriteLine(weight);
        Console.WriteLine("Журнал: " + LogPath);
        return 0;
    }

    static void Help()
    {
        Console.WriteLine("ChoiceOptimizer <папка> [--write] [--backup <папка>] [--textures-only | --particles-only] [--force] [--report] [--half a.blp,b.blp [--half4]] [--max 256 | --keep-size] [--quality 65] [--mark-done]");
        Console.WriteLine("  без --write  — только пробный прогон и журнал _packfix_log.txt");
        Console.WriteLine("  --write      — изменить файлы (оригиналы копируются в <папка>_backup, если их там ещё нет)");
        Console.WriteLine("  --force      — урезать и файлы из _packfix_done.txt (уже обработанные)");
        Console.WriteLine("  --report     — отчёт: самые тяжёлые модели по частицам и крупные текстуры");
        Console.WriteLine("  --mark-done  — отметить все текущие .mdx/.blp как уже обработанные (для уже урезанной папки)");
        Console.WriteLine("  --half       — только эти текстуры, ещё в 2 раза по стороне (например, у одного героя); с --half4 — в 4 раза");
        Console.WriteLine("  --max N      — вместо правила: все текстуры больше N по большей стороне уменьшить до N (например, 256)");
        Console.WriteLine("  --keep-size  — размер текстур не менять (только пережатие JPEG)");
        Console.WriteLine("  --quality Q  — пережать JPEG-BLP с качеством выше Q до Q (по умолчанию 65; 0 — не пережимать);");
        Console.WriteLine("                 пиксели и альфа как есть, мип-уровни — полная цепочка до 1x1");
    }

    static string MB(long b) { return Math.Round(b / 1048576.0, 1).ToString("0.0", IC); }
    static string Rel(string p) { return p.Substring(Root.Length).TrimStart('\\', '/'); }
    static string Stamp(string p) { FileInfo f = new FileInfo(p); return f.Length + "|" + f.LastWriteTimeUtc.Ticks; }
    static bool Skip(string p) { string s; return !Force && Done.TryGetValue(Rel(p), out s) && s == Stamp(p); }

    static void Save(string p, byte[] data)
    {
        string d = Path.Combine(Backup, Rel(p));
        if (!File.Exists(d)) { Directory.CreateDirectory(Path.GetDirectoryName(d)); File.Copy(p, d); }
        File.WriteAllBytes(p, data);
        Done[Rel(p)] = Stamp(p);
    }

    static uint U(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
    static float F(byte[] b, int o) { return BitConverter.ToSingle(b, o); }
    static void WU(byte[] b, int o, uint v) { Array.Copy(BitConverter.GetBytes(v), 0, b, o, 4); }
    static void WF(byte[] b, int o, float v) { Array.Copy(BitConverter.GetBytes(v), 0, b, o, 4); }
    static string Tag(byte[] b, int o) { return Encoding.ASCII.GetString(b, o, 4); }

    // ---------- BLP1 ----------
    // forceShift 1 — режим --half; 0 — по правилу площади
    static void Blp(string p, int forceShift)
    {
        byte[] b = File.ReadAllBytes(p);
        if (b.Length < 160 || Tag(b, 0) != "BLP1") return;
        uint comp = U(b, 4), w = U(b, 12), h = U(b, 16), mips = U(b, 24);
        int s = forceShift;
        // --max не зависит от _packfix_done.txt: текстура, которая уже не больше N, всё равно не меняется
        if (s == 0 && !KeepSize && !(Skip(p) && MaxSide == 0))
        {
            if (MaxSide > 0) { while (Math.Max(w, h) >> s > MaxSide) s++; }
            else { long a = (long)w * h; if (a > 512 * 512) s = 2; else if (a == 512 * 512) s = 1; }
        }
        // пережатие: только JPEG-BLP с качеством заметно выше заданного (иначе лишняя потеря качества)
        if (Quality > 0 && comp == 0)
        {
            int q = BlpQuality(b);
            if (q > Quality + 2)
            {
                try { RecBlp(p, b, s, q); return; }
                catch (Exception e) { nRecErr++; Log.Add("JPG ошибка " + Rel(p) + ": " + e.Message + " — без пережатия"); }
            }
            else nRecLow++;
        }
        if (s == 0) return;
        uint[] off = new uint[16], sz = new uint[16];
        for (int i = 0; i < 16; i++) { off[i] = U(b, 28 + i * 4); sz[i] = U(b, 92 + i * 4); }
        if (mips == 0 || off[s] == 0 || sz[s] == 0 || off[s] + sz[s] > b.Length) { nTexSkip++; Log.Add("SKIP (нет мипов) " + Rel(p) + " " + w + "x" + h); return; }
        int preLen;
        if (comp == 0) preLen = 4 + (int)U(b, 156); else if (comp == 1) preLen = 1024; else { nTexSkip++; return; }
        uint nw = Math.Max(1u, w >> s), nh = Math.Max(1u, h >> s);
        List<byte> o = new List<byte>(b.Length);
        byte[] head = new byte[156]; Array.Copy(b, head, 156);
        WU(head, 12, nw); WU(head, 16, nh);
        uint pos = (uint)(156 + preLen);
        List<byte[]> parts = new List<byte[]>();
        for (int i = 0; i < 16; i++)
        {
            int j = i + s;
            if (j < 16 && off[j] != 0 && sz[j] != 0 && off[j] + sz[j] <= b.Length)
            {
                WU(head, 28 + i * 4, pos); WU(head, 92 + i * 4, sz[j]);
                byte[] m = new byte[sz[j]]; Array.Copy(b, off[j], m, 0, sz[j]); parts.Add(m); pos += sz[j];
            }
            else { WU(head, 28 + i * 4, 0); WU(head, 92 + i * 4, 0); }
        }
        o.AddRange(head); for (int i = 0; i < preLen; i++) o.Add(b[156 + i]); foreach (byte[] m in parts) o.AddRange(m);
        byte[] res = o.ToArray();
        string err = CheckBlp(res);
        if (err != null) { nTexBad++; Log.Add("BAD (оставлен как был) " + Rel(p) + ": " + err); return; }
        if (s >= 2) nTex4++; else nTex2++;
        diskSaved += b.Length - res.Length;
        savedMB += ((double)w * h - (double)nw * nh) * 4 * 4 / 3 / 1048576;
        Log.Add("BLP " + Rel(p) + " " + w + "x" + h + " -> " + nw + "x" + nh);
        if (Write) Save(p, res);
    }

    // JPEG мипа i (общий заголовок + данные мипа) или null
    static byte[] MipJpeg(byte[] b, int i)
    {
        uint o = U(b, 28 + i * 4), sz = U(b, 92 + i * 4), jh = U(b, 156);
        if (o == 0 || sz == 0 || o + sz > b.Length || 160 + jh > b.Length) return null;
        byte[] j = new byte[jh + sz]; Array.Copy(b, 160, j, 0, jh); Array.Copy(b, o, j, jh, sz);
        return j;
    }

    static int BlpQuality(byte[] b) { byte[] j = MipJpeg(b, 0); return j == null ? -1 : Jpeg.Quality(j); }

    // полная цепочка мипов до 1x1: 512x512 — 10, 1024x1024 — 11, 256x256 — 9
    static int FullMips(uint w, uint h) { int n = 1; uint m = Math.Max(w, h); while (m > 1 && n < 16) { m >>= 1; n++; } return n; }
    static int MipCount(byte[] b) { int n = 0; while (n < 16 && U(b, 28 + n * 4) != 0 && U(b, 92 + n * 4) != 0) n++; return n; }

    // 2x2 усреднение каждого канала
    static byte[][] HalfPx(byte[][] px, ref int w, ref int h)
    {
        int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
        byte[][] r = new byte[px.Length][];
        for (int c = 0; c < px.Length; c++)
        {
            byte[] s = px[c], o = new byte[nw * nh];
            for (int y = 0; y < nh; y++)
            {
                int y0 = Math.Min(2 * y, h - 1) * w, y1 = Math.Min(2 * y + 1, h - 1) * w;
                for (int x = 0; x < nw; x++)
                {
                    int x0 = Math.Min(2 * x, w - 1), x1 = Math.Min(2 * x + 1, w - 1);
                    o[y * nw + x] = (byte)((s[y0 + x0] + s[y0 + x1] + s[y1 + x0] + s[y1 + x1] + 2) >> 2);
                }
            }
            r[c] = o;
        }
        w = nw; h = nh;
        return r;
    }

    // пережатие JPEG-BLP с качеством Quality (+ уменьшение на s мип-уровней), как save_blp1 в tools/icons/blp1.py
    static void RecBlp(string p, byte[] b, int s, int q0)
    {
        uint w = U(b, 12), h = U(b, 16), hasMips = U(b, 24);
        uint nw = Math.Max(1u, w >> s), nh = Math.Max(1u, h >> s);
        // источник — готовый мип s (если есть), иначе мип 0 с уменьшением
        int src = MipJpeg(b, s) != null ? s : 0;
        byte[] j = MipJpeg(b, src);
        if (j == null) throw new Exception("мип 0 вне файла");
        int jw, jh;
        byte[][] px = Jpeg.Decode(j, out jw, out jh);
        if (px.Length != 4) throw new Exception("каналов " + px.Length + ", ожидалось 4");
        for (int k = src; k < s; k++) px = HalfPx(px, ref jw, ref jh);
        if (jw != nw || jh != nh) throw new Exception("размер JPEG " + jw + "x" + jh + " != " + nw + "x" + nh);
        // мипы: полная цепочка до 1x1 (у текстур без мипов в заголовке — только основной)
        List<byte[]> mp = new List<byte[]>();
        while (true)
        {
            mp.Add(Jpeg.Encode(px, jw, jh, Quality));
            if (hasMips == 0 || (jw == 1 && jh == 1) || mp.Count == 16) break;
            px = HalfPx(px, ref jw, ref jh);
        }
        // общий заголовок — общий префикс всех мипов (не длиннее 624, как в blp1.py)
        int n = mp.Min(x => x.Length), k0 = 0;
        while (k0 < n && k0 < 624) { byte c = mp[0][k0]; if (mp.Any(x => x[k0] != c)) break; k0++; }
        byte[] head = new byte[160];
        Array.Copy(b, head, 28);
        WU(head, 12, nw); WU(head, 16, nh); WU(head, 156, (uint)k0);
        uint pos = (uint)(160 + k0);
        for (int i = 0; i < mp.Count; i++) { WU(head, 28 + i * 4, pos); WU(head, 92 + i * 4, (uint)(mp[i].Length - k0)); pos += (uint)(mp[i].Length - k0); }
        List<byte> o = new List<byte>((int)pos);
        o.AddRange(head);
        for (int i = 0; i < k0; i++) o.Add(mp[0][i]);
        foreach (byte[] m in mp) for (int i = k0; i < m.Length; i++) o.Add(m[i]);
        byte[] res = o.ToArray();
        string err = CheckBlp(res) ?? CheckMips(res);
        if (err != null) { nTexBad++; Log.Add("BAD (оставлен как был) " + Rel(p) + ": " + err); return; }
        if (s == 0 && res.Length >= b.Length) { nRecLow++; Log.Add("JPG не меньше — оставлен " + Rel(p) + " q" + q0); return; }
        nRec++; recBefore += b.Length; recAfter += res.Length; diskSaved += b.Length - res.Length;
        if (s > 0) { if (s >= 2) nTex4++; else nTex2++; savedMB += ((double)w * h - (double)nw * nh) * 4 * 4 / 3 / 1048576; }
        Log.Add("JPG " + Rel(p) + " q" + q0 + " -> " + Quality + ", " + w + "x" + h + (s > 0 ? " -> " + nw + "x" + nh : "")
            + ", " + (b.Length + 512) / 1024 + " -> " + (res.Length + 512) / 1024 + " КБ, мипов " + mp.Count);
        if (Write) Save(p, res);
    }

    // все мипы на месте, их число — полная цепочка, размер каждого в SOF совпадает с ожидаемым
    static string CheckMips(byte[] b)
    {
        uint w = U(b, 12), h = U(b, 16);
        int n = MipCount(b), need = U(b, 24) == 0 ? 1 : FullMips(w, h);
        if (n != need) return "мипов " + n + ", нужно " + need;
        for (int i = 0; i < n; i++)
        {
            byte[] j = MipJpeg(b, i);
            if (j == null) return "мип " + i + " вне файла";
            int jw, jh; Jpeg.Decode(j, out jw, out jh);
            if (jw != Math.Max(1u, w >> i) || jh != Math.Max(1u, h >> i)) return "мип " + i + ": " + jw + "x" + jh;
        }
        return null;
    }

    // мип 0 в границах файла; у JPEG размер в маркере SOF совпадает с заголовком
    static string CheckBlp(byte[] b)
    {
        uint comp = U(b, 4), w = U(b, 12), h = U(b, 16), o0 = U(b, 28), s0 = U(b, 92);
        if (o0 == 0 || s0 == 0 || o0 + s0 > b.Length) return "мип 0 вне файла";
        if (comp != 0) return s0 >= w * h ? null : "мало данных палитры";
        int jh = (int)U(b, 156);
        byte[] j = new byte[jh + s0]; Array.Copy(b, 160, j, 0, jh); Array.Copy(b, o0, j, jh, s0);
        int i = 2;
        while (i < j.Length - 9)
        {
            if (j[i] != 0xFF) { i++; continue; }
            int m = j[i + 1];
            if (m >= 0xC0 && m <= 0xC2)
            {
                int hh = (j[i + 5] << 8) | j[i + 6], ww = (j[i + 7] << 8) | j[i + 8];
                return (ww == w && hh == h) ? null : ("SOF " + ww + "x" + hh + " != " + w + "x" + h);
            }
            if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            if (m == 0xDA) break;
            i += 2 + ((j[i + 2] << 8) | j[i + 3]);
        }
        return "нет SOF";
    }

    // ---------- MDX: эмиттеры PRE2 ----------
    class Em { public int K; public float Rate, RMax, Life; public bool Squirt; public List<int> KeyPos = new List<int>(); public string Name; }

    static List<Em> ReadEmitters(byte[] b)
    {
        List<Em> list = new List<Em>();
        if (b.Length < 12 || Tag(b, 0) != "MDLX") return list;
        int o = 4;
        while (o + 8 <= b.Length)
        {
            string tag = Tag(b, o); int sz = (int)U(b, o + 4), s = o + 8, e = s + sz;
            if (e > b.Length || sz < 0) break;
            if (tag == "PRE2")
            {
                int q = s;
                while (q < e)
                {
                    int isz = (int)U(b, q); int ns = (int)U(b, q + 4); int k = q + 4 + ns;
                    if (isz <= 0) break;
                    Em m = new Em();
                    m.K = k; m.Life = F(b, k + 16); m.Rate = F(b, k + 20); m.Squirt = U(b, k + 159) != 0; m.RMax = m.Rate;
                    m.Name = Encoding.GetEncoding(1252).GetString(b, q + 8, 80).Split('\0')[0];
                    int kk = k + 171;
                    while (kk + 16 <= q + isz)
                    {
                        string t = Tag(b, kk); if (!t.StartsWith("KP2")) break;
                        int n = (int)U(b, kk + 4), it = (int)U(b, kk + 8); int r = kk + 16;
                        for (int i = 0; i < n; i++)
                        {
                            r += 4;
                            if (t == "KP2E") { m.KeyPos.Add(r); m.RMax = Math.Max(m.RMax, F(b, r)); }
                            r += 4;
                            if (it > 1) { if (t == "KP2E") { m.KeyPos.Add(r); m.KeyPos.Add(r + 4); } r += 8; }
                        }
                        kk = r;
                    }
                    if (m.RMax > 0 && !float.IsInfinity(m.RMax) && !float.IsNaN(m.RMax) && !float.IsNaN(m.Life)) list.Add(m);
                    q += isz;
                }
            }
            o = e;
        }
        return list;
    }

    static void Cut(byte[] b, Em m, double f)
    {
        float g = (float)Math.Sqrt(f);
        WF(b, m.K + 20, (float)(F(b, m.K + 20) / f));
        foreach (int x in m.KeyPos) WF(b, x, (float)(F(b, x) / f));
        foreach (int x in new int[] { 95, 99, 103 }) WF(b, m.K + x, F(b, m.K + x) * g);
        m.Rate = (float)(m.Rate / f); m.RMax = (float)(m.RMax / f);
        nEmit++;
    }

    static string N(double v) { return Math.Round(v).ToString(IC); }

    static void Mdx(string p)
    {
        if (Skip(p)) return;
        byte[] b = File.ReadAllBytes(p);
        List<Em> em = ReadEmitters(b);
        if (em.Count == 0) return;
        List<string> notes = new List<string>();
        bool touched = false;
        // 1. отдельные эмиттеры
        foreach (Em m in em)
        {
            double f = 1;
            if (m.Squirt) { if (m.RMax > 200) f = Math.Min(4, m.RMax / 150.0); }
            else { double pk = m.RMax * m.Life; if (pk > 1000) f = Math.Min(4, pk / 600.0); }
            if (f > 1.01) { notes.Add(m.Name + ": " + (m.Squirt ? "выброс " : "частота ") + N(m.RMax) + " -> " + N(m.RMax / f)); Cut(b, m, f); touched = true; }
        }
        List<Em> cont = em.Where(x => !x.Squirt).ToList(), sq = em.Where(x => x.Squirt).ToList();
        // 2. средние эмиттеры, но много в сумме
        double tot = cont.Sum(x => (double)x.RMax * x.Life);
        if (!touched && tot > 1000)
        {
            double f = Math.Min(2, tot / 1000); foreach (Em m in cont) Cut(b, m, f);
            notes.Add("сумма " + N(tot) + " -> " + N(tot / f)); touched = true;
        }
        // 3. лимиты на модель
        tot = cont.Sum(x => (double)x.RMax * x.Life);
        if (tot > 3000) { double f = Math.Min(2, tot / 3000); foreach (Em m in cont) Cut(b, m, f); notes.Add("сумма " + N(tot) + " -> " + N(tot / f)); touched = true; }
        double sqt = sq.Sum(x => (double)x.RMax);
        if (sqt > 300) { double f = Math.Min(4, sqt / 300); foreach (Em m in sq) Cut(b, m, f); notes.Add("выброс всего " + N(sqt) + " -> " + N(sqt / f)); touched = true; }
        if (!touched) return;
        nMdx++;
        Log.Add("MDX " + Rel(p) + " | " + string.Join("; ", notes));
        if (Write) Save(p, b);
    }

    // ---------- отчёт ----------
    static void DoReport()
    {
        List<Tuple<string, double, double>> m = new List<Tuple<string, double, double>>();
        List<Tuple<string, uint, uint>> t = new List<Tuple<string, uint, uint>>();
        int[] qs = new int[4]; List<string> mipBad = new List<string>();
        foreach (string p in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(p).ToLowerInvariant();
            try
            {
                if (ext == ".mdx")
                {
                    List<Em> em = ReadEmitters(File.ReadAllBytes(p));
                    m.Add(Tuple.Create(Rel(p), em.Where(x => !x.Squirt).Sum(x => (double)x.RMax * x.Life), em.Where(x => x.Squirt).Sum(x => (double)x.RMax)));
                }
                else if (ext == ".blp")
                {
                    byte[] h = File.ReadAllBytes(p);
                    if (h.Length < 160 || Tag(h, 0) != "BLP1") continue;
                    t.Add(Tuple.Create(Rel(p), U(h, 12), U(h, 16)));
                    int q = U(h, 4) == 0 ? BlpQuality(h) : 0;
                    qs[q <= 0 ? 0 : q <= 65 ? 1 : q <= 80 ? 2 : 3]++;
                    int n = MipCount(h), need = U(h, 24) == 0 ? 1 : FullMips(U(h, 12), U(h, 16));
                    if (n < need) mipBad.Add(Rel(p) + " — " + U(h, 12) + "x" + U(h, 16) + ", мипов " + n + " из " + need);
                }
            }
            catch { }
        }
        Console.WriteLine("Качество JPEG текстур: до 65 — " + qs[1] + ", 66-80 — " + qs[2] + ", выше 80 — " + qs[3] + ", палитровые/неизвестно — " + qs[0]);
        Console.WriteLine("Неполная цепочка мипов: " + mipBad.Count + (mipBad.Count > 0 ? " (пережатие строит полную)" : ""));
        foreach (string x in mipBad.Take(15)) Console.WriteLine("  " + x);
        Console.WriteLine("Моделей " + m.Count + ", текстур " + t.Count);
        Console.WriteLine("\nБольше всего частиц одновременно:");
        foreach (var x in m.OrderByDescending(x => x.Item2).Take(25)) Console.WriteLine("  " + N(x.Item2) + "\t" + x.Item1);
        Console.WriteLine("\nБольше всего частиц за один выброс:");
        foreach (var x in m.OrderByDescending(x => x.Item3).Take(15)) Console.WriteLine("  " + N(x.Item3) + "\t" + x.Item1);
        Console.WriteLine("\nКрупнейшие текстуры:");
        foreach (var x in t.OrderByDescending(x => (long)x.Item2 * x.Item3).Take(20)) Console.WriteLine("  " + x.Item2 + "x" + x.Item3 + "\t" + x.Item1);
    }
}
