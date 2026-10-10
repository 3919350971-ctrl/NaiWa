using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Naiwa
{
    internal sealed class PetForm : LayeredForm
    {
        internal readonly PetSettings Settings;
        internal readonly FocusClock Clock;
        internal readonly SpriteLibrary Sprites;
        internal readonly PetRenderer Renderer;
        private readonly SettingsStore store;
        private readonly Timer pulse = new Timer();
        private readonly Random random = new Random();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private float scale;
        private string action;
        private double actionStart, actionDuration;
        private Action afterAction;
        private string bubble;
        private DateTime bubbleUntil;
        private DateTime lastReminder = DateTime.UtcNow;
        private double nextBlink, nextIdle, nextPoll;
        private bool locked, suspended, suppressed, exiting, pressed, dragging, saveWarning, resourcesDisposed;
        private Point pressScreen, pressLocation;
        private RectangleF bodyAtPress;
        private Point walkStart, walkEnd;
        private SettingsDialog settingsDialog;
        internal int RenderCount { get; private set; }
        internal int TickCount { get; private set; }
        internal string Diagnostics { get { return CurrentAction + " duration=" + actionDuration + " interval=" + pulse.Interval + " ticks=" + TickCount; } }
        internal string CurrentAction { get { return action ?? BasePose; } }
        internal bool Suppressed { get { return suppressed; } }
        internal bool Dragging { get { return dragging; } }
        private string BasePose { get { return Settings.Sleeping ? "sleep" : Clock.Active ? "sit" : "idle"; } }

        internal PetForm(SettingsStore settingsStore, bool testMode)
        {
            store = settingsStore; Settings = store.Load(); Clock = new FocusClock(Settings, delegate { return DateTime.UtcNow; });
            Sprites = new SpriteLibrary(); Renderer = new PetRenderer(Sprites);
            Text = "奶蛙 · 桌面宠物"; Icon = Sprites.Icon;
            scale = Math.Max(1, Native.Scale(IntPtr.Zero)); Size = DesiredSize();
            if (Settings.X == Int32.MinValue || Settings.Y == Int32.MinValue) ResetPosition(false);
            else Location = Clamp(new Point(Settings.X, Settings.Y));
            Through = Settings.ClickThrough;
            menu = new ContextMenuStrip(); menu.Font = new Font("Microsoft YaHei UI", 10);
            menu.BackColor = Theme.Cream; menu.ForeColor = Theme.Ink; menu.ShowImageMargin = false;
            menu.Renderer = new ToolStripProfessionalRenderer(new PetMenuColors());
            menu.Opening += delegate { PopulateMenu(menu); };
            tray = new NotifyIcon { Icon = Sprites.Icon, Text = "奶蛙 · 住在桌面上的慢半拍朋友", Visible = !testMode, ContextMenuStrip = menu };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Wake(); };
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Wake(); };
            pulse.Tick += delegate { Tick(); }; pulse.Interval = 200;
            nextBlink = 4 + random.NextDouble() * 4; nextIdle = 45 + random.NextDouble() * 75;
            Clock.Tick();
            if (!testMode)
            {
                SystemEvents.SessionSwitch += SessionChanged;
                SystemEvents.DisplaySettingsChanged += DisplayChanged;
                SystemEvents.PowerModeChanged += PowerChanged;
            }
            IsTestMode = testMode;
        }
        internal bool IsTestMode { get; private set; }
        private Size DesiredSize()
        { return new Size((int)Math.Ceiling(220 * scale), (int)Math.Ceiling((Settings.PetHeight * 440f / 360 + 104) * scale)); }
        private Point Clamp(Point p)
        { return Geometry.Clamp(p, Size, Screen.FromPoint(new Point(p.X + Width / 2, p.Y + Height / 2)).WorkingArea); }
        internal void ResetPosition(bool save)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = Geometry.Clamp(new Point(area.Right - Width - (int)(145 * scale), area.Bottom - Height), Size, area);
            if (save) { Save(); DrawFrame(); }
        }
        internal void Start()
        {
            IntPtr unused = Handle;
            UpdateScale();
            // Opening the executable is an explicit request to see the pet.
            // Persisted sleep/hidden flags must never make a fresh launch invisible.
            if (Settings.Hidden || Settings.Sleeping) Wake();
            else { DrawFrame(); Show(); }
            if (!Settings.SeenWelcome)
            {
                Settings.SeenWelcome = true;
                Bubble("摸摸头、戳肚子\n拖动我，右键还有更多", 8);
                Save();
            }
            if (store.LastError != null) Bubble("设置已恢复为可用版本", 5);
            pulse.Start(); Tick();
        }
        internal void Save()
        {
            Settings.X = Left; Settings.Y = Top;
            if (!store.Save(Settings) && !saveWarning)
            {
                saveWarning = true; Bubble("位置暂时没存上\n请把我放在可写的文件夹", 7);
                Program.Log("Settings save failed: " + store.LastError);
            }
        }
        private void UpdateScale()
        {
            float newScale = Math.Max(.75f, Native.Scale(IsHandleCreated ? Handle : IntPtr.Zero));
            if (Math.Abs(newScale - scale) < .01) return;
            Point feet = new Point(Left + Width / 2, Top + Height - (int)(25 * scale));
            scale = newScale; Size = DesiredSize();
            Location = Clamp(new Point(feet.X - Width / 2, feet.Y - Height + (int)(25 * scale)));
        }
        internal void ApplySettings(int height, bool avoidFullscreen, bool wander, bool rest, bool reduced, bool through)
        {
            Point feet = new Point(Left + Width / 2, Top + Height - (int)(25 * scale));
            Settings.PetHeight = Math.Max(96, Math.Min(240, height));
            Settings.AvoidFullscreen = avoidFullscreen; Settings.Wander = wander; Settings.RestReminders = rest;
            Settings.ReducedMotion = reduced; Settings.ClickThrough = through; Through = through;
            Size = DesiredSize(); Location = Clamp(new Point(feet.X - Width / 2, feet.Y - Height + (int)(25 * scale)));
            lastReminder = DateTime.UtcNow; Save(); Tick(); DrawFrame();
        }
        private void Ui(Action callback)
        {
            if (exiting || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(callback); } catch (InvalidOperationException) { }
        }
        private void SessionChanged(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) Ui(delegate { locked = true; Tick(); });
            else if (e.Reason == SessionSwitchReason.SessionUnlock) Ui(delegate { locked = false; Tick(); });
        }
        private void DisplayChanged(object sender, EventArgs e)
        { Ui(delegate { UpdateScale(); Location = Clamp(Location); Save(); Tick(); }); }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) Ui(delegate { suspended = true; nextPoll = 0; Save(); Tick(); });
            if (e.Mode == PowerModes.Resume) Ui(delegate { suspended = false; nextPoll = 0; Clock.Tick(); Tick(); });
        }
        internal void Tick()
        {
            if (exiting) return;
            TickCount++;
            double t = elapsed.Elapsed.TotalSeconds;
            if (Clock.Tick()) Save();
            if (t >= nextPoll)
            {
                bool fullscreen = !IsTestMode && Settings.AvoidFullscreen &&
                    Native.IsFullscreen(Handle, Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).Bounds);
                suppressed = locked || suspended || fullscreen; nextPoll = t + .5;
            }
            // Sleeping is a visible desktop pose; only explicit hiding/system suppression hides it.
            bool shouldShow = !suppressed && !Settings.Hidden;
            if (!shouldShow)
            {
                if (Visible) Hide();
                if (dragging || pressed) { pressed = dragging = false; Capture = false; }
                if (action == "sleep" && t - actionStart >= actionDuration) FinishAction();
                pulse.Interval = 500; return;
            }
            if (!Visible) { DrawFrame(); Show(); }
            if (Clock.Pending && !Settings.Sleeping && !dragging && action != "sleep")
            {
                Clock.ConsumeCompletion(); Save(); Play("wake", 1.8, null);
                Bubble("这一段完成了\n歇一小会儿？", 6);
            }
            if (!String.IsNullOrEmpty(bubble) && DateTime.UtcNow >= bubbleUntil) bubble = null;
            if (action != null && !dragging && t - actionStart >= actionDuration) FinishAction();
            if (action == "walk")
            {
                double u = Math.Min(1, (t - actionStart) / actionDuration); u = u * u * (3 - 2 * u);
                Location = Clamp(new Point(walkStart.X + (int)((walkEnd.X - walkStart.X) * u), walkStart.Y));
            }
            if (action == null && !Settings.Sleeping && !Clock.Active && !pressed && !menu.Visible)
            {
                if (t >= nextIdle && !Settings.ReducedMotion)
                {
                    nextIdle = t + 45 + random.NextDouble() * 75;
                    Rectangle area = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).WorkingArea;
                    if (Settings.Wander && Top + Height >= area.Bottom - 35 * scale)
                    {
                        walkStart = Location;
                        walkEnd = Clamp(new Point(Left + (int)((random.Next(2) == 0 ? -1 : 1) * 40 * scale), Top));
                        Play("walk", 2.4, delegate { Save(); });
                    }
                    else Play(random.Next(2) == 0 ? "look-left" : "look-right", 2, null);
                }
                else if (t >= nextBlink && !Settings.ReducedMotion)
                { nextBlink = t + 5 + random.NextDouble() * 4; Play("blink", .28, null); }
            }
            if (Settings.RestReminders && !Settings.Sleeping && !Clock.Active && DateTime.UtcNow - lastReminder >= TimeSpan.FromHours(1))
            { lastReminder = DateTime.UtcNow; Bubble("起来活动一下？\n我在这里等你", 5); }
            pulse.Interval = action != null || dragging ? 33 : Settings.ReducedMotion ? 1000 : 200;
            if (Visible) DrawFrame();
            tray.Text = Settings.Sleeping ? "奶蛙 · 正在打盹，单击叫醒" : Clock.Active ? "奶蛙 · " + (Clock.Paused ? "专注已暂停 " : "陪你专注 ") + Clock.Label : "奶蛙 · 住在桌面上的慢半拍朋友";
        }
        private void FinishAction()
        {
            Action callback = afterAction; action = null; afterAction = null; actionDuration = 0;
            if (callback != null) callback();
        }
        internal void Play(string name, double seconds, Action completed)
        {
            action = name; actionStart = elapsed.Elapsed.TotalSeconds; actionDuration = seconds; afterAction = completed;
            pulse.Interval = 33; if (Visible) DrawFrame();
        }
        internal void Bubble(string text, int seconds)
        { bubble = text; bubbleUntil = DateTime.UtcNow.AddSeconds(seconds); if (Visible) DrawFrame(); }
        internal void DrawFrame()
        {
            if (exiting || IsDisposed) return;
            double t = action == null ? elapsed.Elapsed.TotalSeconds : elapsed.Elapsed.TotalSeconds - actionStart;
            Present(Renderer.Render(Size, scale, Settings.PetHeight, CurrentAction, BasePose, t,
                action == null ? 0 : actionDuration, bubble, Clock.Active, Clock.Paused, Clock.Progress, Clock.Label, Settings.ReducedMotion));
            RenderCount++;
        }
        internal void StartFocus(int minutes)
        {
            Clock.Start(minutes * 60); Settings.Sleeping = Settings.Hidden = false;
            action = null; afterAction = null; lastReminder = DateTime.UtcNow;
            Bubble("我陪你，慢慢来。", 4); Save(); Tick();
        }
        internal void ToggleFocusPause()
        { if (Clock.Paused) Clock.Resume(); else Clock.Pause(); Save(); Tick(); }
        internal void CancelFocus()
        { Clock.Cancel(); lastReminder = DateTime.UtcNow; Save(); Tick(); }
        internal void Wake()
        {
            // Cancel the previous completion BEFORE Tick can finish it. In particular,
            // a delayed sleep timer must not hide a pet that the user just called back.
            action = null; afterAction = null; actionDuration = 0;
            pressed = dragging = false; Capture = false;
            Settings.Hidden = Settings.Sleeping = false; Settings.ClickThrough = false; Through = false;
            UpdateScale(); Location = Clamp(Location); nextPoll = 0;
            Play("wake", 1.6, null); Save(); Tick();
        }
        internal void Sleep()
        {
            pressed = dragging = false; Capture = false;
            Settings.Sleeping = true; Settings.Hidden = false; bubble = null;
            Play("sleep", 1.4, null); Save(); Tick();
        }
        internal void HidePet()
        {
            Settings.Hidden = true; action = null; afterAction = null;
            pressed = dragging = false; Capture = false; Save(); Hide(); pulse.Interval = 500;
        }
        private void SetThrough(bool value)
        {
            Settings.ClickThrough = value; Through = value; Save();
            if (value) Bubble("托盘点我\n就能恢复鼠标互动", 5);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Hit(e.Location)) return;
            if (Clock.Active && Renderer.ProgressArea.Contains(e.Location)) { ShowMenu(); return; }
            pressed = true; dragging = false; pressScreen = PointToScreen(e.Location);
            pressLocation = Location; bodyAtPress = Renderer.Body; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!pressed) return;
            Point screen = PointToScreen(e.Location); int dx = screen.X - pressScreen.X, dy = screen.Y - pressScreen.Y;
            if (!dragging && (Math.Abs(dx) >= 5 * scale || Math.Abs(dy) >= 5 * scale))
            { dragging = true; bubble = null; Play("drag", Double.MaxValue, null); }
            if (dragging) { Location = Clamp(new Point(pressLocation.X + dx, pressLocation.Y + dy)); DrawFrame(); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right && Hit(e.Location)) { ShowMenu(); return; }
            if (e.Button != MouseButtons.Left || !pressed) return;
            pressed = false; Capture = false;
            if (dragging)
            { dragging = false; UpdateScale(); Play("land", .55, null); Save(); return; }
            if (Settings.Sleeping) { Wake(); return; }
            if (Clock.Active) { ShowMenu(); return; }
            bool head = e.Y < bodyAtPress.Top + bodyAtPress.Height * .40f;
            Play(head ? "pat" : "belly", head ? 1.8 : SpriteLibrary.LaughDuration, null);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && pressed)
            { pressed = false; dragging = false; Play("land", .55, null); Save(); }
        }
        private void ShowMenu()
        { PopulateMenu(menu); menu.Show(Cursor.Position); }
        private ToolStripMenuItem Item(string text, Action callback)
        { ToolStripMenuItem item = new ToolStripMenuItem(text); item.Click += delegate { callback(); }; return item; }
        internal void PopulateMenu(ContextMenuStrip target)
        {
            while (target.Items.Count > 0) { ToolStripItem old = target.Items[0]; target.Items.RemoveAt(0); old.Dispose(); }
            target.Items.Add(Item(Settings.Hidden ? "叫奶蛙出来" : Settings.Sleeping ? "叫醒奶蛙" : "摸摸头", delegate
            { if (Settings.Hidden || Settings.Sleeping) Wake(); else Play("pat", 1.8, null); }));
            if (Clock.Active)
            {
                ToolStripMenuItem label = new ToolStripMenuItem("专注剩余 " + Clock.Label) { Enabled = false };
                target.Items.Add(label);
                target.Items.Add(Item(Clock.Paused ? "继续专注" : "暂停专注", ToggleFocusPause));
                target.Items.Add(Item("结束本次专注", CancelFocus));
            }
            else
            {
                ToolStripMenuItem focus = new ToolStripMenuItem("陪我专注");
                focus.DropDownItems.Add(Item("25 分钟", delegate { StartFocus(25); }));
                focus.DropDownItems.Add(Item("50 分钟", delegate { StartFocus(50); }));
                focus.DropDownItems.Add(Item("自定义时长…", delegate
                {
                    using (DurationDialog dialog = new DurationDialog(Sprites.Icon))
                        if (dialog.ShowDialog() == DialogResult.OK) StartFocus(dialog.Minutes);
                }));
                target.Items.Add(focus);
            }
            target.Items.Add(Item("回窝睡觉", Sleep)); target.Items.Add(new ToolStripSeparator());
            target.Items.Add(Item("设置…", ShowSettings));
            ToolStripMenuItem through = Item(Settings.ClickThrough ? "恢复鼠标互动" : "鼠标穿透", delegate { SetThrough(!Settings.ClickThrough); });
            through.Checked = Settings.ClickThrough; target.Items.Add(through);
            target.Items.Add(Item("放回桌面右下角", delegate { ResetPosition(true); Wake(); }));
            target.Items.Add(Item("暂时隐藏", HidePet)); target.Items.Add(Item("使用说明…", ShowHelp));
            target.Items.Add(new ToolStripSeparator()); target.Items.Add(Item("退出奶蛙", Close));
        }
        internal void ShowSettings()
        {
            if (settingsDialog != null && !settingsDialog.IsDisposed) { settingsDialog.Activate(); return; }
            settingsDialog = new SettingsDialog(this); settingsDialog.Show(); settingsDialog.Activate();
        }
        private void ShowHelp()
        {
            MessageBox.Show("摸头：眯眼回应\n戳肚子：捧腹大笑\n按住拖动：把我提到别处\n右键：专注、睡觉与设置\n\n睡觉时留在桌面，头顶右上方显示 Z。单击身体即可叫醒。\n单击或双击托盘图标、重新打开 exe 也可以叫醒。\n\n全屏时我会暂时躲起来；专注计时仍会继续。\n设置保存在程序旁的 data 文件夹。\n\n奶蛙 1.0.4 · 住在桌面上的慢半拍朋友", "奶蛙 · 使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WakeMessage) { Wake(); return; }
            // Read-only frame query for native integration diagnostics (0 means not laughing).
            if (m.Msg == Native.AnimationMessage)
            {
                m.Result = new IntPtr(CurrentAction == "belly" ? 1 + SpriteLibrary.LaughIndex(elapsed.Elapsed.TotalSeconds - actionStart, Settings.ReducedMotion) : 0);
                return;
            }
            if (m.Msg == 0x2E0 && !dragging) Ui(delegate { UpdateScale(); Location = Clamp(Location); Save(); DrawFrame(); });
            base.WndProc(ref m);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        { Save(); exiting = true; base.OnFormClosing(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !exiting) { Save(); exiting = true; }
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true;
                pulse.Stop(); pulse.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose();
                if (!IsTestMode)
                {
                    SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                    SystemEvents.PowerModeChanged -= PowerChanged;
                }
                if (settingsDialog != null) settingsDialog.Dispose(); Sprites.Dispose();
            }
            base.Dispose(disposing);
        }
        // Deterministic test input exercises the same handlers as desktop mouse messages.
        internal void TestDown(Point p) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)); }
        internal void TestMove(Point p) { OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, p.X, p.Y, 0)); }
        internal void TestUp(Point p) { OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)); }
        internal void TestLock(bool value) { locked = value; nextPoll = 0; Tick(); }
        internal void TestSuspend(bool value) { suspended = value; nextPoll = 0; Tick(); }
        internal void TestActualFullscreen()
        {
            bool previous = IsTestMode; IsTestMode = false; nextPoll = 0;
            try { Tick(); } finally { IsTestMode = previous; }
        }
    }

    internal sealed class PetMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Cream; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Cream; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Cream; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Cream; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(238, 241, 225); } }
        public override Color MenuItemBorder { get { return Theme.Line; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.Cream; } }
    }
}
