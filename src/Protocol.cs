using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AromaShooterUdpBridge
{
    public sealed class Command
    {
        public bool IsStop { get; private set; }
        public string Target { get; private set; }
        public int[] Chambers { get; private set; }
        public int Duration { get; private set; }
        public int[] Intensities { get; private set; }
        public string Text { get; private set; }
        public bool All => string.Equals(Target, "ALL", StringComparison.OrdinalIgnoreCase);

        public static Command Parse(byte[] bytes)
        {
            if (bytes.Length == 0 || bytes.Length > 1024) throw new FormatException("データグラムは1〜1024バイトです。");
            string text = new UTF8Encoding(false, true).GetString(bytes).Trim();
            if (text.Any(c => c == '\uFEFF' || (char.IsWhiteSpace(c) && c != ' ') || char.IsControl(c)))
                throw new FormatException("内部の改行・タブ・BOM・制御文字は使用できません。");
            string[] p = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 2 && p[0].Equals("STOP", StringComparison.OrdinalIgnoreCase))
                return new Command { IsStop = true, Target = p[1], Text = text };
            if ((p.Length != 4 && p.Length != 5) || !p[0].Equals("SHOOT", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("SHOOT target chambers durationMs [intensities] または STOP target を指定してください。");
            var chambers = p[2].Split(',').Select(x => Number(x, 1, 6)).ToArray();
            if (chambers.Distinct().Count() != chambers.Length) throw new FormatException("チャンバーが重複しています。");
            int duration = Number(p[3], 1, int.MaxValue);
            int[] intensities = p.Length == 5 ? p[4].Split(',').Select(x => Number(x, 0, 100)).ToArray() : null;
            if (intensities != null && intensities.Length != chambers.Length) throw new FormatException("強度とチャンバーの個数が一致しません。");
            return new Command { Target = p[1], Chambers = chambers, Duration = duration, Intensities = intensities, Text = text };
        }

        private static int Number(string text, int min, int max)
        {
            int value;
            if (text.Length == 0 || text.Any(c => c < '0' || c > '9') ||
                !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < min || value > max)
                throw new FormatException("整数の範囲が不正です: " + text);
            return value;
        }
    }
}
