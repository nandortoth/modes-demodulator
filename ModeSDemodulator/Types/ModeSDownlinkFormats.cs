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

namespace ModeSDemodulator.Types
{
    /// <summary>
    /// Downlink Formats of Mode S frames.
    /// </summary>
    public enum ModeSDownlinkFormats
    {
        DF0 = 0,
        DF4 = 4,
        DF5 = 5,
        DF11 = 11,
        DF16 = 16,
        DF17 = 17,
        DF18 = 18,
        DF20 = 20,
        DF21 = 21,
        DF24 = 24,
        Invalid = 99
    }
}