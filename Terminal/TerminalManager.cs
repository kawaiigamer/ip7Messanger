using ip7Messanger.Base;
using ip7Messanger.Configs;
using System;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace ip7Messanger.Terminal
{

    public sealed class TerminalManager : IDisposable
    {
        private const int StdOutputHandle = -11;
        private const uint EnableVirtualTerminalProcessingFlag = 0x0004;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        private readonly object _consoleLock = new();
        private const int HeaderHeight = 5;
        private const int FooterHeight = 2; // 1 строка разделитель + 1 строка ввода
        private const string InputPrompt = "> ";

        private int _lastWindowHeight;
        private int _lastWindowWidth;
        private int _logRow;
        private int _logCol = 1;

        private readonly StringBuilder _inputBuffer = new();
        private readonly IASMSession _session;

        private CancellationTokenSource? _resizeCts;
        private Task? _resizeWatcherTask;


        public TerminalManager(IASMSession session)
        {
            _session = session;
        }

        private void StartResizeWatcher()
        {
            _resizeCts = new CancellationTokenSource();
            var token = _resizeCts.Token;

            _resizeWatcherTask = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                try
                {
                    while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    {
                        // Быстрая проверка без блокировки
                        if (Console.WindowHeight != _lastWindowHeight || Console.WindowWidth != _lastWindowWidth)
                        {
                            lock (_consoleLock)
                            {
                                CheckResize();
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Штатная остановка при Dispose
                }
            }, token);
        }

        public void Initialize()
        {
            EnableVirtualTerminalProcessing();
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            lock (_consoleLock)
            {
                Console.Clear();
                _lastWindowHeight = Console.WindowHeight;
                _lastWindowWidth = Console.WindowWidth;
                _logRow = HeaderHeight + 1;
                _logCol = 1;

                SetupLayout();
                RedrawHeaderInternal();
                RedrawInputInternal();
                RestoreInputCursor();
            }

            StartResizeWatcher();
        }

        public void UpdateHeader()
        {
            lock (_consoleLock)
            {
                CheckResize();
                RedrawHeaderInternal();
                RestoreInputCursor();
            }
        }

        public void WriteLog(string message)
        {
            lock (_consoleLock)
            {
                CheckResize();

                // 1. Переводим курсор в сохраненную позицию области логов
                Console.Write(AnsiCodes.SetCursorPosition(_logRow, _logCol));

                // 2. Печатаем сообщение (терминал сам прокрутит только зону логов)
                Console.WriteLine(message);

                // 3. Запоминаем новую позицию курсора в логах
                _logRow = Console.CursorTop + 1;
                _logCol = Console.CursorLeft + 1;

                // 4. Возвращаем фокус курсора в строку ввода
                RestoreInputCursor();
            }
        }

        private string PrepareMessageTime()
        {
            return $"{AnsiCodes.BoldBlue}{DateTime.Now.ToString("HH:mm:ss")}{AnsiCodes.ResetStyle}";
        }

        public void WriteMessage(string message, bool is_local)
        {
            WriteLog(
                $"[{PrepareMessageTime()}]{AnsiCodes.BoldMagenta}[{(is_local ? _session.LocalMember.SavableConfig.Name : _session.RemoteMember.SavableConfig.Name)}{AnsiCodes.ResetStyle}]: {message}");
        }

        public void WriteInfoMessage(string message, bool is_local)
        {
            WriteLog(
                $"[{PrepareMessageTime()}]{AnsiCodes.BoldPurple256}[{(is_local ? "LOCAL" : "RECIVED")}{AnsiCodes.ResetStyle}][{AnsiCodes.BoldBrightGreen}INFO{AnsiCodes.ResetStyle}]:{AnsiCodes.ResetStyle}{message}");
        }

        public string ReadInputLine(Action<bool>? onTypingChanged = null)
        {
            bool wasTyping = false;

            while (true)
            {
                var keyInfo = Console.ReadKey(intercept: true);

                lock (_consoleLock)
                {
                    CheckResize();

                    if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        string result = _inputBuffer.ToString();
                        _inputBuffer.Clear();

                        if (wasTyping)
                        {
                            wasTyping = false;
                            onTypingChanged?.Invoke(false);
                        }

                        RedrawInputInternal();
                        RestoreInputCursor();
                        return result;
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace)
                    {
                        if (_inputBuffer.Length > 0)
                        {
                            _inputBuffer.Length--;
                            RedrawInputInternal();
                            RestoreInputCursor();
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Escape)
                    {
                        if (_inputBuffer.Length > 0)
                        {
                            _inputBuffer.Clear();
                            RedrawInputInternal();
                            RestoreInputCursor();
                        }
                    }
                    else if (!char.IsControl(keyInfo.KeyChar))
                    {
                        _inputBuffer.Append(keyInfo.KeyChar);
                        RedrawInputInternal();
                        RestoreInputCursor();
                    }

                    bool isTypingNow = _inputBuffer.Length > 0;
                    if (isTypingNow != wasTyping)
                    {
                        wasTyping = isTypingNow;
                        onTypingChanged?.Invoke(isTypingNow);
                    }
                }
            }
        }

        private void CheckResize()
        {
            int currentHeight = Console.WindowHeight;
            int currentWidth = Console.WindowWidth;

            // Защита от падения, если пользователь сжал окно до нуля
            if (currentHeight <= HeaderHeight + FooterHeight || currentWidth <= InputPrompt.Length + 2)
                return;

            if (currentHeight != _lastWindowHeight || currentWidth != _lastWindowWidth)
            {
                _lastWindowHeight = currentHeight;
                _lastWindowWidth = currentWidth;

                int maxLogRow = Math.Max(HeaderHeight + 1, _lastWindowHeight - FooterHeight);
                if (_logRow > maxLogRow)
                    _logRow = maxLogRow;

                // Сбрасываем область прокрутки и очищаем экран от артефактов старого размера
                Console.Write(AnsiCodes.SetScrollRegion(1, _lastWindowHeight));
                Console.Clear();

                SetupLayout();
                RedrawHeaderInternal();
                RedrawInputInternal();
                RestoreInputCursor();
            }
        }

        private void SetupLayout()
        {
            int bottomLogRow = Math.Max(HeaderHeight + 1, _lastWindowHeight - FooterHeight);
            Console.Write(AnsiCodes.SetScrollRegion(HeaderHeight + 1, bottomLogRow));
        }

        private void RedrawInputInternal()
        {
            int separatorRow = Math.Max(HeaderHeight + 2, _lastWindowHeight - 1);
            int inputRow = Math.Max(HeaderHeight + 3, _lastWindowHeight);

            var sb = new StringBuilder();

            // Разделитель на всю ширину окна
            sb.Append(AnsiCodes.SetCursorPosition(separatorRow, 1)).Append(AnsiCodes.ClearLine);
            sb.Append($"{AnsiCodes.BoldDarkGray}{new string('-', Math.Max(1, _lastWindowWidth - 1))}{AnsiCodes.ResetStyle}");

            // Сама строка ввода
            sb.Append(AnsiCodes.SetCursorPosition(inputRow, 1)).Append(AnsiCodes.ClearLine);
            sb.Append($"{AnsiCodes.BoldBrightGreen}{InputPrompt}{AnsiCodes.ResetStyle}");

            int maxTextWidth = Math.Max(10, _lastWindowWidth - InputPrompt.Length - 1);
            string visibleText =
                _inputBuffer.Length > maxTextWidth
                    ? _inputBuffer.ToString(_inputBuffer.Length - maxTextWidth, maxTextWidth)
                    : _inputBuffer.ToString();

            sb.Append(visibleText);
            Console.Write(sb.ToString());
        }

        private void RestoreInputCursor()
        {
            int inputRow = Math.Max(HeaderHeight + 3, _lastWindowHeight);
            int maxTextWidth = Math.Max(10, Console.WindowWidth - InputPrompt.Length - 1);
            int visibleLength = Math.Min(_inputBuffer.Length, maxTextWidth);

            Console.Write(
                AnsiCodes.SetCursorPosition(inputRow, InputPrompt.Length + visibleLength + 1));
        }

        private void RedrawHeaderInternal()
        {
            var sb = new StringBuilder(512);

            // --- Строка 1 ---
            sb.Append(AnsiCodes.SetCursorPosition(1, 1)).Append(AnsiCodes.ClearLine);
            sb.Append(
                $"{AnsiCodes.BoldBrightGreen}{Consts.ProgramName} v{Consts.Version} {AnsiCodes.ResetStyle}");
            sb.Append(
                $"Release type: {AnsiCodes.ResetStyle}{AnsiCodes.BoldGoldRgb}{Consts.ReleaseType}{AnsiCodes.ResetStyle}");

            // --- Строка 2 ---
            sb.Append(AnsiCodes.SetCursorPosition(2, 1)).Append(AnsiCodes.ClearLine);
            sb.Append(
                $"{AnsiCodes.BoldDarkGray}Interlocutor: {AnsiCodes.ResetStyle}{AnsiCodes.BoldMagenta}{_session.RemoteMember.SavableConfig.Name} {AnsiCodes.ResetStyle}");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray}Address: {AnsiCodes.ResetStyle}{AnsiCodes.BoldBlue}{_session.RemoteMember.SavableConfig.Address}:{_session.RemoteMember.SavableConfig.Port} {AnsiCodes.ResetStyle}");
            sb.Append($"{AnsiCodes.BoldDarkGray}Status: {AnsiCodes.ResetStyle}");
            sb.Append(_session.RemoteMember.Statistics.Online
                          ? $"{AnsiCodes.BoldBrightGreen}Online {AnsiCodes.ResetStyle}"
                          : $"{AnsiCodes.BoldRed}Offline {AnsiCodes.ResetStyle}");
            if (_session.RemoteMember.Statistics.Writing)
                sb.Append($"{AnsiCodes.BoldBlue} Writing...{AnsiCodes.ResetStyle}   ");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray}Method: {AnsiCodes.ResetStyle}{AnsiCodes.BoldRed}{_session.RemoteMember.SavableConfig.CryptoMethod} {AnsiCodes.ResetStyle}");

            // --- Строка 3 ---
            sb.Append(AnsiCodes.SetCursorPosition(3, 1)).Append(AnsiCodes.ClearLine);
            sb.Append(
                $"{AnsiCodes.BoldDarkGray}User: {AnsiCodes.ResetStyle}{AnsiCodes.BoldMagenta}{_session.LocalMember.SavableConfig.Name} {AnsiCodes.ResetStyle}");
            sb.Append($"{AnsiCodes.BoldDarkGray}Status: {AnsiCodes.ResetStyle}");
            sb.Append(_session.LocalMember.Statistics.Online
                          ? $"{AnsiCodes.BoldBrightGreen}Online {AnsiCodes.ResetStyle}"
                          : $"{AnsiCodes.BoldRed}Offline {AnsiCodes.ResetStyle}");
            if (_session.LocalMember.Statistics.Writing)
                sb.Append($"{AnsiCodes.BoldBlue} Writing...{AnsiCodes.ResetStyle}   ");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray}Method: {AnsiCodes.ResetStyle}{AnsiCodes.BoldRed}{_session.LocalMember.SavableConfig.CryptoMethod} {AnsiCodes.ResetStyle}");

            // --- Строка 4 ---
            sb.Append(AnsiCodes.SetCursorPosition(4, 1)).Append(AnsiCodes.ClearLine);
            sb.Append(
                $"{AnsiCodes.BoldDarkGray}Incoming[{AnsiCodes.ResetStyle}{AnsiCodes.Blue}{_session.RemoteMember.Statistics.MsgKb()}{AnsiCodes.ResetStyle}{AnsiCodes.BoldDarkGray} kb]: {AnsiCodes.ResetStyle}{AnsiCodes.BoldPurple256}{_session.RemoteMember.Statistics.MsgTotal}{AnsiCodes.ResetStyle}");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray}(error: {AnsiCodes.ResetStyle}{AnsiCodes.BoldRed}{_session.RemoteMember.Statistics.MsgError}{AnsiCodes.ResetStyle}");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray} control: {AnsiCodes.ResetStyle}{AnsiCodes.Orange}{_session.RemoteMember.Statistics.MsgControl}{AnsiCodes.ResetStyle}");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray} info: {AnsiCodes.ResetStyle}{AnsiCodes.Green}{_session.RemoteMember.Statistics.MsgInfo}{AnsiCodes.ResetStyle}");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray} text: {AnsiCodes.ResetStyle}{AnsiCodes.BoldGoldRgb}{_session.RemoteMember.Statistics.MsgText}{AnsiCodes.ResetStyle}");
            sb.Append(
            $"{AnsiCodes.BoldDarkGray} media: {AnsiCodes.ResetStyle}{AnsiCodes.Cyan}{_session.RemoteMember.Statistics.MsgMedia}{AnsiCodes.ResetStyle}{AnsiCodes.BoldDarkGray}){AnsiCodes.ResetStyle}    ");

            sb.Append(
                $"{AnsiCodes.BoldDarkGray}Outgoing[{AnsiCodes.ResetStyle}{AnsiCodes.Blue}{_session.LocalMember.Statistics.MsgKb()}{AnsiCodes.ResetStyle}{AnsiCodes.BoldDarkGray} kb]: {AnsiCodes.ResetStyle}{AnsiCodes.BoldPurple256}{_session.LocalMember.Statistics.MsgTotal}{AnsiCodes.ResetStyle}");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray}(error: {AnsiCodes.ResetStyle}{AnsiCodes.BoldRed}{_session.LocalMember.Statistics.MsgError}{AnsiCodes.ResetStyle}");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray} control: {AnsiCodes.ResetStyle}{AnsiCodes.Orange}{_session.RemoteMember.Statistics.MsgControl}{AnsiCodes.ResetStyle}");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray} info: {AnsiCodes.ResetStyle}{AnsiCodes.Green}{_session.LocalMember.Statistics.MsgInfo}{AnsiCodes.ResetStyle}");
            sb.Append(
                $"{AnsiCodes.BoldDarkGray} text: {AnsiCodes.ResetStyle}{AnsiCodes.BoldGoldRgb}{_session.LocalMember.Statistics.MsgText}{AnsiCodes.ResetStyle}");
            sb.Append(
            $"{AnsiCodes.BoldDarkGray} media: {AnsiCodes.ResetStyle}{AnsiCodes.Cyan}{_session.LocalMember.Statistics.MsgMedia}{AnsiCodes.ResetStyle}{AnsiCodes.BoldDarkGray}){AnsiCodes.ResetStyle}");
            // --- Строка 5 ---
            sb.Append(AnsiCodes.SetCursorPosition(5, 1)).Append(AnsiCodes.ClearLine);
            sb.Append($"{AnsiCodes.BoldDarkGray}{new string('-', Math.Max(1, _lastWindowWidth - 1))}{AnsiCodes.ResetStyle}");
            //sb.Append(
            //    $"{AnsiCodes.BoldDarkGray}---------------------------------{AnsiCodes.ResetStyle}");
            Console.Write(sb.ToString());
        }

        //public void Dispose()
        //{
        //    lock (_consoleLock)
        //    {
        //        Console.Write(AnsiCodes.ResetScrollRegion + AnsiCodes.ResetStyle);
        //        Console.SetCursorPosition(0, Math.Max(0, Console.WindowHeight - 1));
        //    }
        //}
        public void Dispose()
        {
            _resizeCts?.Cancel();
            _resizeCts?.Dispose();

            // Возвращаем стандартную область прокрутки терминалу перед выходом
            Console.Write(AnsiCodes.SetScrollRegion(1, Console.WindowHeight));
        }

        private static void EnableVirtualTerminalProcessing()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;
            var handle = GetStdHandle(StdOutputHandle);
            if (GetConsoleMode(handle, out uint mode))
                SetConsoleMode(handle, mode | EnableVirtualTerminalProcessingFlag);
        }
    }
}
