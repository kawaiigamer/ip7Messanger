using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ip7Messanger.Configs
{
    public class MemberSavableConfig
    {
        public string Name { get; private set; }
        public string Address { get; init; }
        public int Port { get; init; }
        public byte[] Key { get; init; }
        public int TagLength { get; init; } = 16;
        public int NonceLength { get; init; } = 12;
        public int SaltLength { get; init; } = 32;
        public string CryptoMethod { get; private set; } = "";
        public int MESSAGE_MAX_LENGTH { get; init; } = 2048;
        public int PHOTO_MAX_SIZE { get; init; } = 1024 * 1024 * 8;
        public int VOICE_MAX_SIZE { get; init; } = 1024 * 1024 * 4;
        public int VIDEO_MAX_SIZE { get; init; } = 1024 * 1024 * 128;

        private void SetCryptoMethodName() => CryptoMethod = $"AES-256-GCM(Nonce: {NonceLength}  Tag: {TagLength})+HKDF-SHA256";

        public string Serialize()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = true
            });
        }

        public static MemberSavableConfig? Deserialize(string serialized)
        {
            MemberSavableConfig msc = JsonSerializer.Deserialize<MemberSavableConfig>(serialized, new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = true
            })!;
            msc.SetCryptoMethodName();
            return msc;
        }

        public MemberSavableConfig(string name, string address, int port, byte[] key)
        {
            Name = name;
            Address = address;
            Port = port;
            Key = key;
            SetCryptoMethodName();
        }
    }
}
