using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace PiceVoice;

sealed class MainForm : Form
{
    readonly VoiceEngine engine = new();
    readonly MyInstantsClient instants = new();
    readonly SoundboardPlayer soundPlayer = new();
    readonly List<MMDevice> inputs = [];
    readonly List<MMDevice> outputs = [];
    readonly Panel pageHost = new() { Dock = DockStyle.Fill };
    readonly Label pageTitle = new() { AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text };
    readonly Label activeModel = new() { AutoSize = true, ForeColor = Theme.Cyan, Font = new("Segoe UI Semibold", 10) };
    readonly ComboBox inputBox = Combo();
    readonly ComboBox outputBox = Combo();
    readonly ComboBox monitorBox = Combo();
    readonly TrackBar pitch = Slider(-12, 12, 0);
    readonly TrackBar robot = Slider(0, 100, 0);
    readonly TrackBar drive = Slider(0, 100, 8);
    readonly TrackBar gate = Slider(-70, -20, -52);
    readonly IconButton power = new() { Icon = AppIcon.Mic, Text = "ATIVAR VOZ", Height = 48, Width = 250, BackColor = Theme.Purple };
    readonly IconButton monitor = new() { Icon = AppIcon.Headphones, Text = "OUVIR MINHA VOZ", Height = 48, Width = 220 };
    readonly RoundedPanel levelTrack = new() { Height = 10, Width = 480, Radius = 5, BackColor = Theme.Surface2 };
    readonly RoundedPanel levelFill = new() { Height = 10, Width = 2, Radius = 5, BackColor = Theme.Cyan };
    readonly Label liveStatus = new() { AutoSize = true, ForeColor = Theme.Muted, Text = "●  voz desligada" };
    readonly FlowLayoutPanel soundResults = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    readonly FlowLayoutPanel savedSounds = new() { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true };
    readonly TextBox soundSearch = TextInput();
    readonly Label referenceLabel = new() { AutoSize = true, ForeColor = Theme.Muted, Text = "nenhuma referência analisada" };
    readonly Label aiStatus = new() { AutoSize = true, ForeColor = Theme.Muted, Text = "aguardando uma voz de referência" };
    readonly Dictionary<string, Panel> pages = [];
    VoicePreset currentPreset;
    CancellationTokenSource? searchCts;

    readonly VoicePreset[] presets =
    [
        new("Natural Studio", "limpo, presente e equilibrado", 0, 0, .05f, -54, Theme.Cyan),
        new("Deep Titan", "corpo grave para narração e jogos", -5, 0, .12f, -55, Color.FromArgb(103, 131, 255)),
        new("Crystal Pop", "voz brilhante e definida", 4, 0, .04f, -50, Color.FromArgb(255, 104, 202)),
        new("Cyber Droid", "modulação digital controlada", -1, .72f, .16f, -48, Color.FromArgb(99, 246, 215)),
        new("Broadcast FM", "compressão quente de rádio", 0, .08f, .56f, -45, Color.FromArgb(255, 173, 79)),
        new("Shadow Beast", "grave agressivo e saturado", -8, .15f, .48f, -50, Color.FromArgb(224, 86, 108)),
        new("Arcade Bit", "efeito robótico leve para partidas", 2, .46f, .28f, -47, Color.FromArgb(177, 116, 255)),
        new("AI Reference", "replica o timbre da referência sem afinar", 0, 0, .07f, -53, Theme.Purple, true)
    ];

