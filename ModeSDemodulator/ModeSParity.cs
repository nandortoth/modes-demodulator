// .NET Core library to demodulate Mode S frames
// Copyright (C) 2018 Nandor Toth <dev@nandortoth.eu>
//  
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see http://www.gnu.org/licenses.

namespace ModeSDemodulator
{
    /// <summary>
    /// Helper class to manage Address/Parity and Parity/Interrogation fields.
    /// Described in 3.1.2.3.3.
    /// </summary>
    internal static class ModeSParity
    {
        /// <summary>
        /// Private field to store CRC table.
        /// </summary>
        private static readonly uint[] CRCTable =
        {
            0x000000, 0xfff409, 0x001c1b, 0xffe812, 0x003836, 0xffcc3f, 0x00242d, 0xffd024,
            0x00706c, 0xff8465, 0x006c77, 0xff987e, 0x00485a, 0xffbc53, 0x005441, 0xffa048,
            0x00e0d8, 0xff14d1, 0x00fcc3, 0xff08ca, 0x00d8ee, 0xff2ce7, 0x00c4f5, 0xff30fc,
            0x0090b4, 0xff64bd, 0x008caf, 0xff78a6, 0x00a882, 0xff5c8b, 0x00b499, 0xff4090,
            0x01c1b0, 0xfe35b9, 0x01ddab, 0xfe29a2, 0x01f986, 0xfe0d8f, 0x01e59d, 0xfe1194,
            0x01b1dc, 0xfe45d5, 0x01adc7, 0xfe59ce, 0x0189ea, 0xfe7de3, 0x0195f1, 0xfe61f8,
            0x012168, 0xfed561, 0x013d73, 0xfec97a, 0x01195e, 0xfeed57, 0x010545, 0xfef14c,
            0x015104, 0xfea50d, 0x014d1f, 0xfeb916, 0x016932, 0xfe9d3b, 0x017529, 0xfe8120,
            0x038360, 0xfc7769, 0x039f7b, 0xfc6b72, 0x03bb56, 0xfc4f5f, 0x03a74d, 0xfc5344,
            0x03f30c, 0xfc0705, 0x03ef17, 0xfc1b1e, 0x03cb3a, 0xfc3f33, 0x03d721, 0xfc2328,
            0x0363b8, 0xfc97b1, 0x037fa3, 0xfc8baa, 0x035b8e, 0xfcaf87, 0x034795, 0xfcb39c,
            0x0313d4, 0xfce7dd, 0x030fcf, 0xfcfbc6, 0x032be2, 0xfcdfeb, 0x0337f9, 0xfcc3f0,
            0x0242d0, 0xfdb6d9, 0x025ecb, 0xfdaac2, 0x027ae6, 0xfd8eef, 0x0266fd, 0xfd92f4,
            0x0232bc, 0xfdc6b5, 0x022ea7, 0xfddaae, 0x020a8a, 0xfdfe83, 0x021691, 0xfde298,
            0x02a208, 0xfd5601, 0x02be13, 0xfd4a1a, 0x029a3e, 0xfd6e37, 0x028625, 0xfd722c,
            0x02d264, 0xfd266d, 0x02ce7f, 0xfd3a76, 0x02ea52, 0xfd1e5b, 0x02f649, 0xfd0240,
            0x0706c0, 0xf8f2c9, 0x071adb, 0xf8eed2, 0x073ef6, 0xf8caff, 0x0722ed, 0xf8d6e4,
            0x0776ac, 0xf882a5, 0x076ab7, 0xf89ebe, 0x074e9a, 0xf8ba93, 0x075281, 0xf8a688,
            0x07e618, 0xf81211, 0x07fa03, 0xf80e0a, 0x07de2e, 0xf82a27, 0x07c235, 0xf8363c,
            0x079674, 0xf8627d, 0x078a6f, 0xf87e66, 0x07ae42, 0xf85a4b, 0x07b259, 0xf84650,
            0x06c770, 0xf93379, 0x06db6b, 0xf92f62, 0x06ff46, 0xf90b4f, 0x06e35d, 0xf91754,
            0x06b71c, 0xf94315, 0x06ab07, 0xf95f0e, 0x068f2a, 0xf97b23, 0x069331, 0xf96738,
            0x0627a8, 0xf9d3a1, 0x063bb3, 0xf9cfba, 0x061f9e, 0xf9eb97, 0x060385, 0xf9f78c,
            0x0657c4, 0xf9a3cd, 0x064bdf, 0xf9bfd6, 0x066ff2, 0xf99bfb, 0x0673e9, 0xf987e0,
            0x0485a0, 0xfb71a9, 0x0499bb, 0xfb6db2, 0x04bd96, 0xfb499f, 0x04a18d, 0xfb5584,
            0x04f5cc, 0xfb01c5, 0x04e9d7, 0xfb1dde, 0x04cdfa, 0xfb39f3, 0x04d1e1, 0xfb25e8,
            0x046578, 0xfb9171, 0x047963, 0xfb8d6a, 0x045d4e, 0xfba947, 0x044155, 0xfbb55c,
            0x041514, 0xfbe11d, 0x04090f, 0xfbfd06, 0x042d22, 0xfbd92b, 0x043139, 0xfbc530,
            0x054410, 0xfab019, 0x05580b, 0xfaac02, 0x057c26, 0xfa882f, 0x05603d, 0xfa9434,
            0x05347c, 0xfac075, 0x052867, 0xfadc6e, 0x050c4a, 0xfaf843, 0x051051, 0xfae458,
            0x05a4c8, 0xfa50c1, 0x05b8d3, 0xfa4cda, 0x059cfe, 0xfa68f7, 0x0580e5, 0xfa74ec,
            0x05d4a4, 0xfa20ad, 0x05c8bf, 0xfa3cb6, 0x05ec92, 0xfa189b, 0x05f089, 0xfa0480
        };

