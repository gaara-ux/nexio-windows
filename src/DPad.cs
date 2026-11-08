using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Telecommande
{
    /// <summary>Round D-pad: four arcs around an OK button. Hover highlight, hold to repeat.</summary>
    class DPad : FrameworkElement
    {
        public event Action<int> Key;
        int hover, pressed;
        readonly DispatcherTimer repeat = new DispatcherTimer();

        public DPad()
        {
            Cursor = Cursors.Hand;
            repeat.Tick += (s, e) =>
            {
                repeat.Interval = TimeSpan.FromMilliseconds(110);
                if (pressed != 0 && pressed != AtvRemote.CENTER) Fire(pressed);
            };
        }

        double R { get { return Math.Min(ActualWidth, ActualHeight) / 2 - 2; } }
        double Inner { get { return R * 0.40; } }
        Point C { get { return new Point(ActualWidth / 2, ActualHeight / 2); } }

        int Zone(Point p)
        {
            double dx = p.X - C.X, dy = p.Y - C.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > R) return 0;
            if (d < Inner) return AtvRemote.CENTER;
            if (Math.Abs(dx) > Math.Abs(dy)) return dx > 0 ? AtvRemote.RIGHT : AtvRemote.LEFT;
            return dy > 0 ? AtvRemote.DOWN : AtvRemote.UP;
        }

        public void Flash(int key)
        {
            pressed = key;
            InvalidateVisual();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            t.Tick += (s, e) => { t.Stop(); if (!IsMouseCaptured) { pressed = 0; InvalidateVisual(); } };
            t.Start();
        }

        void Fire(int k) { var h = Key; if (h != null) h(k); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int z = Zone(e.GetPosition(this));
            if (z != hover) { hover = z; InvalidateVisual(); }
        }

        protected override void OnMouseLeave(MouseEventArgs e) { hover = 0; InvalidateVisual(); }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            int z = Zone(e.GetPosition(this));
            if (z == 0) return;
            pressed = z;
            CaptureMouse();
            Fire(z);
            if (z != AtvRemote.CENTER) { repeat.Interval = TimeSpan.FromMilliseconds(400); repeat.Start(); }
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            repeat.Stop();
            pressed = 0;
            ReleaseMouseCapture();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double r = R, ri = Inner;
            if (r <= 0) return;
            Point c = C;
            dc.DrawEllipse(Theme.B(Theme.Ring), null, c, r, r);
            int lit = pressed != 0 ? pressed : hover;
            if (lit != 0 && lit != AtvRemote.CENTER) dc.DrawGeometry(Theme.B(pressed != 0 ? Theme.Accent : Theme.Pressed), null, Arc(lit, c, r));
            dc.DrawEllipse(Theme.B(Theme.Bg), null, c, ri + r * 0.04, ri + r * 0.04);
            Color mid = lit == AtvRemote.CENTER ? (pressed != 0 ? Theme.Accent : Theme.Pressed) : Theme.Surface;
            dc.DrawEllipse(Theme.B(mid), null, c, ri, ri);
            var ok = new FormattedText("OK", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), ri * 0.42, Theme.B(Theme.Text), 1.0);
            dc.DrawText(ok, new Point(c.X - ok.Width / 2, c.Y - ok.Height / 2));

            var pen = new Pen(Theme.B(Theme.Text), r * 0.025) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            double mr = (r + ri) / 2, s = r * 0.07;
            Chevron(dc, pen, new Point(c.X, c.Y - mr), s, 0);
            Chevron(dc, pen, new Point(c.X + mr, c.Y), s, 90);
            Chevron(dc, pen, new Point(c.X, c.Y + mr), s, 180);
            Chevron(dc, pen, new Point(c.X - mr, c.Y), s, 270);
        }

        static Geometry Arc(int key, Point c, double r)
        {
            double start = key == AtvRemote.RIGHT ? -45 : key == AtvRemote.DOWN ? 45 : key == AtvRemote.LEFT ? 135 : 225;
            Func<double, Point> at = deg => new Point(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
            var fig = new PathFigure { StartPoint = c, IsClosed = true };
            fig.Segments.Add(new LineSegment(at(start), false));
            fig.Segments.Add(new ArcSegment(at(start + 90), new Size(r, r), 0, false, SweepDirection.Clockwise, false));
            return new PathGeometry(new[] { fig });
        }

        static void Chevron(DrawingContext dc, Pen pen, Point p, double s, double angle)
        {
            dc.PushTransform(new RotateTransform(angle, p.X, p.Y));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(p.X - s, p.Y + s * 0.5), false, false);
                ctx.LineTo(new Point(p.X, p.Y - s * 0.5), true, true);
                ctx.LineTo(new Point(p.X + s, p.Y + s * 0.5), true, true);
            }
            dc.DrawGeometry(null, pen, g);
            dc.Pop();
        }
    }
}