    public MainForm()
    {
        currentPreset = presets[0];
        Text = "Pice Voice";
        ClientSize = new Size(1160, 760);
        MinimumSize = new Size(1040, 700);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        DoubleBuffered = true;

        var titleBar = BuildTitleBar();
        var shell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 48, 0, 0) };
        var sidebar = BuildSidebar();
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(34, 24, 34, 30) };
        var header = new Panel { Dock = DockStyle.Top, Height = 58 };
        header.Controls.Add(pageTitle);
        pageTitle.Location = new Point(0, 5);
        activeModel.Location = new Point(650, 18);
        activeModel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(activeModel);
        content.Controls.Add(pageHost);
        content.Controls.Add(header);
        shell.Controls.Add(pageHost);
        shell.Controls.Add(sidebar);
        shell.Controls.Add(header);
        pageHost.Padding = new Padding(250, 106, 32, 28);
        Controls.Add(shell);
        Controls.Add(titleBar);

        pages["voice"] = BuildVoicePage();
        pages["models"] = BuildModelsPage();
        pages["soundboard"] = BuildSoundboardPage();
        pages["ai"] = BuildAiPage();
        pages["tutorial"] = BuildTutorialPage();
        foreach (var p in pages.Values) { p.Visible = false; pageHost.Controls.Add(p); }

        LoadDevices();
        ApplyPreset(currentPreset);
        ShowPage("voice", "Minha voz");
        power.Click += (_, _) => ToggleVoice();
        monitor.Click += (_, _) => ToggleMonitor();
        foreach (var bar in new[] { pitch, robot, drive, gate }) bar.ValueChanged += (_, _) => engine.Settings = CurrentSettings();
        engine.LevelChanged += value =>
        {
            if (!IsHandleCreated) return;
            BeginInvoke(() => levelFill.Width = Math.Clamp((int)(levelTrack.Width * value * 1.7f), 2, levelTrack.Width));
        };
        soundPlayer.Finished += () => { };
        FormClosed += (_, _) => { engine.Dispose(); soundPlayer.Dispose(); foreach (var d in inputs.Concat(outputs)) d.Dispose(); };
        Resize += (_, _) => ApplyRoundRegion();
        ApplyRoundRegion();
    }

    static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, Height = 34, BackColor = Theme.Surface2, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
    static TrackBar Slider(int min, int max, int val) => new() { Minimum = min, Maximum = max, Value = val, TickStyle = TickStyle.None, Width = 420, Height = 30 };
    static TextBox TextInput() => new() { BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface2, ForeColor = Theme.Text, Font = new("Segoe UI", 11) };

    void ApplyRoundRegion()
    {
        using var p = Theme.Round(new Rectangle(0, 0, Width, Height), 22);
        Region = new Region(p);
    }

    Panel BuildTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(17, 19, 29) };
        var logo = new Label { Text = "◉  PICE VOICE", AutoSize = true, Font = new("Segoe UI Semibold", 11), ForeColor = Theme.Text, Location = new Point(22, 14) };
        var close = new Button { Text = "×", Dock = DockStyle.Right, Width = 50, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Muted, BackColor = bar.BackColor, Font = new("Segoe UI", 16) };
        close.FlatAppearance.BorderSize = 0; close.Click += (_, _) => Close();
        var min = new Button { Text = "—", Dock = DockStyle.Right, Width = 50, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Muted, BackColor = bar.BackColor };
        min.FlatAppearance.BorderSize = 0; min.Click += (_, _) => WindowState = FormWindowState.Minimized;
        bar.Controls.Add(close); bar.Controls.Add(min); bar.Controls.Add(logo);
        bar.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
        logo.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
        return bar;
    }

    Panel BuildSidebar()
    {
        var side = new Panel { Dock = DockStyle.Left, Width = 220, Padding = new Padding(18, 72, 18, 18), BackColor = Color.FromArgb(16, 18, 28) };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        side.Controls.Add(stack);
        Nav(stack, "Minha voz", AppIcon.Mic, "voice", "Minha voz");
        Nav(stack, "Modelos", AppIcon.Wave, "models", "Modelos de voz");
        Nav(stack, "Soundboard", AppIcon.Board, "soundboard", "Soundboard");
        Nav(stack, "AI Reference", AppIcon.Brain, "ai", "AI Reference");
        Nav(stack, "Tutorial", AppIcon.Help, "tutorial", "Como usar");
        var version = new Label { Text = "Pice Voice 1.0", AutoSize = true, ForeColor = Color.FromArgb(90, 98, 125), Margin = new Padding(12, 35, 0, 0) };
        stack.Controls.Add(version);
        return side;
    }

    void Nav(FlowLayoutPanel stack, string text, AppIcon icon, string key, string title)
    {
        var b = new IconButton { Text = text, Icon = icon, Width = 184, Height = 46, Margin = new Padding(0, 0, 0, 8) };
        b.Click += (_, _) => ShowPage(key, title);
        stack.Controls.Add(b);
    }

    void ShowPage(string key, string title)
    {
        foreach (var p in pages.Values) p.Visible = false;
        pages[key].Visible = true;
        pages[key].BringToFront();
        pageTitle.Text = title;
        activeModel.Text = $"modelo: {currentPreset.Name}";
    }

    Panel BuildVoicePage()
    {
        var page = Page();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 185, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var devices = Card(520, 175);
        var dv = Stack(devices);
        dv.Controls.Add(Caption("ENTRADA E ROTEAMENTO"));
        Field(dv, "Microfone", inputBox);
        Field(dv, "Saída para jogos (CABLE Input)", outputBox);
        var state = Card(360, 175);
        var st = Stack(state);
        st.Controls.Add(Caption("STATUS EM TEMPO REAL"));
        st.Controls.Add(liveStatus);
        levelTrack.Controls.Add(levelFill);
        st.Controls.Add(levelTrack);
        st.Controls.Add(new Label { Text = "o medidor confirma que sua voz está chegando", AutoSize = true, ForeColor = Theme.Muted });
        top.Controls.Add(devices); top.Controls.Add(state);
        page.Controls.Add(top);

        var controls = Card(900, 315); controls.Dock = DockStyle.Top;
        var flow = Stack(controls);
        flow.Controls.Add(Caption("AJUSTE FINO"));
        AddSlider(flow, "Tom", pitch, () => $"{pitch.Value:+0;-0;0} semitons");
        AddSlider(flow, "Robô", robot, () => $"{robot.Value}%");
        AddSlider(flow, "Saturação", drive, () => $"{drive.Value}%");
        AddSlider(flow, "Gate de ruído", gate, () => $"{gate.Value} dB");
        var buttons = new FlowLayoutPanel { Width = 700, Height = 58, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(power); buttons.Controls.Add(monitor);
        flow.Controls.Add(buttons);
        page.Controls.Add(controls);
        controls.BringToFront();
        return page;
    }

    Panel BuildModelsPage()
    {
        var page = Page();
        var intro = new Label { Text = "efeitos originais para chamadas, streams e partidas", AutoSize = true, ForeColor = Theme.Muted, Dock = DockStyle.Top, Height = 35 };
        var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(0, 12, 0, 0) };
        foreach (var p in presets)
        {
            var card = Card(270, 135);
            card.Cursor = Cursors.Hand;
            var dot = new Panel { BackColor = p.Accent, Size = new Size(10, 10), Location = new Point(20, 22) };
            var name = new Label { Text = p.Name, Font = new("Segoe UI Semibold", 12), AutoSize = true, Location = new Point(42, 16), ForeColor = Theme.Text };
            var desc = new Label { Text = p.Description, Size = new Size(225, 42), Location = new Point(20, 50), ForeColor = Theme.Muted };
            var use = new IconButton { Text = "USAR MODELO", Icon = AppIcon.Play, Width = 225, Height = 34, Location = new Point(20, 94), BackColor = Theme.Surface2 };
            use.Click += (_, _) => { ApplyPreset(p); ShowPage("voice", "Minha voz"); };
            card.Controls.Add(dot); card.Controls.Add(name); card.Controls.Add(desc); card.Controls.Add(use);
            grid.Controls.Add(card);
        }
        page.Controls.Add(grid); page.Controls.Add(intro);
        return page;
    }

    Panel BuildSoundboardPage()
    {
        var page = Page();
        var searchCard = Card(900, 94); searchCard.Dock = DockStyle.Top;
        soundSearch.SetBounds(20, 26, 610, 38);
        soundSearch.PlaceholderText = "buscar memes, jogos, reações...";
        var search = new IconButton { Icon = AppIcon.Search, Text = "BUSCAR", Width = 210, Height = 40, Location = new Point(650, 24), BackColor = Theme.Purple };
        search.Click += async (_, _) => await SearchSounds();
        soundSearch.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchSounds(); } };
        searchCard.Controls.Add(soundSearch); searchCard.Controls.Add(search);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 14, 0, 0) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        var resultsCard = Card(500, 500); resultsCard.Dock = DockStyle.Fill;
        soundResults.Dock = DockStyle.Fill; soundResults.Padding = new Padding(14); resultsCard.Controls.Add(soundResults);
        var savedCard = Card(360, 500); savedCard.Dock = DockStyle.Fill;
        var savedTitle = Caption("MEU SOUNDBOARD"); savedTitle.Dock = DockStyle.Top; savedTitle.Padding = new Padding(16, 14, 0, 8);
        savedSounds.Dock = DockStyle.Fill; savedSounds.Padding = new Padding(14);
        savedCard.Controls.Add(savedSounds); savedCard.Controls.Add(savedTitle);
        split.Controls.Add(resultsCard, 0, 0); split.Controls.Add(savedCard, 1, 0);
        page.Controls.Add(split); page.Controls.Add(searchCard);
        return page;
    }

    async Task SearchSounds()
    {
        searchCts?.Cancel(); searchCts = new CancellationTokenSource();
        soundResults.Controls.Clear();
        soundResults.Controls.Add(new Label { Text = "buscando no MyInstants...", AutoSize = true, ForeColor = Theme.Muted });
        try
        {
            var sounds = await instants.SearchAsync(soundSearch.Text, searchCts.Token);
            soundResults.Controls.Clear();
            foreach (var sound in sounds)
            {
                var row = new RoundedPanel { Width = 460, Height = 58, Radius = 13, BackColor = Theme.Surface2, Margin = new Padding(0, 0, 0, 8) };
                var title = new Label { Text = sound.Title, AutoEllipsis = true, Size = new Size(315, 38), Location = new Point(15, 10), ForeColor = Theme.Text };
                var add = new IconButton { Icon = AppIcon.Download, Width = 48, Height = 38, Location = new Point(395, 10), BackColor = Theme.Purple };
                add.Click += async (_, _) => await AddSound(sound, add);
                row.Controls.Add(title); row.Controls.Add(add); soundResults.Controls.Add(row);
            }
            if (sounds.Count == 0) soundResults.Controls.Add(new Label { Text = "nenhum som encontrado", AutoSize = true, ForeColor = Theme.Muted });
        }
        catch (Exception ex) { soundResults.Controls.Clear(); soundResults.Controls.Add(new Label { Text = $"falha na busca: {ex.Message}", AutoSize = true, ForeColor = Color.Salmon }); }
    }

    async Task AddSound(InstantSound sound, IconButton button)
    {
        button.Enabled = false; button.Text = "...";
        try
        {
            var saved = await instants.DownloadAsync(sound);
            AddSoundButton(saved);
            button.Icon = AppIcon.Check; button.Text = "";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Pice Voice", MessageBoxButtons.OK, MessageBoxIcon.Error); button.Text = ""; }
        finally { button.Enabled = true; }
    }

    void AddSoundButton(InstantSound sound)
    {
        var b = new IconButton { Text = sound.Title, Icon = AppIcon.Play, Width = 300, Height = 48, BackColor = Theme.Surface2, Margin = new Padding(0, 0, 8, 8) };
        b.Click += (_, _) =>
        {
            if (soundPlayer.Playing) { soundPlayer.Stop(); b.Icon = AppIcon.Play; return; }
            if (outputBox.SelectedIndex < 0 || sound.LocalPath is null) return;
            soundPlayer.Play(sound.LocalPath, outputs[outputBox.SelectedIndex]);
            b.Icon = AppIcon.Stop;
        };
        savedSounds.Controls.Add(b);
    }

    Panel BuildAiPage()
    {
        var page = Page();
        var hero = Card(900, 210); hero.Dock = DockStyle.Top;
        var brain = new RoundedPanel { Size = new Size(74, 74), Radius = 24, BackColor = Color.FromArgb(51, 38, 86), Location = new Point(28, 30) };
        brain.Paint += (_, e) => IconPainter.Draw(e.Graphics, AppIcon.Brain, new Rectangle(22, 22, 30, 30), Theme.Purple);
        hero.Controls.Add(brain);
        hero.Controls.Add(new Label { Text = "AI Reference", Font = new("Segoe UI Semibold", 20), AutoSize = true, Location = new Point(125, 27), ForeColor = Theme.Text });
        hero.Controls.Add(new Label { Text = "analisa o timbre de uma gravação e aproxima graves, médios, agudos e dinâmica\nsem afinar sua voz e sem enviar o áudio para servidores", AutoSize = true, Location = new Point(127, 67), ForeColor = Theme.Muted });
        var choose = new IconButton { Icon = AppIcon.Wave, Text = "ESCOLHER REFERÊNCIA", Width = 240, Height = 42, Location = new Point(127, 125), BackColor = Theme.Purple };
        choose.Click += async (_, _) => await ChooseReference();
        var apply = new IconButton { Icon = AppIcon.Play, Text = "USAR AI REFERENCE", Width = 220, Height = 42, Location = new Point(380, 125) };
        apply.Click += (_, _) => { if (!engine.Reference.Ready) { aiStatus.Text = "analise uma referência primeiro"; return; } ApplyPreset(presets[^1]); ShowPage("voice", "Minha voz"); };
        hero.Controls.Add(choose); hero.Controls.Add(apply);

        var info = Card(900, 190); info.Dock = DockStyle.Top;
        var st = Stack(info);
        st.Controls.Add(Caption("PERFIL ATUAL"));
        st.Controls.Add(referenceLabel);
        st.Controls.Add(aiStatus);
        st.Controls.Add(new Label { Text = "dica: use 10–30 segundos de fala limpa, sem música ou eco", AutoSize = true, ForeColor = Theme.Cyan, Margin = new Padding(0, 18, 0, 0) });
        page.Controls.Add(info); page.Controls.Add(hero);
        return page;
    }

    async Task ChooseReference()
    {
        using var dialog = new OpenFileDialog { Filter = "Áudio|*.wav;*.mp3;*.m4a;*.aac;*.wma", Title = "Escolha uma referência de voz" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        aiStatus.Text = "analisando timbre localmente...";
        try
        {
            await engine.Reference.AnalyzeAsync(dialog.FileName);
            referenceLabel.Text = engine.Reference.Label;
            aiStatus.Text = "✓ perfil pronto — nenhum pitch será aplicado";
            aiStatus.ForeColor = Theme.Success;
        }
        catch (Exception ex) { aiStatus.Text = ex.Message; aiStatus.ForeColor = Color.Salmon; }
    }

    Panel BuildTutorialPage()
    {
        var page = Page();
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        flow.Controls.Add(new Label { Text = "configure em menos de dois minutos", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 18) });
        Step(flow, 1, "Instale um cabo virtual", "Instale o VB-CABLE e reinicie o Windows.");
        Step(flow, 2, "Escolha os dispositivos", "Em Minha voz, selecione seu microfone e CABLE Input como saída.");
        Step(flow, 3, "Ative e teste", "Clique em Ativar voz. O medidor precisa reagir. Use Ouvir minha voz com fones.");
        Step(flow, 4, "Configure o jogo", "No Discord, navegador ou jogo, escolha CABLE Output como microfone.");
        Step(flow, 5, "Escolha um modelo", "Abra Modelos ou crie um perfil em AI Reference. As mudanças são aplicadas ao vivo.");
        Step(flow, 6, "Monte o soundboard", "Pesquise no MyInstants, baixe os botões e reproduza pela mesma saída virtual.");
        return page;
    }

    static void Step(FlowLayoutPanel flow, int number, string title, string body)
    {
        var card = Card(850, 76);
        var badge = new RoundedPanel { Size = new Size(42, 42), Radius = 15, BackColor = Theme.Purple, Location = new Point(18, 17) };
        badge.Controls.Add(new Label { Text = number.ToString(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new("Segoe UI Semibold", 12) });
        card.Controls.Add(badge);
        card.Controls.Add(new Label { Text = title, AutoSize = true, Font = new("Segoe UI Semibold", 11), Location = new Point(78, 14), ForeColor = Theme.Text });
        card.Controls.Add(new Label { Text = body, AutoSize = true, Location = new Point(78, 40), ForeColor = Theme.Muted });
        flow.Controls.Add(card);
    }

    void LoadDevices()
    {
        using var e = new MMDeviceEnumerator();
        inputs.AddRange(e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active));
        outputs.AddRange(e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active));
        inputBox.Items.AddRange(inputs.Select(x => x.FriendlyName).ToArray());
        outputBox.Items.AddRange(outputs.Select(x => x.FriendlyName).ToArray());
        monitorBox.Items.AddRange(outputs.Select(x => x.FriendlyName).ToArray());
        if (inputBox.Items.Count > 0) inputBox.SelectedIndex = 0;
        var cable = outputs.FindIndex(x => x.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
        if (outputBox.Items.Count > 0) outputBox.SelectedIndex = cable >= 0 ? cable : 0;
        if (monitorBox.Items.Count > 0) monitorBox.SelectedIndex = outputs.FindIndex(x => x.ID == e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID);
    }

    void ToggleVoice()
    {
        if (engine.Running)
        {
            engine.Stop(); power.Text = "ATIVAR VOZ"; power.BackColor = Theme.Purple; liveStatus.Text = "●  voz desligada"; liveStatus.ForeColor = Theme.Muted; monitor.Text = "OUVIR MINHA VOZ"; return;
        }
        if (inputBox.SelectedIndex < 0 || outputBox.SelectedIndex < 0) return;
        try
        {
            engine.Start(inputs[inputBox.SelectedIndex], outputs[outputBox.SelectedIndex], CurrentSettings());
            power.Text = "DESATIVAR VOZ"; power.BackColor = Color.FromArgb(214, 73, 101); liveStatus.Text = "●  voz ativa"; liveStatus.ForeColor = Theme.Success;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Pice Voice", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void ToggleMonitor()
    {
        if (engine.Monitoring) { engine.StopMonitor(); monitor.Text = "OUVIR MINHA VOZ"; return; }
        if (!engine.Running) { liveStatus.Text = "ative a voz antes de monitorar"; return; }
        if (monitorBox.SelectedIndex < 0) return;
        engine.StartMonitor(outputs[monitorBox.SelectedIndex]);
        monitor.Text = "PARAR DE OUVIR";
    }

    VoiceSettings CurrentSettings() => new(pitch.Value, robot.Value / 100f, drive.Value / 100f, gate.Value, currentPreset.UsesReference && engine.Reference.Ready);

    void ApplyPreset(VoicePreset p)
    {
        currentPreset = p;
        pitch.Value = Math.Clamp((int)p.Pitch, pitch.Minimum, pitch.Maximum);
        robot.Value = Math.Clamp((int)(p.Robot * 100), 0, 100);
        drive.Value = Math.Clamp((int)(p.Drive * 100), 0, 100);
        gate.Value = Math.Clamp((int)p.GateDb, gate.Minimum, gate.Maximum);
        activeModel.Text = $"modelo: {p.Name}";
        engine.Settings = CurrentSettings();
    }

    static Panel Page() => new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
    static RoundedPanel Card(int w, int h) => new() { Width = w, Height = h, Radius = 18, BackColor = Theme.Surface, Margin = new Padding(0, 0, 14, 14) };
    static FlowLayoutPanel Stack(Control parent)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), AutoScroll = true };
        parent.Controls.Add(p); return p;
    }
    static Label Caption(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Muted, Font = new("Segoe UI Semibold", 9), Margin = new Padding(0, 0, 0, 10) };
    static void Field(FlowLayoutPanel p, string label, Control c)
    {
        p.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 3, 0, 2) }); p.Controls.Add(c);
    }
    static void AddSlider(FlowLayoutPanel p, string name, TrackBar bar, Func<string> value)
    {
        var line = new Panel { Width = 700, Height = 24 };
        var left = new Label { Text = name, AutoSize = true, ForeColor = Theme.Text, Location = new Point(0, 2) };
        var right = new Label { Text = value(), Width = 150, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Cyan, Location = new Point(500, 2) };
        bar.ValueChanged += (_, _) => right.Text = value();
        line.Controls.Add(left); line.Controls.Add(right); p.Controls.Add(line); p.Controls.Add(bar);
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}