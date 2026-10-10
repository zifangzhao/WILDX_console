using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace USB512_interface
{
    internal sealed class ImpedanceReportForm : Form
    {
        private IList<Ce64MscZtestResult> results;
        private uint frequencyHz;
        private uint elapsedMilliseconds;
        private Action exportCsv;
        private TabPage mapTab;
        private TabControl tabs;
        private Label summaryLabel;
        private CheckBox continuousCheckBox;
        private readonly bool continuous;
        private readonly Action stopContinuous;
        private int sweepNumber;
        private bool stopRequested;
        private readonly Dictionary<int, Label> electrodeLabels = new Dictionary<int, Label>();
        private Button eisButton;
        private int selectedEisChannel = -1;
        private bool eisAvailable;

        internal int SelectedEisChannel { get { return selectedEisChannel; } }
        internal bool ContinuousRequested { get { return continuousCheckBox != null && continuousCheckBox.Checked; } }

        internal ImpedanceReportForm(IList<Ce64MscZtestResult> results, uint frequencyHz,
            uint elapsedMilliseconds, Action exportCsv)
            : this(results, frequencyHz, elapsedMilliseconds, exportCsv, false, null, 1)
        {
        }

        internal ImpedanceReportForm(IList<Ce64MscZtestResult> results, uint frequencyHz,
            uint elapsedMilliseconds, Action exportCsv, bool continuous, Action stopContinuous, int sweepNumber)
        {
            if (results == null) throw new ArgumentNullException("results");
            this.results = results;
            this.frequencyHz = frequencyHz;
            this.elapsedMilliseconds = elapsedMilliseconds;
            this.exportCsv = exportCsv;
            this.continuous = continuous;
            this.stopContinuous = stopContinuous;
            this.sweepNumber = Math.Max(1, sweepNumber);

            Text = "WILD64 EEG impedance report";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(940, 760);
            MinimumSize = new Size(820, 680);
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;

            Panel header = BuildHeader();
            header.Dock = DockStyle.Top;
            header.Height = 72;

            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 5) };
            mapTab = BuildMapTab();
            tabs.TabPages.Add(mapTab);
            tabs.TabPages.Add(BuildDetailsTab());

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 6),
                WrapContents = false
            };
            Button close = new Button { Text = "Close", DialogResult = DialogResult.OK, Size = new Size(92, 30) };
            Button saveImage = new Button { Text = "Save map PNG...", Size = new Size(130, 30) };
            Button export = new Button { Text = "Export CSV...", Size = new Size(112, 30) };
            Button refresh = new Button
            {
                Text = continuous ? "Auto refresh" : "Refresh measurement",
                DialogResult = continuous ? DialogResult.None : DialogResult.Retry,
                Enabled = !continuous,
                Size = new Size(142, 30)
            };
            saveImage.Click += delegate { SaveMapPng(); };
            export.Click += delegate { if (this.exportCsv != null) this.exportCsv(); };
            continuousCheckBox = new CheckBox
            {
                Text = "Continuous",
                Checked = continuous,
                AutoSize = false,
                Size = new Size(108, 30),
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleDescription = "Repeat complete impedance sweeps after Refresh until this box is cleared"
            };
            continuousCheckBox.CheckedChanged += delegate
            {
                if (continuous && !continuousCheckBox.Checked) RequestContinuousStop();
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(saveImage);
            buttons.Controls.Add(export);
            buttons.Controls.Add(refresh);
            buttons.Controls.Add(continuousCheckBox);
            if (!continuous)
            {
                eisButton = new Button { Text = "Select electrode for EIS", Enabled = false, Size = new Size(166, 30) };
                eisButton.Visible = false;
                eisButton.DialogResult = DialogResult.Yes;
                buttons.Controls.Add(eisButton);
            }
            Controls.Add(tabs);
            Controls.Add(header);
            Controls.Add(buttons);
            AcceptButton = close;
            FormClosing += delegate
            {
                if (continuous && !stopRequested) RequestContinuousStop();
            };
        }

        private Panel BuildHeader()
        {
            Panel panel = new Panel { Padding = new Padding(12, 8, 12, 4) };
            Label title = new Label
            {
                Text = "64-electrode impedance map",
                Font = new Font(Font.FontFamily, 14.0f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(12, 7)
            };
            summaryLabel = new Label { Text = BuildSummary(), AutoSize = true, Location = new Point(14, 36) };
            Label warning = new Label
            {
                Text = "Nominal measurement only - not calibrated absolute impedance.",
                ForeColor = Color.DarkRed,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(555, 12)
            };
            panel.Controls.Add(title);
            panel.Controls.Add(summaryLabel);
            panel.Controls.Add(warning);
            panel.Resize += delegate { warning.Left = Math.Max(12, panel.ClientSize.Width - warning.Width - 12); };
            return panel;
        }

        private string BuildSummary()
        {
            List<uint> valid = new List<uint>();
            int good = 0, okay = 0, bad = 0;
            foreach (Ce64MscZtestResult result in results)
            {
                if ((result.Flags & Ce64MscProtocol.ZtestResultFlagValid) == 0U) continue;
                valid.Add(result.Ohms);
                if (result.Ohms < 50000U) good++;
                else if (result.Ohms <= 2000000U) okay++;
                else bad++;
            }
            valid.Sort();
            if (valid.Count == 0)
                return (continuous ? "sweep " + sweepNumber + "  |  " : "") + frequencyHz + " Hz  |  " + elapsedMilliseconds + " ms  |  no valid electrodes";
            uint median = valid[valid.Count / 2];
            return (continuous ? "sweep " + sweepNumber + "  |  " : "") + frequencyHz + " Hz  |  " + elapsedMilliseconds + " ms  |  good " + good + "  |  OK " + okay +
                "  |  bad " + bad + "  |  median " + FormatImpedance(median);
        }

        internal void UpdateMeasurement(IList<Ce64MscZtestResult> newResults, uint newFrequencyHz,
            uint newElapsedMilliseconds, Action newExportCsv, int newSweepNumber)
        {
            if (newResults == null) throw new ArgumentNullException("newResults");
            if (InvokeRequired)
            {
                BeginInvoke(new Action<IList<Ce64MscZtestResult>, uint, uint, Action, int>(UpdateMeasurement),
                    newResults, newFrequencyHz, newElapsedMilliseconds, newExportCsv, newSweepNumber);
                return;
            }
            results = newResults;
            frequencyHz = newFrequencyHz;
            elapsedMilliseconds = newElapsedMilliseconds;
            exportCsv = newExportCsv;
            sweepNumber = Math.Max(1, newSweepNumber);
            summaryLabel.Text = BuildSummary();
            int selected = tabs.SelectedIndex;
            while (tabs.TabPages.Count != 0)
            {
                TabPage old = tabs.TabPages[0];
                tabs.TabPages.RemoveAt(0);
                old.Dispose();
            }
            mapTab = BuildMapTab();
            tabs.TabPages.Add(mapTab);
            tabs.TabPages.Add(BuildDetailsTab());
            tabs.SelectedIndex = Math.Max(0, Math.Min(selected, tabs.TabPages.Count - 1));
        }

        internal void MarkContinuousStopped(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(MarkContinuousStopped), message);
                return;
            }
            stopRequested = true;
            if (continuousCheckBox != null)
            {
                continuousCheckBox.Checked = false;
                continuousCheckBox.Enabled = false;
            }
            if (!String.IsNullOrWhiteSpace(message)) summaryLabel.Text = BuildSummary() + "  |  " + message;
        }

        private void RequestContinuousStop()
        {
            if (stopRequested) return;
            stopRequested = true;
            if (continuousCheckBox != null)
            {
                continuousCheckBox.Checked = false;
                continuousCheckBox.Enabled = false;
            }
            if (stopContinuous != null) stopContinuous();
        }

        private TabPage BuildMapTab()
        {
            electrodeLabels.Clear();
            TabPage tab = new TabPage("Electrode map") { BackColor = Color.White, Padding = new Padding(10) };
            ToolTip toolTip = new ToolTip { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 10000 };
            TableLayoutPanel body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.White
            };
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 50.0f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 50.0f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 66.0f));
            body.Controls.Add(BuildBankGroup(0, toolTip), 0, 0);
            body.Controls.Add(BuildBankGroup(1, toolTip), 0, 1);
            body.Controls.Add(BuildLegend(), 0, 2);
            tab.Controls.Add(body);
            return tab;
        }

        private GroupBox BuildBankGroup(int bank, ToolTip toolTip)
        {
            int firstChannel = bank * 32;
            GroupBox group = new GroupBox
            {
                Text = "Bank " + bank + " - electrodes E" + firstChannel.ToString("00") + " to E" +
                    (firstChannel + 31).ToString("00"),
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 18, 8, 7),
                Margin = new Padding(3, 3, 3, 6)
            };
            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 4,
                BackColor = Color.FromArgb(235, 238, 240),
                Padding = new Padding(2)
            };
            for (int column = 0; column < 8; column++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));
            for (int row = 0; row < 4; row++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25.0f));

            for (int index = 0; index < 32; index++)
            {
                int channel = firstChannel + index;
                Ce64MscZtestResult result = FindChannel(channel);
                bool valid = result != null && (result.Flags & Ce64MscProtocol.ZtestResultFlagValid) != 0U;
                Color backColor = valid ? GetImpedanceColor(result.Ohms) : Color.FromArgb(117, 117, 117);
                Label electrode = new Label
                {
                    Text = valid
                        ? "E" + channel.ToString("00") + "  " + GetQualityText(result.Ohms) + Environment.NewLine + FormatResultImpedance(result)
                        : "E" + channel.ToString("00") + Environment.NewLine + "INVALID",
                    Dock = DockStyle.Fill,
                    Margin = new Padding(3),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font(Font.FontFamily, 8.0f, FontStyle.Bold),
                    BackColor = backColor,
                    ForeColor = GetContrastingTextColor(backColor),
                    BorderStyle = BorderStyle.FixedSingle,
                    AccessibleName = "Electrode E" + channel.ToString("00"),
                    AccessibleDescription = valid ? FormatResultImpedance(result) : "Invalid result"
                };
                electrode.Tag = channel;
                if (!continuous)
                {
                    electrode.Cursor = Cursors.Hand;
                    electrode.Click += delegate { SelectEisElectrode((int)electrode.Tag); };
                }
                electrodeLabels[channel] = electrode;
                if (valid)
                    toolTip.SetToolTip(electrode, "E" + channel.ToString("00") + " | Bank " + bank + " | " +
                        result.Ohms.ToString("N0", CultureInfo.InvariantCulture) + " ohm | Phase " +
                        (result.PhaseTenths / 10.0).ToString("F1", CultureInfo.InvariantCulture) +
                        " deg | Flags 0x" + result.Flags.ToString("X4") +
                        (((result.Flags & Ce64MscProtocol.ZtestResultFlagLowerBound) != 0U) ? " | Lower-bound estimate (weakest range near limit)" : "") +
                        (!continuous ? " | Click to select for EIS" : ""));
                else
                    toolTip.SetToolTip(electrode, "E" + channel.ToString("00") + " | Invalid impedance result");
                grid.Controls.Add(electrode, index % 8, index / 8);
            }
            group.Controls.Add(grid);
            return group;
        }

        private void SelectEisElectrode(int channel)
        {
            if (!eisAvailable) return;
            Label previous;
            if (electrodeLabels.TryGetValue(selectedEisChannel, out previous)) previous.BorderStyle = BorderStyle.FixedSingle;
            selectedEisChannel = channel;
            Label selected;
            if (electrodeLabels.TryGetValue(channel, out selected)) selected.BorderStyle = BorderStyle.Fixed3D;
            if (eisButton != null)
            {
                eisButton.Enabled = true;
                eisButton.Text = "EIS spectrum: E" + channel.ToString("00");
            }
        }

        internal void SetEisAvailable(bool available)
        {
            eisAvailable = available;
            if (eisButton != null) eisButton.Visible = available;
            foreach (Label label in electrodeLabels.Values)
                label.Cursor = available ? Cursors.Hand : Cursors.Default;
        }

        private Control BuildLegend()
        {
            Panel legend = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(8, 7, 8, 0)
            };
            legend.Paint += delegate(object sender, PaintEventArgs e)
            {
                Rectangle gradient = new Rectangle(2, 3, Math.Max(1, legend.ClientSize.Width - 4), 22);
                for (int x = 0; x < gradient.Width; x++)
                {
                    double fraction = gradient.Width <= 1 ? 0.0 : (double)x / (gradient.Width - 1);
                    using (Pen pen = new Pen(GetViridisColor(1.0 - fraction)))
                        e.Graphics.DrawLine(pen, gradient.Left + x, gradient.Top, gradient.Left + x, gradient.Bottom);
                }
                e.Graphics.DrawRectangle(Pens.DimGray, gradient);
                using (Font tickFont = new Font(Font.FontFamily, 7.5f, FontStyle.Regular))
                using (Brush textBrush = new SolidBrush(Color.FromArgb(45, 45, 45)))
                {
                    DrawLegendText(e.Graphics, tickFont, textBrush, "10 kOhm", gradient.Left, gradient.Right, 0.0, 30.0f, StringAlignment.Near);
                    DrawLegendText(e.Graphics, tickFont, textBrush, "50 kOhm | GOOD below", gradient.Left, gradient.Right, ImpedanceFraction(50000U), 30.0f, StringAlignment.Center);
                    DrawLegendText(e.Graphics, tickFont, textBrush, "500 kOhm", gradient.Left, gradient.Right, ImpedanceFraction(500000U), 30.0f, StringAlignment.Center);
                    DrawLegendText(e.Graphics, tickFont, textBrush, "1 MOhm | OK", gradient.Left, gradient.Right, ImpedanceFraction(1000000U), 30.0f, StringAlignment.Center);
                    DrawLegendText(e.Graphics, tickFont, textBrush, "2 MOhm+ | BAD above", gradient.Left, gradient.Right, 1.0, 45.0f, StringAlignment.Far);
                }
            };
            return legend;
        }

        private static void DrawLegendText(Graphics graphics, Font font, Brush brush, string text,
            int left, int right, double fraction, float y, StringAlignment alignment)
        {
            float x = left + (float)(fraction * (right - left));
            using (StringFormat format = new StringFormat { Alignment = alignment, LineAlignment = StringAlignment.Near })
                graphics.DrawString(text, font, brush, new PointF(x, y), format);
        }

        private TabPage BuildDetailsTab()
        {
            TabPage tab = new TabPage("Detailed results") { Padding = new Padding(8) };
            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            grid.Columns.Add("channel", "Electrode ID");
            grid.Columns.Add("bank", "Bank");
            grid.Columns.Add("display", "Impedance");
            grid.Columns.Add("ohms", "Magnitude (ohm)");
            grid.Columns.Add("phase", "Phase (deg)");
            grid.Columns.Add("flags", "Flags");
            foreach (Ce64MscZtestResult result in results)
            {
                int row = grid.Rows.Add("E" + result.Channel.ToString("00"), result.Channel < 32 ? "0" : "1",
                    FormatResultImpedance(result), result.Ohms.ToString("N0", CultureInfo.InvariantCulture),
                    (result.PhaseTenths / 10.0).ToString("F1", CultureInfo.InvariantCulture),
                    "0x" + result.Flags.ToString("X4"));
                Color color = (result.Flags & Ce64MscProtocol.ZtestResultFlagValid) != 0U ? GetImpedanceColor(result.Ohms) : Color.FromArgb(117, 117, 117);
                grid.Rows[row].Cells[2].Style.BackColor = color;
                grid.Rows[row].Cells[2].Style.ForeColor = GetContrastingTextColor(color);
                grid.Rows[row].Cells[2].Style.Font = new Font(grid.Font, FontStyle.Bold);
            }
            tab.Controls.Add(grid);
            return tab;
        }

        private Ce64MscZtestResult FindChannel(int channel)
        {
            foreach (Ce64MscZtestResult result in results)
                if (result.Channel == channel) return result;
            return null;
        }

        private static string FormatImpedance(uint ohms)
        {
            if (ohms < 1000U) return ohms.ToString(CultureInfo.InvariantCulture) + " Ohm";
            if (ohms < 1000000U) return (ohms / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " kOhm";
            return (ohms / 1000000.0).ToString("F2", CultureInfo.InvariantCulture) + " MOhm";
        }

        private static string FormatResultImpedance(Ce64MscZtestResult result)
        {
            string value = FormatImpedance(result.Ohms);
            return (result.Flags & Ce64MscProtocol.ZtestResultFlagLowerBound) != 0U ? ">" + value : value;
        }

        private static Color GetImpedanceColor(uint ohms)
        {
            return GetViridisColor(1.0 - ImpedanceFraction(ohms));
        }

        private static string GetQualityText(uint ohms)
        {
            if (ohms < 50000U) return "GOOD";
            if (ohms <= 2000000U) return "OK";
            return "BAD";
        }

        private static double ImpedanceFraction(uint ohms)
        {
            const double minimum = 10000.0;
            const double maximum = 2000000.0;
            double clamped = Math.Max(minimum, Math.Min(maximum, ohms));
            return (Math.Log10(clamped) - Math.Log10(minimum)) / (Math.Log10(maximum) - Math.Log10(minimum));
        }

        private static Color GetViridisColor(double fraction)
        {
            double[] stops = { 0.0, 0.25, 0.5, 0.75, 1.0 };
            Color[] colors =
            {
                Color.FromArgb(68, 1, 84), Color.FromArgb(59, 82, 139), Color.FromArgb(33, 145, 140),
                Color.FromArgb(94, 201, 98), Color.FromArgb(253, 231, 37)
            };
            fraction = Math.Max(0.0, Math.Min(1.0, fraction));
            int segment = 0;
            while (segment < stops.Length - 2 && fraction > stops[segment + 1]) segment++;
            double amount = (fraction - stops[segment]) / (stops[segment + 1] - stops[segment]);
            Color first = colors[segment], second = colors[segment + 1];
            return Color.FromArgb(
                (int)Math.Round(first.R + (second.R - first.R) * amount),
                (int)Math.Round(first.G + (second.G - first.G) * amount),
                (int)Math.Round(first.B + (second.B - first.B) * amount));
        }

        private static Color GetContrastingTextColor(Color color)
        {
            double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
            return luminance >= 150.0 ? Color.Black : Color.White;
        }

        private void SaveMapPng()
        {
            using (SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "PNG image (*.png)|*.png",
                FileName = "WILD64_impedance_map_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                using (Bitmap bitmap = new Bitmap(mapTab.ClientSize.Width, mapTab.ClientSize.Height))
                {
                    mapTab.DrawToBitmap(bitmap, mapTab.ClientRectangle);
                    bitmap.Save(dialog.FileName, ImageFormat.Png);
                }
            }
        }
    }
}
