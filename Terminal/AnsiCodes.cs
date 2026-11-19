using System;
using System.Collections.Generic;
using System.Text;

namespace ip7Messanger.Terminal
{
    public static class AnsiCodes
    {
        // --- Базовые управляющие последовательности ---
        public const string Escape = "\x1b";
        public const string SaveCursor = Escape + "7";
        public const string RestoreCursor = Escape + "8";
        public const string ClearLine = Escape + "[2K";
        public const string ClearScreen = Escape + "[2J";
        public const string ResetScrollRegion = Escape + "[r";
        public const string ResetStyle = Escape + "[0m";

        // --- Стили, используемые в шапке ---
        public const string BoldDarkGray = Escape + "[97m"; //Escape + "[1;30;49m";
        public const string BoldRed = Escape + "[1;31;49m";
        public const string BoldBrightGreen = Escape + "[1;92m";
        public const string BoldBlue = Escape + "[1;94;49m";
        public const string BoldMagenta = Escape + "[1;95;49m";
        public const string BoldCyan = Escape + "[1;96;49m";
        public const string BoldPurple256 = Escape + "[1;38;5;93m";
        public const string BoldGoldRgb = Escape + "[1;38;2;225;185;70m";
        public const string Orange = Escape + "[38;5;208m";
        public const string BoldOrange = Escape + "[1;38;5;208m";

        // --- Базовые цвета для форматирования логов ---
        public const string Gray = Escape + "[90m";
        public const string Red = Escape + "[91m";
        public const string Green = Escape + "[92m";
        public const string Yellow = Escape + "[93m";
        public const string Blue = Escape + "[94m";
        public const string Magenta = Escape + "[95m";
        public const string Cyan = Escape + "[96m";
        public const string White = Escape + "[97m";

        // --- Динамические команды ---
        public static string SetCursorPosition(int row, int col) => $"{Escape}[{row};{col}H";
        public static string SetScrollRegion(int top, int bottom) => $"{Escape}[{top};{bottom}r";
        public static string Foreground256(byte colorCode, bool bold = false) =>
            $"{Escape}[{(bold ? "1;" : "")}38;5;{colorCode}m";
        public static string ForegroundRgb(byte r, byte g, byte b, bool bold = false) =>
            $"{Escape}[{(bold ? "1;" : "")}38;2;{r};{g};{b}m";
    }
}
