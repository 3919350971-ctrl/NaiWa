using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("奶蛙 · 桌面宠物")]
[assembly: AssemblyDescription("住在桌面上的慢半拍朋友")]
[assembly: AssemblyVersion("1.0.4.0")]
[assembly: AssemblyFileVersion("1.0.4.0")]

namespace Naiwa
{
    internal static class Program
    {
        internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        internal static void Log(string message)
        {
            try
            {
                string dir = System.IO.Path.Combine(Root, "data"); Directory.CreateDirectory(dir);
                File.AppendAllText(System.IO.Path.Combine(dir, "app.log"), DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
            }
            catch { }
        }
        [STAThread]
        internal static int Main(string[] args)
        {
            string traceFile = args.Length > 1 && args[0] == "--trace-start" ? args[1] : null;
            Action<string> trace = delegate(string line) { if (traceFile != null) File.AppendAllText(traceFile, line + Environment.NewLine); };
            trace("entry");
            Native.EnableDpi(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            trace("dpi+styles ready");
            if (args.Length > 0 && args[0] == "--self-test") return Tests.Run(args.Length > 1 ? args[1] : System.IO.Path.Combine(Root, "test-results"));
            if (args.Length > 1 && args[0] == "--sleep-regression") return Tests.SleepRegression(args[1]);
            if (args.Length > 1 && args[0] == "--startup-probe")
            {
                string probeDirectory = args[1]; Directory.CreateDirectory(probeDirectory);
                File.WriteAllText(System.IO.Path.Combine(probeDirectory, "startup.txt"), "constructing");
                using (PetForm probe = new PetForm(new SettingsStore(System.IO.Path.Combine(probeDirectory, "settings.json")), false))
                using (System.Windows.Forms.Timer finish = new System.Windows.Forms.Timer { Interval = 1500 })
                {
                    probe.FormClosed += delegate { Application.ExitThread(); };
                    probe.Start();
                    File.WriteAllText(System.IO.Path.Combine(probeDirectory, "startup.txt"),
                        "visible=" + Native.IsWindowVisible(probe.Handle) + "\nhandle=" + probe.Handle +
                        "\nfind=" + Native.FindWindow(null, probe.Text) + "\nforegroundIsPet=" + (Native.GetForegroundWindow() == probe.Handle));
                    finish.Tick += delegate { probe.Close(); }; finish.Start(); Application.Run();
                }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--fullscreen-probe")
            {
                using (Form probe = new Form { Text = "Naiwa-Fullscreen-Probe", FormBorderStyle = FormBorderStyle.None,
                    ShowInTaskbar = false, BackColor = Theme.Cream, StartPosition = FormStartPosition.Manual, TopMost = true })
                using (System.Windows.Forms.Timer finish = new System.Windows.Forms.Timer { Interval = 3500 })
                {
                    probe.Bounds = Screen.PrimaryScreen.Bounds;
                    probe.Controls.Add(new Label { Text = "奶蛙 · 正在验证全屏避让", AutoSize = true,
                        Font = new Font("Microsoft YaHei UI", 16), ForeColor = Theme.Ink, Location = new Point(30, 30) });
                    finish.Tick += delegate { probe.Close(); }; finish.Start(); Application.Run(probe);
                }
                return 0;
            }
            bool created;
            trace("before mutex");
            using (Mutex mutex = new Mutex(true, "Local\\NaiwaDesktopPet-v1", out created))
            {
                trace("mutex ready created=" + created);
                if (!created)
                {
                    IntPtr pet = Native.FindWindow(null, "奶蛙 · 桌面宠物");
                    if (pet != IntPtr.Zero) Native.PostMessage(pet, Native.WakeMessage, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                trace("exception mode ready");
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Log(e.Exception.ToString());
                    MessageBox.Show("奶蛙遇到了问题，已记录到 data/app.log。\n请退出后重新打开。", "奶蛙", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                };
                try
                {
                    trace("before constructor");
                    using (PetForm pet = new PetForm(new SettingsStore(System.IO.Path.Combine(Root, "data", "settings.json")), false))
                    {
                        trace("constructed");
                        pet.FormClosed += delegate { Application.ExitThread(); };
                        pet.Start(); trace("started visible=" + Native.IsWindowVisible(pet.Handle) + " handle=" + pet.Handle + " find=" + Native.FindWindow(null, pet.Text)); Application.Run();
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    Log(ex.ToString());
                    MessageBox.Show("奶蛙没有启动成功。\n请查看程序旁 data/app.log。\n" + ex.Message, "奶蛙", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
