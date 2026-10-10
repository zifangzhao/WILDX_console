using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace USB512_interface
{
    internal sealed class EisSpectrumForm : Form
    {
        private readonly IList<Ce64MscEisResult> results;

        internal EisSpectrumForm(int channel, IList<Ce64MscEisResult> results,
            uint sampleRateHz, uint elapsedMilliseconds, Action exportCsv)
        {
            this.results = results ?? throw new ArgumentNullException("results");
            Text = "WILD64 EIS spectrum - E" + channel.ToString("00");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(820, 600);
            ClientSize = new Size(1040, 720);

            Label header = new Label
            {
                Dock = DockStyle.Top,
                Height = 62,
                Padding = new Padding(12, 8, 12, 4),
                Font = new Font(Font.FontFamily, 9.0f, FontStyle.Regular),
                Text = "Selected electrode E" + channel.ToString("00") + "  |  " + results.Count +
                    " logarithmic points  |  " + sampleRateHz.ToString("N0", CultureInfo.InvariantCulture) +
                    " Hz measurement cadence  |  " + elapsedMilliseconds + " ms" + Environment.NewLine +
                    "Nominal EIS - not calibrated absolute impedance or phase. Orange/red points have sparse waveform sampling."
            };

            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            TabPage bode = new TabPage("Bode spectrum") { BackColor = Color.White, Padding = new Padding(8) };
            TableLayoutPanel plots = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            plots.RowStyles.Add(new RowStyle(SizeType.Percent, 56.0f));
            plots.RowStyles.Add(new RowStyle(SizeType.Percent, 44.0f));
            plots.Controls.Add(new EisPlotPanel(results, true), 0, 0);
            plots.Controls.Add(new EisPlotPanel(results, false), 0, 1);
            bode.Controls.Add(plots);
            tabs.TabPages.Add(bode);
            tabs.TabPages.Add(BuildTable());

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8), WrapContents = false
            };
            Button close = new Button { Text = "Close", DialogResult = DialogResult.OK, Size = new Size(92, 30) };
            Button export = new Button { Text = "Export CSV...", Size = new Size(112, 30) };
            export.Click += delegate { if (exportCsv != null) exportCsv(); };
            buttons.Controls.Add(close);
            buttons.Controls.Add(export);

            Controls.Add(tabs);
            Controls.Add(header);
            Controls.Add(buttons);
            AcceptButton = close;
        }

        private TabPage BuildTable()
        {
            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false, BackgroundColor = Color.White
            };
            grid.Columns.Add("frequency", "Frequency (Hz)");
            grid.Columns.Add("magnitude", "Magnitude");
            grid.Columns.Add("phase", "Phase (deg)");
            grid.Columns.Add("range", "ZCHK range");
            grid.Columns.Add("quality", "Quality");
            foreach (Ce64MscEisResult result in results)
            {
                int scale = (result.Flags >> 4) & 3;
                string quality = (result.Flags & Ce64MscProtocol.EisResultFlagSparseBelowFourSamples) != 0 ? "<4 samples/cycle" :
                    (result.Flags & Ce64MscProtocol.EisResultFlagSparseBelowSixSamples) != 0 ? "<6 samples/cycle" :
                    (result.Flags & Ce64MscProtocol.ZtestResultFlagLowerBound) != 0 ? "lower-bound" : "nominal";
                grid.Rows.Add(result.FrequencyHz.ToString(CultureInfo.InvariantCulture), FormatOhms(result.Ohms),
                    (result.PhaseTenths / 10.0).ToString("F1", CultureInfo.InvariantCulture),
                    scale == 0 ? "0.1 pF" : scale == 1 ? "1 pF" : "10 pF", quality);
            }
            TabPage tab = new TabPage("Point data") { Padding = new Padding(8) };
            tab.Controls.Add(grid);
            return tab;
        }

        private static string FormatOhms(uint ohms)
        {
            if (ohms >= 1000000U) return (ohms / 1000000.0).ToString("F3", CultureInfo.InvariantCulture) + " MOhm";
            if (ohms >= 1000U) return (ohms / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " kOhm";
            return ohms.ToString(CultureInfo.InvariantCulture) + " Ohm";
        }

        private sealed class EisPlotPanel : Panel
        {
            private readonly IList<Ce64MscEisResult> values;
            private readonly bool magnitude;

            internal EisPlotPanel(IList<Ce64MscEisResult> values, bool magnitude)
            {
                this.values = values; this.magnitude = magnitude;
                Dock = DockStyle.Fill; BackColor = Color.White; DoubleBuffered = true;
                Margin = new Padding(4);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (values.Count == 0) return;
                Rectangle plot = new Rectangle(72, 28, Math.Max(1, ClientSize.Width - 96), Math.Max(1, ClientSize.Height - 68));
                double minX = Math.Log10(values[0].FrequencyHz), maxX = Math.Log10(values[values.Count - 1].FrequencyHz);
                double minY, maxY;
                if (magnitude)
                {
                    minY = Double.MaxValue; maxY = Double.MinValue;
                    foreach (Ce64MscEisResult value in values)
                    {
                        double y = Math.Log10(Math.Max(1U, value.Ohms));
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }
                    if (maxY - minY < 0.2) { minY -= 0.1; maxY += 0.1; }
                }
                else { minY = -180.0; maxY = 180.0; }

                using (Pen grid = new Pen(Color.FromArgb(220, 224, 228)))
                using (Pen axis = new Pen(Color.FromArgb(55, 55, 55)))
                using (Font font = new Font(Font.FontFamily, 8.0f))
                using (Font titleFont = new Font(Font.FontFamily, 9.0f, FontStyle.Bold))
                using (Brush text = new SolidBrush(Color.FromArgb(45, 45, 45)))
                {
                    for (int i = 0; i <= 4; i++)
                    {
                        int y = plot.Bottom - i * plot.Height / 4;
                        e.Graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                        double value = minY + (maxY - minY) * i / 4.0;
                        string label = magnitude ? FormatOhms((uint)Math.Min(UInt32.MaxValue, Math.Pow(10.0, value))) : value.ToString("F0") + " deg";
                        e.Graphics.DrawString(label, font, text, new RectangleF(0, y - 8, 66, 18), new StringFormat { Alignment = StringAlignment.Far });
                    }
                    int[] ticks = { 100, 200, 500, 1000, 2000, 5000, 10000 };
                    foreach (int tick in ticks)
                    {
                        int x = plot.Left + (int)Math.Round((Math.Log10(tick) - minX) / (maxX - minX) * plot.Width);
                        e.Graphics.DrawLine(grid, x, plot.Top, x, plot.Bottom);
                        e.Graphics.DrawString(tick >= 1000 ? (tick / 1000) + "k" : tick.ToString(), font, text,
                            new RectangleF(x - 24, plot.Bottom + 5, 48, 18), new StringFormat { Alignment = StringAlignment.Center });
                    }
                    e.Graphics.DrawRectangle(axis, plot);
                    e.Graphics.DrawString(magnitude ? "Magnitude" : "Phase", titleFont, text, plot.Left, 5);
                    e.Graphics.DrawString("Frequency (Hz, log)", font, text, plot.Left + plot.Width / 2 - 48, plot.Bottom + 23);
                }

                PointF? previous = null;
                foreach (Ce64MscEisResult value in values)
                {
                    double rawY = magnitude ? Math.Log10(Math.Max(1U, value.Ohms)) : value.PhaseTenths / 10.0;
                    float x = plot.Left + (float)((Math.Log10(value.FrequencyHz) - minX) / (maxX - minX) * plot.Width);
                    float y = plot.Bottom - (float)((rawY - minY) / (maxY - minY) * plot.Height);
                    PointF point = new PointF(x, y);
                    if (previous.HasValue) using (Pen line = new Pen(Color.FromArgb(42, 96, 148), 1.6f)) e.Graphics.DrawLine(line, previous.Value, point);
                    Color color = (value.Flags & Ce64MscProtocol.EisResultFlagSparseBelowFourSamples) != 0 ? Color.Firebrick :
                        (value.Flags & Ce64MscProtocol.EisResultFlagSparseBelowSixSamples) != 0 ? Color.DarkOrange : Color.FromArgb(31, 119, 180);
                    using (Brush dot = new SolidBrush(color)) e.Graphics.FillEllipse(dot, x - 3, y - 3, 6, 6);
                    previous = point;
                }
            }
        }
    }
}
