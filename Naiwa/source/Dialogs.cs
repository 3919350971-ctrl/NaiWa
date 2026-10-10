using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Naiwa
{
    internal sealed class SettingsDialog : Form
    {
        private readonly PetForm pet;
        private readonly TrackBar size;
        private readonly Label sizeLabel;
        private readonly CheckBox avoid, wander, reminders, reduced, through;
        internal SettingsDialog(PetForm value)
        {
            pet = value; Text = "奶蛙 · 设置"; Icon = pet.Sprites.Icon;
            Font = new Font("Microsoft YaHei UI", 10); BackColor = Theme.Cream; ForeColor = Theme.Ink;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(420, 646); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(24, 20, 24, 20), AutoScroll = true };
            Controls.Add(layout);
            Label heading = new Label { Text = "奶蛙", AutoSize = true, Font = new Font("Microsoft YaHei UI", 24, FontStyle.Bold), Margin = new Padding(0) };
            Add(layout, heading);
            Add(layout, new Label { Text = "住在桌面上的慢半拍朋友", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(1, 3, 0, 9) });
            Add(layout, new CharacterPreview(pet.Sprites) { Height = 132, Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 9) });
            sizeLabel = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            Add(layout, sizeLabel);
            size = new TrackBar { Minimum = 96, Maximum = 240, Value = pet.Settings.PetHeight,
                TickFrequency = 48, SmallChange = 8, LargeChange = 16, Dock = DockStyle.Top, Height = 44, Margin = new Padding(0, 3, 0, 0), AccessibleName = "角色高度" };
            size.ValueChanged += delegate { SizeText(); }; Add(layout, size); SizeText();
            avoid = Option("全屏时自动躲开", pet.Settings.AvoidFullscreen); Add(layout, avoid);
            wander = Option("沿桌面底边偶尔走两步", pet.Settings.Wander); Add(layout, wander);
            reminders = Option("每小时轻轻提醒我休息", pet.Settings.RestReminders); Add(layout, reminders);
            reduced = Option("减少动作，安静地待着", pet.Settings.ReducedMotion); Add(layout, reduced);
            through = Option("鼠标穿透（点击托盘可恢复）", pet.Settings.ClickThrough); Add(layout, through);
            Add(layout, new Label { Text = "专注期间暂停散步和休息提醒。\n默认静音，设置自动保存在本地。", AutoSize = true,
                ForeColor = Theme.Muted, Font = new Font("Microsoft YaHei UI", 9), Margin = new Padding(0, 12, 0, 17) });
            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0) };
            Button save = MakeButton("保存设置", true), cancel = MakeButton("取消", false);
            save.Click += delegate
            {
                pet.ApplySettings(size.Value, avoid.Checked, wander.Checked, reminders.Checked, reduced.Checked, through.Checked);
                DialogResult = DialogResult.OK; Close();
            };
            cancel.Click += delegate { Close(); };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); Add(layout, buttons);
            AcceptButton = save; CancelButton = cancel;
            Shown += delegate
            {
                int preferred = layout.GetPreferredSize(new Size(ClientSize.Width, 0)).Height + 8;
                ClientSize = new Size(ClientSize.Width, Math.Min(Screen.FromControl(this).WorkingArea.Height - 80, preferred));
                layout.PerformLayout();
                Location = new Point(Location.X, Math.Max(Screen.FromControl(this).WorkingArea.Top + 16,
                    Math.Min(Location.Y, Screen.FromControl(this).WorkingArea.Bottom - Height - 16)));
            };
        }
        private void SizeText() { sizeLabel.Text = "角色大小  ·  " + size.Value + " 像素"; }
        private static void Add(TableLayoutPanel panel, Control control)
        { panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(control, 0, panel.RowCount++); }
        private static CheckBox Option(string text, bool value)
        { return new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 8, 0, 0), UseVisualStyleBackColor = false }; }
        internal static Button MakeButton(string text, bool primary)
        {
            return new Button { Text = text, AutoSize = true, MinimumSize = new Size(96, 36), Padding = new Padding(10, 3, 10, 3),
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Theme.Yellow : Theme.Cream, ForeColor = Theme.Ink,
                Margin = new Padding(0, 0, 10, 0) };
        }
    }

    internal sealed class CharacterPreview : Control
    {
        private readonly SpriteLibrary library;
        internal CharacterPreview(SpriteLibrary sprites)
        { library = sprites; DoubleBuffered = true; AccessibleName = "奶蛙站立与睡觉的预览"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Theme.FillRound(g, Color.FromArgb(240, 243, 230), new RectangleF(0, 0, Width - 1, Height - 1), 16);
            float h = Height - 16, w = h * 400 / 440;
            library.Draw(g, "idle", new RectangleF(Width * .28f - w / 2, 5, w, h), 1);
            library.Draw(g, "sleep", new RectangleF(Width * .72f - w / 2, 5, w, h), 1);
        }
    }

    internal sealed class DurationDialog : Form
    {
        private readonly NumericUpDown minutes;
        internal int Minutes { get { return (int)minutes.Value; } }
        internal DurationDialog(Icon icon)
        {
            Text = "奶蛙 · 陪我专注"; Icon = icon; BackColor = Theme.Cream; ForeColor = Theme.Ink;
            Font = new Font("Microsoft YaHei UI", 10); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(340, 227); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Label title = new Label { Text = "这次，陪你多久？", AutoSize = true, Location = new Point(24, 21), Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold) };
            minutes = new NumericUpDown { Minimum = 1, Maximum = 240, Value = 25, Location = new Point(25, 83), Width = 180, AccessibleName = "专注分钟数" };
            Label unit = new Label { Text = "分钟", AutoSize = true, Location = new Point(218, 85) };
            Label hint = new Label { Text = "可以随时暂停或结束。", AutoSize = true, ForeColor = Theme.Muted, Location = new Point(25, 125) };
            Button start = SettingsDialog.MakeButton("开始专注", true); start.Location = new Point(25, 170);
            start.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Button cancel = SettingsDialog.MakeButton("取消", false); cancel.Location = new Point(158, 170); cancel.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { title, minutes, unit, hint, start, cancel }); AcceptButton = start; CancelButton = cancel;
        }
    }
}
