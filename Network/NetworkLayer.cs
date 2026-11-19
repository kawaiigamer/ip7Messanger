using ip7Messanger.Base;
using ip7Messanger.Configs;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ip7Messanger.Network
{
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public sealed class NetworkLayer : IAsyncDisposable
    {
        private const int MaxPacketSize = 2 * 1024 * 1024; // 2 МБ
        private readonly IASMSession _session;
        private QuicListener? _listener;
        private QuicConnection? _connection;
        private QuicStream? _controlStream;
        private QuicStream? _messageStream;
        private readonly SemaphoreSlim _controlSendLock = new(1, 1);
        private readonly SemaphoreSlim _messageSendLock = new(1, 1);
        private CancellationTokenSource _sessionCts = new();
        private readonly CancellationTokenSource _globalCts = new();
        public bool IsConnected =>
            _connection != null && _controlStream != null && _messageStream != null;

        public event Action<ReadOnlySpan<byte>>? PacketReceived;
        public event Action<bool>? ConnectionStateChanged;
        public event Action<string>? SystemLog;
        public event Action? DecryptionFailed;

        public NetworkLayer(IASMSession session)
        {
            _session = session;
        }
        public void ApplyPacket(ReadOnlySpan<byte> message)
        {
            if (message.Length < Consts.L_PROTO_HEADER_SIZE)
            {
                _session.Logger.E($"Message length too short: {message.Length}");
                return;
            }

            if (message[0] != Consts.L_PROTO_VERSION)
            {
                _session.Logger.E($"Message version unsupported: {message[0]}");
                return;
            }

            var message_type = (PacketType)message[1];
            _session.RemoteMember.Statistics.MsgTotal++;
            switch (message_type)
            {
                case PacketType.INFO:
                    _session.RemoteMember.Statistics.MsgInfo++;
                    _session.Ui.WriteInfoMessage(Encoding.UTF8.GetString(message.Slice(Consts.L_PROTO_HEADER_SIZE)), false);
                    break;
                case PacketType.TEXT:
                    _session.RemoteMember.Statistics.MsgInfo++;
                    _session.Ui.WriteMessage(Encoding.UTF8.GetString(message.Slice(Consts.L_PROTO_HEADER_SIZE)), false);
                    break;
                case PacketType.SYSTEM:
                    break;
                case PacketType.PING:
                    // Можно отправить ответный PONG или обновить время последней активности
                    break;
                case PacketType.MEDIA:
                case PacketType.IMG:
                case PacketType.AUDIO:
                case PacketType.OTHER:


                    break;
                case PacketType.ACTION:
                    _session.RemoteMember.Statistics.MsgControl++;
                    PacketAction action = (PacketAction)message[2];
                    switch (action)
                    {
                        case PacketAction.WRITING:
                            _session.RemoteMember.Statistics.Writing = true;
                            _session.Ui.UpdateHeader();
                            break;
                        case PacketAction.NOT_WRITING:
                            _session.RemoteMember.Statistics.Writing = false;
                            _session.Ui.UpdateHeader();
                            break;
                        case PacketAction.ONLINE:
                            _session.RemoteMember.Statistics.Online = true;
                            _session.Ui.UpdateHeader();
                            break;
                        case PacketAction.OFFLINE:
                            _session.RemoteMember.Statistics.Online = false;
                            _session.Ui.UpdateHeader();
                            break;
                        case PacketAction.ROTATE_KEY:
                            int requiredLength = _session.RemoteMember.SavableConfig.SaltLength + Consts.L_PROTO_HEADER_SIZE;
                            if (message.Length < requiredLength)
                            {
                                _session.Logger.E($"ROTATE_KEY failed, message length: {message.Length} must be >= {requiredLength}");
                                break;
                            }
                            _session.Logger.D($"ROTATE_KEY processing, previous key version: {_session.RemoteMember.Statistics.KeyVersion}, usage: {_session.RemoteMember.Statistics.KeyUsage}");
                            _session.RemoteMember.RotateKey(message.Slice(Consts.L_PROTO_HEADER_SIZE, _session.RemoteMember.SavableConfig.SaltLength).ToArray());
                            break;
                        default:
                            _session.RemoteMember.Statistics.MsgError++;
                            _session.Logger.E($"Unknown command: {message[2]}"); 
                            break;
                    }
                    break;
                default:
                    _session.RemoteMember.Statistics.MsgError++;
                    break;
            }
        }
        public async Task StartListeningAsync(int listenPort)
        {
            if (!QuicListener.IsSupported)
                throw new PlatformNotSupportedException("QUIC не поддерживается в текущей ОС.");

            var cert = CreateSelfSignedCertificate();

            var serverConnectionOptions = new QuicServerConnectionOptions
            {
                DefaultStreamErrorCode = 0x0A,
                DefaultCloseErrorCode = 0x0B,
                MaxInboundBidirectionalStreams = 2,
                MaxInboundUnidirectionalStreams = 0,
                ServerAuthenticationOptions =
                    new SslServerAuthenticationOptions
                    {
                        ApplicationProtocols = [new SslApplicationProtocol(Consts.ProtocolName)],
                        ServerCertificate = cert
                    }
            };

            _listener = await QuicListener.ListenAsync(
                new QuicListenerOptions
                {
                    ListenEndPoint =
                                              new IPEndPoint(IPAddress.Any, listenPort),
                    ApplicationProtocols =
                                              [new SslApplicationProtocol(Consts.ProtocolName)],
                    ConnectionOptionsCallback = (_, _, _) =>
                        ValueTask.FromResult(serverConnectionOptions)
                },
                _globalCts.Token);

            SystemLog?.Invoke(
                $"QUIC-сервер запущен на порту {listenPort}. Ожидание собеседника...");
            _ = AcceptConnectionLoopAsync(_globalCts.Token);
        }

        public async Task ConnectAsync(string host, int port)
        {
            if (!QuicConnection.IsSupported)
                throw new PlatformNotSupportedException("QUIC не поддерживается в текущей ОС.");

            var clientOptions = new QuicClientConnectionOptions
            {
                RemoteEndPoint = new DnsEndPoint(host, port),
                DefaultStreamErrorCode = 0x0A,
                DefaultCloseErrorCode = 0x0B,
                MaxInboundBidirectionalStreams = 2,
                ClientAuthenticationOptions =
                    new SslClientAuthenticationOptions
                    {
                        ApplicationProtocols = [new SslApplicationProtocol(Consts.ProtocolName)],
                        RemoteCertificateValidationCallback = (_, _, _, _) => true
                    }
            };

            SystemLog?.Invoke($"Подключение к {host}:{port} по QUIC...");
            var connection = await QuicConnection.ConnectAsync(clientOptions, _globalCts.Token);

            var controlStream =
                await OpenIdentifiedStreamAsync(connection, ChannelType.Control, _globalCts.Token);
            var messageStream =
                await OpenIdentifiedStreamAsync(connection, ChannelType.Messages, _globalCts.Token);

            await InitializeSessionAsync(connection, controlStream, messageStream);
        }

        // --- Методы отправки ---

        public Task SendTextAsync(PacketType type, string content) =>
            SendPacketAsync(NetworkPacket.FromText(type, content));

        public Task SendActionAsync(PacketAction action) =>
            SendPacketAsync(NetworkPacket.FromAction(action));

        public Task SendBinaryAsync(PacketType type,
                                    byte[] data) => SendPacketAsync(new NetworkPacket(type, data));

        public async Task SendPacketAsync(NetworkPacket packet)
        {
            ChannelType targetChannel =
                packet.Type <= 0 ? ChannelType.Control : ChannelType.Messages;

            QuicStream? stream =
                targetChannel == ChannelType.Control ? _controlStream : _messageStream;
            SemaphoreSlim sendLock =
                targetChannel == ChannelType.Control ? _controlSendLock : _messageSendLock;

            if (stream == null)
                throw new InvalidOperationException($"Поток {targetChannel} не инициализирован.");

            // 1. Формируем открытые данные: [MessageType] + [Payload]
            byte[] plainBody = packet.SerializePlainBody();

            // 2. Шифруем пакет перед отправкой (адаптируйте вызов под сигнатуру вашего
            // EncryptMessage)
            byte[] encryptedBuffer =
                new byte[plainBody.Length + 256]; // Запас под IV/Nonce/Tag/Padding
            int encryptedLength = _session.LocalMember.EncryptMessage(plainBody, encryptedBuffer);

            if (encryptedLength <= 0)
            {

                _session.Logger.E(
                    $"Encryption error: {_session.LocalMember.Crypter.GetErrorDescr(encryptedLength)}");
                return;
            }

            // 3. Оборачиваем в фрейм с 4-байтовой длиной
            byte[] frame =
                NetworkPacket.WrapEncryptedFrame(encryptedBuffer.AsSpan(0, encryptedLength));

            await sendLock.WaitAsync(_sessionCts.Token);
            try
            {
                await stream.WriteAsync(frame, _sessionCts.Token);
                await stream.FlushAsync(_sessionCts.Token);
            }
            finally
            {
                sendLock.Release();
            }
        }

        private static async Task<QuicStream> OpenIdentifiedStreamAsync(QuicConnection connection,
                                                                        ChannelType channel,
                                                                        CancellationToken token)
        {
            var stream =
                await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, token);
            await stream.WriteAsync(new byte[] { (byte)channel }, token);
            await stream.FlushAsync(token);
            return stream;
        }

        private async Task AcceptConnectionLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _listener != null)
                {
                    var connection = await _listener.AcceptConnectionAsync(token);

                    QuicStream? controlStream = null;
                    QuicStream? messageStream = null;

                    for (int i = 0; i < 2; i++)
                    {
                        var stream = await connection.AcceptInboundStreamAsync(token);
                        byte[] header = new byte[1];
                        await stream.ReadExactlyAsync(header, token);

                        var channelType = (ChannelType)header[0];
                        if (channelType == ChannelType.Control)
                            controlStream = stream;
                        else if (channelType == ChannelType.Messages)
                            messageStream = stream;
                        else
                            throw new InvalidDataException(
                                $"Неизвестный тип канала QUIC: {header}");
                    }

                    if (controlStream == null || messageStream == null)
                        throw new InvalidDataException("Клиент не открыл оба обязательных канала.");

                    await InitializeSessionAsync(connection, controlStream, messageStream);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SystemLog?.Invoke($"Ошибка приема подключения: {ex.Message}");
            }
        }

        private async Task InitializeSessionAsync(QuicConnection connection,
                                                  QuicStream controlStream,
                                                  QuicStream messageStream)
        {
            await CloseCurrentSessionAsync();

            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token);
            _connection = connection;
            _controlStream = controlStream;
            _messageStream = messageStream;

            ConnectionStateChanged?.Invoke(true);
            SystemLog?.Invoke($"QUIC-сессия установлена с {connection.RemoteEndPoint}");

            _ = ReceiveLoopAsync(controlStream, ChannelType.Control, _sessionCts.Token);
            _ = ReceiveLoopAsync(messageStream, ChannelType.Messages, _sessionCts.Token);
        }

        private async Task ReceiveLoopAsync(QuicStream stream, ChannelType channel,
                                            CancellationToken token)
        {
            byte[] lengthBuffer = new byte[1];
            // Выделяем отдельный буфер расшифровки для каждого потока, чтобы исключить гонку данных
            byte[] remoteTextBuffer = new byte[65536];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // 1. Читаем 4 байта длины зашифрованного пакета
                    await stream.ReadExactlyAsync(lengthBuffer, token);
                    int encryptedLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);

                    if (encryptedLength <= 0 || encryptedLength > MaxPacketSize)
                        throw new InvalidDataException(
                            $"[{channel}] Некорректный размер пакета: {encryptedLength}");
                    // 2. Читаем зашифрованный пакет
                    byte[] item = new byte[encryptedLength];
                    await stream.ReadExactlyAsync(item, token);
                    // Если пришел пакет больше стандартного буфера (например, MEDIA/IMG), расширяем
                    // буфер

                    if (remoteTextBuffer.Length < encryptedLength + 256)
                        remoteTextBuffer = new byte[encryptedLength + 256];
                    // 3. Расшифровываем принятый пакет перед его разбором
                    int decryptedLength = _session.RemoteMember.DecryptMessage(item, remoteTextBuffer);
                    if (decryptedLength > 0)
                    {
                        ApplyPacket(remoteTextBuffer.AsSpan(0, decryptedLength));
                    }
                    else
                    {
                        _session.Logger.E(
                            $"Decryption error: {_session.RemoteMember.Crypter.GetErrorDescr(decryptedLength)}");
                        DecryptionFailed?.Invoke();
                    }
                }
            }
            catch (EndOfStreamException)
            {
                SystemLog?.Invoke($"Собеседник закрыл канал {channel}.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SystemLog?.Invoke($"Ошибка чтения в канале {channel}: {ex.Message}");
            }
            finally
            {
                await CloseCurrentSessionAsync();
            }
        }

        private void ProcessMessage(ReadOnlySpan<byte> decryptedData)
        {
            if (decryptedData.IsEmpty)
                return;
            PacketReceived?.Invoke(decryptedData);
        }

        private async Task CloseCurrentSessionAsync()
        {
            if (_connection == null && _controlStream == null && _messageStream == null)
                return;
            _sessionCts.Cancel();
            if (_controlStream != null)
            {
                await _controlStream.DisposeAsync();
                _controlStream = null;
            }
            if (_messageStream != null)
            {
                await _messageStream.DisposeAsync();
                _messageStream = null;
            }
            if (_connection != null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
            ConnectionStateChanged?.Invoke(false);
        }

        private static X509Certificate2 CreateSelfSignedCertificate()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var req = new CertificateRequest("CN=ip7Messanger", ecdsa, HashAlgorithmName.SHA256);
            var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                                            DateTimeOffset.UtcNow.AddYears(1));
            return new X509Certificate2(cert.Export(X509ContentType.Pfx));
        }

        public async ValueTask DisposeAsync()
        {
            _globalCts.Cancel();
            await CloseCurrentSessionAsync();
            if (_listener != null)
                await _listener.DisposeAsync();
            _controlSendLock.Dispose();
            _messageSendLock.Dispose();
            _sessionCts.Dispose();
            _globalCts.Dispose();
        }
    }
}