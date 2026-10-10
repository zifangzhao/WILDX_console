using System.Drawing;
using System.Windows.Forms;

namespace USB512_interface
{
    // One palette for the workspace and its native WinForms dialogs.
    internal static class WildXTheme
    {
        internal static readonly Color Canvas = Color.FromArgb(244,246,248);
        internal static readonly Color Surface = Color.White;
        internal static readonly Color SurfaceSoft = Color.FromArgb(247,249,250);
        internal static readonly Color Ink = Color.FromArgb(37,52,68);
        internal static readonly Color Muted = Color.FromArgb(96,113,132);
        internal static readonly Color Border = Color.FromArgb(214,223,229);
        internal static readonly Color Accent = Color.FromArgb(8,126,117);
        internal static readonly Color AccentHover = Color.FromArgb(7,105,95);
        internal static readonly Color AccentPressed = Color.FromArgb(5,85,77);
        internal static readonly Color AccentLight = Color.FromArgb(229,242,239);
        internal static readonly Color Disabled = Color.FromArgb(132,145,157);
        internal static readonly Color Danger = Color.FromArgb(173,69,75);
        internal static readonly Color DangerLight = Color.FromArgb(250,238,238);
        internal static readonly Color Dark = Color.FromArgb(23,43,54);
        internal static readonly Font Body = new Font("Segoe UI",9F);
        internal static readonly Font Strong = new Font("Segoe UI",9F,FontStyle.Bold);
        internal static readonly Font Title = new Font("Segoe UI",22F,FontStyle.Bold);

        internal static Label Label(string text, bool strong=false)
        {
            return new Label { Text=text, AutoSize=true, UseMnemonic=false, Font=strong?Strong:Body,
                ForeColor=strong?Ink:Muted, Margin=new Padding(0,0,0,8) };
        }
        internal static void Button(Button button,bool primary=false,bool danger=false)
        {
            button.Font=Strong; button.FlatStyle=FlatStyle.Flat; button.UseVisualStyleBackColor=false;
            button.BackColor=primary?Accent:Surface;
            button.ForeColor=primary?Surface:danger?Danger:Ink;
            button.FlatAppearance.BorderSize=primary?0:1; button.FlatAppearance.BorderColor=Border;
            button.FlatAppearance.MouseOverBackColor=primary?AccentHover:danger?DangerLight:AccentLight;
            button.FlatAppearance.MouseDownBackColor=primary?AccentPressed:danger?DangerLight:Border;
            button.Cursor=Cursors.Hand; button.MinimumSize=new Size(0,34);
            button.Padding=new Padding(8,2,8,2); button.Margin=new Padding(0,0,8,8);
            button.Paint-=PaintButton;button.Paint+=PaintButton;
        }
        static void PaintButton(object sender,PaintEventArgs e)
        {
            var button=(Button)sender;
            bool hover=button.Enabled && button.ClientRectangle.Contains(button.PointToClient(Control.MousePosition));
            bool pressed=hover && button.Capture && (Control.MouseButtons & MouseButtons.Left)!=0;
            var background=!button.Enabled?SurfaceSoft:pressed?button.FlatAppearance.MouseDownBackColor:hover?button.FlatAppearance.MouseOverBackColor:button.BackColor;
            e.Graphics.Clear(background);
            if(button.FlatAppearance.BorderSize>0)using(var pen=new Pen(button.FlatAppearance.BorderColor))e.Graphics.DrawRectangle(pen,0,0,button.Width-1,button.Height-1);
            TextRenderer.DrawText(e.Graphics,button.Text,button.Font,Rectangle.Inflate(button.ClientRectangle,-5,-2),button.Enabled?button.ForeColor:Disabled,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            if(button.Focused)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(button.ClientRectangle,-4,-4),button.Enabled?button.ForeColor:Disabled,background);
        }
        internal static void Apply(Control root)
        {
            if(root.Font.FontFamily.Name!=FontFamily.GenericMonospace.Name)root.Font=Body;
            if(root.ForeColor==SystemColors.ControlText || root.ForeColor==Color.Black)root.ForeColor=Ink;
            if(root is Form || ((root is Panel || root is GroupBox || root is TrackBar) && root.BackColor==SystemColors.Control))root.BackColor=Surface;
            var button=root as Button; if(button!=null){Button(button);return;}
            var combo=root as ComboBox; if(combo!=null){combo.FlatStyle=FlatStyle.Flat;combo.BackColor=SurfaceSoft;return;}
            var text=root as TextBox; if(text!=null){text.BorderStyle=text.ReadOnly && text.Multiline?BorderStyle.None:BorderStyle.FixedSingle;text.BackColor=text.ReadOnly?SurfaceSoft:Surface;return;}
            var list=root as ListBox; if(list!=null){list.BorderStyle=BorderStyle.None;list.BackColor=SurfaceSoft;return;}
            var number=root as NumericUpDown; if(number!=null){number.BorderStyle=BorderStyle.FixedSingle;number.BackColor=Surface;return;}
            var tree=root as TreeView; if(tree!=null){tree.BorderStyle=BorderStyle.None;tree.BackColor=SurfaceSoft;return;}
            var strip=root as ToolStrip; if(strip!=null){strip.BackColor=SurfaceSoft;strip.ForeColor=Ink;}
            var check=root as CheckBox; if(check!=null){check.FlatStyle=FlatStyle.Flat;check.FlatAppearance.BorderColor=Border;check.FlatAppearance.CheckedBackColor=AccentLight;check.FlatAppearance.MouseOverBackColor=AccentLight;return;}
            var link=root as LinkLabel; if(link!=null){link.LinkColor=link.VisitedLinkColor=Accent;link.ActiveLinkColor=AccentPressed;link.DisabledLinkColor=Disabled;}
            foreach(Control child in root.Controls)Apply(child);
        }
    }

    internal sealed class WildXCard : Panel
    {
        internal WildXCard(){DoubleBuffered=true;BackColor=WildXTheme.Surface;Padding=new Padding(18);Margin=new Padding(0,0,12,12);}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using(var pen=new Pen(WildXTheme.Border))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
        }
    }
}
