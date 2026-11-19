using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ip7Messanger.Cryptography
{
    public sealed class AesGcmExtended
    {
        private readonly AesGcm _instance;
        private readonly string[] ErrorsDescr =  {
                                                "Buffer is empty!",
                                                "Output buffer length must be >= input buffer length + AES header length",
                                                "Input buffer length must be > AES header length",
                                                "Output buffer length must be >= input buffer length - AES header length",
                                                "CryptographicException while crypting operation!"
                                                };
        public const int NONCE_LENGTH = 12;
        public readonly int TAG_LENGTH;
        public readonly int HEADER_LENGTH;
        public readonly byte[] KEY;
        public string GetErrorDescr(int code)
        {
            return ErrorsDescr[Math.Abs(code)];
        }
        public AesGcmExtended(ReadOnlySpan<byte> key, int tag_length)
        {
            TAG_LENGTH = tag_length;
            HEADER_LENGTH = NONCE_LENGTH + TAG_LENGTH;
            KEY = key.ToArray();
            _instance = new AesGcm(key, tag_length);
        }

        public int Encrypt(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (input.IsEmpty || output.IsEmpty)
            {
                return 0;
            }
            int expected_length = HEADER_LENGTH + input.Length;
            if (output.Length < expected_length)
            {
                return -1;
            }
            Span<byte> nonce = output.Slice(0, NONCE_LENGTH);
            Span<byte> tag = output.Slice(NONCE_LENGTH, TAG_LENGTH);
            Span<byte> ciphertext = output.Slice(HEADER_LENGTH, input.Length);
            RandomNumberGenerator.Fill(nonce);
            try
            {
                _instance.Encrypt(nonce, input, ciphertext, tag);
            }
            catch (CryptographicException)
            {
                return -4;
            }
            return expected_length;
        }

        public int Decrypt(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (input.IsEmpty || output.IsEmpty)
            {
                return 0;
            }
            if (input.Length < HEADER_LENGTH)
            {
                return -2;
            }
            int ciphertext_length = input.Length - HEADER_LENGTH;
            if (output.Length < ciphertext_length)
            {
                return -3;
            }
            ReadOnlySpan<byte> nonce = input.Slice(0, NONCE_LENGTH);
            ReadOnlySpan<byte> tag = input.Slice(NONCE_LENGTH, TAG_LENGTH);
            ReadOnlySpan<byte> ciphertext = input.Slice(HEADER_LENGTH);
            try
            {
                _instance.Decrypt(nonce, ciphertext, tag, output.Slice(0, ciphertext_length));
            }
            catch (CryptographicException)
            {
                return -4;
            }
            return ciphertext_length;
        }
    }
}
