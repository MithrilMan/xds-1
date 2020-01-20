using System.IO;

namespace NBitcoin.BouncyCastle.util.encoders
{
    /**
     * Encode and decode byte arrays (typically from binary to 7-bit ASCII
     * encodings).
     */
    interface IEncoder
    {
        int Encode(byte[] data, int off, int length, Stream outStream);

        int Decode(byte[] data, int off, int length, Stream outStream);

        int DecodeString(string data, Stream outStream);
    }
}