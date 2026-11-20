// ChoicePackFix — окно: те же действия, что у ChoiceOptimizer.exe и .bat-файлов, движок PackFix.cs (собирается с /define:GUI).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

class PackFixGui : Form
{
    TextBox tDir, tBak, tHalf, tOut;
    RadioButton rAll, rTex, rPart;
    CheckBox cForce;
    ComboBox cHalf, cTex;
    TrackBar trQ;
    NumericUpDown nQ;
    Label lQ;
    static readonly int[] TexMax = { 0, -1, 512, 256, 128 }; // -1 — не менять размер

    // тёмная тема
    static readonly Color cBg = Color.FromArgb(30, 30, 32), cField = Color.FromArgb(45, 45, 48), cBtn = Color.FromArgb(55, 55, 60),
        cBtnHover = Color.FromArgb(70, 70, 76), cBorder = Color.FromArgb(80, 80, 86), cFg = Color.FromArgb(225, 225, 225),
        cDim = Color.FromArgb(150, 150, 155), cAccent = Color.FromArgb(0, 102, 184);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
    Label lHint;
    Button bDry, bWrite, bReport, bMark, bLog;
    List<Control> lockable = new List<Control>();
    Bar pb;

    // полоса прогресса в цветах темы (стандартная ProgressBar не перекрашивается)
    class Bar : Control
    {
        double v; public Color Fill = Color.FromArgb(0, 102, 184), Frame = Color.FromArgb(80, 80, 86);
        public Bar() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        public double Value { get { return v; } set { v = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            int w = (int)Math.Round((Width - 2) * v);
            if (w > 0) using (var b = new SolidBrush(Fill)) e.Graphics.FillRectangle(b, 1, 1, w, Height - 2);
            using (var p = new Pen(Frame)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
    Label lStat;
    bool bakAuto = true, busy;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new PackFixGui(args.Length > 0 ? args[0] : null));
    }

    PackFixGui(string startDir)
    {
        Text = "ChoicePackFix — облегчение пака моделей";
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(940, 680);
        MinimumSize = new Size(700, 480);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += OnDragEnter; DragDrop += OnDragDrop;

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(t);

        var tip = new ToolTip { AutoPopDelay = 20000 };

        // папка
        tDir = Box(); tDir.TextChanged += delegate { if (bakAuto) SetBak(AutoBak()); };
        AddRow(t, "Папка с моделями:", tDir, Btn("Обзор…", delegate { Pick(tDir); }));
        tip.SetToolTip(tDir, "Можно перетащить папку прямо в окно.");

        // резервные копии
        tBak = Box(); tBak.TextChanged += delegate { if (!settingBak) bakAuto = tBak.Text == AutoBak(); };
        AddRow(t, "Резервные копии:", tBak, Btn("Обзор…", delegate { Pick(tBak); }));
        tip.SetToolTip(tBak, "Оригиналы изменённых файлов. Уже лежащая там копия не перезаписывается — всегда хранится самый первый оригинал.");

        // что урезать
        var f = Flow();
        rAll = Radio("Текстуры и частицы", true); rTex = Radio("Только текстуры", false); rPart = Radio("Только частицы", false);
        cForce = new CheckBox { Text = "Заново и уже обработанные", AutoSize = true, Margin = new Padding(24, 3, 3, 3) };
        lockable.Add(cForce);
        tip.SetToolTip(cForce, "--force: урезать и файлы из _packfix_done.txt ещё раз. Обычно не нужно.");
        f.Controls.AddRange(new Control[] { rAll, rTex, rPart, cForce });
        AddRow(t, "Что урезать:", f, null);
        var tf = Flow();
        cTex = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        cTex.Items.AddRange(new object[] { "по правилу: больше 512 — в 4 раза, 512 — в 2 раза", "не менять размер (только сжатие)", "все до 512", "все до 256", "все до 128" });
        cTex.SelectedIndex = 0;
        lockable.Add(cTex);
        tf.Controls.Add(cTex);
        AddRow(t, "Размер текстур:", tf, null);
        tip.SetToolTip(cTex, "«Все до N» (--max N): каждая текстура больше N по большей стороне уменьшается до N готовыми мип-уровнями.\nТекстуры не больше N не меняются, поэтому повторный запуск ничего не урежет повторно.\n«Не менять размер» (--keep-size): только пережатие JPEG.");

        // сжатие JPEG: ползунок + поле
        var qf = Flow();
        trQ = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 1, LargeChange = 5, Value = 65, Width = 300, AutoSize = false, Height = 30 };
        nQ = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 65, Width = 60, Margin = new Padding(6, 4, 3, 3), BorderStyle = BorderStyle.FixedSingle };
        lQ = new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        trQ.ValueChanged += delegate { if (nQ.Value != trQ.Value) nQ.Value = trQ.Value; };
        nQ.ValueChanged += delegate { if (trQ.Value != (int)nQ.Value) trQ.Value = (int)nQ.Value; UpdateHint(); };
        lockable.Add(trQ); lockable.Add(nQ);
        qf.Controls.AddRange(new Control[] { trQ, nQ, lQ });
        tip.SetToolTip(trQ, "--quality: JPEG-текстуры с качеством выше заданного пересохраняются с этим качеством (как tools/icons/blp1.py).\nПиксели и прозрачность не меняются, мип-уровни строятся заново — полная цепочка до 1×1.\n0 — не пережимать. Стандарт — 65.");
        tip.SetToolTip(nQ, tip.GetToolTip(trQ));

