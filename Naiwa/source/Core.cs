using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Drawing;

namespace Naiwa
{
    [DataContract]
    public sealed class PetSettings
    {
        [DataMember] public int Version = 1;
        [DataMember] public int PetHeight = 160;
        [DataMember] public int X = Int32.MinValue;
        [DataMember] public int Y = Int32.MinValue;
        [DataMember] public bool ClickThrough;
        [DataMember] public bool AvoidFullscreen = true;
        [DataMember] public bool Wander;
        [DataMember] public bool RestReminders;
        [DataMember] public bool ReducedMotion;
        [DataMember] public bool SeenWelcome;
        [DataMember] public bool Sleeping;
        [DataMember] public bool Hidden;
        [DataMember] public bool FocusActive;
        [DataMember] public bool FocusPaused;
        [DataMember] public bool CompletionPending;
        [DataMember] public long FocusDeadlineTicks;
        [DataMember] public double FocusRemaining;
        [DataMember] public double FocusDuration;

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        { Version = 1; PetHeight = 160; X = Y = Int32.MinValue; AvoidFullscreen = true; }

        public void Validate()
        {
            PetHeight = Math.Max(96, Math.Min(240, PetHeight));
            if (Double.IsNaN(FocusDuration) || Double.IsInfinity(FocusDuration) || FocusDuration < 1 || FocusDuration > 14400)
            { FocusActive = false; FocusPaused = false; FocusDuration = 0; FocusRemaining = 0; }
            if (Double.IsNaN(FocusRemaining) || Double.IsInfinity(FocusRemaining)) FocusRemaining = 0;
            FocusRemaining = Math.Max(0, Math.Min(FocusDuration, FocusRemaining));
            if (FocusDeadlineTicks < DateTime.MinValue.Ticks || FocusDeadlineTicks > DateTime.MaxValue.Ticks)
            { FocusActive = false; FocusPaused = false; FocusDeadlineTicks = 0; }
            if (!FocusActive) FocusPaused = false;
        }
    }

    public sealed class SettingsStore
    {
        public readonly string Path;
        public string LastError { get; private set; }
        public SettingsStore(string path) { Path = path; }
        private PetSettings Read(string file)
        {
            using (FileStream stream = File.OpenRead(file))
            {
                PetSettings value = (PetSettings)new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream);
                value.Validate(); return value;
            }
        }
        public PetSettings Load()
        {
            if (!File.Exists(Path)) return new PetSettings();
            try { return Read(Path); }
            catch (Exception ex)
            {
                LastError = ex.Message;
                try { if (File.Exists(Path + ".bak")) return Read(Path + ".bak"); } catch { }
                return new PetSettings();
            }
        }
        public bool Save(PetSettings settings)
        {
            string temporary = Path + ".tmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(PetSettings)).WriteObject(stream, settings);
                    stream.Flush(true);
                }
                if (File.Exists(Path)) File.Replace(temporary, Path, Path + ".bak", true);
                else File.Move(temporary, Path);
                LastError = null; return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                return false;
            }
        }
    }

    // UTC deadline survives suspension/restart. Reading time never consumes completion.
    public sealed class FocusClock
    {
        private readonly PetSettings state;
        private readonly Func<DateTime> now;
        public FocusClock(PetSettings value, Func<DateTime> utcNow)
        { state = value; now = utcNow; state.Validate(); }
        public bool Active { get { return state.FocusActive; } }
        public bool Paused { get { return state.FocusPaused; } }
        public bool Pending { get { return state.CompletionPending; } }
        public double Remaining
        {
            get
            {
                if (!Active) return 0;
                if (Paused) return state.FocusRemaining;
                return Math.Max(0, Math.Min(state.FocusDuration,
                    (new DateTime(state.FocusDeadlineTicks, DateTimeKind.Utc) - now()).TotalSeconds));
            }
        }
        public double Progress { get { return state.FocusDuration <= 0 ? 0 : 1 - Remaining / state.FocusDuration; } }
        public string Label
        {
            get
            {
                int seconds = (int)Math.Ceiling(Remaining);
                return String.Format("{0:00}:{1:00}", seconds / 60, seconds % 60);
            }
        }
        public void Start(double seconds)
        {
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < 1 || seconds > 14400)
                throw new ArgumentOutOfRangeException("seconds");
            state.FocusDuration = seconds; state.FocusRemaining = seconds;
            state.FocusDeadlineTicks = now().AddSeconds(seconds).Ticks;
            state.FocusActive = true; state.FocusPaused = false; state.CompletionPending = false;
        }
        public bool Tick()
        {
            if (!Active || Paused || Remaining > 0) return false;
            state.FocusActive = false; state.FocusPaused = false;
            state.FocusRemaining = 0; state.CompletionPending = true; return true;
        }
        public void Pause()
        {
            Tick(); if (!Active || Paused) return;
            state.FocusRemaining = Remaining; state.FocusPaused = true;
        }
        public void Resume()
        {
            if (!Active || !Paused) return;
            state.FocusDeadlineTicks = now().AddSeconds(state.FocusRemaining).Ticks;
            state.FocusPaused = false;
        }
        public void Cancel()
        {
            state.FocusActive = false; state.FocusPaused = false; state.CompletionPending = false;
            state.FocusRemaining = 0;
        }
        public bool ConsumeCompletion()
        {
            if (!Pending) return false;
            state.CompletionPending = false; return true;
        }
    }

    public static class Geometry
    {
        public static Point Clamp(Point location, Size size, Rectangle area)
        {
            int x = Math.Max(area.Left, Math.Min(location.X, area.Right - size.Width));
            int y = Math.Max(area.Top, Math.Min(location.Y, area.Bottom - size.Height));
            return new Point(x, y);
        }
        public static bool Covers(Rectangle window, Rectangle monitor)
        {
            return window.Left <= monitor.Left + 2 && window.Top <= monitor.Top + 2 &&
                window.Right >= monitor.Right - 2 && window.Bottom >= monitor.Bottom - 2;
        }
    }
}
