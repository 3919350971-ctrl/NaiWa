using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Naiwa
{
    internal static class Tests
    {
        private static readonly List<string> results = new List<string>();
        private static int passed, failed;
        private static void Check(string name, Action test)
        {
            try { test(); passed++; results.Add("- PASS · " + name); }
            catch (Exception ex) { failed++; results.Add("- FAIL · " + name + "：" + ex.Message); }
        }
        private static void Expect(bool value, string reason)
        { if (!value) throw new InvalidOperationException(reason); }
        private static void Near(double expected, double actual)
        { Expect(Math.Abs(expected - actual) < .02, "expected " + expected + ", actual " + actual); }
        private static void Pump(int milliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
        }
        internal static int Run(string directory)
        {
            Directory.CreateDirectory(directory);
            string scratch = System.IO.Path.Combine(directory, "scratch-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            Directory.CreateDirectory(scratch);
            DateTime t = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
            PetSettings state = new PetSettings(); FocusClock clock = new FocusClock(state, delegate { return t; });
            Check("默认静音策略：散步、提醒、穿透均关闭；全屏避让开启", delegate
            { Expect(!state.Wander && !state.RestReminders && !state.ClickThrough && state.AvoidFullscreen, "defaults"); });
            Check("计时按 UTC 结束时间前进", delegate
            { clock.Start(1500); t = t.AddSeconds(90); Near(1410, clock.Remaining); Expect(clock.Label == "23:30", clock.Label); });
            Check("暂停后时间冻结", delegate
            { clock.Pause(); double saved = clock.Remaining; t = t.AddHours(2); Near(saved, clock.Remaining); });
            Check("恢复后从剩余时间继续", delegate
            { clock.Resume(); t = t.AddSeconds(30); Near(1380, clock.Remaining); });
            Check("跨休眠到期生成一次待通知事件", delegate
            { t = t.AddHours(4); Expect(clock.Tick() && clock.Pending && !clock.Active, "completion missing"); Expect(!clock.Tick(), "repeat completion"); });
            Check("读状态不消耗通知；实际通知仅消费一次", delegate
            { string ignored = clock.Label; Expect(clock.Pending, "read consumed"); Expect(clock.ConsumeCompletion(), "consume"); Expect(!clock.ConsumeCompletion(), "repeat"); });
            Check("取消专注不会产生完成通知", delegate
            { clock.Start(60); clock.Cancel(); t = t.AddHours(1); Expect(!clock.Tick() && !clock.Pending && !clock.Active, "cancel"); });
            Check("非法时长拒绝", delegate
            {
                foreach (double seconds in new double[] { 0, -10, 14401, Double.NaN, Double.PositiveInfinity })
                { bool rejected = false; try { clock.Start(seconds); } catch (ArgumentOutOfRangeException) { rejected = true; } Expect(rejected, seconds.ToString()); }
            });
            SettingsStore store = new SettingsStore(System.IO.Path.Combine(scratch, "state.json"));
            Check("原子保存与位置/尺寸/模式恢复", delegate
            {
                state.X = -700; state.Y = 123; state.PetHeight = 224; state.ClickThrough = true; state.SeenWelcome = true;
                clock.Start(60); Expect(store.Save(state), store.LastError);
                PetSettings copy = store.Load(); Expect(copy.X == -700 && copy.Y == 123 && copy.PetHeight == 224 && copy.ClickThrough && copy.FocusActive, "round trip");
            });
            Check("重启后使用原截止时间", delegate
            {
                t = t.AddSeconds(20); FocusClock restored = new FocusClock(store.Load(), delegate { return t; }); Near(40, restored.Remaining);
                t = t.AddSeconds(50); Expect(restored.Tick() && restored.Pending, "restored expiry");
            });
            Check("待通知事件可跨重启保留", delegate
            {
                state.CompletionPending = true; state.FocusActive = false; Expect(store.Save(state), store.LastError);
                FocusClock copy = new FocusClock(store.Load(), delegate { return t; }); Expect(copy.Pending && copy.ConsumeCompletion() && !copy.ConsumeCompletion(), "pending recovery");
            });
            Check("损坏设置回退备份", delegate
            { state.PetHeight = 176; store.Save(state); state.PetHeight = 192; store.Save(state); File.WriteAllText(store.Path, "{bad"); Expect(store.Load().PetHeight == 176, "backup failed"); });
            Check("旧/缺省设置保留正确默认值", delegate
            { File.WriteAllText(store.Path, "{\"Version\":1}"); PetSettings copy = store.Load(); Expect(copy.PetHeight == 160 && copy.AvoidFullscreen && copy.X == Int32.MinValue, "missing field defaults"); });
            Check("设置异常数值被校正", delegate
            { PetSettings bad = new PetSettings { PetHeight = 900, FocusActive = true, FocusDuration = Double.NaN, FocusRemaining = Double.PositiveInfinity }; bad.Validate(); Expect(bad.PetHeight == 240 && !bad.FocusActive && bad.FocusRemaining == 0, "validate"); });
            Check("负坐标显示器定位与拔插后的边界约束", delegate
            {
                Rectangle area = new Rectangle(-1920, -200, 1920, 1040); Size size = new Size(220, 330);
                Expect(Geometry.Clamp(new Point(-2500, -300), size, area) == new Point(-1920, -200), "negative monitor");
                Expect(Geometry.Clamp(new Point(9000, 9000), size, new Rectangle(0, 0, 1920, 1040)) == new Point(1700, 710), "unplug clamp");
            });
            Check("全屏与普通最大化工作区的几何区分", delegate
            {
                Rectangle screen = new Rectangle(0, 0, 1920, 1080);
                Expect(Geometry.Covers(new Rectangle(-1, -1, 1922, 1082), screen), "full screen");
                Expect(!Geometry.Covers(new Rectangle(0, 0, 1920, 1040), screen), "normal maximized");
            });
            try { DesktopTests(directory, scratch); }
            catch (Exception ex) { failed++; results.Add("- FAIL · 桌面测试中断：" + ex); }
            StringBuilder report = new StringBuilder();
            report.AppendLine("# 奶蛙 1.0.4 · 验证报告\n");
            report.AppendLine("验证时间（UTC）：" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("系统：" + Environment.OSVersion + "；逻辑处理器：" + Environment.ProcessorCount);
            report.AppendLine("\n结果：" + passed + " 项通过，" + failed + " 项失败。\n");
            foreach (string line in results) report.AppendLine(line);
            report.AppendLine("\n限制：已测当前机器上的原生窗口与计时逻辑；没有完成两小时长时间运行、真实锁屏/休眠、物理显示器拔插和全部 DPI 组合的人工验收。上述系统事件的逻辑分支及负坐标边界已自动验证。动画为 12 个姿态加轻微形变/过渡，尚不是完整骨骼动画。\n");
            File.WriteAllText(System.IO.Path.Combine(directory, "验证报告.md"), report.ToString(), new UTF8Encoding(true));
            File.WriteAllText(System.IO.Path.Combine(directory, "result.txt"), "passed=" + passed + "\nfailed=" + failed, new UTF8Encoding(false));
            return failed == 0 ? 0 : 1;
        }
        private static Point BodyPoint(PetForm pet, double vertical)
        {
            RectangleF body = pet.Renderer.Body;
            int y = (int)(body.Top + body.Height * vertical);
            for (int dx = 0; dx < body.Width / 2; dx++)
                foreach (int sign in new int[] { -1, 1 })
                { Point p = new Point((int)(body.Left + body.Width / 2) + dx * sign, y); if (pet.Hit(p)) return p; }
            throw new InvalidOperationException("No character pixel in test band");
        }
        internal static int SleepRegression(string directory)
        {
            Directory.CreateDirectory(directory);
            using (PetForm pet = new PetForm(new SettingsStore(System.IO.Path.Combine(directory, "fixture.json")), true))
            { pet.Start(); SleepTests(pet, directory); }
            File.WriteAllLines(System.IO.Path.Combine(directory, "sleep-result.txt"), results, new UTF8Encoding(true));
            return failed == 0 ? 0 : 1;
        }
        private static void SleepTests(PetForm pet, string directory)
        {
            Check("睡觉完成但 UI 尚未处理计时器时，叫出不会被旧回调再次隐藏", delegate
            {
                pet.Sleep(); Thread.Sleep(1550); // Deliberately leave the timer callback queued.
                pet.Wake(); Pump(100);
                Expect(pet.Visible && Native.IsWindowVisible(pet.Handle) && !pet.Settings.Hidden && !pet.Settings.Sleeping, "stale sleep callback hid pet");
            });
            Check("睡觉动画中提前叫出，延迟处理后仍保持可见", delegate
            {
                pet.Wake(); pet.Sleep(); Pump(100); pet.Wake(); Pump(1600);
                Expect(pet.Visible && !pet.Settings.Hidden && !pet.Settings.Sleeping, "early wake reverted");
            });
            Check("睡觉后通过托盘菜单叫出", delegate
            {
                pet.Sleep(); Pump(1600);
                using (ContextMenuStrip menu = new ContextMenuStrip())
                {
                    pet.PopulateMenu(menu); ToolStripItem wake = null;
                    foreach (ToolStripItem item in menu.Items) if (item.Text == "叫醒奶蛙") wake = item;
                    Expect(wake != null, "wake menu missing"); wake.PerformClick();
                }
                Pump(100); Expect(pet.Visible && Native.IsWindowVisible(pet.Handle) && !pet.Settings.Hidden, "menu wake failed");
            });
            Check("重复启动的原生唤醒消息可恢复休眠角色", delegate
            {
                pet.Sleep(); Thread.Sleep(1550);
                Native.SendMessage(pet.Handle, Native.WakeMessage, IntPtr.Zero, IntPtr.Zero); Pump(100);
                Expect(pet.Visible && !pet.Settings.Hidden && !pet.Settings.Sleeping, "IPC wake failed");
            });
            foreach (string notification in new string[] { "OnMouseClick", "OnMouseDoubleClick" })
            {
                string eventName = notification;
                Check("托盘通知事件唤醒：" + eventName, delegate
                {
                    pet.Sleep(); Thread.Sleep(1550);
                    NotifyIcon icon = (NotifyIcon)typeof(PetForm).GetField("tray", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pet);
                    typeof(NotifyIcon).GetMethod(eventName, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(icon,
                        new object[] { new MouseEventArgs(MouseButtons.Left, eventName == "OnMouseClick" ? 1 : 2, 0, 0, 0) });
                    Pump(100); Expect(pet.Visible && Native.IsWindowVisible(pet.Handle) && !pet.Settings.Hidden, "tray notification wake failed");
                });
            }
            Check("连续睡觉与叫出后不会遗留隐藏回调", delegate
            {
                for (int i = 0; i < 5; i++) { pet.Sleep(); pet.Wake(); }
                Pump(1700); Expect(pet.Visible && !pet.Settings.Hidden && !pet.Settings.Sleeping, "repeated wake reverted");
            });
            Check("从已保存睡眠状态启动立即可见，保留尺寸与专注", delegate
            {
                SettingsStore store = new SettingsStore(System.IO.Path.Combine(directory, "asleep-start.json"));
                store.Save(new PetSettings { Sleeping = true, Hidden = true, SeenWelcome = true, PetHeight = 192,
                    FocusActive = true, FocusDuration = 1500, FocusDeadlineTicks = DateTime.UtcNow.AddMinutes(25).Ticks });
                using (PetForm restart = new PetForm(store, true))
                {
                    restart.Start(); Pump(100);
                    Expect(restart.Visible && Native.IsWindowVisible(restart.Handle) && !restart.Settings.Hidden && !restart.Settings.Sleeping, "saved sleep prevented startup");
                    Expect(restart.Settings.PetHeight == 192 && restart.Clock.Active, "preferences or focus lost");
                    PetSettings saved = store.Load(); Expect(!saved.Hidden && !saved.Sleeping, "recovered visibility not saved");
                }
            });
        }
        private static void DesktopTests(string directory, string scratch)
        {
            SettingsStore store = new SettingsStore(System.IO.Path.Combine(scratch, "desktop.json"));
            store.Save(new PetSettings { SeenWelcome = true });
            using (Form behind = new Form { Text = "Naiwa QA", FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual, BackColor = Theme.Cream, ShowInTaskbar = false, TopMost = true })
            using (PetForm pet = new PetForm(store, true))
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                behind.Bounds = new Rectangle(area.Left + 50, area.Top + 50, (int)(450 * Native.Scale(IntPtr.Zero)), (int)(600 * Native.Scale(IntPtr.Zero)));
                behind.Show(); behind.Activate(); Native.SetForegroundWindow(behind.Handle); Pump(100);
                IntPtr foreground = Native.GetForegroundWindow();
                pet.Start(); pet.Location = new Point(behind.Left + 30, behind.Top + 80); pet.DrawFrame(); Pump(80);
                Check("角色显示不抢当前窗口焦点", delegate { Expect(Native.GetForegroundWindow() == foreground, "before=" + foreground + " after=" + Native.GetForegroundWindow() + " pet=" + pet.Handle + " behind=" + behind.Handle); });
                Check("12 个透明角色资源均可加载", delegate
                {
                    foreach (string name in SpriteLibrary.Names)
                    {
                        Bitmap b = pet.Sprites.Get(name); int opaque = 0;
                        Expect(b.GetPixel(0, 0).A == 0 && b.GetPixel(b.Width - 1, b.Height - 1).A == 0, "nontransparent corner " + name);
                        for (int y = 0; y < b.Height; y += 4) for (int x = 0; x < b.Width; x += 4) if (b.GetPixel(x, y).A > 100) opaque++;
                        Expect(opaque > 1000, "empty sprite " + name);
                    }
                });
                Check("透明区域原生命中穿透到底层窗口", delegate
                {
                    Point p = pet.PointToScreen(new Point(3, 80));
                    IntPtr actual = Native.WindowFromPoint(new Native.PointValue(p.X, p.Y));
                    Expect(actual == behind.Handle, "transparent point hit " + actual + " behind=" + behind.Handle + " pet=" + pet.Handle);
                });
                Check("角色轮廓可以接收原生鼠标命中", delegate
                {
                    Point p = pet.PointToScreen(BodyPoint(pet, .60));
                    Expect(Native.WindowFromPoint(new Native.PointValue(p.X, p.Y)) == pet.Handle, "body missed");
                });
                Check("鼠标激活消息被拦截，点击不抢焦点", delegate
                {
                    Expect(Native.SendMessage(pet.Handle, Native.WM_MOUSEACTIVATE, behind.Handle, IntPtr.Zero).ToInt32() == Native.MA_NOACTIVATE, "mouse activation");
                    Point p = BodyPoint(pet, .60); IntPtr beforeClick = Native.GetForegroundWindow();
                    IntPtr position = new IntPtr((p.Y << 16) | (p.X & 0xffff));
                    Native.SendMessage(pet.Handle, 0x201, new IntPtr(1), position);
                    Native.SendMessage(pet.Handle, 0x202, IntPtr.Zero, position);
                    Expect(Native.GetForegroundWindow() == beforeClick, "before=" + beforeClick + " after=" + Native.GetForegroundWindow() + " pet=" + pet.Handle);
                    Expect(pet.CurrentAction == "belly", "native click ignored");
                });
                Check("摸头与戳肚子分区，反馈立即开始", delegate
                {
                    Point head = BodyPoint(pet, .27); Stopwatch watch = Stopwatch.StartNew(); pet.TestDown(head); pet.TestUp(head);
                    Expect(pet.CurrentAction == "pat" && watch.ElapsedMilliseconds < 100, "head feedback");
                    Point belly = BodyPoint(pet, .60); pet.TestDown(belly); pet.TestUp(belly); Expect(pet.CurrentAction == "belly", "belly feedback");
                });
                Check("捧腹大笑 12 帧均透明且脚底对齐", delegate
                {
                    HashSet<string> images = new HashSet<string>();
                    for (int i = 0; i < SpriteLibrary.LaughFrameCount; i++)
                    {
                        Bitmap frame = pet.Sprites.GetFrame("belly", (i + .1) / 12, false);
                        Expect(frame.Size == new Size(400, 440) && frame.GetPixel(0, 0).A == 0 && frame.GetPixel(399, 439).A == 0, "frame size/alpha " + i);
                        int bottom = 0, opaque = 0;
                        for (int y = 0; y < frame.Height; y++) for (int x = 0; x < frame.Width; x++)
                            if (frame.GetPixel(x, y).A > 100) { opaque++; bottom = y; }
                        Expect(opaque > 20000 && bottom >= 410 && bottom <= 415, "frame body/baseline " + i + " bottom=" + bottom);
                        using (MemoryStream data = new MemoryStream())
                        { frame.Save(data, ImageFormat.Png); images.Add(Convert.ToBase64String(data.ToArray())); }
                    }
                    Expect(images.Count == 12, "duplicate or missing laughing frames");
                });
                Check("肚子原生点击播放连续大笑帧，结束回到待机", delegate
                {
                    Point p = BodyPoint(pet, .60); IntPtr position = new IntPtr((p.Y << 16) | (p.X & 0xffff));
                    Native.SendMessage(pet.Handle, 0x201, new IntPtr(1), position); Native.SendMessage(pet.Handle, 0x202, IntPtr.Zero, position);
                    int first = Native.SendMessage(pet.Handle, Native.AnimationMessage, IntPtr.Zero, IntPtr.Zero).ToInt32();
                    Pump(260); int next = Native.SendMessage(pet.Handle, Native.AnimationMessage, IntPtr.Zero, IntPtr.Zero).ToInt32();
                    Expect(first > 0 && next > 0 && first != next, "animation did not advance");
                    pet.Surface.Save(System.IO.Path.Combine(directory, "捧腹大笑.png"), ImageFormat.Png);
                    Pump(1900); Expect(Native.SendMessage(pet.Handle, Native.AnimationMessage, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero, "laugh never ended");
                });
                Check("减少动作时大笑保持静态，普通模式循环使用完整帧序列", delegate
                {
                    Expect(Object.ReferenceEquals(pet.Sprites.GetFrame("belly", .1, true), pet.Sprites.GetFrame("belly", .8, true)), "reduced motion advanced frames");
                    Expect(!Object.ReferenceEquals(pet.Sprites.GetFrame("belly", .1, false), pet.Sprites.GetFrame("belly", .8, false)), "normal frames did not vary");
                    Expect(Object.ReferenceEquals(pet.Sprites.GetFrame("belly", .04, false), pet.Sprites.GetFrame("belly", 1.04, false)), "loop not continuous");
                });
                Check("连续点击合并反馈，不排队", delegate
                {
                    for (int i = 0; i < 30; i++) { Point p = BodyPoint(pet, .60); pet.TestDown(p); pet.TestUp(p); }
                    Expect(pet.CurrentAction == "belly", "action changed"); Pump(2200); Expect(pet.CurrentAction == "idle", "queue remained");
                });
                Check("拖动跟手，松开回弹并保存位置", delegate
                {
                    Point p = BodyPoint(pet, .60), origin = pet.Location;
                    pet.TestDown(p); pet.TestMove(new Point(p.X + 35, p.Y + 22));
                    Expect(pet.Dragging && pet.CurrentAction == "drag" && pet.Left == origin.X + 35 && pet.Top == origin.Y + 22, "drag mismatch");
                    pet.TestUp(p); Expect(!pet.Dragging && pet.CurrentAction == "land", "drop feedback");
                    PetSettings saved = store.Load(); Expect(saved.X == pet.Left && saved.Y == pet.Top, "location not saved");
                });
                Check("鼠标穿透可通过叫出动作恢复", delegate
                {
                    pet.ApplySettings(160, true, false, false, false, true);
                    Point p = pet.PointToScreen(BodyPointIgnoringThrough(pet));
                    Expect(Native.WindowFromPoint(new Native.PointValue(p.X, p.Y)) != pet.Handle, "through mode missed");
                    pet.Wake(); Expect(!pet.Through && !pet.Settings.ClickThrough && pet.Visible, "recover failed");
                });
                Check("专注模式、暂停恢复及菜单内容", delegate
                {
                    pet.StartFocus(25); Expect(pet.Clock.Active && pet.CurrentAction == "sit", "focus pose");
                    pet.ToggleFocusPause(); Expect(pet.Clock.Paused, "pause"); pet.ToggleFocusPause(); Expect(!pet.Clock.Paused, "resume");
                    using (ContextMenuStrip menu = new ContextMenuStrip())
                    { pet.PopulateMenu(menu); bool found = false; foreach (ToolStripItem item in menu.Items) if (item.Text == "暂停专注") found = true; Expect(found, "pause menu missing"); }
                    pet.CancelFocus(); Expect(!pet.Clock.Active && !pet.Clock.Pending, "cancel");
                });
                Check("锁定/隐藏状态停止渲染，恢复后继续", delegate
                {
                    pet.TestLock(true); int count = pet.RenderCount; Pump(650);
                    Expect(!pet.Visible && pet.RenderCount == count, "render continued hidden");
                    pet.TestLock(false); Expect(pet.Visible && pet.RenderCount > count, "resume failed");
                });
                Check("锁屏后休眠再唤醒仍等待解锁", delegate
                {
                    pet.TestLock(true); pet.TestSuspend(true); pet.TestSuspend(false);
                    Expect(!pet.Visible, "resume cleared session lock");
                    pet.TestLock(false); Expect(pet.Visible, "unlock failed");
                });
                Check("隐藏期间到期延迟通知，叫出后只提示一次", delegate
                {
                    pet.Clock.Start(1); pet.HidePet(); Pump(1300); Expect(pet.Clock.Pending && !pet.Clock.Active, "hidden expiry");
                    pet.Wake(); Expect(!pet.Clock.Pending, "completion not consumed"); Pump(120); Expect(!pet.Clock.Pending, "completion repeated");
                });
                Check("睡觉结束后仍留在桌面，点击身体即可唤醒", delegate
                {
                    pet.Sleep(); Pump(2200);
                    Expect(pet.Settings.Sleeping && !pet.Settings.Hidden && pet.Visible && Native.IsWindowVisible(pet.Handle) && pet.CurrentAction == "sleep", "sleep should stay visible");
                    pet.Surface.Save(System.IO.Path.Combine(directory, "桌面睡觉.png"), ImageFormat.Png);
                    Point p = BodyPoint(pet, .6); pet.TestDown(p); pet.TestUp(p);
                    Expect(pet.Visible && !pet.Settings.Sleeping && !pet.Settings.Hidden && pet.CurrentAction == "wake", "body click wake failed");
                });
                Check("睡觉期间专注到期不打断打盹，叫醒后只通知一次", delegate
                {
                    pet.Clock.Start(1); pet.Sleep(); Pump(1700);
                    Expect(pet.Visible && pet.Settings.Sleeping && pet.Clock.Pending && pet.CurrentAction == "sleep", "focus interrupted sleep");
                    pet.Wake(); Expect(!pet.Clock.Pending && !pet.Settings.Sleeping && pet.Visible, "wake completion missing");
                });
                Check("睡觉保持关闭随机动作，解锁后仍为睡觉姿态", delegate
                {
                    pet.Sleep(); Pump(6000);
                    Expect(pet.CurrentAction == "sleep" && pet.Visible, "idle action interrupted sleep");
                    pet.TestLock(true); Expect(!pet.Visible, "lock should hide"); pet.TestLock(false);
                    Expect(pet.Visible && pet.Settings.Sleeping && pet.CurrentAction == "sleep", "unlock should restore sleep");
                    pet.Wake();
                });
                SleepTests(pet, directory);
                Check("尺寸限制与脚底位置保持", delegate
                {
                    int feet = pet.Top + pet.Height; pet.ApplySettings(240, true, false, false, false, false);
                    Expect(pet.Settings.PetHeight == 240 && Math.Abs(pet.Top + pet.Height - feet) <= 1, "size anchor");
                    pet.ApplySettings(96, true, false, false, false, false); Expect(pet.Settings.PetHeight == 96, "small size");
                    pet.ApplySettings(160, true, false, false, false, false);
                });
                Check("重复绘制不会累积 GDI 句柄", delegate
                {
                    Pump(1700); Process process = Process.GetCurrentProcess(); int before = Native.GetGuiResources(process.Handle, 0);
                    for (int i = 0; i < 300; i++) pet.DrawFrame();
                    GC.Collect(); GC.WaitForPendingFinalizers(); int after = Native.GetGuiResources(process.Handle, 0);
                    Expect(after - before <= 3, "GDI grew " + before + " -> " + after); results.Add("  GDI：" + before + " → " + after + "（300 帧）");
                });
                Check("实际全屏窗口触发隐藏，退出全屏后恢复", delegate
                {
                    using (Process probe = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--fullscreen-probe") { UseShellExecute = false, CreateNoWindow = true }))
                    {
                        IntPtr hwnd = IntPtr.Zero; Stopwatch wait = Stopwatch.StartNew();
                        while (wait.ElapsedMilliseconds < 1800)
                        { hwnd = Native.FindWindow(null, "Naiwa-Fullscreen-Probe"); if (hwnd != IntPtr.Zero) break; Pump(40); }
                        Expect(hwnd != IntPtr.Zero, "probe not found"); Native.SetForegroundWindow(hwnd); Pump(100);
                        bool detected = Native.IsFullscreen(pet.Handle, Screen.PrimaryScreen.Bounds);
                        pet.TestActualFullscreen(); bool hidden = !pet.Visible;
                        int render = pet.RenderCount; pet.TestActualFullscreen(); bool stopped = pet.RenderCount == render;
                        Native.PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero); Pump(120);
                        behind.Activate(); Native.SetForegroundWindow(behind.Handle); pet.TestActualFullscreen();
                        Expect(detected && hidden && stopped && pet.Visible, "detected=" + detected + " hidden=" + hidden + " stopped=" + stopped + " resumed=" + pet.Visible);
                    }
                });
                behind.Activate(); Native.SetForegroundWindow(behind.Handle);
                GeneratePreviews(pet, directory);
                Check("20 秒待机性能采样", delegate
                {
                    pet.ApplySettings(160, true, false, false, false, false); pet.Play("idle", 0, null); Pump(300);
                    Process process = Process.GetCurrentProcess(); process.Refresh(); TimeSpan cpu = process.TotalProcessorTime;
                    results.Add("  采样起点：" + pet.Diagnostics);
                    Stopwatch sample = Stopwatch.StartNew(); int frames = pet.RenderCount; int handles = Native.GetGuiResources(process.Handle, 0);
                    Pump(20000); process.Refresh();
                    double single = (process.TotalProcessorTime - cpu).TotalSeconds / sample.Elapsed.TotalSeconds * 100;
                    double total = single / Environment.ProcessorCount; double memory = process.WorkingSet64 / 1048576.0;
                    results.Add("  待机：" + total.ToString("F3") + "% CPU（整机归一），" + single.ToString("F2") + "%（单核归一），" + memory.ToString("F1") + " MB 工作集，" + (pet.RenderCount - frames) + " 帧/20s，GDI " + handles + " → " + Native.GetGuiResources(process.Handle, 0));
                    results.Add("  采样终点：" + pet.Diagnostics);
                    Expect(total < 1 && memory < 150, "performance target exceeded");
                });
                pet.HidePet(); behind.Hide();
            }
        }
        private static Point BodyPointIgnoringThrough(PetForm pet)
        {
            RectangleF body = pet.Renderer.Body;
            for (int y = (int)(body.Top + body.Height * .5); y < body.Bottom; y++)
            for (int x = (int)body.Left; x < body.Right; x++)
                if (pet.Surface.GetPixel(x, y).A > 100) return new Point(x, y);
            throw new InvalidOperationException("Empty body");
        }
        private static void GeneratePreviews(PetForm pet, string directory)
        {
            using (SettingsDialog dialog = new SettingsDialog(pet))
            {
                dialog.Show(); Pump(150); dialog.PerformLayout();
                Check("设置面板保存按钮可见且无裁切", delegate
                {
                    Button save = (Button)dialog.AcceptButton;
                    Rectangle actual = new Rectangle(dialog.PointToClient(save.PointToScreen(Point.Empty)), save.Size);
                    Expect(save.Visible && dialog.ClientRectangle.Contains(actual), "save button outside client " + actual + ", client " + dialog.ClientRectangle);
                });
                using (Bitmap image = new Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height)); image.Save(System.IO.Path.Combine(directory, "设置面板.png"), ImageFormat.Png); }
                Check("设置面板保存操作真正生效", delegate
                {
                    TrackBar slider = Find<TrackBar>(dialog); int before = pet.Settings.PetHeight;
                    int wanted = before == 176 ? 160 : 176; slider.Value = wanted;
                    ((Button)dialog.AcceptButton).PerformClick(); Expect(pet.Settings.PetHeight == wanted, "settings not applied");
                    pet.ApplySettings(before, true, false, false, false, false);
                });
            }
            string[] labels = { "抱肚发呆", "眨眼", "摸摸头", "捧腹大笑", "被提起", "放下回弹", "陪你专注", "打盹", "伸懒腰", "左看看", "右看看", "走两步" };
            using (Bitmap image = new Bitmap(1000, 870, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(image))
            using (Font font = new Font("Microsoft YaHei UI", 14))
            using (Font title = new Font("Microsoft YaHei UI", 24, FontStyle.Bold))
            using (Brush ink = new SolidBrush(Theme.Ink))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.Clear(Theme.Cream); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawString("奶蛙 · 第一版角色资源", title, ink, 30, 20);
                for (int i = 0; i < 12; i++)
                {
                    int x = i % 4 * 250, y = 82 + i / 4 * 256;
                    pet.Sprites.Draw(g, SpriteLibrary.Names[i], new RectangleF(x + 25, y, 200, 220), 1);
                    g.DrawString(labels[i], font, ink, new RectangleF(x, y + 220, 250, 28), center);
                }
                image.Save(System.IO.Path.Combine(directory, "角色姿态预览.png"), ImageFormat.Png);
            }
            Check("设置面板与资源预览可渲染", delegate
            { Expect(File.Exists(System.IO.Path.Combine(directory, "设置面板.png")) && File.Exists(System.IO.Path.Combine(directory, "角色姿态预览.png")), "preview missing"); });
        }
        private static T Find<T>(Control parent) where T : Control
        {
            foreach (Control child in parent.Controls)
            { if (child is T) return (T)child; T nested = Find<T>(child); if (nested != null) return nested; }
            return null;
        }
    }
}