        // --half
        tHalf = Box();
        AddRow(t, "Только эти текстуры:", tHalf, Btn("Выбрать…", delegate { PickHalf(); }));
        tip.SetToolTip(tHalf, "Необязательно. Имена .blp через запятую — тогда урезаются только они (как --half),\nвсё остальное не трогается. Имя совпадёт в любой подпапке.");
        var hf = Flow();
        cHalf = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        cHalf.Items.AddRange(new object[] { "ещё в 2 раза", "ещё в 4 раза" });
        cHalf.SelectedIndex = 0;
        lockable.Add(cHalf);
        hf.Controls.Add(cHalf);
        AddRow(t, "Насколько урезать:", hf, null);

        // что именно будет сделано
        lHint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 6) };
        t.Controls.Add(lHint, 1, t.RowCount); t.SetColumnSpan(lHint, 2); t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowCount++;
        t.SizeChanged += delegate { lHint.MaximumSize = new Size(Math.Max(200, t.ClientSize.Width - 200), 0); };
        EventHandler upd = delegate { UpdateHint(); };
        rAll.CheckedChanged += upd; rTex.CheckedChanged += upd; rPart.CheckedChanged += upd; cForce.CheckedChanged += upd;
        tHalf.TextChanged += upd; cHalf.SelectedIndexChanged += upd; cTex.SelectedIndexChanged += upd;

        // ползунок сжатия — под описанием, над кнопками
        AddRow(t, "Сжатие JPEG:", qf, null);
        UpdateHint();

        // кнопки
        var bf = Flow();
        bDry = Btn("Пробный прогон", delegate { Go(false); });
        bWrite = Btn("Урезать", delegate { Go(true); });
        bWrite.Font = new Font(Font, FontStyle.Bold);
        bReport = Btn("Отчёт", delegate { Start("Отчёт", new[] { Dir(), "--report" }, false); });
        bMark = Btn("Отметить всё как обработанное", delegate { MarkDone(); });
        bLog = Btn("Открыть журнал", delegate { OpenLog(); });
        tip.SetToolTip(bDry, "Ничего не меняет: показывает, что будет урезано (журнал — _packfix_log.txt в папке).");
        tip.SetToolTip(bReport, "Самые тяжёлые модели по частицам и самые крупные текстуры. Ничего не меняет.");
        tip.SetToolTip(bMark, "--mark-done: все текущие .mdx/.blp папки считаются уже обработанными. Файлы не меняются.");
        bf.Controls.AddRange(new Control[] { bDry, bWrite, bReport, bMark, bLog });
        t.Controls.Add(bf, 0, t.RowCount); t.SetColumnSpan(bf, 3); t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowCount++;

        // вывод
        tOut = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, MaxLength = 0,
            Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window
        };
        t.Controls.Add(tOut, 0, t.RowCount); t.SetColumnSpan(tOut, 3); t.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); t.RowCount++;

        // строка состояния
        var sf = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
        sf.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        sf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pb = new Bar { Dock = DockStyle.Fill, Height = 16, Margin = new Padding(3, 5, 3, 5) };
        lStat = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "Сначала пробный прогон, затем «Урезать»." };
        sf.Controls.Add(pb, 0, 0); sf.Controls.Add(lStat, 1, 0);
        t.Controls.Add(sf, 0, t.RowCount); t.SetColumnSpan(sf, 3); t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowCount++;

        string d0 = startDir ?? (Directory.Exists(@"F:\ChoiceModels") ? @"F:\ChoiceModels" : "");
        tDir.Text = d0;
        Theme(this);
        lHint.ForeColor = cDim;
        tOut.ForeColor = Color.White; // журнал — чисто белый, чтобы читался на тёмном фоне
        bWrite.BackColor = cAccent; bWrite.FlatAppearance.BorderColor = cAccent; bWrite.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 120, 212);
        tOut.HandleCreated += delegate { SetWindowTheme(tOut.Handle, "DarkMode_Explorer", null); };

        FormClosing += (s, e) => { if (busy && MessageBox.Show(this, "Обработка ещё идёт. Закрыть всё равно?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) e.Cancel = true; };
    }

    // ---------- тёмная тема ----------
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1; // тёмный заголовок окна (Windows 10 1809+ / 11)
        try { if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch { }
    }

    void Theme(Control c)
    {
        c.BackColor = cBg; c.ForeColor = cFg;
        if (c is TextBox) { ((TextBox)c).BorderStyle = BorderStyle.FixedSingle; c.BackColor = cField; }
        else if (c is NumericUpDown) c.BackColor = cField;
        else if (c is ComboBox) { ((ComboBox)c).FlatStyle = FlatStyle.Flat; c.BackColor = cField; }
        else if (c is Button)
        {
            var b = (Button)c;
            b.FlatStyle = FlatStyle.Flat; b.BackColor = cBtn; b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderColor = cBorder; b.FlatAppearance.MouseOverBackColor = cBtnHover; b.FlatAppearance.MouseDownBackColor = cBorder;
        }
        foreach (Control ch in c.Controls) Theme(ch);
    }

    // на время работы: поля только для чтения (выключенный TextBox в тёмной теме становится светлым), остальное выключено
    void Lock(bool on)
    {
        foreach (Control c in lockable)
        {
            if (c is TextBox) ((TextBox)c).ReadOnly = on;
            else c.Enabled = !on;
        }
    }

    // ---------- построение ----------
    TextBox Box() { var b = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 3) }; lockable.Add(b); return b; }
    FlowLayoutPanel Flow() { return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 4) }; }
    RadioButton Radio(string s, bool on) { var r = new RadioButton { Text = s, Checked = on, AutoSize = true }; lockable.Add(r); return r; }
    Button Btn(string s, EventHandler h)
    {
        var b = new Button { Text = s, Name = s, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2) };
        b.Click += h; lockable.Add(b); return b;
    }
    void AddRow(TableLayoutPanel t, string label, Control c, Control right)
    {
        int r = t.RowCount;
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, 0, r);
        t.Controls.Add(c, 1, r);
        if (right != null) t.Controls.Add(right, 2, r); else t.SetColumnSpan(c, 2);
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowCount++;
    }

    // ---------- поля ----------
    bool settingBak;
    void SetBak(string s) { settingBak = true; tBak.Text = s; settingBak = false; }
    string Dir() { return tDir.Text.Trim().Trim('"').TrimEnd('\\', '/'); }
    string AutoBak() { return Dir() == "" ? "" : Dir() + "_backup"; }
    string[] HalfNames()
    {
        return tHalf.Text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Path.GetFileName(x.Trim().Trim('"'))).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    const string TexRule = "Текстуры: больше 512×512 — в 4 раза по стороне (1024×1024 → 256×256, памяти в 16 раз меньше), "
        + "ровно 512×512 — в 2 раза (→ 256×256, памяти в 4 раза меньше), меньше 512×512 — не трогаются.";
    const string PartRule = "Частицы: эмиттер больше 1000 частиц одновременно — частота до ~600, выброс больше 200 за раз — до ~150 "
        + "(не больше чем в 4 раза); модель больше 1000 частиц в сумме — частота до 2 раз меньше. Частицы становятся крупнее и закрывают ту же площадь.";

    void UpdateHint()
    {
        bool half = HalfNames().Length > 0;
        cHalf.Enabled = half && !busy;
        rAll.Enabled = rTex.Enabled = rPart.Enabled = cForce.Enabled = !half && !busy;
        cTex.Enabled = !half && !rPart.Checked && !busy;
        bool qOn = (half || !rPart.Checked) && !busy;
        trQ.Enabled = nQ.Enabled = qOn;
        int q = (int)nQ.Value;
        lQ.Text = q == 0 ? "не пережимать" : "% качества (стандарт 65, 0 — не пережимать)";
        int mx = TexMax[cTex.SelectedIndex];
        string texRule = mx == 0 ? TexRule : mx < 0 ? "Текстуры: размер не меняется." : "Текстуры: все больше " + mx + " по большей стороне уменьшаются до " + mx
            + " (например, 1024×1024 → " + mx + "×" + mx + ", 1024×512 → " + mx + "×" + (mx / 2) + "), остальные не трогаются. "
            + "Касается всех текстур, в том числе портретов и раскадровок, и уже урезанных раньше.";
        texRule += "\n" + (q == 0 ? "Сжатие JPEG выключено."
            : "Сжатие: JPEG-текстуры с качеством выше " + q + " пересохраняются с качеством " + q + " — пиксели и прозрачность как есть, "
              + "мип-уровни строятся заново полной цепочкой (512×512 — 10). Текстуры уже не выше " + q + " и палитровые не трогаются.");
        string s;
        if (half)
            s = "Только выбранные текстуры (" + HalfNames().Length + "): " + (cHalf.SelectedIndex == 1
                ? "ещё в 4 раза по стороне — 512×512 → 128×128, 256×256 → 64×64 (памяти в 16 раз меньше)."
                : "ещё в 2 раза по стороне — 512×512 → 256×256, 256×256 → 128×128 (памяти в 4 раза меньше).")
                + " Остальное не трогается. Урезается от текущего размера: каждый запуск уменьшает их снова."
                + (q == 0 ? "" : " Если их качество JPEG выше " + q + ", они заодно пережимаются.");
        else
        {
            s = rTex.Checked ? texRule : rPart.Checked ? PartRule : texRule + "\n" + PartRule;
            s += cForce.Checked ? "\nУже обработанные файлы урежутся ещё раз — от текущего размера." : "\nУже обработанные (_packfix_done.txt) пропускаются.";
        }
        lHint.Text = s;
    }

    void Pick(TextBox box)
    {
        using (var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(box.Text) ? box.Text : Dir(), ShowNewFolderButton = box == tBak })
            if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
    }

    void PickHalf()
    {
        using (var d = new OpenFileDialog { Multiselect = true, Filter = "Текстуры BLP|*.blp", Title = "Текстуры, которые урезать ещё в 2 раза" })
        {
            if (Directory.Exists(Dir())) d.InitialDirectory = Dir();
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var all = HalfNames().ToList();
            foreach (string p in d.FileNames) if (!all.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)) all.Add(Path.GetFileName(p));
            tHalf.Text = string.Join(", ", all);
        }
    }

    void OnDragEnter(object s, DragEventArgs e) { if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
    void OnDragDrop(object s, DragEventArgs e)
    {
        var p = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];
        tDir.Text = Directory.Exists(p) ? p : Path.GetDirectoryName(p);
    }

    bool CheckDir()
    {
        if (Dir() != "" && Directory.Exists(Dir())) return true;
        MessageBox.Show(this, "Папка не найдена: " + Dir(), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    // ---------- действия ----------
    void Go(bool write)
    {
        if (!CheckDir()) return;
        var a = new List<string> { Dir() };
        string bak = tBak.Text.Trim().Trim('"').TrimEnd('\\', '/');
        if (bak != "" && !string.Equals(bak, AutoBak(), StringComparison.OrdinalIgnoreCase)) { a.Add("--backup"); a.Add(bak); }
        if (bak == "") bak = AutoBak();
        string[] half = HalfNames();
        string what;
        if (half.Length > 0)
        {
            a.Add("--half"); a.Add(string.Join(",", half)); if (cHalf.SelectedIndex == 1) a.Add("--half4");
            what = "только выбранные текстуры (" + half.Length + ") ещё в " + (cHalf.SelectedIndex == 1 ? "4 раза" : "2 раза") + " по стороне";
        }
        else
        {
            if (rTex.Checked) a.Add("--textures-only"); else if (rPart.Checked) a.Add("--particles-only");
            what = rTex.Checked ? "текстуры" : rPart.Checked ? "частицы" : "текстуры и частицы";
            int mx = TexMax[cTex.SelectedIndex];
            if (mx > 0 && !rPart.Checked) { a.Add("--max"); a.Add(mx.ToString()); what = what.Replace("текстуры", "текстуры (все до " + mx + ")"); }
            if (mx < 0 && !rPart.Checked) { a.Add("--keep-size"); what = what.Replace("текстуры", "текстуры (без изменения размера)"); }
        }
        a.Add("--quality"); a.Add(((int)nQ.Value).ToString());
        if (half.Length > 0 || !rPart.Checked) what += nQ.Value == 0 ? ", без пережатия JPEG" : ", сжатие JPEG " + nQ.Value + "%";
        if (cForce.Checked && half.Length == 0) { a.Add("--force"); what += ", включая уже обработанные"; }
        if (write)
        {
            string msg = "Изменить файлы в папке\n" + Dir() + "\n\nЧто: " + what + ".\nОригиналы будут скопированы в\n" + bak
                + (cForce.Checked || half.Length > 0 ? "\n\nВнимание: уже урезанные файлы урежутся ещё раз." : "");
            if (MessageBox.Show(this, msg, "Урезать", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            a.Add("--write");
        }
        Start(write ? "Урезание: " + what : "Пробный прогон: " + what, a.ToArray(), true);
    }

    void MarkDone()
    {
        if (!CheckDir()) return;
        if (MessageBox.Show(this, "Отметить все текущие .mdx и .blp в\n" + Dir() + "\nкак уже обработанные?\n\nФайлы не меняются, но при следующем урезании они будут пропущены.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        Start("Отметить всё как обработанное", new[] { Dir(), "--mark-done" }, false);
    }

    void OpenLog()
    {
        string p = Path.Combine(Dir(), "_packfix_log.txt");
        if (File.Exists(p)) Process.Start(p);
        else MessageBox.Show(this, "Журнала ещё нет — сначала пробный прогон.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void Start(string title, string[] args, bool showLog)
    {
        if (!CheckDir()) return;
        busy = true;
        Lock(true);
        tOut.Clear();
        Append("=== " + title + " ===\r\n");
        pb.Value = 0;
        lStat.Text = "Идёт… подсчёт файлов";
        var sw = Stopwatch.StartNew();
        string logPath = Path.Combine(args[0], "_packfix_log.txt");
        var th = new Thread(() =>
        {
            int code = -1;
            var old = Console.Out;
            try
            {
                Console.SetOut(new BoxWriter(Append));
                var tick = Stopwatch.StartNew();
                PackFix.OnFiles = (n, total) =>
                {
                    if (n != 0 && n != total && tick.ElapsedMilliseconds < 50) return; // не чаще 20 раз в секунду
                    tick.Restart();
                    BeginInvoke((MethodInvoker)delegate
                    {
                        pb.Value = total == 0 ? 1 : (double)n / total;
                        lStat.Text = "Идёт… файлов " + n + " из " + total;
                    });
                };
                code = PackFix.Run(args);
                if (showLog && File.Exists(logPath))
                {
                    var lines = File.ReadAllLines(logPath, Encoding.UTF8);
                    Append("\r\n--- журнал (" + lines.Length + " строк) ---\r\n");
                    Append(string.Join("\r\n", lines.Take(5000)) + (lines.Length > 5000 ? "\r\n… остальное в " + logPath : "") + "\r\n");
                }
            }
            catch (Exception ex) { Append("\r\nОШИБКА: " + ex.Message + "\r\n"); }
            finally { Console.SetOut(old); PackFix.OnFiles = null; }
            BeginInvoke((MethodInvoker)delegate
            {
                busy = false;
                Lock(false);
                UpdateHint();
                pb.Value = code == 0 ? 1 : pb.Value; // отчёт и отметка идут без счётчика — по завершении полоса полная
                lStat.Text = (code == 0 ? "Готово" : "Завершено с ошибкой") + " за " + sw.Elapsed.TotalSeconds.ToString("0.0") + " с.";
                tOut.SelectionStart = 0; tOut.ScrollToCaret();
            });
        });
        th.IsBackground = true;
        th.Start();
    }

    void Append(string s)
    {
        s = s.Replace("\r\n", "\n").Replace("\n", "\r\n");
        if (tOut.InvokeRequired) tOut.BeginInvoke((MethodInvoker)delegate { tOut.AppendText(s); });
        else tOut.AppendText(s);
    }

    class BoxWriter : TextWriter
    {
        readonly Action<string> f;
        public BoxWriter(Action<string> f) { this.f = f; }
        public override Encoding Encoding { get { return Encoding.UTF8; } }
        public override void Write(char c) { f(c.ToString()); }
        public override void Write(string s) { if (s != null) f(s); }
        public override void WriteLine(string s) { f((s ?? "") + "\n"); }
    }
}
