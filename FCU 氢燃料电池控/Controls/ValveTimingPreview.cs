using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FCU_氢燃料电池控.Controls
{
    /// <summary>
    /// 仅用于上位机参数预览的双轨阀门时序图。
    /// 图形不表示设备实时执行进度，也不向下位机发送任何数据。
    /// </summary>
    public sealed class ValveTimingPreview : FrameworkElement
    {
        private static readonly Brush HydrogenBrush = CreateFrozenBrush("#1976D2");
        private static readonly Brush DrainBrush = CreateFrozenBrush("#2E7D32");
        private static readonly Brush GridBrush = CreateFrozenBrush("#DCE3EA");
        private static readonly Brush TextBrush = CreateFrozenBrush("#455A64");
        private static readonly Brush MutedTextBrush = CreateFrozenBrush("#78909C");
        private static readonly Brush ErrorBrush = CreateFrozenBrush("#C62828");
        private static readonly Brush BackgroundBrush = CreateFrozenBrush("#FBFCFE");
        private static readonly Pen BorderPen = CreateFrozenPen("#CFD8DC", 1);

        private ValveTrack _hydrogenTrack = ValveTrack.Empty;
        private ValveTrack _drainTrack = ValveTrack.Empty;
        private double _totalSeconds = 1;
        private string? _errorMessage;

        public void SetTimeline(double totalSeconds, ValveTrack hydrogenTrack, ValveTrack drainTrack)
        {
            _totalSeconds = Math.Max(totalSeconds, 0.01);
            _hydrogenTrack = hydrogenTrack;
            _drainTrack = drainTrack;
            _errorMessage = null;
            InvalidateVisual();
        }

        public void SetError(string message)
        {
            _errorMessage = message;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double width = Math.Max(ActualWidth, 1);
            double height = Math.Max(ActualHeight, 1);
            Rect bounds = new(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1));
            drawingContext.DrawRoundedRectangle(BackgroundBrush, BorderPen, bounds, 6, 6);

            if (!string.IsNullOrWhiteSpace(_errorMessage))
            {
                DrawCenteredText(drawingContext, _errorMessage, ErrorBrush, 12, width, height);
                return;
            }

            const double labelWidth = 58;
            const double rightPadding = 14;
            const double topPadding = 15;
            const double trackGap = 62;
            const double amplitude = 13;
            double plotWidth = Math.Max(20, width - labelWidth - rightPadding);
            double hydrogenCenter = topPadding + 30;
            double drainCenter = hydrogenCenter + trackGap;
            double axisY = Math.Min(height - 22, drainCenter + 32);

            DrawTrackGrid(drawingContext, labelWidth, plotWidth, hydrogenCenter, amplitude);
            DrawTrackGrid(drawingContext, labelWidth, plotWidth, drainCenter, amplitude);
            DrawText(drawingContext, "氢阀", TextBrush, 12, 10, hydrogenCenter - 8);
            DrawText(drawingContext, "排水阀", TextBrush, 12, 4, drainCenter - 8);

            DrawTrack(drawingContext, _hydrogenTrack, HydrogenBrush, labelWidth, plotWidth,
                hydrogenCenter, amplitude);
            DrawTrack(drawingContext, _drainTrack, DrainBrush, labelWidth, plotWidth,
                drainCenter, amplitude);

            DrawTimeAxis(drawingContext, labelWidth, plotWidth, axisY);
            DrawText(drawingContext, "高=开  低=关", MutedTextBrush, 10, labelWidth, 3);
        }

        private void DrawTrackGrid(DrawingContext drawingContext, double left, double width,
            double centerY, double amplitude)
        {
            Pen gridPen = new(GridBrush, 1) { DashStyle = DashStyles.Dash };
            drawingContext.DrawLine(gridPen, new Point(left, centerY - amplitude),
                new Point(left + width, centerY - amplitude));
            drawingContext.DrawLine(gridPen, new Point(left, centerY + amplitude),
                new Point(left + width, centerY + amplitude));
        }

        private void DrawTrack(DrawingContext drawingContext, ValveTrack track, Brush brush,
            double left, double plotWidth, double centerY, double amplitude)
        {
            Pen pen = new(brush, 2.2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            bool isOpen = track.InitiallyOpen;
            double currentX = left;
            double currentY = isOpen ? centerY - amplitude : centerY + amplitude;

            foreach (var transition in track.Transitions
                .Select((time, index) => new
                {
                    Time = time,
                    Label = index < track.TransitionLabels.Count
                        ? track.TransitionLabels[index]
                        : string.Empty
                })
                .Where(item => item.Time >= 0 && item.Time <= _totalSeconds)
                .OrderBy(item => item.Time))
            {
                double transitionX = left + (transition.Time / _totalSeconds * plotWidth);
                drawingContext.DrawLine(pen, new Point(currentX, currentY),
                    new Point(transitionX, currentY));

                isOpen = !isOpen;
                double nextY = isOpen ? centerY - amplitude : centerY + amplitude;
                drawingContext.DrawLine(pen, new Point(transitionX, currentY),
                    new Point(transitionX, nextY));

                if (!string.IsNullOrWhiteSpace(transition.Label))
                {
                    FormattedText label = CreateText(transition.Label, brush, 8);
                    double labelX = Math.Clamp(transitionX - label.Width / 2,
                        left, left + plotWidth - label.Width);
                    double labelY = centerY < 80
                        ? centerY - amplitude - label.Height - 2
                        : centerY + amplitude + 2;
                    drawingContext.DrawText(label, new Point(labelX, labelY));
                }

                currentX = transitionX;
                currentY = nextY;
            }

            drawingContext.DrawLine(pen, new Point(currentX, currentY),
                new Point(left + plotWidth, currentY));
        }

        private void DrawTimeAxis(DrawingContext drawingContext, double left, double plotWidth,
            double axisY)
        {
            Pen axisPen = new(GridBrush, 1);
            drawingContext.DrawLine(axisPen, new Point(left, axisY),
                new Point(left + plotWidth, axisY));

            var allTimes = new[] { 0d, _totalSeconds }
                .Concat(_hydrogenTrack.Transitions)
                .Concat(_drainTrack.Transitions)
                .Where(value => value >= 0 && value <= _totalSeconds)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

            double lastLabelRight = double.NegativeInfinity;
            foreach (double time in allTimes)
            {
                double x = left + (time / _totalSeconds * plotWidth);
                drawingContext.DrawLine(axisPen, new Point(x, axisY - 4), new Point(x, axisY + 4));

                FormattedText label = CreateText($"{time:0.##}s", MutedTextBrush, 9);
                double labelX = Math.Clamp(x - label.Width / 2, left, left + plotWidth - label.Width);
                if (labelX < lastLabelRight + 5 && time != _totalSeconds)
                {
                    continue;
                }

                drawingContext.DrawText(label, new Point(labelX, axisY + 5));
                lastLabelRight = labelX + label.Width;
            }
        }

        private void DrawCenteredText(DrawingContext drawingContext, string text, Brush brush,
            double fontSize, double width, double height)
        {
            FormattedText formatted = CreateText(text, brush, fontSize);
            drawingContext.DrawText(formatted,
                new Point(Math.Max(8, (width - formatted.Width) / 2),
                    Math.Max(8, (height - formatted.Height) / 2)));
        }

        private void DrawText(DrawingContext drawingContext, string text, Brush brush,
            double fontSize, double x, double y)
        {
            drawingContext.DrawText(CreateText(text, brush, fontSize), new Point(x, y));
        }

        private FormattedText CreateText(string text, Brush brush, double fontSize)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), fontSize, brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        private static Brush CreateFrozenBrush(string color)
        {
            SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        private static Pen CreateFrozenPen(string color, double thickness)
        {
            Pen pen = new(CreateFrozenBrush(color), thickness);
            pen.Freeze();
            return pen;
        }
    }

    public sealed record ValveTrack(bool InitiallyOpen, IReadOnlyList<double> Transitions,
        IReadOnlyList<string> TransitionLabels)
    {
        public static ValveTrack Empty { get; } = new(false, Array.Empty<double>(), Array.Empty<string>());
    }
}
