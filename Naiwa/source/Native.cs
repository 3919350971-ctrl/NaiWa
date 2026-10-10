using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Naiwa
{
    internal static class Native
    {
        internal const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        internal const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
        internal const int WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOOLWINDOW = 0x80;
        internal const int GWL_EXSTYLE = -20, SWP_NOACTIVATE = 0x10, SWP_NOSIZE = 1, SWP_NOMOVE = 2;
        internal const int WakeMessage = 0x8000 + 41;
        internal const int AnimationMessage = 0x8000 + 42;
        [StructLayout(LayoutKind.Sequential)] internal struct PointValue
        { internal int X, Y; internal PointValue(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct SizeValue
        { internal int Width, Height; internal SizeValue(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        { internal int Left, Top, Right, Bottom; internal Rectangle Value { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend
        { internal byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr hwnd,
            IntPtr dstDc, ref PointValue dst, ref SizeValue size, IntPtr srcDc, ref PointValue src,
            int colorKey, ref Blend blend, int flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
            int x, int y, int width, int height, int flags);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(PointValue point);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out PointValue point);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern int GetGuiResources(IntPtr process, int flags);

        internal static void EnableDpi()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
        }
        internal static float Scale(IntPtr hwnd)
        {
            try { return (hwnd == IntPtr.Zero ? GetDpiForSystem() : GetDpiForWindow(hwnd)) / 96f; }
            catch (EntryPointNotFoundException) { return 1f; }
        }
        internal static bool IsFullscreen(IntPtr own, Rectangle monitor)
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == own) return false;
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return false;
            StringBuilder text = new StringBuilder(100); GetClassName(hwnd, text, text.Capacity);
            string name = text.ToString();
            if (name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd") return false;
            Rect bounds; return GetWindowRect(hwnd, out bounds) && Geometry.Covers(bounds.Value, monitor);
        }
    }

    internal class LayeredForm : Form
    {
        private Bitmap surface;
        private bool through;
        internal bool Through
        {
            get { return through; }
            set
            {
                through = value;
                if (IsHandleCreated)
                {
                    int style = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                    Native.SetWindowLong(Handle, Native.GWL_EXSTYLE,
                        value ? style | Native.WS_EX_TRANSPARENT : style & ~Native.WS_EX_TRANSPARENT);
                }
            }
        }
        internal Bitmap Surface { get { return surface; } }
        internal LayeredForm()
        {
            // WinForms' managed TopMost branch explicitly activates the form on Show.
            // Keep it topmost at the native layer using SWP_NOACTIVATE instead.
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            SetStyle(ControlStyles.Selectable, false); TabStop = false;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams result = base.CreateParams;
                result.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | 0x8;
                if (through) result.ExStyle |= Native.WS_EX_TRANSPARENT;
                return result;
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WM_MOUSEACTIVATE) { message.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref message);
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && IsHandleCreated)
                Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0,
                    Native.SWP_NOACTIVATE | Native.SWP_NOMOVE | Native.SWP_NOSIZE | 0x40);
        }
        internal bool Hit(Point local)
        {
            return !through && surface != null && local.X >= 0 && local.Y >= 0 &&
                local.X < surface.Width && local.Y < surface.Height && surface.GetPixel(local.X, local.Y).A >= 16;
        }
        internal void Present(Bitmap image)
        {
            IntPtr dc = Native.GetDC(IntPtr.Zero), memory = Native.CreateCompatibleDC(dc);
            IntPtr bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                bitmap = image.GetHbitmap(Color.FromArgb(0)); previous = Native.SelectObject(memory, bitmap);
                Native.PointValue destination = new Native.PointValue(Left, Top), source = new Native.PointValue(0, 0);
                Native.SizeValue size = new Native.SizeValue(image.Width, image.Height);
                Native.Blend blend = new Native.Blend { Operation = 0, Flags = 0, Alpha = 255, Format = 1 };
                if (!Native.UpdateLayeredWindow(Handle, dc, ref destination, ref size, memory, ref source, 0, ref blend, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                Bitmap old = surface; surface = image;
                if (old != null) old.Dispose();
            }
            catch { image.Dispose(); throw; }
            finally
            {
                if (previous != IntPtr.Zero) Native.SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
                if (memory != IntPtr.Zero) Native.DeleteDC(memory);
                if (dc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, dc);
            }
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void Dispose(bool disposing)
        {
            if (disposing && surface != null) { surface.Dispose(); surface = null; }
            base.Dispose(disposing);
        }
    }
}
