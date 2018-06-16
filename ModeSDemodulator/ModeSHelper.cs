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

using System;
using System.Linq;
using ModeSDemodulator.Types;

namespace ModeSDemodulator
{
    /// <summary>
    /// Static class for Mode S helper functions.
    /// </summary>
    internal static class ModeSHelper
    {
        /// <summary>
        /// Get the Downlink Format of the frame.
        /// </summary>
        /// <param name="rawFrame">Mode S frame.</param>
        /// <returns></returns>
        public static ModeSDownlinkFormats GetDownlinkFormat(byte[] rawFrame)
        {
            // The first five bit is the DF.
            var df = (rawFrame[0] >> 3) & 0x1F;

            // Convert the ModeSDownlinkFormats enum to an int list.
            var validDFs = Enum.GetValues(typeof(ModeSDownlinkFormats)).Cast<int>().ToList();

            // The DF is invalid if the DF is not in the validDFs list.
            if (!validDFs.Contains(df))
            {
                return ModeSDownlinkFormats.Invalid;
            }

            // Return the DF.
            return (ModeSDownlinkFormats) df;
        }

        /// <summary>
        /// Get the ICAO from the Mode S frame.
        /// </summary>
        /// <param name="rawFrame">Mode S frame.</param>
        /// <returns>ICAO or 0 if something is wrong.</returns>
        public static uint GetICAOAddress(byte[] rawFrame)
        {
            // Get the DF.
            var df = GetDownlinkFormat(rawFrame);

            // Calculate the frame's syndrome.
            var syndrome = ModeSParity.Syndrome(rawFrame);

            // Local variable to store the information: do the frame have ICAO address field or not.
            bool hasAddress;

            // Assign the "hasAddress" variable.
            switch (df)
            {
                // DF11 has ICAO address field.
                case ModeSDownlinkFormats.DF11:
                    syndrome &= 0xFFFF80;
                    hasAddress = true;
                    break;

                // DF17 has ICAO address field.
                case ModeSDownlinkFormats.DF17:
                    hasAddress = true;
                    break;

                // DF18 can have ICAO address field, if the Control Field is 0.
                case ModeSDownlinkFormats.DF18:

                    // Get the control field.
                    var cf = rawFrame[0] & 0x07;

                    // If the CF is 0, DF18 has ICAO address field (AA).
                    if (cf == 0)
                    {
                        hasAddress = true;
                        break;
                    }

                    // If the CF is not 0, DF18 does not have ICAO address field.
                    hasAddress = false;
                    break;

                // DF0, DF4, DF5, DF16, DF20, DF21, DF24 do not have ICAO address field.
                case ModeSDownlinkFormats.DF0:
                case ModeSDownlinkFormats.DF4:
                case ModeSDownlinkFormats.DF5:
                case ModeSDownlinkFormats.DF16:
                case ModeSDownlinkFormats.DF20:
                case ModeSDownlinkFormats.DF21:
                case ModeSDownlinkFormats.DF24:
                    hasAddress = false;
                    break;

                // Other DFs are invalid.
                default:
                    return 0;
            }

            // If the frame does not have address, return the syndrome which is the ICAO itself.
            if (!hasAddress)
            {
                return syndrome;
            }

            // If the syndrome is 0, return the ICAO (9-32 bit).
            if (syndrome == 0x000000)
            {
                return (uint) (rawFrame[1] << 16) | (uint) (rawFrame[2] << 8) | rawFrame[3];
            }

            // If the syndrome is not 0, try to fix.
            var errorBit = ModeSParity.ErrorBit(rawFrame.Length, syndrome);

            // If the faulty bit's position less than 5, fix is not possible.
            if (errorBit < 5)
            {
                return 0;
            }

            // Return fixed ICAO.
            rawFrame[errorBit / 8] = (byte) (rawFrame[errorBit / 8] ^ (1 << (7 - errorBit % 8)));
            return (uint) (rawFrame[1] << 16) | (uint) (rawFrame[2] << 8) | rawFrame[3];
        }

        /// <summary>
        /// Get ICAO from the Address/Parity.
        /// </summary>
        /// <param name="rawFrame">Mode S frame.</param>
        /// <returns>ICAO or 0 if something is wrong.</returns>
        public static uint GetICAOAddressFromAddressParity(byte[] rawFrame)
        {
            // Get the DF.
            var df = GetDownlinkFormat(rawFrame);

            // DF11, DF17 and DF18 do not have AP, they have PI.
            if (df == ModeSDownlinkFormats.DF11 ||
                df == ModeSDownlinkFormats.DF17 ||
                df == ModeSDownlinkFormats.DF18)
            {
                return 0;
            }

            // Compute checksum of the frame.
            var checksum = ModeSParity.Checksum(rawFrame);

            // Store the frame's length.
            var length = rawFrame.Length;

            // Recover the ICAO from the AP field.
            // Formula: (ICAO ^ CRC) ^ CRC = ICAO
            rawFrame[length - 1] = (byte) (rawFrame[length - 1] ^ ((checksum >> 0) & 0xff));
            rawFrame[length - 2] = (byte) (rawFrame[length - 2] ^ ((checksum >> 8) & 0xff));
            rawFrame[length - 3] = (byte) (rawFrame[length - 3] ^ ((checksum >> 16) & 0xff));

            // Return the ICAO.
            return rawFrame[length - 1] | (uint) rawFrame[length - 2] << 8 | (uint) rawFrame[length - 3] << 16;
        }
    }
}