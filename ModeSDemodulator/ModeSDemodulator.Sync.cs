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

using System.Collections.Generic;
using RtlSdrManager.Types;
using ModeSDemodulator.Exceptions;

namespace ModeSDemodulator
{
    /// <summary>
    /// This class demodulates I/Q data to Mode S frames.
    /// </summary>
    /// <inheritdoc />
    public partial class ModeSDemodulator
    {
        /// <summary>
        /// Demodulate the I/Q samples, and create Mode S frames.
        /// </summary>
        /// <param name="samples"></param>
        public void DemodulateSamples(List<IQData> samples)
        {
            // Check the asynchronous sample demodulation.
            if (_asyncWorker != null)
            {
                throw new ModeSLibraryExecutionException(
                    "Problem happened during sample demodulation. Asynchronous sample demodulation is in progress.");
            }

            // Start processing of the samples.
            foreach (var sample in samples)
            {
                // Calculate the magnitude.
                var magnitude = _magnitudeLookupTable[sample.I, sample.Q];

                // Set the candidate head to the current value.
                _candidateFrameBuffer[_candidateFramePointer] = magnitude;

                // Step the index.
                _candidateFramePointer = (_candidateFramePointer + 1) % _candidateFrameBuffer.Length;

                // Start the data processing, if the frame's preamble is valid. 
                if (IsPreambleValid())
                {
                    ProcessSample();
                }
            }
        }
    }
}