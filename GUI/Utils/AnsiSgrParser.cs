using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RauskuClaw.GUI.Utils
{
    public sealed class AnsiSgrParser
    {
        private const char Esc = '\u001b';

        private readonly List<AnsiSegment> _segments = new();
        private string _pendingEscape = string.Empty;
        private string _buffer = string.Empty;

        private Brush _foreground = Brushes.Lime;
        private Brush _background = Brushes.Transparent;
        private FontWeight _weight = FontWeights.Normal;

        public IReadOnlyList<AnsiSegment> ParseChunk(string chunk)
        {
            _segments.Clear();
            if (string.IsNullOrEmpty(chunk))
            {
                return _segments;
            }

            var text = _pendingEscape + chunk;
            _pendingEscape = string.Empty;
            _buffer = string.Empty;

            var i = 0;
            while (i < text.Length)
            {
                var ch = text[i];
                if (ch != Esc)
                {
                    _buffer += ch;
                    i++;
                    continue;
                }

                FlushBuffer();
                var escapeStart = i;
                i++;
                if (i >= text.Length)
                {
                    _pendingEscape = text[escapeStart..];
                    break;
                }

                var marker = text[i];
                if (marker != '[' && marker != ']')
                {
                    // Non-CSI sequences are ignored.
                    i++;
                    continue;
                }

                i++;
                var commandFound = false;
                var cmdStart = i;
                while (i < text.Length)
                {
                    var c = text[i];
                    if (c >= '@' && c <= '~')
                    {
                        commandFound = true;
                        var payload = text[cmdStart..i];
                        HandleEscape(marker, payload, c);
                        i++;
                        break;
                    }

                    i++;
                }

                if (!commandFound)
                {
                    _pendingEscape = text[escapeStart..];
                    break;
                }
            }

            FlushBuffer();
            return _segments;
        }

        public void Reset()
        {
            _pendingEscape = string.Empty;
            _buffer = string.Empty;
            _foreground = Brushes.Lime;
            _background = Brushes.Transparent;
            _weight = FontWeights.Normal;
            _segments.Clear();
        }

        private void HandleEscape(char marker, string payload, char command)
        {
            // We only style ANSI SGR (CSI ... m). Other control sequences are dropped.
            if (marker != '[' || command != 'm')
            {
                return;
            }

            var parts = string.IsNullOrWhiteSpace(payload)
                ? new[] { 0 }
                : ParseIntList(payload);
            for (var i = 0; i < parts.Length; i++)
            {
                var code = parts[i];
                if ((code == 38 || code == 48) && i + 1 < parts.Length)
                {
                    var isForeground = code == 38;
                    var mode = parts[i + 1];

                    // 256-color: 38;5;<idx> / 48;5;<idx>
                    if (mode == 5 && i + 2 < parts.Length)
                    {
                        var brush = Map256Color(parts[i + 2]);
                        if (isForeground) _foreground = brush;
                        else _background = brush;
                        i += 2;
                        continue;
                    }

                    // Truecolor: 38;2;<r>;<g>;<b> / 48;2;<r>;<g>;<b>
                    if (mode == 2 && i + 4 < parts.Length)
                    {
                        var brush = BuildRgbBrush(parts[i + 2], parts[i + 3], parts[i + 4]);
                        if (isForeground) _foreground = brush;
                        else _background = brush;
                        i += 4;
                        continue;
                    }
                }

                ApplySgr(code);
            }
        }

        private void ApplySgr(int code)
        {
            switch (code)
            {
                case 0:
                    _foreground = Brushes.Lime;
                    _background = Brushes.Transparent;
                    _weight = FontWeights.Normal;
                    return;
                case 1:
                    _weight = FontWeights.SemiBold;
                    return;
                case 22:
                    _weight = FontWeights.Normal;
                    return;
                case 39:
                    _foreground = Brushes.Lime;
                    return;
                case 49:
                    _background = Brushes.Transparent;
                    return;
            }

            if (code >= 30 && code <= 37)
            {
                _foreground = MapBasicColor(code - 30, bright: false);
                return;
            }

            if (code >= 90 && code <= 97)
            {
                _foreground = MapBasicColor(code - 90, bright: true);
                return;
            }

            if (code >= 40 && code <= 47)
            {
                _background = MapBasicColor(code - 40, bright: false);
                return;
            }

            if (code >= 100 && code <= 107)
            {
                _background = MapBasicColor(code - 100, bright: true);
            }
        }

        private static Brush MapBasicColor(int idx, bool bright)
        {
            return (idx, bright) switch
            {
                // Pure black is unreadable on the app's dark terminal background.
                (0, false) => new SolidColorBrush(Color.FromRgb(0x86, 0x92, 0xA6)),
                (1, false) => Brushes.IndianRed,
                (2, false) => Brushes.LimeGreen,
                (3, false) => Brushes.Goldenrod,
                (4, false) => Brushes.DodgerBlue,
                (5, false) => Brushes.MediumOrchid,
                (6, false) => Brushes.CadetBlue,
                (7, false) => Brushes.Gainsboro,
                (0, true) => Brushes.DimGray,
                (1, true) => Brushes.OrangeRed,
                (2, true) => Brushes.LawnGreen,
                (3, true) => Brushes.Khaki,
                (4, true) => Brushes.DeepSkyBlue,
                (5, true) => Brushes.Violet,
                (6, true) => Brushes.PaleTurquoise,
                (7, true) => Brushes.White,
                _ => Brushes.Lime
            };
        }

        private static Brush Map256Color(int idx)
        {
            if (idx < 0) idx = 0;
            if (idx > 255) idx = 255;

            // 0-15: ANSI base palette
            if (idx < 8) return EnsureReadableOnDark(MapBasicColor(idx, bright: false));
            if (idx < 16) return MapBasicColor(idx - 8, bright: true);

            // 16-231: 6x6x6 color cube
            if (idx <= 231)
            {
                var n = idx - 16;
                var r = n / 36;
                var g = (n % 36) / 6;
                var b = n % 6;
                return EnsureReadableOnDark(BuildRgbBrush(CubeLevel(r), CubeLevel(g), CubeLevel(b)));
            }

            // 232-255: grayscale ramp
            var gray = 8 + ((idx - 232) * 10);
            return EnsureReadableOnDark(BuildRgbBrush(gray, gray, gray));
        }

        private static int CubeLevel(int value) => value == 0 ? 0 : 55 + (value * 40);

        private static Brush BuildRgbBrush(int r, int g, int b)
        {
            byte rr = (byte)Math.Clamp(r, 0, 255);
            byte gg = (byte)Math.Clamp(g, 0, 255);
            byte bb = (byte)Math.Clamp(b, 0, 255);
            return EnsureReadableOnDark(new SolidColorBrush(Color.FromRgb(rr, gg, bb)));
        }

        private static Brush EnsureReadableOnDark(Brush brush)
        {
            if (brush is not SolidColorBrush solid)
            {
                return brush;
            }

            var c = solid.Color;
            var luminance = (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B);
            if (luminance < 42)
            {
                return new SolidColorBrush(Color.FromRgb(
                    (byte)Math.Clamp(c.R + 90, 0, 255),
                    (byte)Math.Clamp(c.G + 90, 0, 255),
                    (byte)Math.Clamp(c.B + 90, 0, 255)));
            }

            return brush;
        }

        private static int[] ParseIntList(string payload)
        {
            var raw = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var list = new List<int>(raw.Length);
            foreach (var item in raw)
            {
                if (int.TryParse(item, out var value))
                {
                    list.Add(value);
                }
            }

            if (list.Count == 0)
            {
                list.Add(0);
            }

            return list.ToArray();
        }

        private void FlushBuffer()
        {
            if (string.IsNullOrEmpty(_buffer))
            {
                return;
            }

            _segments.Add(new AnsiSegment(_buffer, _foreground, _background, _weight));
            _buffer = string.Empty;
        }
    }

    public sealed record AnsiSegment(string Text, Brush Foreground, Brush Background, FontWeight Weight);
}
