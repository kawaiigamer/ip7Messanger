using ip7Messanger.Base;
using ip7Messanger.Logging;
using ip7Messanger.Terminal;
using System.Text;
using System.Threading.Channels;

public sealed class GenericLogger : IAsyncDisposable, IDisposable
{
    private const int DatePrefixLength = 10;
    private readonly string _logDir;
    private readonly Channel<LogEntry> _channel;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts = new();
    private DateOnly _currentDate;
    private FileStream? _fileStream;
    private StreamWriter? _writer;
    private int _disposed;
    private readonly IASMSession _session;

    // Добавлено необязательное поле FlushCompletion для реализации барьерного сброса очереди
    private readonly record struct LogEntry(
        DateTime Timestamp,
        LogLevel Level,
        string Message,
        TaskCompletionSource? FlushCompletion = null);

    public GenericLogger(IASMSession session)
    {
        _session = session;
        _logDir = session.Storage.Logs; // Исправлена инициализация поля _logDir
        Directory.CreateDirectory(_logDir);

        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(50_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        _writerTask = Task.Run(ProcessQueueAsync);
    }

    public void D(string message) => Log(message, LogLevel.Debug);
    public void W(string message) => Log(message, LogLevel.Warn);
    public void E(string message) => Log(message, LogLevel.Error);
    public void F(string message) => Log(message, LogLevel.Fatal);

    public void Log(string message, LogLevel level)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        var entry = new LogEntry(DateTime.Now, level, message);

        if (!_channel.Writer.TryWrite(entry))
        {
            _channel.Writer.WriteAsync(entry, _cts.Token).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Принудительно записывает все накопленные в очереди сообщения в файл и UI.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        // RunContinuationsAsynchronously предотвращает блокировку фонового потока логгера вызывающим кодом
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flushMarker = new LogEntry(default, default, string.Empty, tcs);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        
        // Ставим маркер в конец очереди
        await _channel.Writer.WriteAsync(flushMarker, linkedCts.Token).ConfigureAwait(false);

        // Ждем, пока фоновый поток дойдет до маркера, запишет все предыдущие логи и сделает FlushToOs()
        await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        D("All messages accumulated in the queue have been forcibly written to the log file!");
    }

    /// <summary>
    /// Синхронная обертка для принудительного сброса очереди логов.
    /// </summary>
    public void Flush()
    {
        FlushAsync().GetAwaiter().GetResult();
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _channel.Reader;
        char[] prefixBuffer = new char[32];

        try
        {
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var entry))
                {
                    // Если встретили команду принудительного сброса
                    if (entry.FlushCompletion is not null)
                    {
                        FlushToOs();
                        entry.FlushCompletion.TrySetResult();
                        continue;
                    }

                    EnsureWriter(DateOnly.FromDateTime(entry.Timestamp));

                    int prefixLen = FormatPrefix(entry, prefixBuffer);

                    _writer!.Write(prefixBuffer, 0, prefixLen);
                    _writer.WriteLine(entry.Message);
                    _session.Ui.WriteLog($"{prefixBuffer.AsSpan(DatePrefixLength+1, prefixLen - DatePrefixLength)}{entry.Message}");
                }

                // Сбрасываем буфер в конце каждой вычитанной пачки
                FlushToOs();
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная отмена при принудительном завершении
        }
        finally
        {
            // Если при завершении в канале остались необработанные маркеры Flush, освобождаем ожидающие потоки
            while (reader.TryRead(out var remaining))
            {
                remaining.FlushCompletion?.TrySetCanceled();
            }

            FlushToOs();
            _writer?.Dispose();
            _fileStream?.Dispose();
        }
    }

    private static int FormatPrefix(in LogEntry entry, Span<char> destination)
    {
        entry.Timestamp.TryFormat(destination, out int pos, "yyyy-MM-dd HH:mm:ss");

        destination[pos++] = '[';

        ReadOnlySpan<char> levelSpan = entry.Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Warn => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Fatal => "FATAL",
            _ => "INFO"
        };

        levelSpan.CopyTo(destination.Slice(pos));
        pos += levelSpan.Length;

        "]:".CopyTo(destination.Slice(pos));
        pos += 3;

        return pos;
    }

    private void FlushToOs()
    {
        if (_writer is null || _fileStream is null) return;
        _writer.Flush();
        _fileStream.Flush(flushToDisk: false);
    }

    private void EnsureWriter(DateOnly date)
    {
        if (_writer is not null && _currentDate == date) return;

        FlushToOs();
        _writer?.Dispose();
        _fileStream?.Dispose();

        _currentDate = date;
        string filePath = Path.Combine(_logDir, $"{date:yyyy-MM-dd}_log.txt");

        _fileStream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 65536,
            FileOptions.SequentialScan);

        _writer = new StreamWriter(_fileStream, Encoding.UTF8, bufferSize: 65536, leaveOpen: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _channel.Writer.TryComplete();

        await _writerTask.ConfigureAwait(false);

        _cts.Cancel();
        _cts.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
