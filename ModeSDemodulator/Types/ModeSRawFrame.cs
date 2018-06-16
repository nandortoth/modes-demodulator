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

namespace ModeSDemodulator.Types
{
    /// <summary>
    /// The class represents a Mode S frame.
    /// </summary>
    public class ModeSRawFrame
    {
        /// <summary>
        /// Initialize ModeSFrame instance.
        /// </summary>
        /// <param name="rawFrame">Mode S frame in byte array.</param>
        /// <param name="icao">ICAO.</param>
        /// <param name="df">Downlink Format.</param>
        internal ModeSRawFrame(byte[] rawFrame, uint icao, ModeSDownlinkFormats df)
        {
            RawBytes = rawFrame;
            ICAO = icao;
            DownlinkFormat = df;
        }

        /// <summary>
        /// Get the Downlink Format of the frame.
        /// </summary>
        public ModeSDownlinkFormats DownlinkFormat { get; }

        /// <summary>
        /// Get the ICAO of the frame.
        /// </summary>
        public uint ICAO { get; }

        /// <summary>
        /// Get the length of the frame in bytes.
        /// </summary>
        public int ByteLength => RawBytes.Length;

        /// <summary>
        /// Get the length of the frame in bits.
        /// </summary>
        public int BitLength => RawBytes.Length * 8;

        /// <summary>
        /// Get the raw bytes of the frame.
        /// </summary>
        public byte[] RawBytes { get; }

        /// <summary>
        /// Print the frame in hexadecimal format.
        /// </summary>
        /// <returns>Hexadecimal format of the frame.</returns>
        public override string ToString()
        {
            var hex = BitConverter.ToString(RawBytes).Replace("-", string.Empty).ToLower();
            return $"*{hex};";
        }
    }
}