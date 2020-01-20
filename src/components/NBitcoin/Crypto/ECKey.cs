﻿using System;
using NBitcoin.BouncyCastle.asn1.x9;
using NBitcoin.BouncyCastle.crypto.ec;
using NBitcoin.BouncyCastle.crypto.parameters;
using NBitcoin.BouncyCastle.crypto.signers;
using NBitcoin.BouncyCastle.math;
using NBitcoin.BouncyCastle.math.ec;
using NBitcoin.BouncyCastle.math.ec.custom.sec;

namespace NBitcoin.Crypto
{
    class ECKey
    {
        public static readonly BigInteger HALF_CURVE_ORDER;
        public static readonly BigInteger CURVE_ORDER;
        public static readonly ECDomainParameters CURVE;
        public static readonly X9ECParameters _Secp256k1;

        readonly ECKeyParameters _Key;

        ECDomainParameters _DomainParameter;

        static ECKey()
        {
            _Secp256k1 = CustomNamedCurves.Secp256k1;
            CURVE = new ECDomainParameters(_Secp256k1.Curve, _Secp256k1.G, _Secp256k1.N, _Secp256k1.H);
            HALF_CURVE_ORDER = _Secp256k1.N.ShiftRight(1);
            CURVE_ORDER = _Secp256k1.N;
        }

        public ECKey(byte[] vch, bool isPrivate)
        {
            if (isPrivate)
            {
                this._Key = new ECPrivateKeyParameters(new BigInteger(1, vch), this.DomainParameter);
            }
            else
            {
                var q = Secp256k1.Curve.DecodePoint(vch);
                this._Key = new ECPublicKeyParameters("EC", q, this.DomainParameter);
            }
        }

        public ECPrivateKeyParameters PrivateKey => this._Key as ECPrivateKeyParameters;


        public static X9ECParameters Secp256k1 => _Secp256k1;

        public ECDomainParameters DomainParameter
        {
            get
            {
                if (this._DomainParameter == null)
                    this._DomainParameter =
                        new ECDomainParameters(Secp256k1.Curve, Secp256k1.G, Secp256k1.N, Secp256k1.H);
                return this._DomainParameter;
            }
        }


        public ECDSASignature Sign(uint256 hash)
        {
            AssertPrivateKey();
            var signer = new DeterministicECDSA();
            signer.setPrivateKey(this.PrivateKey);
            var sig = ECDSASignature.FromDER(signer.signHash(hash.ToBytes()));
            return sig.MakeCanonical();
        }

        void AssertPrivateKey()
        {
            if (this.PrivateKey == null)
                throw new InvalidOperationException("This key should be a private key for such operation");
        }


        internal bool Verify(uint256 hash, ECDSASignature sig)
        {
            var signer = new ECDsaSigner();
            signer.Init(false, GetPublicKeyParameters());
            return signer.VerifySignature(hash.ToBytes(), sig.R, sig.S);
        }


        public PubKey GetPubKey(bool isCompressed)
        {
            var q = GetPublicKeyParameters().Q;
            //Pub key (q) is composed into X and Y, the compressed form only include X, which can derive Y along with 02 or 03 prepent depending on whether Y in even or odd.
            q = q.Normalize();
            var result = Secp256k1.Curve.CreatePoint(q.XCoord.ToBigInteger(), q.YCoord.ToBigInteger())
                .GetEncoded(isCompressed);
            return new PubKey(result);
        }


        public ECPublicKeyParameters GetPublicKeyParameters()
        {
            if (this._Key is ECPublicKeyParameters)
            {
                return (ECPublicKeyParameters) this._Key;
            }

            var q = Secp256k1.G.Multiply(this.PrivateKey.D);
            return new ECPublicKeyParameters("EC", q, this.DomainParameter);
        }


        public static ECKey RecoverFromSignature(int recId, ECDSASignature sig, uint256 message, bool compressed)
        {
            if (recId < 0)
                throw new ArgumentException("recId should be positive");
            if (sig.R.SignValue < 0)
                throw new ArgumentException("r should be positive");
            if (sig.S.SignValue < 0)
                throw new ArgumentException("s should be positive");
            if (message == null)
                throw new ArgumentNullException("message");


            var curve = Secp256k1;

            // 1.0 For j from 0 to h   (h == recId here and the loop is outside this function)
            //   1.1 Let x = r + jn

            var n = curve.N;
            var i = BigInteger.ValueOf((long) recId / 2);
            var x = sig.R.Add(i.Multiply(n));

            //   1.2. Convert the integer x to an octet string X of length mlen using the conversion routine
            //        specified in Section 2.3.7, where mlen = ⌈(log2 p)/8⌉ or mlen = ⌈m/8⌉.
            //   1.3. Convert the octet string (16 set binary digits)||X to an elliptic curve point R using the
            //        conversion routine specified in Section 2.3.4. If this conversion routine outputs “invalid”, then
            //        do another iteration of Step 1.
            //
            // More concisely, what these points mean is to use X as a compressed public key.
            var prime = ((SecP256K1Curve) curve.Curve).QQ;
            if (x.CompareTo(prime) >= 0) return null;

            // Compressed keys require you to know an extra bit of data about the y-coord as there are two possibilities.
            // So it's encoded in the recId.
            var R = DecompressKey(x, (recId & 1) == 1);
            //   1.4. If nR != point at infinity, then do another iteration of Step 1 (callers responsibility).

            if (!R.Multiply(n).IsInfinity)
                return null;

            //   1.5. Compute e from M using Steps 2 and 3 of ECDSA signature verification.
            var e = new BigInteger(1, message.ToBytes());
            //   1.6. For k from 1 to 2 do the following.   (loop is outside this function via iterating recId)
            //   1.6.1. Compute a candidate public key as:
            //               Q = mi(r) * (sR - eG)
            //
            // Where mi(x) is the modular multiplicative inverse. We transform this into the following:
            //               Q = (mi(r) * s ** R) + (mi(r) * -e ** G)
            // Where -e is the modular additive inverse of e, that is z such that z + e = 0 (mod n). In the above equation
            // ** is point multiplication and + is point addition (the EC group operator).
            //
            // We can find the additive inverse by subtracting e from zero then taking the mod. For example the additive
            // inverse of 3 modulo 11 is 8 because 3 + 8 mod 11 = 0, and -3 mod 11 = 8.

            var eInv = BigInteger.Zero.Subtract(e).Mod(n);
            var rInv = sig.R.ModInverse(n);
            var srInv = rInv.Multiply(sig.S).Mod(n);
            var eInvrInv = rInv.Multiply(eInv).Mod(n);
            var q = ECAlgorithms.SumOfTwoMultiplies(curve.G, eInvrInv, R, srInv);
            q = q.Normalize();
            if (compressed) q = new SecP256K1Point(curve.Curve, q.XCoord, q.YCoord, true);
            return new ECKey(q.GetEncoded(), false);
        }

        static ECPoint DecompressKey(BigInteger xBN, bool yBit)
        {
            var curve = Secp256k1.Curve;
            var compEnc = X9IntegerConverter.IntegerToBytes(xBN, 1 + X9IntegerConverter.GetByteLength(curve));
            compEnc[0] = (byte) (yBit ? 0x03 : 0x02);
            return curve.DecodePoint(compEnc);
        }
    }
}