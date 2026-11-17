using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SportRentApp
{
    public sealed class BrandLogo : Viewbox
    {
        public BrandLogo()
        {
            IsHitTestVisible = false;
            Stretch = Stretch.Uniform;
            var canvas = new Canvas
            {
                Width = 300,
                Height = 145
            };
            AddTiles(canvas, new[] { 35, 57, 79, 101, 123, 145 }, new[] { 90, 112, 134 }, "#4D3B3B");
            AddTiles(canvas, new[] { 189, 211, 233, 255, 277, 299 }, new[] { 31, 53, 75 }, "#4D3B3B");
            AddTiles(canvas, new[] { 35, 57, 79, 101, 123, 145, 167 }, new[] { 31, 53 }, "#574444");
            AddTiles(canvas, new[] { 167, 189, 211, 233, 255, 277, 299 }, new[] { 113, 135 }, "#574444");
            AddWord(canvas, "Sport", 0, 0);
            AddWord(canvas, "Drive", 130, 62);
            Child = canvas;
        }

        private static void AddTiles(Canvas canvas, int[] columns, int[] rows, string color)
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            foreach (int y in rows)
                foreach (int x in columns)
                {
                    var tile = new Rectangle
                    {
                        Width = 15,
                        Height = 15,
                        RadiusX = 4,
                        RadiusY = 4,
                        Fill = brush
                    };
                    Canvas.SetLeft(tile, x - 35);
                    Canvas.SetTop(tile, y - 10);
                    canvas.Children.Add(tile);
                }
        }

        private static void AddWord(Canvas canvas, string word, double x, double y)
        {
            var text = new TextBlock
            {
                Text = word,
                FontSize = 64,
                Foreground = new SolidColorBrush(Color.FromRgb(211, 211, 211)),
                FontFamily = new FontFamily("pack://application:,,,/SportRentApp;component/Resources/Nunito/#Nunito")
            };
            Canvas.SetLeft(text, x);
            Canvas.SetTop(text, y);
            canvas.Children.Add(text);
        }
    }
}
