// ChoicePackFix — массовое облегчение пака моделей Warcraft III 1.26 (BLP1 / MDX v800).
// Текстуры: площадь > 512x512 — в 4 раза меньше по стороне, ровно 512x512 — в 2 раза (основным становится
// готовый мип-уровень, без пережатия; после записи размер в JPEG проверяется, при ошибке файл возвращается).
// Частицы (PRE2): эмиттер с пиком > 1000 частиц — частота до ~600 (не больше чем в 4 раза); выброс за раз > 200 —
// до ~150 (не больше чем в 4 раза); модель, где ничего не урезано, но сумма > 1000 — частота всех / до 2;
// затем сумма по модели > 3000 — частота всех / до 2, сумма выброса > 300 — выброс / до 4.
// Размер частиц везде растёт на корень из коэффициента — эффект закрывает ту же площадь меньшим числом частиц.
// Обработанные файлы записываются в _packfix_done.txt в папке и повторно не урезаются (кроме --force).
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
    static HashSet<string> Half = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, string> Done = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    static List<string> Log = new List<string>();
    static int nTex4, nTex2, nTexSkip, nTexBad, nMdx, nEmit;
    static double savedMB;
    static CultureInfo IC = CultureInfo.InvariantCulture;
    // окно (Gui.cs): вызывается каждые 200 просмотренных файлов
    public static Action<int> OnFiles;

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
        Write = Force = Report = MarkDone = false; DoTex = DoPart = true; HalfShift = 1; MaxSide = 0;
        Half.Clear(); Done.Clear(); Log.Clear();
        nTex4 = nTex2 = nTexSkip = nTexBad = nMdx = nEmit = 0; savedMB = 0;
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
        foreach (string p in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            if (OnFiles != null && ++nFiles % 200 == 0) OnFiles(nFiles);
            string ext = Path.GetExtension(p).ToLowerInvariant();
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
        File.WriteAllLines(LogPath, Log, Encoding.UTF8);
        if (Write) File.WriteAllLines(DonePath, Done.Select(kv => kv.Key + "\t" + kv.Value), Encoding.UTF8);
        Console.WriteLine("Текстуры: /4 — " + nTex4 + ", /2 — " + nTex2 + ", без мипов — " + nTexSkip + ", возвращено из-за ошибки — " + nTexBad
            + " | экономия памяти ~" + Math.Round(savedMB) + " МБ");
        Console.WriteLine("Модели: " + nMdx + ", эмиттеров: " + nEmit);
        Console.WriteLine("Журнал: " + LogPath);
        return 0;
    }

    static void Help()
    {
        Console.WriteLine("ChoicePackFix <папка> [--write] [--backup <папка>] [--textures-only | --particles-only] [--force] [--report] [--half a.blp,b.blp [--half4]] [--max 256] [--mark-done]");
        Console.WriteLine("  без --write  — только пробный прогон и журнал _packfix_log.txt");
        Console.WriteLine("  --write      — изменить файлы (оригиналы копируются в <папка>_backup, если их там ещё нет)");
        Console.WriteLine("  --force      — урезать и файлы из _packfix_done.txt (уже обработанные)");
        Console.WriteLine("  --report     — отчёт: самые тяжёлые модели по частицам и крупные текстуры");
        Console.WriteLine("  --mark-done  — отметить все текущие .mdx/.blp как уже обработанные (для уже урезанной папки)");
        Console.WriteLine("  --half       — только эти текстуры, ещё в 2 раза по стороне (например, у одного героя); с --half4 — в 4 раза");
        Console.WriteLine("  --max N      — вместо правила: все текстуры больше N по большей стороне уменьшить до N (например, 256)");
    }

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
        // --max не зависит от _packfix_done.txt: текстура, которая уже не больше N, всё равно не меняется
        if (Skip(p) && forceShift == 0 && MaxSide == 0) return;
        byte[] b = File.ReadAllBytes(p);
        if (b.Length < 160 || Tag(b, 0) != "BLP1") return;
        uint comp = U(b, 4), w = U(b, 12), h = U(b, 16), mips = U(b, 24);
        int s = forceShift;
        if (s == 0 && MaxSide > 0) { while (Math.Max(w, h) >> s > MaxSide) s++; if (s == 0) return; }
        else if (s == 0) { long a = (long)w * h; if (a > 512 * 512) s = 2; else if (a == 512 * 512) s = 1; else return; }
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
        savedMB += ((double)w * h - (double)nw * nh) * 4 * 4 / 3 / 1048576;
        Log.Add("BLP " + Rel(p) + " " + w + "x" + h + " -> " + nw + "x" + nh);
        if (Write) Save(p, res);
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
                    byte[] h = new byte[20]; using (FileStream fs = File.OpenRead(p)) fs.Read(h, 0, 20);
                    if (Tag(h, 0) == "BLP1") t.Add(Tuple.Create(Rel(p), U(h, 12), U(h, 16)));
                }
            }
            catch { }
        }
        Console.WriteLine("Моделей " + m.Count + ", текстур " + t.Count);
        Console.WriteLine("\nБольше всего частиц одновременно:");
        foreach (var x in m.OrderByDescending(x => x.Item2).Take(25)) Console.WriteLine("  " + N(x.Item2) + "\t" + x.Item1);
        Console.WriteLine("\nБольше всего частиц за один выброс:");
        foreach (var x in m.OrderByDescending(x => x.Item3).Take(15)) Console.WriteLine("  " + N(x.Item3) + "\t" + x.Item1);
        Console.WriteLine("\nКрупнейшие текстуры:");
        foreach (var x in t.OrderByDescending(x => (long)x.Item2 * x.Item3).Take(20)) Console.WriteLine("  " + x.Item2 + "x" + x.Item3 + "\t" + x.Item1);
    }
}
