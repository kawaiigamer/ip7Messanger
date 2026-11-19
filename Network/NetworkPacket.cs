using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace ip7Messanger.Network
{
    public readonly record struct NetworkPacket(PacketType Type, byte[] Payload)
    {
        public string PayloadAsString => Payload.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Payload);

        public PacketAction? Action => Type == PacketType.ACTION && Payload.Length > 0
            ? (PacketAction)Payload[2]
            : null;

        public static NetworkPacket FromText(PacketType type, string text) =>
            new(type, Encoding.UTF8.GetBytes(text ?? string.Empty));

        public static NetworkPacket FromAction(PacketAction action) =>
            new(PacketType.ACTION, [(byte)action]);

        public static NetworkPacket Ping() =>
            new(PacketType.PING, Array.Empty<byte>());

         public byte[] SerializePlainBody()
        {
            byte[] plainBody = new byte[1 + Payload.Length];
            plainBody[1] = unchecked((byte)Type);
            Payload.CopyTo(plainBody, 1);
            return plainBody;
        }

        public static NetworkPacket DeserializePlainBody(ReadOnlySpan<byte> decryptedSpan)
        {
            var msgType = unchecked((PacketType)decryptedSpan[1]);
            byte[] payload = decryptedSpan.Length > 1
                ? decryptedSpan.Slice(1).ToArray()
                : Array.Empty<byte>();

            return new NetworkPacket(msgType, payload);
        }

        public static byte[] WrapEncryptedFrame(ReadOnlySpan<byte> encryptedData)
        {
            byte[] frame = new byte[4 + encryptedData.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), encryptedData.Length);
            encryptedData.CopyTo(frame.AsSpan(4));
            return frame;
        }
    }
}
