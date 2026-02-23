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

            foreach (var code in parts)
            {
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
                (0, false) => Brushes.Black,
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
