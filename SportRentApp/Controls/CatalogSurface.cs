using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SportRentApp
{
    public sealed class CatalogSurface : Grid
    {
        private readonly Path fill = new Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(94, 73, 73)),
            IsHitTestVisible = false
        };
        private readonly Grid shadow = new Grid
        {
            IsHitTestVisible = false
        };
        private readonly Path edge = new Path
        {
            Stroke = Brushes.Black,
            StrokeThickness = 8,
            Opacity = .22,
            Effect = new BlurEffect
            {
                Radius = 8
            }
        };
        public static readonly DependencyProperty SelectedTabProperty = DependencyProperty.Register("SelectedTab", typeof(int), typeof(CatalogSurface), new PropertyMetadata(0, (d, e) => ((CatalogSurface)d).MoveTab((int)e.NewValue)));
        private static readonly DependencyProperty TabPositionProperty = DependencyProperty.Register("TabPosition", typeof(double), typeof(CatalogSurface), new PropertyMetadata(0.0, (d, e) => ((CatalogSurface)d).UpdateShape()));
        private void MoveTab(int tab)
        {
            if (!IsLoaded)
            {
                SetValue(TabPositionProperty, (double)tab);
                return;
            }

            BeginAnimation(TabPositionProperty, new DoubleAnimation(tab, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        public int SelectedTab { get => (int)GetValue(SelectedTabProperty); set => SetValue(SelectedTabProperty, value); }

        public static readonly DependencyProperty ShowTabsProperty = DependencyProperty.Register("ShowTabs", typeof(bool), typeof(CatalogSurface), new PropertyMetadata(true, (d, e) => ((CatalogSurface)d).UpdateShape()));
        public bool ShowTabs { get => (bool)GetValue(ShowTabsProperty); set => SetValue(ShowTabsProperty, value); }

        public static readonly DependencyProperty TabCountProperty = DependencyProperty.Register("TabCount", typeof(int), typeof(CatalogSurface), new PropertyMetadata(6, (d, e) => ((CatalogSurface)d).UpdateShape()));
        public int TabCount { get => (int)GetValue(TabCountProperty); set => SetValue(TabCountProperty, value); }

        public CatalogSurface()
        {
            Children.Add(fill);
            shadow.Children.Add(edge);
            Children.Add(shadow);
            SizeChanged += (s, e) => UpdateShape();
        }

        private void UpdateShape()
        {
            if (ActualWidth < 180 || ActualHeight < 200)
                return;
            if (!ShowTabs)
            {
                var simple = new RectangleGeometry(new Rect(18, 18, ActualWidth - 36, ActualHeight - 36), 24, 24);
                simple.Freeze();
                fill.Data = simple;
                edge.Data = simple;
                shadow.Clip = simple;
                return;
            }

            double right = ActualWidth - 60, outer = ActualWidth - 18, bottom = ActualHeight - 18;
            double height = (ActualHeight - 36) / Math.Max(1, TabCount);
            double a = 18 + (double)GetValue(TabPositionProperty) * height, b = Math.Min(a + height, bottom);
            var pts = new List<Point>
            { new Point(18, 18) };
            if (a > 18)
            {
                pts.Add(new Point(right, 18));
                pts.Add(new Point(right, a));
            }

            pts.Add(new Point(outer, a));
            pts.Add(new Point(outer, b));
            if (b < bottom)
            {
                pts.Add(new Point(right, b));
                pts.Add(new Point(right, bottom));
            }

            pts.Add(new Point(18, bottom));
            var geometry = Round(pts, 24);
            fill.Data = geometry;
            edge.Data = geometry;
            shadow.Clip = geometry;
        }

        private static Geometry Round(IList<Point> points, double radius)
        {
            var entry = new Point[points.Count];
            var exit = new Point[points.Count];
            var radii = new double[points.Count];
            var directions = new SweepDirection[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                var previous = points[(i + points.Count - 1) % points.Count];
                var current = points[i];
                var next = points[(i + 1) % points.Count];
                Vector p = previous - current, n = next - current;
                double r = Math.Min(radius, Math.Min(p.Length, n.Length) / 2);
                p.Normalize();
                n.Normalize();
                entry[i] = current + p * r;
                exit[i] = current + n * r;
                radii[i] = r;
                Vector incoming = current - previous, outgoing = next - current;
                directions[i] = incoming.X * outgoing.Y - incoming.Y * outgoing.X >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(exit[0], true, true);
                for (int step = 1; step <= points.Count; step++)
                {
                    int i = step % points.Count;
                    ctx.LineTo(entry[i], true, false);
                    ctx.ArcTo(exit[i], new Size(radii[i], radii[i]), 0, false, directions[i], true, true);
                }
            }

            geometry.Freeze();
            return geometry;
        }
    }
}
