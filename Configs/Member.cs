using ip7Messanger.Cryptography;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ip7Messanger.Configs
{
    public class Member
    {
        public MemberSavableConfig SavableConfig { get; private set; }
        public AesGcmExtended Crypter { get; private set; }
        public MemberStatistics Statistics = new MemberStatistics();

        public Member(MemberSavableConfig data)
        {
            SavableConfig = data;
            Crypter = new AesGcmExtended(SavableConfig.Key, SavableConfig.TagLength);
        }

        public int EncryptMessage(ReadOnlySpan<byte> message_bytes, Span<byte> buffer)
        {
            Statistics.KeyUsage++;
            return Crypter.Encrypt(message_bytes, buffer);
        }

        public int DecryptMessage(ReadOnlySpan<byte> message_bytes, Span<byte> buffer)
        {
            Statistics.KeyUsage++;
            return Crypter.Decrypt(message_bytes, buffer);
        }

        public void RotateKey(byte[] salt)
        {
            Statistics.KeyVersion++;
            Statistics.KeyUsage = 0;
            byte[] info = System.Text.Encoding.UTF8.GetBytes(
                $"AES-256-ROTATION-V{Statistics.KeyVersion}"
            );

            Crypter = new AesGcmExtended(HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                Crypter.KEY,
                32,
                salt,
                info
            ), Crypter.TAG_LENGTH);
        }
    }
}