        /// <summary>
        /// Private field to store DF17's syndromes.
        /// </summary>
        private static readonly uint[] DF17Syndromes =
        {
            0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x9E31E9, 0xB0E2F0, 0x587178,
            0x2C38BC, 0x161C5E, 0x0B0E2F, 0xFA7D13, 0x82C48D, 0xBE9842, 0x5F4C21, 0xD05C14,
            0x682E0A, 0x341705, 0xE5F186, 0x72F8C3, 0xC68665, 0x9CB936, 0x4E5C9B, 0xD8D449,
            0x939020, 0x49C810, 0x24E408, 0x127204, 0x093902, 0x049C81, 0xFDB444, 0x7EDA22,
            0x3F6D11, 0xE04C8C, 0x702646, 0x381323, 0xE3F395, 0x8E03CE, 0x4701E7, 0xDC7AF7,
            0x91C77F, 0xB719BB, 0xA476D9, 0xADC168, 0x56E0B4, 0x2B705A, 0x15B82D, 0xF52612,
            0x7A9309, 0xC2B380, 0x6159C0, 0x30ACE0, 0x185670, 0x0C2B38, 0x06159C, 0x030ACE,
            0x018567, 0xFF38B7, 0x80665F, 0xBFC92B, 0xA01E91, 0xAFF54C, 0x57FAA6, 0x2BFD53,
            0xEA04AD, 0x8AF852, 0x457C29, 0xDD4410, 0x6EA208, 0x375104, 0x1BA882, 0x0DD441,
            0xF91024, 0x7C8812, 0x3E4409, 0xE0D800, 0x706C00, 0x383600, 0x1C1B00, 0x0E0D80,
            0x0706C0, 0x038360, 0x01C1B0, 0x00E0D8, 0x00706C, 0x003836, 0x001C1B, 0xFFF409,
            0x800000, 0x400000, 0x200000, 0x100000, 0x080000, 0x040000, 0x020000, 0x010000,
            0x008000, 0x004000, 0x002000, 0x001000, 0x000800, 0x000400, 0x000200, 0x000100,
            0x000080, 0x000040, 0x000020, 0x000010, 0x000008, 0x000004, 0x000002, 0x000001
        };

        /// <summary>
        /// Private field to store DF11's syndromes.
        /// </summary>
        private static readonly uint[] DF11Syndromes =
        {
            0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x9E31E9, 0xB0E2F0, 0x587178,
            0x2C38BC, 0x161C5E, 0x0B0E2F, 0xFA7D13, 0x82C48D, 0xBE9842, 0x5F4C21, 0xD05C14,
            0x682E0A, 0x341705, 0xE5F186, 0x72F8C3, 0xC68665, 0x9CB936, 0x4E5C9B, 0xD8D449,
            0x939020, 0x49C810, 0x24E408, 0x127204, 0x093902, 0x049C81, 0xFDB444, 0x7EDA22,
            0x800000, 0x400000, 0x200000, 0x100000, 0x080000, 0x040000, 0x020000, 0x010000,
            0x008000, 0x004000, 0x002000, 0x001000, 0x000800, 0x000400, 0x000200, 0x000100,
            0x000080, 0x000040, 0x000020, 0x000010, 0x000008, 0x000004, 0x000002, 0x000001
        };

        /// <summary>
        /// Calculate the checksum of the Mode S frame.
        /// </summary>
        /// <param name="rawFrame">Mode S frame.</param>
        /// <returns>Checksum.</returns>
        public static uint Checksum(byte[] rawFrame)
        {
            uint crc = 0x00000000;

            if (rawFrame.Length != 7 && rawFrame.Length != 14)
            {
                return 0x0F000000;
            }

            for (var i = 0; i < rawFrame.Length - 3; ++i)
            {
                var tmp = (byte) (rawFrame[i] ^ (crc >> 16));
                crc = CRCTable[tmp] ^ (crc << 8);
            }

            return crc & 0x00FFFFFF;
        }

        /// <summary>
        /// Calculate the syndrome of the Mode S frame.
        /// </summary>
        /// <param name="rawFrame">Mode S frame.</param>
        /// <returns>Syndrome.</returns>
        public static uint Syndrome(byte[] rawFrame)
        {
            var len = rawFrame.Length;

            var crc = Checksum(rawFrame);
            var sum = ((uint) rawFrame[len - 3] << 16) | (uint) (rawFrame[len - 2] << 8) |
                      (uint) (rawFrame[len - 1] << 0);
            return crc ^ sum;
        }

        /// <summary>
        /// Calculate the position of the error bit.
        /// </summary>
        /// <param name="frameLength">Length of the Mode S frame.</param>
        /// <param name="syndrome">Syndrome.</param>
        /// <returns></returns>
        public static int ErrorBit(int frameLength, uint syndrome)
        {
            var syndromeTable = frameLength == 14 ? DF17Syndromes : DF11Syndromes;

            for (var i = 0; i < frameLength * 8; ++i)
            {
                if (syndrome == syndromeTable[i])
                {
                    return i;
                }
            }

            return -1;
        }
    }
}